using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct X509VAL
{
	public IntPtr notBefore;
	public IntPtr notAfter;
}
