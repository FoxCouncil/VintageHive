using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct X509CINF
{
	public IntPtr version;

	public IntPtr serialNumber;

	public IntPtr signature;

	public IntPtr issuer;

	public IntPtr validity;

	public IntPtr subject;

	public IntPtr key;

	public IntPtr issuerUID;

	public IntPtr subjectUID;

	public IntPtr extensions;

	public Asn1Encoding enc;
}
