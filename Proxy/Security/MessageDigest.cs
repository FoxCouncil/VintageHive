using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class MessageDigest : NativeRef
{
    internal MessageDigest(IntPtr ptr, bool owner = true) : base(ptr, owner) { }

    public static readonly MessageDigest Null = new(EVP_md_null(), false);

    public static readonly MessageDigest MD4 = new(EVP_md4(), false);

    public static readonly MessageDigest MD5 = new(EVP_md5(), false);

    public static readonly MessageDigest SHA = new(EVP_sha(), false);

    public static readonly MessageDigest SHA1 = new(EVP_sha1(), false);

    public static readonly MessageDigest SHA224 = new(EVP_sha224(), false);

    public static readonly MessageDigest SHA256 = new(EVP_sha256(), false);

    public static readonly MessageDigest SHA384 = new(EVP_sha384(), false);

    public static readonly MessageDigest SHA512 = new(EVP_sha512(), false);

    public static readonly MessageDigest DSS = new(EVP_dss(), false);

    public static readonly MessageDigest DSS1 = new(EVP_dss1(), false);

    public static readonly MessageDigest RipeMD160 = new(EVP_ripemd160(), false);

    public static readonly MessageDigest ECDSA = new(EVP_ecdsa(), false);
}
