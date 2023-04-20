using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using VintageHive.Data.Types;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;

namespace VintageHive.Processors;

internal static class ProtoWebProcessor
{
    static readonly TimeSpan CacheTtl = TimeSpan.FromDays(365);
    
    static byte[] RedirectPacketSignatureBytes { get; } = Encoding.ASCII.GetBytes(RedirectPacketSignature);

    const string FetchRequestUserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 1.0.3705)";
    
    const string RedirectPacketSignature = "HTTP/1.0 301 Moved Permanently\n";

    static List<string> AvailableHttpSites;
    
    static List<string> AvailableFtpSites;

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        var protoWebEnabled = Mind.Db.ConfigLocalGet<bool>(req.ListenerSocket.RemoteIP, ConfigNames.ProtoWeb);

        if (!protoWebEnabled)
        {
            return false;
        }
        
        if (AvailableHttpSites == null)
        {
            AvailableHttpSites = await ProtoWebUtils.GetAvailableHttpSites();

            if (AvailableHttpSites == null || AvailableHttpSites.Count == 0)
            {
                return false;
            }
        }

        if (AvailableHttpSites.Any(x => req.Uri.Host.EndsWith(x)))
        {
            res.Cache = false;
            
            var cachedResponse = Mind.Cache.GetProtoweb(req.Uri);

            if (cachedResponse == null)
            {
                using var proxyClient = HttpClientUtils.GetProxiedHttpClient(req, ProtoWebUtils.MainProxyUri);

                try
                {
                    var proxyRes = await proxyClient.GetAsync(req.Uri);

                    var contentType = proxyRes.Content.Headers.ContentType?.ToString() ?? "text/html";

                    byte[] contentData = await proxyClient.GetByteArrayAsync(req.Uri);
                    
                    res.SetBodyData(contentData, contentType);

                    var cachedData = new ContentCachedData { ContentType = contentType, ContentDataBase64 = Convert.ToBase64String(contentData) };

                    var jsonData = JsonSerializer.Serialize<ContentCachedData>(cachedData);

                    Mind.Cache.SetProtoweb(req.Uri, CacheTtl, jsonData);

                    return true;
                }
                catch (HttpRequestException) { /* Ignore */ }
                catch (Exception ex)
                {
                    Log.WriteException(nameof(ProtoWebProcessor), ex, req.ListenerSocket.TraceId.ToString());

                    Debugger.Break();
                }

                return false;
            }
            else
            {
                var cachedData = JsonSerializer.Deserialize<ContentCachedData>(cachedResponse);

                var contentType = cachedData.ContentType;
                var contentData = Convert.FromBase64String(cachedData.ContentDataBase64);

                res.SetBodyData(contentData, contentType);

                return true;
            }
        }

        return false;
    }

    public static async Task<bool> ProcessFtpRequest(FtpRequest req)
    {
        if (req.ConnectionType != FtpRequestConnectionType.OverHttp) 
        { 
            return false;
        }

        if (AvailableFtpSites == null)
        {
            AvailableFtpSites = await ProtoWebUtils.GetAvailableFtpSites();

            if (AvailableFtpSites == null)
            {
                return false;
            }
        }

        if (AvailableFtpSites.Any(req.Uri.Host.EndsWith))
        {
            // We're streaming the data to the client, so it's not caching.
            // We could cache the small pages, but file downloads are not appropriate for SQLite storage.
            // We could possibly cache the files to disk...
            if (req.Uri.Scheme != "ftp")
            {
                return false;
            }

            using var tcpClient = new TcpClient(ProtoWebUtils.MainProxyHost, ProtoWebUtils.MainProxyPort)
            {
                NoDelay = true,
            };
            
            tcpClient.Client.NoDelay = true;

            var stream = tcpClient.GetStream();

            var request = ProtoWebUtils.CreateFtpContentRequest(req.Uri.ToString(), FetchRequestUserAgent, req.Uri.Host);

            var rawRequest = Encoding.ASCII.GetBytes(request);

            stream.Write(rawRequest, 0, rawRequest.Length);

            var buffer = new byte[1024];

            var readBytes = await stream.ReadAsync(buffer);

            if (readBytes == RedirectPacketSignatureBytes.Length && buffer.Take(RedirectPacketSignatureBytes.Length).SequenceEqual(RedirectPacketSignatureBytes))
            {
                readBytes = await stream.ReadAsync(buffer);

                await req.ListenerSocket.Stream.WriteAsync(buffer.Take(readBytes).ToArray());

                //var responseText = Encoding.ASCII.GetString(buffer, 0, readBytes);

                //var location = responseText.Split("Location: ")[1].Split("\n")[0];

                //if (!location.Contains($"ftp://{req.Uri.Host}"))
                //{
                //    location = $"ftp://{req.Uri.Host}{location}";
                //}

                //req.Uri = new Uri(location);

                //await ProcessFtpRequest(req);

                return true;
            }

            await req.ListenerSocket.Stream.WriteAsync(buffer.Take(readBytes).ToArray());

            await stream.CopyToAsync(req.ListenerSocket.Stream);

            return true;
        }

        return false;
    }
}
