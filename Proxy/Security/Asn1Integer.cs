// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class Asn1Integer : NativeRef
{
    public int Value
    {
        get { return ASN1_INTEGER_get(this); }
        set { CheckResultSuccess(ASN1_INTEGER_set(this, value)); }
    }

    public Asn1Integer(IntPtr ptr, bool takeOwnership) : base(ptr, takeOwnership) { }

    public Asn1Integer() : base(ASN1_INTEGER_new()) { }

    public Asn1Integer(int value) : this()
    {
        Value = value;
    }

    public static int ToInt32(IntPtr ptr)
    {
        return ASN1_INTEGER_get(ptr);
    }

    public override void Dispose()
    {
        ASN1_INTEGER_free(this);
    }
}
