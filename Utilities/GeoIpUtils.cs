// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

public static class GeoIpUtils
{
    const string GeoIPApiUri = "http://ip-api.com/json/";

    public static async Task ResetGeoIP()
    {
        Mind.Db.ConfigSet<GeoIp>(ConfigNames.Location, null);

        await CheckGeoIp();
    }

    public static async Task CheckGeoIp()
    {
        var location = Mind.Db.ConfigGet<GeoIp>(ConfigNames.Location);

        if (location == null)
        {
            var geoIpData = await HttpClientUtils.GetHttpJson<GeoIp>(GeoIPApiUri);

            Mind.Db.ConfigSet(ConfigNames.Location, geoIpData);
        }
    }
}
