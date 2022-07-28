using System.Runtime.InteropServices;
using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

internal class X509 : NativeRef
{
    public static int GlobalSerialNumber { get; private set; } = 1;

    public int Version 
    {
        get { return 0; }
        set { CheckResultSuccess(X509_set_version(this, value)); }
    }

    X509Raw Raw { get { return (X509Raw)Marshal.PtrToStructure(this, typeof(X509Raw)); } }

    X509CINF RawCertInfo { get { return (X509CINF)Marshal.PtrToStructure(Raw.cert_info, typeof(X509CINF));  } }

    X509VAL RawValidity { get { return (X509VAL)Marshal.PtrToStructure(RawCertInfo.validity, typeof(X509VAL)); } }

    public X509() : base(X509_new()) { }

    public X509(BasicInputOutput bio) : base(CheckResultSuccess(PEM_read_bio_X509(bio, IntPtr.Zero, null, IntPtr.Zero))) { }

    public X509(object subject, object issuer, CryptoKey key, DateTime start, DateTime end) : this()
    {
        Version = 2;

        // SerialNumber = GlobalSerialNumber++;
    }

    public string ToPEM()
    {
        var writeBio = new BasicInputOutput();

        var result = PEM_write_bio_X509(writeBio, this);

        CheckResultSuccess(result);

        return writeBio.ToString();
    }

    public override void Dispose()
    {
        X509_free(this);
    }
}
