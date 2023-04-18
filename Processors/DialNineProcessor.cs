using VintageHive.Data.Types;
using VintageHive.Proxy.Http;
using VintageHive.Utilities;

namespace VintageHive.Processors;

internal static class DialNineProcessor
{
    public static async Task<bool> ProcessHttpsRequest(HttpRequest req, HttpResponse res)
    {
        if (!Mind.Db.ConfigLocalGet<bool>(req.ListenerSocket.RemoteIP, ConfigNames.Dialnine))
        {
            return false;
        }

        try
        {
            await res.SetExternal();

            Log.WriteLine(Log.LEVEL_INFO, nameof(DialNineProcessor), $"Forwarding to external -> {req.Uri}", req.ListenerSocket.TraceId.ToString());

        }
        catch (Exception e)
        {
            // Custom Error Messages
            Log.WriteLine(Log.LEVEL_ERROR, nameof(DialNineProcessor), e.Message, req.ListenerSocket.TraceId.ToString());
        }

        return true;
    }
}
