// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Usenet;

internal class NntpProxy : Listener
{
    private const string EOL = "\r\n";

    private const string CurrentGroup = "currentgroup";
    private const string CurrentArticleNumber = "currentarticle";
    private const string ArticleList = "articlelist";

    private readonly UsenetDataSource _dataSource;

    public NntpProxy(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp)
    {
        _dataSource = new UsenetDataSource();
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = true;

        connection.DataBag[CurrentGroup] = string.Empty;
        connection.DataBag[CurrentArticleNumber] = 0;
        connection.DataBag[ArticleList] = new List<UsenetArticle>();

        return await SendResponse(NntpResponseCode.ServerReadyNoPosting, "VintageHive NNTP Service Ready - posting not allowed");
    }

    public override async Task<byte[]> ProcessRequest(ListenerSocket connection, byte[] data, int read)
    {
        var bag = connection.DataBag;

        var (command, argument) = ParseCommand(data, read);

        switch (command)
        {
            case "MODE":
            {
                return await HandleMode(argument);
            }

            case "LIST":
            {
                return await HandleList(argument);
            }

            case "GROUP":
            {
                return await HandleGroup(bag, argument);
            }

            case "LISTGROUP":
            {
                return await HandleListGroup(bag, argument);
            }

            case "ARTICLE":
            {
                return await HandleArticle(bag, argument);
            }

            case "HEAD":
            {
                return await HandleHead(bag, argument);
            }

            case "BODY":
            {
                return await HandleBody(bag, argument);
            }

            case "STAT":
            {
                return await HandleStat(bag, argument);
            }

            case "XOVER":
            case "OVER":
            {
                return await HandleXover(bag, argument);
            }

            case "NEXT":
            {
                return await HandleNext(bag);
            }

            case "LAST":
            {
                return await HandleLast(bag);
            }

            case "NEWGROUPS":
            {
                return await SendMultilineResponse(NntpResponseCode.ListOfNewGroupsFollows, "New groups follow", "");
            }

            case "NEWNEWS":
            {
                return await SendMultilineResponse(NntpResponseCode.ListOfNewArticlesFollows, "New articles follow", "");
            }

            case "POST":
            {
                return await SendResponse(NntpResponseCode.PostingNotAllowed, "Posting not allowed");
            }

            case "HELP":
            {
                return await HandleHelp();
            }

            case "AUTHINFO":
            {
                return await SendResponse(NntpResponseCode.AuthenticationAccepted, "Authentication accepted");
            }

            case "QUIT":
            {
                connection.IsKeepAlive = false;

                return await SendResponse(NntpResponseCode.QuitGoodbye, "Goodbye");
            }

            case "CAPABILITIES":
            {
                return await HandleCapabilities();
            }

            default:
            {
                return await SendResponse(NntpResponseCode.CommandNotRecognized, "Command not recognized");
            }
        }
    }

    private async Task<byte[]> HandleMode(string argument)
    {
        if (argument.Equals("READER", StringComparison.OrdinalIgnoreCase))
        {
            return await SendResponse(NntpResponseCode.ServerReadyNoPosting, "VintageHive NNTP Service Ready - posting not allowed");
        }

        return await SendResponse(NntpResponseCode.CommandNotRecognized, "Unknown MODE variant");
    }

    private async Task<byte[]> HandleList(string argument)
    {
        var groups = await _dataSource.GetGroupsAsync();

        var body = new StringBuilder();

        foreach (var group in groups)
        {
            body.Append($"{group.Name} {group.LastArticle} {group.FirstArticle} {group.PostingStatus}{EOL}");
        }

        return await SendMultilineResponse(NntpResponseCode.ListOfNewsgroupsFollows, "List of newsgroups follows", body.ToString());
    }

    private async Task<byte[]> HandleGroup(Dictionary<string, object> bag, string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            return await SendResponse(NntpResponseCode.SyntaxError, "No group specified");
        }

        var group = await _dataSource.GetGroupAsync(groupName);

        if (group == null)
        {
            return await SendResponse(NntpResponseCode.NoSuchGroup, "No such group");
        }

