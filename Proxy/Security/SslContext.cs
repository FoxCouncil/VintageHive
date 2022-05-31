using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Security;

public class SslContext : NativeRef
{
    public SslContext() : base(Native.SSL_CTX_new(Native.SSLv23_server_method())) { }

    public void SetVerify(bool verify, object callback = null)
    {
        Native.SSL_CTX_set_verify(this, verify ? 1 : 0, null);
    }

    public void SetCipherList(string cipherList)
    {
        if (Native.SSL_CTX_set_cipher_list(this, cipherList) != 1)
        {
            throw new OpenSslException();
        }
    }

    public void SetCertificateChain(string filename)
    {
        if (Native.SSL_CTX_use_certificate_chain_file(this, filename) != 1)
        {
            throw new OpenSslException();
        }
    }

    public void SetPrivateKeyFile(string filename)
    {
        if (Native.SSL_CTX_use_PrivateKey_file(this, filename, 1) != 1)
        {
            throw new OpenSslException();
        }

        if (Native.SSL_CTX_check_private_key(this) != 1)
        {
            throw new OpenSslException();
        }
    }

    public override void Dispose()
    {
        Native.SSL_CTX_free(this);
    }
}
