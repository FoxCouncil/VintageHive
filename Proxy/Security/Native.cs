// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

[SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = "Not ready for the commitment")]
[SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "It's an established API, leave me alone.")]
[SuppressMessage("Globalization", "CA2101:Specify marshaling for P/Invoke string arguments", Justification = "I did? They're ANSI, leave me TF alone! ;.;")]
public static class Native
{
    public readonly static Version WrapperVersion = new(0x1000201F); // 1.0.2a

    public readonly static Version LibVersion;

    public const int SHA_DIGEST_LENGTH = 20;

#if _WINDOWS
    const string CORE_DLL_NAME = "libs\\libeay32";
#else
    const string CORE_DLL_NAME = "./libs/libcrypto";
#endif

#if _WINDOWS
    const string SSL_DLL_NAME = "libs\\ssleay32";
#else
    const string SSL_DLL_NAME = "./libs/libssl";
#endif

    static Native()
    {
        // OpenSSL 1.0.2a (March 2015) has a bug in its SHA-NI (Intel SHA Extensions)
        // assembly code: the sha1_block_data_order_shaext function clobbers RAX (the
        // saved frame pointer) inside its processing loop, causing an access violation
        // when restoring XMM registers in the epilogue. This code path was untested in
        // 2015 because no consumer CPUs had SHA-NI at the time. Modern CPUs (Intel
        // Ice Lake+, AMD Zen+) have SHA-NI, triggering the bug.
        //
        // Fix: after the DLL loads (which sets up CPUID caps in DllMain), patch the
        // OPENSSL_ia32cap_P array to clear the SHA-NI bit (bit 29 of word 2, which
        // corresponds to CPUID leaf 7 EBX bit 29). This forces the generic SHA code
        // path which works correctly.
        LibVersion = new(SSLeay());

        if (LibVersion.Raw != WrapperVersion.Raw)
        {
            throw new ApplicationException($"Wrong OpenSSL DLL version detected, got {LibVersion} but require {WrapperVersion}");
        }

        // Patch out SHA-NI capability BEFORE any SHA operations occur.
        // OPENSSL_ia32cap_loc() returns uint* to the 4-element cap array.
        // cap[2] = CPUID(7,0).EBX - bit 29 is SHA-NI.
        var capPtr = OPENSSL_ia32cap_loc();
        if (capPtr != IntPtr.Zero)
        {
            var cap2 = Marshal.ReadInt32(capPtr, 8); // cap[2] at byte offset 8
            cap2 &= ~0x20000000; // clear bit 29 (SHA-NI)
            Marshal.WriteInt32(capPtr, 8, cap2);
        }

        SSL_load_error_strings();
        _ = SSL_library_init();

        InitializeThreadLocks();

        // ERR_load_crypto_strings();
    }

    /* THREADING */

    // OpenSSL before 1.1.0 does no locking of its own; it calls back into the application for
    // every internal lock, and with no callback installed those locks are silent no-ops. One
    // SSL_CTX is shared across every connection (SslContext) and driven from a pool thread per
    // connection (Listener), so without these the ctx refcount, session cache and error queue
    // all race. Dynamic locks (CRYPTO_set_dynlock_*) stay unset: 1.0.2 only takes them on engine
    // and CRL paths this proxy never enters, and a missing dynlock callback degrades safely.
    const int CRYPTO_LOCK = 1;

    // Rooted in static fields on purpose. OpenSSL keeps these pointers for the life of the
    // process, so letting the GC collect the thunks would crash on the next internal lock.
    static readonly LockingCallback _lockingCallback = OnLocking;

    static readonly ThreadIdCallback _threadIdCallback = OnThreadId;

    static object[] _cryptoLocks;

    static void InitializeThreadLocks()
    {
        var lockCount = CRYPTO_num_locks();

        _cryptoLocks = new object[lockCount];

        for (var i = 0; i < lockCount; i++)
        {
            _cryptoLocks[i] = new object();
        }

        CRYPTO_THREADID_set_callback(_threadIdCallback);

        CRYPTO_set_locking_callback(_lockingCallback);
    }

    static void OnLocking(int mode, int type, IntPtr file, int line)
    {
        if (type < 0 || type >= _cryptoLocks.Length)
        {
            return;
        }

        // OpenSSL always pairs a lock and its unlock on the same thread, which is exactly what
        // Monitor requires, and it never yields between the two, so no await can intervene.
        if ((mode & CRYPTO_LOCK) != 0)
        {
            Monitor.Enter(_cryptoLocks[type]);
        }
        else
        {
            Monitor.Exit(_cryptoLocks[type]);
        }
    }

    static void OnThreadId(IntPtr threadId)
    {
        // set_pointer rather than set_numeric: OpenSSL's numeric id is an unsigned long, which is
        // 4 bytes on Windows and 8 on Linux. The value is only ever hashed and compared, so a
        // pointer-sized managed thread id is a valid identity on both.
        CRYPTO_THREADID_set_pointer(threadId, Environment.CurrentManagedThreadId);
    }

