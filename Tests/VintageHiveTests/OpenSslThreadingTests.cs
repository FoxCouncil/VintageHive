// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VintageHive.Proxy.Security;

using NativeX509 = VintageHive.Proxy.Security.X509Certificate;

namespace Security;

// OpenSSL before 1.1.0 does no locking of its own. Every internal lock is a callback into the application, and
// with no callback installed those locks are silent no-ops rather than an error. VintageHive shares one SSL_CTX
// across every HTTPS connection (SslContext, held by Listener.SecurityContext) and drives it from a fresh pool
// thread per connection, so a missing callback is heap corruption under ordinary retro-browser load - one
// connection per resource - not a tidiness problem.
//
// The failure it prevents is a data race, so it can never be made into a reliably red test. What these assert
// instead is that the mechanism is present and stays present: the library wants locks, a callback is installed,
// and the managed thunk is rooted so the GC cannot collect it out from under native code.
[TestClass]
public class OpenSslThreadingTests
{
    [TestMethod]
    public void TheLibraryActuallyWantsLocks()
    {
        Assert.IsTrue(Native.CRYPTO_num_locks() > 0, "OpenSSL reported no lock slots, which would mean the rest of these tests are measuring nothing.");
    }

    [TestMethod]
    public void ALockingCallbackIsInstalled()
    {
        Assert.AreNotEqual(IntPtr.Zero, Native.CRYPTO_get_locking_callback(), "No locking callback is installed, so every OpenSSL internal lock is a no-op and the shared SSL_CTX is unprotected.");
    }

    // The thunks are reachable only from native code, so nothing but the static fields in Native keeps them
    // alive. If that rooting is ever dropped this is what turns it into a red test, instead of an access
    // violation in production some hours after the first GC.
    [TestMethod]
    public void TheCallbackIsRootedAgainstCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.AreNotEqual(IntPtr.Zero, Native.CRYPTO_get_locking_callback(), "The locking callback was collected, which leaves native code holding a dangling function pointer.");
    }

    // Drives the per-connection path Listener actually runs (parse a leaf PEM and its key, allocate a context)
    // from many threads at once. This is not proof of correctness - a race that survived would usually still
    // pass - but it does exercise the refcounting and error queue that the callbacks protect, so a gross
    // mistake in the locking wiring (a deadlock, or a lock/unlock pair split across threads) surfaces here.
    [TestMethod]
    public void TheLivePerConnectionPathSurvivesConcurrentUse()
    {
        using var rsa = RSA.Create(1024);

        var request = new CertificateRequest("CN=concurrency.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var certificatePem = new string(PemEncoding.Write("CERTIFICATE", certificate.RawData));
        var keyPem = new string(PemEncoding.Write("RSA PRIVATE KEY", certificate.GetRSAPrivateKey()!.ExportRSAPrivateKey()));

        var failures = 0;

        Parallel.For(0, 128, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
        {
            try
            {
                using var context = new SslContext();

                using var cert = NativeX509.FromPEM(certificatePem);
                using var key = Rsa.FromPEMPrivateKey(keyPem);
            }
            catch
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.AreEqual(0, failures, "Concurrent use of the per-connection OpenSSL path threw, which points at the locking wiring rather than at the certificate material.");
    }
}
