using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class CryptoKey : NativeRef
{
    public CryptoKey PublicKey 
    {
        get 
        {
            using var bio = new BasicInputOutput();

            CheckResultSuccess(PEM_write_bio_RSA_PUBKEY(bio, this));

            var ptr = CheckResultSuccess(PEM_read_bio_RSA_PUBKEY(bio, IntPtr.Zero, null, IntPtr.Zero));

            return new CryptoKey(ptr, true);
        }
    }

    public CryptoKey() : base(EVP_PKEY_new()) { }

    public CryptoKey(IntPtr ptr, bool owner = true) : base(ptr, owner) { }

    public CryptoKey(Rsa rsaKey) : this()
    {
        CheckResultSuccess(EVP_PKEY_assign_RSA(this, rsaKey));
    }

    public Rsa GetRSA()
    {
        return new Rsa(CheckResultSuccess(EVP_PKEY_get1_RSA(this)), true);
    }

    public static CryptoKey FromRSAPrivateKey(string pem)
    {
        using (var bio = new BasicInputOutput(pem))
        {
            var ptr = CheckResultSuccess(PEM_read_bio_RSAPrivateKey(bio, IntPtr.Zero, null, IntPtr.Zero));

            return new CryptoKey(ptr, true);
        }
    }

    public static CryptoKey FromRSAPublicKey(string pem)
    {
        using (var bio = new BasicInputOutput(pem))
        {
            var ptr = CheckResultSuccess(PEM_read_bio_RSA_PUBKEY(bio, IntPtr.Zero, null, IntPtr.Zero));

            return new CryptoKey(ptr, true);
        }
    }

    public override void Dispose()
    {
        EVP_PKEY_free(this);
    }
}
