// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Text.RegularExpressions;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain(HiveDomains.Admin)]
internal partial class AdminController : Controller
{
    private const string LoginSessionKeyName = "login";

    private readonly string[] AllowListedEndpoints = new string[] { "/login.html", "/css/", "/js/", "/img/", "/api/login" };

    // Services the admin dashboard is allowed to toggle, mapped to their config keys.
    // Listener-backed services (everything except intranet) apply on the next restart;
    // intranet is checked per-request and applies immediately.
    private static readonly IReadOnlyDictionary<string, string> ToggleableServices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "intranet", ConfigNames.ServiceIntranet },
        { "smtp", ConfigNames.ServiceSmtp },
        { "pop3", ConfigNames.ServicePop3 },
        { "imap", ConfigNames.ServiceImap },
        { "irc", ConfigNames.ServiceIrc },
        { "usenet", ConfigNames.ServiceUsenet },
        { "dns", ConfigNames.ServiceDns },
        { "printer", ConfigNames.ServicePrinter },
        { "ils", ConfigNames.ServiceIls },
        { "ras", ConfigNames.ServiceRas },
        { "h323", ConfigNames.ServiceH323 },
        { "t120", ConfigNames.ServiceT120 },
        { "finger", ConfigNames.ServiceFinger },
    };

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

    [Route("/index.html")]
    public async Task Index()
    {
        await Task.Delay(0);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
        Response.Context.SetValue("ia_worker_url", Mind.Db.ConfigGet<string>(ConfigNames.ServiceInternetArchiveWorkerUrl));
    }

    [Route("/users.html")]
    public async Task Users()
    {
        await Task.Delay(0);

        Response.Context.SetValue("users", Mind.Db.UserList());
    }

    [Route("/api/usercreate")]
    public async Task UserCreate()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("username") || !Request.FormData.ContainsKey("password"))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(Request.FormData["username"]) || string.IsNullOrWhiteSpace(Request.FormData["password"]))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        var username = Request.FormData["username"];
        var password = Request.FormData["password"];

        // Min = 3, Max = 8
        // Hey man, it was the 90's! Shit was wild back then...
        // I don't make the rulzes <3
        if (username.Length > 8 || password.Length > 8 || username.Length < 3 || password.Length < 3) 
        {
            Response.SetJsonSuccess(false);
            return;
        }

        if (!UserRegexPattern().IsMatch(username) || !UserRegexPattern().IsMatch(password))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Response.SetJsonSuccess(Mind.Db.UserCreate(username, password));
    }

    [Route("/api/userdelete")]
    public async Task UserDelete()
    {
        await Task.Delay(0);

        var username = Request.Body;

        if (string.IsNullOrWhiteSpace(username))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        if (username.Length > 8 || username.Length < 3)
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Response.SetJsonSuccess(Mind.Db.UserDelete(username));
    }

    [Route("/api/cachegetcounts")]
    public async Task CacheGetCounts()
    {
        await Task.Delay(0);

        var list = Mind.Cache.GetCounters();

        Response.SetJsonSuccess(list);
    }

    [Route("/api/cacheclearall")]
    public async Task CacheClearAll()
    {
        await Task.Delay(0);

        Mind.Cache.Clear();

        Response.SetJsonSuccess();
    }

    [Route("/api/getdownloadlocations")]
    public async Task GetDownloadLocations()
    {
        await Task.Delay(0);

        var list = RepoUtils.Get();

        Response.SetJsonSuccess(list);
    }

    [Route("/api/repoadd")]
    public async Task RepoAdd()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("shortname") || !Request.FormData.ContainsKey("name") || !Request.FormData.ContainsKey("path"))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Response.SetJsonSuccess(RepoUtils.Add(Request.FormData["shortname"], Request.FormData["name"], Request.FormData["path"]));
    }

    [Route("/api/repodelete")]
    public async Task RepoDelete()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("shortname"))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Response.SetJsonSuccess(RepoUtils.Remove(Request.FormData["shortname"]));
    }

    [Route("/api/linksgetall")]
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

    [Route("/api/iatoggle")]
    public async Task InternetArchiveToggle()
    {
        await Task.Delay(0);
        
        Mind.Db.ConfigSet(ConfigNames.ServiceInternetArchive, !Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchive));
        
        Response.SetJsonSuccess();
    }

    [Route("/api/iasetyear")]
    public async Task InternetArchiveSetYear()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("year"))
        {
            Response.SetJsonSuccess(false);

            return;
        }

        if (!int.TryParse(Request.FormData["year"], out var year) || !InternetArchiveProcessor.ValidYears.Contains(year))
        {
            Response.SetJsonSuccess(false);

            return;
        }

        Mind.Db.ConfigSet(ConfigNames.ServiceInternetArchiveYear, year);

        Response.SetJsonSuccess();
    }

    [Route("/api/iaworkertoggle")]
    public async Task InternetArchiveWorkerToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigSet(ConfigNames.ServiceInternetArchiveWorker, !Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchiveWorker));

        Response.SetJsonSuccess();
    }

    [Route("/api/iasetworkerurl")]
    public async Task InternetArchiveSetWorkerUrl()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("url"))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Mind.Db.ConfigSet(ConfigNames.ServiceInternetArchiveWorkerUrl, Request.FormData["url"]);

        Response.SetJsonSuccess();
    }

    [Route("/api/protowebtoggle")]
    public async Task ProtoWebToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigSet(ConfigNames.ServiceProtoWeb, !Mind.Db.ConfigGet<bool>(ConfigNames.ServiceProtoWeb));

        Response.SetJsonSuccess();
    }

    [Route("/api/servicetoggle")]
    public async Task ServiceToggle()
    {
        await Task.Delay(0);

        if (!Request.FormData.ContainsKey("service"))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        var key = Request.FormData["service"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(key) || !ToggleableServices.TryGetValue(key, out var configName))
        {
            Response.SetJsonSuccess(false);
            return;
        }

        Mind.Db.ConfigSet(configName, !Mind.Db.ConfigGet<bool>(configName));

        Response.SetJsonSuccess();
    }

    [Route("/api/logs/get100")]
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

    [Route("/api/status")]
    public async Task Status()
    {
        await Task.Delay(0);

        if (!IsAuthenticated())
        {
            Response.SetForbidden();

            return;
        }

        var ircChannels = Mind.IrcServer?.GetChannelStats()?.Select(c => new { name = c.Name, members = c.MemberCount, topic = c.Topic }).ToList();

        var data = new {
            ia = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchive),
            iayear = Mind.Db.ConfigGet<int>(ConfigNames.ServiceInternetArchiveYear),
            iaworker = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchiveWorker),
            iaworkerurl = Mind.Db.ConfigGet<string>(ConfigNames.ServiceInternetArchiveWorkerUrl),
            protoweb = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceProtoWeb),
            services = ToggleableServices.ToDictionary(kv => kv.Key, kv => Mind.Db.ConfigGet<bool>(kv.Value)),
            irc = new {
                users = Mind.IrcServer?.UserCount ?? 0,
                channels = Mind.IrcServer?.ChannelCount ?? 0,
                channelList = ircChannels
            }
        };

        Response.SetBodyString(JsonSerializer.Serialize(data), "application/json");

        Response.Handled = true;
    }

    [Route("/api/login")]
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

        // Credentials come from config (defaults admin/vintagehive) instead of being hardcoded in source; change
        // them from the admin panel. The control plane is otherwise reachable via the loopback-URI rewrite.
        var adminUsername = Mind.Db.ConfigGet<string>(ConfigNames.AdminUsername);
        var adminPassword = Mind.Db.ConfigGet<string>(ConfigNames.AdminPassword);

        if (!string.IsNullOrEmpty(adminUsername) && username == adminUsername && password == adminPassword)
        {
            Session.login = username;

            Response.SetFound("/index.html");

            return;
        }

        NotAuthorized();
    }

    [Route("/api/logout")]
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

    [GeneratedRegex("[a-zA-Z0-9]+")]
    private static partial Regex UserRegexPattern();
}
