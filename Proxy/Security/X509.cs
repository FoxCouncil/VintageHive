using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

internal class X509 : NativeRef
{
    public X509() : base(X509_new()) { }
}
