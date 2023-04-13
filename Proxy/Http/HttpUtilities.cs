namespace VintageHive.Proxy.Http
{
    public static class HttpUtilities
    {
        public static readonly string HttpSeperator = "\r\n";

        public static readonly string HttpBodySeperator = "\r\n\r\n";

        public static readonly IReadOnlyList<string> HttpVerbs = new List<string> { "HEAD", "GET", "POST", "CONNECT" }; // TODO: Extend to all!

        public static readonly IReadOnlyList<string> HttpVersions = new List<string> { "HTTP/1.0", "HTTP/1.1" };

        public static class HttpHeaderName
        {
            public const string ContentType = "Content-Type";

            public const string ContentDisposition = "Content-Disposition";

            public const string ContentLength = "Content-Length";

            public const string Date = "Date";

            public const string Server = "Server";

            public const string Location = "Location";

            public const string UserAgent = "User-Agent";

            public const string Host = "Host";

            public const string Cookie = "Cookie";
        }

        public static class HttpMethodName
        {
            public const string Get = "GET";

            public const string Post = "POST";

            public const string Put = "PUT";

            public const string Delete = "DELETE";

            public const string Head = "HEAD";

            public const string Options = "OPTIONS";

            public const string Trace = "TRACE";

            public const string Connect = "CONNECT";

            public const string Patch = "PATCH";
        }

        public static class HttpContentType
        {
            public static class Application
            {
                public const string Json = "application/json";

                public const string OctetStream = "application/octet-stream";

                public const string XWwwFormUrlEncoded = "application/x-www-form-urlencoded";
            }

            public static class Text
            {
                public const string Html = "text/html";

                public const string Plain = "text/plain";
            }

            public static class Multipart
            {
                public const string FormData = "multipart/form-data";
            }
        }

        public static void AddOrUpdate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value) where TKey : notnull
        {
            if (dict == null)
            {
                throw new ArgumentNullException(nameof(dict));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (dict.ContainsKey(key))
            {
                dict[key] = value;
            }
            else
            {
                dict.Add(key, value);
            }
        }
    }
}
