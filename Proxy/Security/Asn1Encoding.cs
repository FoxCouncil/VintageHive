using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct Asn1Encoding
{
	public IntPtr enc;
	public int len;
	public int modified;
}
