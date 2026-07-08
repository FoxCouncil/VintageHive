// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

/// <summary>
/// Single source of truth for VintageHive's virtual "*.hive.com" domain namespace. Every controller
/// [Domain(...)], host check, generated URL, and email address derives from <see cref="Base"/>.
/// Note: controller static assets live in Statics/controllers/&lt;domain&gt;/, so changing the base
/// domain also means renaming those directories.
/// </summary>
public static class HiveDomains
{
    public const string Base = "hive.com";

    public const string Intranet = Base;                        // hive.com
    public const string Admin = "admin." + Base;                // admin.hive.com
    public const string Radio = "radio." + Base;                // radio.hive.com
    public const string Api = "api." + Base;                    // api.hive.com
    public const string Ads = "ads." + Base;                    // ads.hive.com
    public const string Irc = "irc." + Base;                    // irc.hive.com
    public const string Smtp = "smtp." + Base;                  // smtp.hive.com
    public const string Autoconfig = "autoconfig." + Base;      // autoconfig.hive.com
    public const string Autodiscover = "autodiscover." + Base;  // autodiscover.hive.com

    public const string Wildcard = "*." + Base;                 // *.hive.com (controller catch-all)
    public const string DotSuffix = "." + Base;                 // .hive.com (host.EndsWith checks)
    public const string EmailSuffix = "@" + Base;               // @hive.com
}
