using System.Runtime.InteropServices;
using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

internal class X509Certificate : NativeRef
{
    public static int GlobalSerialNumber { get; private set; } = 1;

    public int Version 
    {
        get { return 0; }
        set { CheckResultSuccess(X509_set_version(this, value)); }
    }

    public X509Name Subject
    {
        get
        {
            var name_ptr = CheckResultSuccess(X509_get_subject_name(this));
            var name = new X509Name(name_ptr);
            // Duplicate the native pointer, as the X509_get_subject_name returns a pointer that is owned by the X509 object
            // name.AddRef();
            return name;
        }
        set { CheckResultSuccess(X509_set_subject_name(this, value)); }
    }

    public X509Name Issuer
    {
        get
        {
            var name_ptr = CheckResultSuccess(X509_get_issuer_name(this));
            var name = new X509Name(name_ptr);
            // Duplicate the native pointer, as the X509_get_subject_name returns a pointer that is owned by the X509 object
            // name.AddRef();
            return name;
        }
        set { CheckResultSuccess(X509_set_issuer_name(this, value)); }
    }

    public int SerialNumber
    {
        get { return Asn1Integer.ToInt32(X509_get_serialNumber(this)); }
        set
        {
            using var asnInt = new Asn1Integer(value);
            CheckResultSuccess(X509_set_serialNumber(this, asnInt));
        }
    }

    public DateTime NotBefore
    {
        get { return Asn1DateTime.ToDateTime(RawValidity.notBefore); }
        set
        {
            using (var asnDateTime = new Asn1DateTime(value))
            {
                CheckResultSuccess(X509_set_notBefore(this, asnDateTime));
            }
        }
    }

    public DateTime NotAfter
    {
        get { return Asn1DateTime.ToDateTime(RawValidity.notAfter); }
        set
        {
            using (var asnDateTime = new Asn1DateTime(value))
            {
                CheckResultSuccess(X509_set_notAfter(this, asnDateTime));
            }
        }
    }

    public CryptoKey PublicKey
    {
        get
        {
            // X509_get_pubkey() will increment the refcount internally
            var key_ptr = CheckResultSuccess(X509_get_pubkey(this));
            return new CryptoKey(key_ptr, true);
        }
        set { CheckResultSuccess(X509_set_pubkey(this, value)); }
    }

    X509Raw Raw { get { return (X509Raw)Marshal.PtrToStructure(this, typeof(X509Raw)); } }

    X509CINF RawCertInfo { get { return (X509CINF)Marshal.PtrToStructure(Raw.cert_info, typeof(X509CINF));  } }

    X509VAL RawValidity { get { return (X509VAL)Marshal.PtrToStructure(RawCertInfo.validity, typeof(X509VAL)); } }

    public X509Certificate() : base(X509_new()) { }

    public X509Certificate(BasicInputOutput bio) : base(CheckResultSuccess(PEM_read_bio_X509(bio, IntPtr.Zero, null, IntPtr.Zero))) { }

    public X509Certificate(string subject, string issuer, CryptoKey key, DateTime start, DateTime end) : this(new X509Name(subject), new X509Name(issuer), key, start, end) { }

    public X509Certificate(X509Name subject, X509Name issuer, CryptoKey key, DateTime start, DateTime end) : this()
    {
        Subject = subject;

        Issuer = issuer;
        
        Version = 2;

        SerialNumber = GlobalSerialNumber++;

        NotBefore = start;

        NotAfter = end;

        PublicKey = key;
    }

    public void Sign(CryptoKey pkey, MessageDigest digest)
    {
        if (X509_sign(this, pkey, digest) == 0) throw new OpenSslException();
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
