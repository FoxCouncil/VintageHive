﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using Fluid;
using System.Text.RegularExpressions;

namespace VintageHive.Processors.LocalServer.Controllers;

[Domain("admin.hive.com")]
internal partial class AdminController : Controller
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

    [Route("/index.html")]
    public async Task Index()
    {
        await Task.Delay(0);

        Response.Context.SetValue("ia_years", InternetArchiveProcessor.ValidYears);
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
        }
        
        Mind.Db.ConfigSet(ConfigNames.ServiceInternetArchiveYear, Request.FormData["year"]);

        Response.SetJsonSuccess();
    }

    [Route("/api/protowebtoggle")]
    public async Task ProtoWebToggle()
    {
        await Task.Delay(0);

        Mind.Db.ConfigSet(ConfigNames.ServiceProtoWeb, !Mind.Db.ConfigGet<bool>(ConfigNames.ServiceProtoWeb));

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

        var data = new {
            ia = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceInternetArchive),
            iayear = Mind.Db.ConfigGet<int>(ConfigNames.ServiceInternetArchiveYear),
            protoweb = Mind.Db.ConfigGet<bool>(ConfigNames.ServiceProtoWeb)
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

        if (username == "penis" && password == "penis")
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