        bag[CurrentGroup] = group.Name;
        bag[CurrentArticleNumber] = group.FirstArticle;

        var articles = await _dataSource.GetArticlesAsync(group.Name, group.FirstArticle, group.LastArticle);

        bag[ArticleList] = articles;

        return await SendResponse(NntpResponseCode.GroupSelected, $"{group.ArticleCount} {group.FirstArticle} {group.LastArticle} {group.Name}");
    }

    private async Task<byte[]> HandleListGroup(Dictionary<string, object> bag, string argument)
    {
        var groupName = bag[CurrentGroup].ToString();

        if (!string.IsNullOrEmpty(argument))
        {
            var parts = argument.Split(' ', 2);

            groupName = parts[0];
        }

        if (string.IsNullOrEmpty(groupName))
        {
            return await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected");
        }

        var group = await _dataSource.GetGroupAsync(groupName);

        if (group == null)
        {
            return await SendResponse(NntpResponseCode.NoSuchGroup, "No such group");
        }

        bag[CurrentGroup] = group.Name;
        bag[CurrentArticleNumber] = group.FirstArticle;

        var articles = await _dataSource.GetArticlesAsync(group.Name, group.FirstArticle, group.LastArticle);

        bag[ArticleList] = articles;

        var body = new StringBuilder();

        foreach (var article in articles)
        {
            body.Append($"{article.Number}{EOL}");
        }

        return await SendMultilineResponse(NntpResponseCode.GroupSelected, $"{group.ArticleCount} {group.FirstArticle} {group.LastArticle} {group.Name}", body.ToString());
    }

    private async Task<byte[]> HandleArticle(Dictionary<string, object> bag, string argument)
    {
        var (article, errorResponse) = await ResolveArticle(bag, argument);

        if (article == null)
        {
            return errorResponse;
        }

        bag[CurrentArticleNumber] = article.Number;

        var headers = FormatHeaders(article);

        var body = new StringBuilder();

        body.Append(headers);
        body.Append(EOL);
        body.Append(DotStuff(article.Body));
        body.Append(EOL);

        return await SendMultilineResponse(NntpResponseCode.ArticleFollows, $"{article.Number} {article.MessageId}", body.ToString());
    }

    private async Task<byte[]> HandleHead(Dictionary<string, object> bag, string argument)
    {
        var (article, errorResponse) = await ResolveArticle(bag, argument);

        if (article == null)
        {
            return errorResponse;
        }

        bag[CurrentArticleNumber] = article.Number;

        return await SendMultilineResponse(NntpResponseCode.HeadFollows, $"{article.Number} {article.MessageId}", FormatHeaders(article));
    }

    private async Task<byte[]> HandleBody(Dictionary<string, object> bag, string argument)
    {
        var (article, errorResponse) = await ResolveArticle(bag, argument);

        if (article == null)
        {
            return errorResponse;
        }

        bag[CurrentArticleNumber] = article.Number;

        return await SendMultilineResponse(NntpResponseCode.BodyFollows, $"{article.Number} {article.MessageId}", DotStuff(article.Body) + EOL);
    }

    private async Task<byte[]> HandleStat(Dictionary<string, object> bag, string argument)
    {
        var (article, errorResponse) = await ResolveArticle(bag, argument);

        if (article == null)
        {
            return errorResponse;
        }

        bag[CurrentArticleNumber] = article.Number;

        return await SendResponse(NntpResponseCode.ArticleExists, $"{article.Number} {article.MessageId}");
    }

    private async Task<byte[]> HandleXover(Dictionary<string, object> bag, string argument)
    {
        var groupName = bag[CurrentGroup].ToString();

        if (string.IsNullOrEmpty(groupName))
        {
            return await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected");
        }

        int first;
        int last;

        if (string.IsNullOrEmpty(argument))
        {
            var currentArticle = (int)bag[CurrentArticleNumber];

            first = currentArticle;
            last = currentArticle;
        }
        else if (argument.Contains('-'))
        {
            var parts = argument.Split('-', 2);

            first = int.TryParse(parts[0], out var f) ? f : 1;

            if (string.IsNullOrEmpty(parts[1]))
            {
                var group = await _dataSource.GetGroupAsync(groupName);

                last = group?.LastArticle ?? first;
            }
            else
            {
                last = int.TryParse(parts[1], out var l) ? l : first;
            }
        }
        else
        {
            first = int.TryParse(argument, out var n) ? n : 1;
            last = first;
        }

        var articles = await _dataSource.GetArticlesAsync(groupName, first, last);

        var body = new StringBuilder();

        foreach (var article in articles)
        {
            body.Append($"{article.Number}\t{article.Subject}\t{article.From}\t{article.Date}\t{article.MessageId}\t{article.References}\t{article.Bytes}\t{article.Lines}{EOL}");
        }

        return await SendMultilineResponse(NntpResponseCode.OverviewFollows, "Overview information follows", body.ToString());
    }

    private async Task<byte[]> HandleNext(Dictionary<string, object> bag)
    {
        var groupName = bag[CurrentGroup].ToString();

        if (string.IsNullOrEmpty(groupName))
        {
            return await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected");
        }

        var currentArticle = (int)bag[CurrentArticleNumber];

        if (currentArticle == 0)
        {
            return await SendResponse(NntpResponseCode.NoCurrentArticle, "No current article selected");
        }

        var articles = bag[ArticleList] as List<UsenetArticle>;

        var next = articles?.FirstOrDefault(a => a.Number > currentArticle);

        if (next == null)
        {
            return await SendResponse(NntpResponseCode.NoNextArticle, "No next article");
        }

        bag[CurrentArticleNumber] = next.Number;

        return await SendResponse(NntpResponseCode.ArticleExists, $"{next.Number} {next.MessageId}");
    }

    private async Task<byte[]> HandleLast(Dictionary<string, object> bag)
    {
        var groupName = bag[CurrentGroup].ToString();

        if (string.IsNullOrEmpty(groupName))
        {
            return await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected");
        }

        var currentArticle = (int)bag[CurrentArticleNumber];

        if (currentArticle == 0)
        {
            return await SendResponse(NntpResponseCode.NoCurrentArticle, "No current article selected");
        }

        var articles = bag[ArticleList] as List<UsenetArticle>;

        var prev = articles?.LastOrDefault(a => a.Number < currentArticle);

        if (prev == null)
        {
            return await SendResponse(NntpResponseCode.NoPreviousArticle, "No previous article");
        }

        bag[CurrentArticleNumber] = prev.Number;

        return await SendResponse(NntpResponseCode.ArticleExists, $"{prev.Number} {prev.MessageId}");
    }

    private async Task<byte[]> HandleHelp()
    {
        var helpText = new StringBuilder();

        helpText.Append($"VintageHive NNTP Service{EOL}");
        helpText.Append($"Available commands:{EOL}");
        helpText.Append($"  ARTICLE [number|<message-id>]  Retrieve an article{EOL}");
        helpText.Append($"  BODY [number|<message-id>]     Retrieve article body{EOL}");
        helpText.Append($"  GROUP newsgroup                Select a newsgroup{EOL}");
        helpText.Append($"  HEAD [number|<message-id>]     Retrieve article headers{EOL}");
        helpText.Append($"  HELP                           This help text{EOL}");
        helpText.Append($"  LAST                           Go to previous article{EOL}");
        helpText.Append($"  LIST                           List newsgroups{EOL}");
        helpText.Append($"  LISTGROUP [newsgroup]          List article numbers{EOL}");
        helpText.Append($"  MODE READER                    Set reader mode{EOL}");
        helpText.Append($"  NEWGROUPS                      List new groups{EOL}");
        helpText.Append($"  NEWNEWS                        List new articles{EOL}");
        helpText.Append($"  NEXT                           Go to next article{EOL}");
        helpText.Append($"  OVER/XOVER [range]             Overview data{EOL}");
        helpText.Append($"  QUIT                           Disconnect{EOL}");
        helpText.Append($"  STAT [number|<message-id>]     Check article exists{EOL}");

        return await SendMultilineResponse(NntpResponseCode.HelpTextFollows, "Help text follows", helpText.ToString());
    }

    private async Task<byte[]> HandleCapabilities()
    {
        var body = new StringBuilder();

        body.Append($"VERSION 2{EOL}");
        body.Append($"READER{EOL}");
        body.Append($"LIST ACTIVE{EOL}");
        body.Append($"OVER{EOL}");

        return await SendMultilineResponse(NntpResponseCode.HelpTextFollows, "Capabilities list", body.ToString());
    }

    private async Task<(UsenetArticle Article, byte[] ErrorResponse)> ResolveArticle(Dictionary<string, object> bag, string argument)
    {
        var groupName = bag[CurrentGroup].ToString();

        if (string.IsNullOrEmpty(argument))
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return (null, await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected"));
            }

            var currentNum = (int)bag[CurrentArticleNumber];

            if (currentNum == 0)
            {
                return (null, await SendResponse(NntpResponseCode.NoCurrentArticle, "No current article selected"));
            }

            var article = await _dataSource.GetArticleAsync(groupName, currentNum);

            if (article == null)
            {
                return (null, await SendResponse(NntpResponseCode.NoSuchArticleNumber, "No such article"));
            }

            return (article, null);
        }

        if (argument.StartsWith('<') && argument.EndsWith('>'))
        {
            var article = await _dataSource.GetArticleByIdAsync(argument);

            if (article == null)
            {
                return (null, await SendResponse(NntpResponseCode.NoSuchArticleId, "No article with that message-id"));
            }

            return (article, null);
        }

        if (int.TryParse(argument, out var articleNum))
        {
            if (string.IsNullOrEmpty(groupName))
            {
                return (null, await SendResponse(NntpResponseCode.NoGroupSelected, "No group selected"));
            }

            var article = await _dataSource.GetArticleAsync(groupName, articleNum);

            if (article == null)
            {
                return (null, await SendResponse(NntpResponseCode.NoSuchArticleNumber, "No such article"));
            }

            return (article, null);
        }

        return (null, await SendResponse(NntpResponseCode.SyntaxError, "Invalid argument"));
    }

    internal static string FormatHeaders(UsenetArticle article)
    {
        var sb = new StringBuilder();

        sb.Append($"From: {article.From}{EOL}");
        sb.Append($"Subject: {article.Subject}{EOL}");
        sb.Append($"Date: {article.Date}{EOL}");
        sb.Append($"Message-ID: {article.MessageId}{EOL}");
        sb.Append($"Newsgroups: {article.Newsgroups}{EOL}");

        if (!string.IsNullOrEmpty(article.References))
        {
            sb.Append($"References: {article.References}{EOL}");
        }

        sb.Append($"Bytes: {article.Bytes}{EOL}");
        sb.Append($"Lines: {article.Lines}{EOL}");

        return sb.ToString();
    }

    internal static string DotStuff(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "";
        }

        return body.Replace("\r\n.", "\r\n..");
    }

    private async Task<byte[]> SendResponse(NntpResponseCode code, string message)
    {
        await Task.Delay(0);

        return $"{(int)code} {message}{EOL}".ToASCII();
    }

    private async Task<byte[]> SendMultilineResponse(NntpResponseCode code, string message, string body)
    {
        await Task.Delay(0);

        var sb = new StringBuilder();

        sb.Append($"{(int)code} {message}{EOL}");
        sb.Append(body);
        sb.Append($".{EOL}");

        return sb.ToString().ToASCII();
    }

    internal static (string Command, string Argument) ParseCommand(ReadOnlySpan<byte> data, int read)
    {
        var rawData = data[..read].ToASCII().Split(" ", 2);

        var cmd = rawData[0].Trim().ToUpperInvariant();
        var arg = rawData.Length == 2 ? rawData[1].Trim() : string.Empty;

        return (cmd, arg);
    }
}
