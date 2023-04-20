// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Runtime.InteropServices;

using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

[StructLayout(LayoutKind.Sequential)]
internal struct X509Raw
{
    public IntPtr cert_info;

    public IntPtr sig_alg;

    public IntPtr signature;

    public int valid;

    public int references;

    public IntPtr name;

    public IntPtr ex_data_sk;

    public int ex_data_dummy;

    public int ex_pathlen;

    public int ex_pcpathlen;

    public uint ex_flags;

    public uint ex_kusage;

    public uint ex_xkusage;

    public uint ex_nscert;

    public IntPtr skid;

    public IntPtr akid;

    public IntPtr policy_cache;

    public IntPtr crldp;

    public IntPtr altname;

    public IntPtr nc;

    public IntPtr rfc3779_addr;

    public IntPtr rfc3779_asid;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = SHA_DIGEST_LENGTH)]
    public byte[] sha1_hash;

    public IntPtr aux;
}
