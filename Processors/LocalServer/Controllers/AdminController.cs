using Fluid;
using System.Text.Json;
using VintageHive.Data.Types;
using VintageHive.Utilities;

namespace VintageHive.Processors.LocalServer.Controllers;

internal class AdminController : Controller
{
    private const string LoginSessionKeyName = "login";

    private readonly string[] AllowListedEndpoints = new string[] { "/login.html", "/css/", "/js/", "/img/", "/api/login" };

    public override async Task CallInitial(string rawPath)
    {
        await base.CallInitial(rawPath);

        Response.InitSession();

        if (!AllowListedEndpoints.Any(rawPath.StartsWith))
        {
            if (!IsAuthenticated())
            {
                Response.SetFound("/login.html");

                return;
            }
        }
    }

    [Controller("/index.html")]
    public async Task Index()
    {
        await Task.Delay(0);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
    }

    [Controller("/api/cachegetcounts")]
    public async Task CacheGetCounts()
    {
        await Task.Delay(0);

        var list = Mind.Cache.GetCounters();

        Response.SetJsonSuccess(list);
    }

    [Controller("/api/cacheclearall")]
    public async Task CacheClearAll()
    {
        await Task.Delay(0);

        Mind.Cache.Clear();

        Response.SetJsonSuccess();
    }

    [Controller("/api/getdownloadlocations")]
    public async Task GetDownloadLocations()
    {
        await Task.Delay(0);

        var list = RepoUtils.Get();

        Response.SetJsonSuccess(list);
    }

    [Controller("/api/linksgetall")]
    public async Task LinksGetAll()
    {
        await Task.Delay(0);

        var list = new List<object>();

        foreach (var link in Mind.Db.LinksGetAll())
        {
            list.Add(new { name = link.Key, link = link.Value });
        }

        Response.SetJsonSuccess(list);
    }

    [Controller("/api/iatoggle")]
    public async Task InternetArchiveToggle()
    {
        await Task.Delay(0);
        
        Mind.Db.ConfigSet(ConfigNames.InternetArchive, !Mind.Db.ConfigGet<bool>(ConfigNames.InternetArchive));
        
        Response.SetJsonSuccess();
    }

    [Controller("/api/iasetyear")]
    public async Task InternetArchiveSetYear()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("year"))
        {
            Response.SetJsonSuccess(false);
        }
        
        Mind.Db.ConfigSet(ConfigNames.InternetArchiveYear, Request.FormData["year"]);

        Response.SetJsonSuccess();
    }

    [Controller("/api/protowebtoggle")]
    public async Task ProtoWebToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigSet(ConfigNames.ProtoWeb, !Mind.Db.ConfigGet<bool>(ConfigNames.ProtoWeb));

        Response.SetJsonSuccess();
    }

    [Controller("/api/logs/get100")]
    public async Task LogsGet100()
    {
        await Task.Delay(0);

        if (!IsAuthenticated())
        {
            Response.SetForbidden();

            return;
        }

        var logs = Mind.Db.GetLogItems();

        Response.SetJsonSuccess(logs);
    }

    [Controller("/api/status")]
    public async Task Status()
    {
        await Task.Delay(0);

        if (!IsAuthenticated())
        {
            Response.SetForbidden();

            return;
        }

        var data = new {
            ia = Mind.Db.ConfigGet<bool>(ConfigNames.InternetArchive),
            iayear = Mind.Db.ConfigGet<int>(ConfigNames.InternetArchiveYear),
            protoweb = Mind.Db.ConfigGet<bool>(ConfigNames.ProtoWeb)
        };

        Response.SetBodyString(JsonSerializer.Serialize(data), "application/json");

        Response.Handled = true;
    }

    [Controller("/api/login")]
    public async Task Login()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("username") || !Request.FormData.ContainsKey("password"))
        {
            NotAuthorized();

            return;
        }

        if (string.IsNullOrWhiteSpace(Request.FormData["username"]) || string.IsNullOrWhiteSpace(Request.FormData["password"]))
        {
            NotAuthorized();

            return;
        }

        var username = Request.FormData["username"];
        var password = Request.FormData["password"];

        if (username == "penis" && password == "penis")
        {
            Session.login = username;

            Response.SetFound("/index.html");

            return;
        }

        NotAuthorized();
    }

    [Controller("/api/logout")]
    public async Task Logout()
    {
        await Task.Delay(0);

        if (IsAuthenticated())
        {
            FuckUpAuthorization();
        }

        Response.SetFound("/index.html");
    }

    private void FuckUpAuthorization()
    {
        Response.RemoveSession(LoginSessionKeyName);
    }

    private void NotAuthorized()
    {
        Session.error = "Invalid Credentials Provided";

        Response.SetFound("/login.html");
    }

    public bool IsAuthenticated()
    {
        return Response.HasSession(LoginSessionKeyName);
    }
}
