using System.Net;

namespace VintageHive
{
    public class Config
    {
        public string PublicIPAddress { get; set; } = "127.0.0.1";

        public string CountryCode { get; set; } = "CA";

        public string RegionCode { get; set; } = "BC";

        public string City { get; set; } = "Vancouver";

        public string PostalCode { get; set; } = "V6B";

        public double Latitude { get; set; } = 0;

        public double Longitude { get; set; } = 0;

        public string Timezone { get; set; } = "UTC";

        public string IpAddress { get; set; } = IPAddress.Any.ToString();

        public int PortHttp { get; set; } = 1990;

        public int PortHttps { get; set; } = 1991;

        public int InternetArchiveYear { get; set; } = 1999;
        
        public bool OfflineMode { get; set; } = false;

        public bool InternetArchive { get; set; } = true;

        public bool Intranet { get; set; } = true;
    }
}
