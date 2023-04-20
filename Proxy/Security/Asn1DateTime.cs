// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Globalization;
using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class Asn1DateTime : NativeRef
{
    public DateTime DateTime
    {
        get { return ToDateTime(this); }
        set { ASN1_TIME_set(this, DateTimeToTimeT(value.ToUniversalTime())); }
    }

    public Asn1DateTime(IntPtr ptr, bool takeOwnership = true) : base(ptr, takeOwnership) { }

    public Asn1DateTime() : base(ASN1_TIME_new()) { }

    public Asn1DateTime(DateTime dateTime) : this()
    {
        DateTime = dateTime;
    }

    private long DateTimeToTimeT(DateTime value)
    {
        var dt1970 = new DateTime(1970, 1, 1, 0, 0, 0, 0);

        // # of 100 nanoseconds since 1970
        var ticks = (value.Ticks - dt1970.Ticks) / 10000000L;

        return ticks;
    }

    public static DateTime ToDateTime(IntPtr ptr)
    {
        return AsnTimeToDateTime(ptr).ToLocalTime();
    }

    private static DateTime AsnTimeToDateTime(IntPtr ptr)
    {
        using var bio = new BasicInputOutput();

        CheckResultSuccess(ASN1_UTCTIME_print(bio, ptr));

        string[] formats = { "MMM  d HH:mm:ss yyyy G\\MT", "MMM dd HH:mm:ss yyyy G\\MT" };

        return DateTime.ParseExact(bio.ToString(), formats, new DateTimeFormatInfo(), DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Dispose()
    {
        ASN1_TIME_free(this);
    }
}
