// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct X509VAL
{
	public IntPtr notBefore;
	public IntPtr notAfter;
}