    /* Delegate (Un)Function Pointers */

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PEMPasswordCallback(IntPtr buf, int size, int rwflag, IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LockingCallback(int mode, int type, IntPtr file, int line);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ThreadIdCallback(IntPtr threadId);

    /* INITS */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr OPENSSL_ia32cap_loc();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint SSLeay();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int CRYPTO_num_locks();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void CRYPTO_set_locking_callback(LockingCallback callback);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr CRYPTO_get_locking_callback();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void CRYPTO_THREADID_set_callback(ThreadIdCallback callback);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void CRYPTO_THREADID_set_pointer(IntPtr threadId, IntPtr value);

    /* Errors */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ERR_load_crypto_strings();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static uint ERR_get_error();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ERR_error_string_n(uint err, byte[] buffer, int length);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public extern static string ERR_lib_error_string(uint errorCode);

    /* NCONF */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr NCONF_new(IntPtr meth);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    // TODO Load from internal memory??
    public extern static int NCONF_load(IntPtr conf, string file, ref int eline);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void NCONF_free(IntPtr conf);

    /* EVP_PKEY */

    /* = Macros = */
    /// <summary>
    /// EVP_PKEY_assign_RSA. TRANSFERS OWNERSHIP of the RSA* into the EVP_PKEY on success.
    /// </summary>
    /// <remarks>
    /// The type argument was 0 (EVP_PKEY_NONE), not EVP_PKEY_RSA, so the assign always failed and the caller
    /// threw. Callers must not free the RSA* afterwards: EVP_PKEY_free will free it, and doing both is a
    /// double free. See CryptoKey(Rsa), which is why that constructor takes the wrapper's handle away.
    /// </remarks>
    public static int EVP_PKEY_assign_RSA(IntPtr pkey, IntPtr rsa)
    {
        return EVP_PKEY_assign(pkey, KeyType.RSA, rsa);
    }
    /* = Macros = */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_PKEY_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void EVP_PKEY_free(IntPtr pkey);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int EVP_PKEY_assign(IntPtr pkey, KeyType type, IntPtr key);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int EVP_PKEY_set1_RSA(IntPtr pkey, IntPtr key);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_PKEY_get1_RSA(IntPtr pkey);

    /* Message Digests */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_md_null();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_md2();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_md4();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_md5();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha1();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha224();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha256();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha384();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_sha512();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_dss();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_dss1();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_mdc2();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_ripemd160();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr EVP_ecdsa();

    /* X509 */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_sign(IntPtr x, IntPtr pkey, IntPtr md);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_version(IntPtr x, int version);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_subject_name(IntPtr x, IntPtr name);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_get_subject_name(IntPtr a);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_issuer_name(IntPtr x, IntPtr name);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_get_issuer_name(IntPtr a);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr PEM_read_bio_X509(IntPtr bp, IntPtr x, PEMPasswordCallback cb, IntPtr u);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr PEM_read_bio_PUBKEY(IntPtr bp, IntPtr x, PEMPasswordCallback cb, IntPtr u);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int PEM_write_bio_X509(IntPtr bp, IntPtr x);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void X509_free(IntPtr x);

    /* X509 Name */

    public const int MBSTRING_FLAG = 0x1000;

    public const int MBSTRING_ASC = MBSTRING_FLAG | 1;

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_NAME_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_NAME_dup(IntPtr xn);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_serialNumber(IntPtr x, IntPtr serial);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_get_serialNumber(IntPtr x);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_notBefore(IntPtr x, IntPtr tm);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_notAfter(IntPtr x, IntPtr tm);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_set_pubkey(IntPtr x, IntPtr pkey);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr X509_get_pubkey(IntPtr x);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_NAME_get_index_by_NID(IntPtr name, int nid, int lastpos);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_NAME_get_text_by_NID(IntPtr name, int nid, byte[] buf, int len);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int X509_NAME_add_entry_by_NID(IntPtr name, int nid, int type, byte[] bytes, int len, int loc, int set);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void X509_NAME_free(IntPtr a);

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

    //[DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    //public extern static IntPtr RSA_generate_key(int bits, IntPtr e, IntPtr callback, IntPtr callbackArgs);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int PEM_write_bio_RSA_PUBKEY(IntPtr bp, IntPtr x);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr PEM_read_bio_RSA_PUBKEY(IntPtr bp, IntPtr x, PEMPasswordCallback cb, IntPtr u);

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

    /* ASN1 */

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr ASN1_INTEGER_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ASN1_INTEGER_free(IntPtr x);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int ASN1_INTEGER_set(IntPtr a, int v);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int ASN1_INTEGER_get(IntPtr a);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr ASN1_TIME_set(IntPtr s, long t);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int ASN1_UTCTIME_print(IntPtr bp, IntPtr a);

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr ASN1_TIME_new();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void ASN1_TIME_free(IntPtr x);

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

    /* OBJ */

    public const int NID_UNDEFINED = 0;

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int OBJ_txt2nid(string s);

    /* SSL INITS */

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_load_error_strings();

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_library_init();

    [DllImport(CORE_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void OPENSSL_add_all_algorithms_noconf();

    /* SSL Delegates */

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int VerifyCertCallback(int ok, IntPtr x509_store_ctx);

    /* SSL Context Methods*/

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static IntPtr SSL_CTX_new(IntPtr sslMethod);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_CTX_set_verify(IntPtr ctx, int mode, VerifyCertCallback callback);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static void SSL_CTX_set_options(IntPtr ctx, long options);

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
    public extern static int SSL_use_certificate(IntPtr ssl, IntPtr x509);

    [DllImport(SSL_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
    public extern static int SSL_use_RSAPrivateKey(IntPtr ssl, IntPtr rsaKey);

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
