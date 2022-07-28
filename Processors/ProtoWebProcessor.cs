using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using VintageHive.Proxy.Ftp;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;
using System;

namespace VintageHive.Processors;

internal static class ProtoWebProcessor
{
    const string FetchRequestUserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1; .NET CLR 1.0.3705)";
    
    const string RedirectPacketSignature = "HTTP/1.0 301 Moved Permanently\n";

    static byte[] RedirectPacketSignatureBytes { get; } = Encoding.ASCII.GetBytes(RedirectPacketSignature);

    static List<string> AvailableHttpSites;
    
    static List<string> AvailableFtpSites;

    public static async Task<bool> ProcessHttpRequest(HttpRequest req, HttpResponse res)
    {
        var protoWebEnabled = Mind.Instance.ConfigDb.SettingGet<bool>(ConfigNames.ProtoWeb);

        if (!protoWebEnabled)
        {
            return false;
        }
        
        if (AvailableHttpSites == null)
        {
            AvailableHttpSites = await ProtoWebUtils.GetAvailableHttpSites();
            
            if (AvailableHttpSites == null)
            {                
                return false;
            }
        }

        if (AvailableHttpSites.Any(x => req.Uri.Host.EndsWith(x)))
        {
            var proxyClient = Clients.GetProxiedHttpClient(req, ProtoWebUtils.MainProxyUri);

            try
            {
                var proxyRes = await proxyClient.GetAsync(req.Uri);

                var contentType = proxyRes.Content.Headers.ContentType?.ToString() ?? "text/html";

                res.SetBodyData(await proxyClient.GetByteArrayAsync(req.Uri), contentType);

                res.CacheTtl = TimeSpan.FromDays(30);

                Console.WriteLine($"[{"ProtoWeb HTTP", 17} Request] ({req.Uri}) [{contentType}]");

                return true;
            }
            catch (HttpRequestException) { /* Ignore */ }
            catch (Exception)
            {
                Debugger.Break();
            }

            return false;
        }

        return false;
    }

    public static async Task<byte[]> ProcessFtpRequest(FtpRequest req)
    {
        if (AvailableFtpSites == null)
        {
            AvailableFtpSites = await ProtoWebUtils.GetAvailableFtpSites();

            if (AvailableFtpSites == null)
            {
                return null;
            }
        }

        if (AvailableFtpSites.Any(x => req.Uri.Host.EndsWith(x)))
        {
            Console.WriteLine($"[{"ProtoWeb  FTP", 17} Request] ({req.Uri})");

            // We're streaming the data to the client, so it's not caching.
            // We could cache the small pages, but file downloads are not appropriate for SQLite storage.
            // We could possibly cache the files to disk...
            if (req.Uri.Scheme != "ftp")
            {
                return null;
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

                var responseText = Encoding.ASCII.GetString(buffer, 0, readBytes);
                
                var location = responseText.Split("Location: ")[1].Split("\n")[0];

                req.Uri = new Uri(location);

                await ProcessFtpRequest(req);

                return null;
            }

            await req.ListenerSocket.Stream.WriteAsync(buffer.Take(readBytes).ToArray());

            await stream.CopyToAsync(req.ListenerSocket.Stream);

            return null;
        }

        return null;
    }
}
