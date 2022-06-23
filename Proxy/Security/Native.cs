using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

internal static class Native
{
    public readonly static Version WrapperVersion = new(0x1000201F); // 1.0.2a

    public readonly static Version LibVersion;

    const string CORE_DLL_NAME = "libeay32";
    const string SSL_DLL_NAME = "ssleay32";

    static Native()
    {
        LibVersion = new(SSLeay());

        if (LibVersion.Raw != WrapperVersion.Raw)
        {
            throw new ApplicationException($"Wrong OpenSSL DLL version detected, got {LibVersion} but require {WrapperVersion}");
        }

        CheckResultSuccess(SSL_library_init());

        ERR_load_crypto_strings();

        SSL_load_error_strings();
    }

    /* Delegate (Un)Function Pointers */

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PEMPasswordCallback(IntPtr buf, int size, int rwflag, IntPtr userdata);

    /* INITS */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint SSLeay();

    /* Errors */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ERR_load_crypto_strings();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint ERR_get_error();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ERR_error_string_n(uint err, byte[] buffer, int length);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public extern static string ERR_lib_error_string(uint errorCode);

    /* EVP_PKEY */
    
    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_PKEY_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void EVP_PKEY_free(IntPtr pkey);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int EVP_PKEY_set1_RSA(IntPtr pkey, IntPtr key);

    /* X509 */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_new();

    /* X509 REQ */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_REQ_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void X509_REQ_free(IntPtr a);

    /* RSA Methods */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr RSA_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void RSA_free(IntPtr rsa);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int RSA_size(IntPtr rsa);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int RSA_generate_key_ex(IntPtr rsa, int bits, IntPtr e, IntPtr callback);

    /* PEM */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int PEM_write_bio_RSAPrivateKey(IntPtr bp, IntPtr x, IntPtr enc, byte[] kstr, int klen, IntPtr cb, IntPtr u);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr PEM_read_bio_RSAPrivateKey(IntPtr bp, IntPtr x, PEMPasswordCallback cb, IntPtr u);

    /* BigNumber */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr BN_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void BN_free(IntPtr a);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int BN_set_word(IntPtr a, uint w);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint BN_get_word(IntPtr a);

    /* BIOs */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr BIO_new(IntPtr type);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int BIO_ctrl(IntPtr bio, int cmd, int larg, IntPtr parg);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint BIO_ctrl_pending(IntPtr bio);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void BIO_free(IntPtr bio);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int BIO_read(IntPtr bio, byte[] buffer, int length);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int BIO_write(IntPtr bio, byte[] buffer, int length);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr BIO_s_mem();

    /* BIOs Constants */

    public const int BinaryInputOutputNoClose = 0;

    public const int BinaryInputOutputClose = 1;

    const int BIO_CTRL_SET_CLOSE = 9;

    /* BIOs Defines */

    public static int BIO_set_close(IntPtr bio, int close) => BIO_ctrl(bio, BIO_CTRL_SET_CLOSE, close, IntPtr.Zero);

    /* SSL INITS */

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_load_error_strings();

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_library_init();

    /* SSL Delegates */

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int VerifyCertCallback(int ok, IntPtr x509_store_ctx);

    /* SSL Context Methods*/

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSL_CTX_new(IntPtr sslMethod);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_CTX_set_verify(IntPtr ctx, int mode, VerifyCertCallback callback);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_CTX_set_cipher_list(IntPtr ctx, string cipher_string);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_CTX_use_certificate_chain_file(IntPtr ctx, string file);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_CTX_use_PrivateKey_file(IntPtr ctx, string file, int type);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_CTX_check_private_key(IntPtr ctx);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_CTX_free(IntPtr ctx);

    /* SSL Methods */

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSLv23_method();

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSLv23_server_method();

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSLv3_server_method();

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSL_new(IntPtr ctx);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_get_error(IntPtr ssl, int ret);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSL_set_bio(IntPtr ssl, IntPtr rbio, IntPtr wbio);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_set_accept_state(IntPtr ssl);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_accept(IntPtr ssl);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_set_info_callback(IntPtr ssl, IntPtr callback);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_do_handshake(IntPtr ssl);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_is_init_finished(IntPtr ssl);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_write(IntPtr ssl, byte[] buf, int len);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_read(IntPtr ssl, byte[] buf, int len);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_free(IntPtr ssl);

    /* SSL Constants */

    public const int SSL_ST_CONNECT = 0x1000;
    public const int SSL_ST_ACCEPT = 0x2000;
    public const int SSL_ST_MASK = 0x0FFF;
    public const int SSL_ST_INIT = SSL_ST_CONNECT | SSL_ST_ACCEPT;
    public const int SSL_ST_BEFORE = 0x4000;
    public const int SSL_ST_OK = 0x03;
    public const int SSL_ST_RENEGOTIATE = 0x04 | SSL_ST_INIT;

    public const int SSL_CB_LOOP = 0x01;
    public const int SSL_CB_EXIT = 0x02;
    public const int SSL_CB_READ = 0x04;
    public const int SSL_CB_WRITE = 0x08;
    public const int SSL_CB_ALERT = 0x4000; /* used in callback */
    public const int SSL_CB_READ_ALERT = SSL_CB_ALERT | SSL_CB_READ;
    public const int SSL_CB_WRITE_ALERT = SSL_CB_ALERT | SSL_CB_WRITE;
    public const int SSL_CB_ACCEPT_LOOP = SSL_ST_ACCEPT | SSL_CB_LOOP;
    public const int SSL_CB_ACCEPT_EXIT = SSL_ST_ACCEPT | SSL_CB_EXIT;
    public const int SSL_CB_CONNECT_LOOP = SSL_ST_CONNECT | SSL_CB_LOOP;
    public const int SSL_CB_CONNECT_EXIT = SSL_ST_CONNECT | SSL_CB_EXIT;
    public const int SSL_CB_HANDSHAKE_START = 0x10;
    public const int SSL_CB_HANDSHAKE_DONE = 0x20;

    public const int SSL_ERROR_NONE = 0;
    public const int SSL_ERROR_SSL = 1;
    public const int SSL_ERROR_WANT_READ = 2;
    public const int SSL_ERROR_WANT_WRITE = 3;
    public const int SSL_ERROR_WANT_X509_LOOKUP = 4;
    public const int SSL_ERROR_SYSCALL = 5; /* look at error stack/return value/errno */
    public const int SSL_ERROR_ZERO_RETURN = 6;
    public const int SSL_ERROR_WANT_CONNECT = 7;
    public const int SSL_ERROR_WANT_ACCEPT = 8;

    public static int CheckResultSuccess(int returnCode)
    {
        if (returnCode <= 0)
        {
            throw new OpenSslException();
        }

        return returnCode;
    }

    public static IntPtr CheckResultSuccess(IntPtr returnPtr)
    {
        if (returnPtr == IntPtr.Zero)
        {
            throw new OpenSslException();
        }

        return returnPtr;
    }
}
