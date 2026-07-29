// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Proxy.Security;

namespace Security;

// The CA's subject is the name every member reads in their browser's certificate store and install dialog, so a
// whitelabelled plane must not put VintageHive's name there. It used to be a const that was simultaneously the
// subject AND the row key the root is stored under, which made the two impossible to separate: rebranding moved
// the key, a moved key reads as "no root yet", and a new root silently invalidates every certificate members
// have already installed. They would all start warning at once.
//
// The load-bearing test in here is RenamingTheProduct_DoesNotRotateAnExistingRoot. Everything else guards a way
// that could regress into rotating as a side effect.
[TestClass]
public class CertificateAuthorityTests
{
    const string Product = "RetroPlane";

    static readonly object Gate = new();

    static bool _ready;

    [TestInitialize]
    public void Setup()
    {
        lock (Gate)
        {
            if (!_ready)
            {
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "vfs", "data"));

                if (Mind.Db == null)
                {
                    typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!.Invoke(null, new object[] { new HiveDbContext() });
                }

                _ready = true;
            }
        }

        // Every row these tests could touch, so one test's root cannot become another's "existing" root.
        Clear();

        Mind.Db!.ConfigSet(ConfigNames.ProductName, string.Empty);
        Mind.Db!.ConfigSet(ConfigNames.CertificateKeySize, 0);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Clear();

        Mind.Db!.ConfigSet(ConfigNames.ProductName, string.Empty);
        Mind.Db!.ConfigSet(ConfigNames.CertificateKeySize, 0);
    }

    static void Clear()
    {
        Mind.Db!.CertSet(CertificateAuthority.StorageKey, null);
        Mind.Db!.CertSet(CertificateAuthority.LegacyName, null);
        Mind.Db!.CertSet($"{Product} Certificate Authority", null);
    }

    static void SetProduct(string name) => Mind.Db!.ConfigSet(ConfigNames.ProductName, name);

    static string SubjectOf(X509Certificate2 certificate) => certificate.Subject;

    // Seeds a root under an arbitrary key, standing in for a box built by an older build that keyed the root by
    // its own subject name.
    static SslCertificate SeedRootUnder(string key, string commonName, int keySize = 512)
    {
        using var rsa = RSA.Create(keySize);

        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100));

        var stored = new SslCertificate(
            new string(PemEncoding.Write("CERTIFICATE", certificate.RawData)),
            new string(PemEncoding.Write("PRIVATE KEY", certificate.GetRSAPrivateKey()!.ExportPkcs8PrivateKey())));

        Mind.Db!.CertSet(key, stored);

        return stored;
    }

    // ---- naming ----

    [TestMethod]
    public void WithNoProductNameConfigured_TheSubjectIsUnchangedFromStock()
    {
        Assert.AreEqual("VintageHive Dialnine Cert Authority", CertificateAuthority.SubjectName);
        Assert.AreEqual(CertificateAuthority.LegacyName, CertificateAuthority.SubjectName);
    }

    [TestMethod]
    public void WithAProductNameConfigured_TheSubjectFollowsIt()
    {
        SetProduct(Product);

        Assert.AreEqual($"{Product} Certificate Authority", CertificateAuthority.SubjectName);
        Assert.IsFalse(CertificateAuthority.SubjectName.Contains("VintageHive"), "A whitelabelled plane still names VintageHive in its root's subject.");
    }

    [TestMethod]
    public void AWhitespaceOnlyProductName_FallsBackToStockRatherThanNamingTheRootNothing()
    {
        SetProduct("   ");

        Assert.AreEqual(CertificateAuthority.LegacyName, CertificateAuthority.SubjectName);
    }

    // An unescaped comma or equals sign in the product name would make CN=Acme, Inc. Certificate Authority parse
    // as a common name plus a second, invalid relative name, and throw while generating the root at startup.
    [TestMethod]
    public void AProductNameCarryingDistinguishedNameSyntax_StillProducesAUsableSubject()
    {
        foreach (var hostile in new[] { "Acme, Inc.", "A=B", "Quote\"Co", "Semi;Colon", "Back\\Slash", "Plus+Co", "Angle<Bracket>" })
        {
            SetProduct(hostile);

            var subject = CertificateAuthority.SubjectName;

            // The proof is that a real certificate request accepts it - not that it merely looks clean.
            using var rsa = RSA.Create(512);

            var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            StringAssert.Contains(certificate.Subject, "CN=", $"'{hostile}' produced a subject X.509 could not encode.");
            Assert.AreEqual(1, certificate.SubjectName.EnumerateRelativeDistinguishedNames().Count(), $"'{hostile}' split the subject into more than one relative name.");
        }
    }

    // X.509 bounds the common name at 64 characters, and the suffix is the part that identifies what the
    // certificate IS, so the product portion is what gets clipped.
    [TestMethod]
    public void AVeryLongProductName_IsClippedButKeepsTheSuffix()
    {
        SetProduct(new string('R', 200));

        var subject = CertificateAuthority.SubjectName;

        Assert.IsTrue(subject.Length <= 64, $"The subject is {subject.Length} characters, past the X.509 bound.");
        StringAssert.EndsWith(subject, "Certificate Authority");
    }

    [TestMethod]
    public void Sanitisation_CollapsesTheGapsARemovalLeavesBehind()
    {
        Assert.AreEqual("Acme Inc.", CertificateAuthority.SanitiseForCommonName("Acme, Inc."));
        Assert.AreEqual("Acme", CertificateAuthority.SanitiseForCommonName("Acme"));
        Assert.AreEqual("Acme Co", CertificateAuthority.SanitiseForCommonName("  Acme   Co  "));
        Assert.AreEqual(string.Empty, CertificateAuthority.SanitiseForCommonName(",,,"));
    }

    // ---- generation ----

    [TestMethod]
    public void AFreshPlane_GeneratesARootUnderTheBrandingIndependentKey()
    {
        using var root = CertificateAuthority.LoadOrCreateRoot();

        StringAssert.Contains(SubjectOf(root), CertificateAuthority.LegacyName);

        Assert.IsNotNull(Mind.Db!.CertGet(CertificateAuthority.StorageKey), "The generated root was not stored where it will be looked for.");
        Assert.IsNull(Mind.Db!.CertGet(CertificateAuthority.LegacyName), "The root was filed under its subject name, which is what made rebranding lose it.");
    }

    [TestMethod]
    public void AFreshWhitelabelledPlane_GeneratesARootCarryingTheEmbeddersName()
    {
        SetProduct(Product);

        using var root = CertificateAuthority.LoadOrCreateRoot();

        StringAssert.Contains(SubjectOf(root), $"{Product} Certificate Authority");
        Assert.IsFalse(SubjectOf(root).Contains("VintageHive"), "A whitelabelled plane shipped a VintageHive-branded root into its members' trust stores.");
    }

    [TestMethod]
    public void LoadingTwice_ReturnsTheSameKeyMaterial()
    {
        using var first = CertificateAuthority.LoadOrCreateRoot();
        using var second = CertificateAuthority.LoadOrCreateRoot();

        Assert.AreEqual(first.Thumbprint, second.Thumbprint, "A second load minted a new root instead of reusing the stored one.");
    }

    // ---- migration, the part that must not rotate ----

    [TestMethod]
    public void AnExistingStockRoot_IsAdoptedAndRekeyedWithoutRotating()
    {
        var seeded = SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName);

        using var loaded = CertificateAuthority.LoadOrCreateRoot();

        using var original = X509Certificate2.CreateFromPem(seeded.Certificate, seeded.Key);

        Assert.AreEqual(original.Thumbprint, loaded.Thumbprint, "The existing root was replaced, which invalidates every certificate members have installed.");

        var migrated = Mind.Db!.CertGet(CertificateAuthority.StorageKey);

        Assert.IsNotNull(migrated, "The root was not moved to the branding-independent key.");
        Assert.AreEqual(seeded.Certificate, migrated.Certificate, "The certificate changed during migration.");
        Assert.AreEqual(seeded.Key, migrated.Key, "The private key changed during migration.");
        Assert.IsNull(Mind.Db!.CertGet(CertificateAuthority.LegacyName), "The legacy row was left behind, so the next load has two roots to choose between.");
    }

    // THE load-bearing one. Setting a product name on a box that already has members with the root installed
    // must be a naming change for FUTURE roots only, never a rotation.
    [TestMethod]
    public void RenamingTheProduct_DoesNotRotateAnExistingRoot()
    {
        var seeded = SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName);

        using var original = X509Certificate2.CreateFromPem(seeded.Certificate, seeded.Key);

        SetProduct(Product);

        using var loaded = CertificateAuthority.LoadOrCreateRoot();

        Assert.AreEqual(original.Thumbprint, loaded.Thumbprint, "Setting ProductName rotated the root. Every member's browser starts warning at once.");

        // And the subject stays what it was signed as - the name is in the certificate, so it cannot follow the
        // config without issuing a new one. That is the whole reason rotation has to be deliberate.
        StringAssert.Contains(SubjectOf(loaded), CertificateAuthority.LegacyName);
        Assert.AreEqual($"{Product} Certificate Authority", CertificateAuthority.SubjectName, "The configured subject should still have moved, for whenever a root IS next generated.");
    }

    // The hole a name-keyed store leaves even with a legacy fallback: rename twice and neither the configured
    // name nor the stock literal finds the row.
    [TestMethod]
    public void RenamingTheProductTwice_StillDoesNotRotate()
    {
        SetProduct("FirstName");

        var seeded = SeedRootUnder("FirstName Certificate Authority", "FirstName Certificate Authority");

        using var original = X509Certificate2.CreateFromPem(seeded.Certificate, seeded.Key);

        // First load adopts it under the stable key while the configured name still matches.
        using (var adopted = CertificateAuthority.LoadOrCreateRoot())
        {
            Assert.AreEqual(original.Thumbprint, adopted.Thumbprint);
        }

        // Now rename again. A store keyed by the subject name would lose the row here.
        SetProduct("SecondName");

        using var loaded = CertificateAuthority.LoadOrCreateRoot();

        Assert.AreEqual(original.Thumbprint, loaded.Thumbprint, "A second rename rotated the root.");

        Mind.Db!.CertSet("FirstName Certificate Authority", null);
    }

    [TestMethod]
    public void AnExistingRootUnderTheConfiguredName_IsPreferredOverTheLegacyLiteral()
    {
        SetProduct(Product);

        var configured = SeedRootUnder($"{Product} Certificate Authority", $"{Product} Certificate Authority");

        SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName);

        using var loaded = CertificateAuthority.LoadOrCreateRoot();

        using var expected = X509Certificate2.CreateFromPem(configured.Certificate, configured.Key);

        Assert.AreEqual(expected.Thumbprint, loaded.Thumbprint, "The stock literal won over the configured name.");
    }

    // A row that exists but is unusable must not be adopted as if it were a root.
    [TestMethod]
    public void AnEmptyStoredRoot_IsTreatedAsAbsentRatherThanCrashing()
    {
        Mind.Db!.CertSet(CertificateAuthority.StorageKey, new SslCertificate(string.Empty, string.Empty));

        using var root = CertificateAuthority.LoadOrCreateRoot();

        Assert.IsNotNull(root);
        Assert.IsNotNull(Mind.Db!.CertGet(CertificateAuthority.StorageKey).Certificate);
    }

    // The store key must not be able to collide with a per-domain leaf, which lives in the same table keyed by
    // hostname. '!' cannot appear in a hostname.
    [TestMethod]
    public void TheStoreKeyCannotCollideWithAHostname()
    {
        StringAssert.StartsWith(CertificateAuthority.StorageKey, "!");
        Assert.IsFalse(Uri.CheckHostName(CertificateAuthority.StorageKey) != UriHostNameType.Unknown, "The root's store key is a syntactically valid hostname and could collide with a leaf.");
    }

    // ---- key size ----

    [TestMethod]
    public void KeySize_DefaultsToThePeriodCorrect512()
    {
        using var root = CertificateAuthority.LoadOrCreateRoot();

        Assert.AreEqual(512, root.GetRSAPublicKey()!.KeySize, "The period default changed; 1999 clients are the reason it is 512.");
    }

    [TestMethod]
    public void KeySize_CanBeRaisedForAPlaneWithNoPeriodClients()
    {
        Mind.Db!.ConfigSet(ConfigNames.CertificateKeySize, 2048);

        using var root = CertificateAuthority.LoadOrCreateRoot();

        Assert.AreEqual(2048, root.GetRSAPublicKey()!.KeySize);
    }

    // A configured size too small to carry a SHA-256 PKCS#1 signature would otherwise fail at signing time,
    // which is a much worse place to find out than here.
    [TestMethod]
    public void KeySize_TooSmallToSignFallsBackToTheDefault()
    {
        foreach (var nonsense in new[] { 1, 256, 511, -1 })
        {
            Clear();

            Mind.Db!.ConfigSet(ConfigNames.CertificateKeySize, nonsense);

            using var root = CertificateAuthority.LoadOrCreateRoot();

            Assert.AreEqual(512, root.GetRSAPublicKey()!.KeySize, $"A configured key size of {nonsense} was accepted.");
        }
    }

    // ---- the download endpoint members install from ----

    [TestMethod]
    public void GetRootCertificate_ReturnsTheStoredRootAndNeverThePrivateKey()
    {
        var seeded = SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName);

        using (CertificateAuthority.LoadOrCreateRoot()) { }

        var published = CertificateAuthority.GetRootCertificate();

        Assert.IsNotNull(published);
        Assert.AreEqual(seeded.Certificate, published.Certificate, "The endpoint members install from does not serve the root that is actually in use.");

        // The store hands back both halves together, so this accessor has to drop one. It is reachable from a
        // web route.
        Assert.AreEqual(string.Empty, published.Key, "The publish accessor handed out the certificate authority's private key.");
    }

    [TestMethod]
    public void GetRootCertificate_OnAPlaneWithNoRootYet_ReturnsNothingRatherThanThrowing()
    {
        Assert.IsNull(CertificateAuthority.GetRootCertificate());
    }

    // ---- leaves ----

    // The reason migrating rather than rotating matters: a leaf issued after the move still has to be signed by
    // the same root, or every host on the plane starts warning even though the root in the member's store is
    // still perfectly valid.
    [TestMethod]
    public void ALeafIssuedAfterMigration_IsSignedByThePreRenameRoot()
    {
        SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName);

        SetProduct(Product);

        using var root = CertificateAuthority.LoadOrCreateRoot();

        const string domain = "news.com";

        Mind.Db!.CertSet(domain, null);

        try
        {
            var leaf = CertificateAuthority.GetOrCreateDomainCertificate(domain, root);

            using var leafCertificate = X509Certificate2.CreateFromPem(leaf.Certificate, leaf.Key);

            Assert.AreEqual($"CN={domain}", leafCertificate.Subject);
            Assert.AreEqual(root.Subject, leafCertificate.Issuer, "The leaf was not issued by the migrated root.");
            Assert.AreNotEqual(leafCertificate.Subject, leafCertificate.Issuer, "The leaf is self-signed, so it chains to nothing.");

            // The issuer is still the pre-rename subject, which is the whole point: the root in members' stores
            // did not change, so neither did what its leaves are signed by.
            StringAssert.Contains(leafCertificate.Issuer, CertificateAuthority.LegacyName);
            Assert.IsFalse(leafCertificate.Issuer.Contains(Product), "Renaming the product changed what leaves are signed by, which is a rotation.");
        }
        finally
        {
            Mind.Db!.CertSet(domain, null);
        }
    }

    // Cryptographic proof of the same property, at a key size the host's chain policy will actually evaluate.
    //
    // Deliberately NOT run at the period default: a modern platform refuses to validate a 512-bit chain at all,
    // reporting NotSignatureValid and HasWeakSignature, so a chain-policy assertion there would be measuring the
    // host's minimum-key-length policy rather than anything about the migration. 1999 clients do their own
    // validation and do not care, which is why 512 stays the default.
    [TestMethod]
    public void ALeafIssuedAfterMigration_CryptographicallyChainsToThePreRenameRoot()
    {
        const int ModernKeySize = 2048;

        Mind.Db!.ConfigSet(ConfigNames.CertificateKeySize, ModernKeySize);

        var seeded = SeedRootUnder(CertificateAuthority.LegacyName, CertificateAuthority.LegacyName, ModernKeySize);

        SetProduct(Product);

        using var root = CertificateAuthority.LoadOrCreateRoot();

        const string domain = "modern.example.com";

        Mind.Db!.CertSet(domain, null);

        try
        {
            var leaf = CertificateAuthority.GetOrCreateDomainCertificate(domain, root);

            using var leafCertificate = X509Certificate2.CreateFromPem(leaf.Certificate, leaf.Key);
            using var original = X509Certificate2.CreateFromPem(seeded.Certificate, seeded.Key);

            var chain = new X509Chain();

            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(original);

            var built = chain.Build(leafCertificate);

            Assert.IsTrue(built, $"The leaf did not validate against the pre-migration root: {string.Join(", ", chain.ChainStatus.Select(x => x.Status))}");
            Assert.AreEqual(original.Thumbprint, chain.ChainElements[^1].Certificate.Thumbprint, "The chain terminated at something other than the root that was already installed.");
        }
        finally
        {
            Mind.Db!.CertSet(domain, null);
        }
    }
}
