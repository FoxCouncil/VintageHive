// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class Rsa : NativeRef
{
    public int Size => CheckResultSuccess(RSA_size(this));

    public Rsa() : base(RSA_new()) 
    {

    }
    
    public Rsa(IntPtr pointer, bool owner) : base(pointer, owner) { }

    public void GenerateKey(int bits, BigNumber e)
    {
        if (!IsOwner)
        {
            throw new ApplicationException("Not owner of RSA object, cannot generate a key!");
        }
        
        var result = RSA_generate_key_ex(this, bits, e, IntPtr.Zero);

        CheckResultSuccess(result);
    }

    public string PEMPrivateKey()
    {
        var writeBio = new BasicInputOutput();

        var result = PEM_write_bio_RSAPrivateKey(writeBio, this, IntPtr.Zero, null, 0, IntPtr.Zero, IntPtr.Zero);

        CheckResultSuccess(result);

        return writeBio.ToString();
    }

    public string PEMPublicKey()
    {
        var writeBio = new BasicInputOutput();

        var result = PEM_write_bio_RSA_PUBKEY(writeBio, this);

        CheckResultSuccess(result);

        return writeBio.ToString();
    }

    public override void Dispose()
    {
        RSA_free(this);
    }

    public static Rsa FromPEMPrivateKey(string key)
    {
        var readBio = new BasicInputOutput(key);

        var rsaPtr = PEM_read_bio_RSAPrivateKey(readBio, IntPtr.Zero, null, IntPtr.Zero);

        if (rsaPtr == IntPtr.Zero)
        {
            throw new ApplicationException("Failed to parse PEM private key!");
        }

        return new Rsa(rsaPtr, true);
    }
}
