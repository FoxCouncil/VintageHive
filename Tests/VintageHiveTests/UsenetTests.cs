// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VintageHive.Proxy.Usenet;

namespace Usenet;

[TestClass]
public class UsenetTests
{
    private static List<BundledGroup> _groups;
    private static Dictionary<string, List<UsenetArticle>> _articles;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _groups = LoadEmbeddedJson<List<BundledGroup>>("TestData.usenet.groups.json");
        _articles = LoadArticles();
    }

    #region Bundled Data Integrity Tests

    [TestMethod]
    public void BundledData_Groups_ShouldLoadGroups()
    {
        Assert.IsTrue(_groups.Count >= 2, $"Expected at least 2 groups, got {_groups.Count}");
    }

    [TestMethod]
    public void BundledData_Groups_AllHaveNonEmptyNames()
    {
        foreach (var group in _groups)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(group.Name), $"Group has empty name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(group.Description), $"Group '{group.Name}' has empty description");
        }
    }

    [TestMethod]
    public void BundledData_Groups_NamesAreUnique()
    {
        var names = _groups.Select(g => g.Name).ToList();
        var distinct = names.Distinct().ToList();

        Assert.AreEqual(names.Count, distinct.Count, "Duplicate group names found");
    }

    [TestMethod]
    public void BundledData_Groups_FollowHierarchyNaming()
    {
        foreach (var group in _groups)
        {
            Assert.IsTrue(group.Name.Contains('.'), $"Group '{group.Name}' doesn't follow dotted hierarchy naming");

            var hierarchy = group.Name.Split('.')[0];
            var validHierarchies = new[] { "comp", "rec", "alt", "sci", "news", "misc", "soc", "talk" };

            Assert.IsTrue(validHierarchies.Contains(hierarchy), $"Group '{group.Name}' has unexpected hierarchy '{hierarchy}'");
        }
    }

    [TestMethod]
    public void BundledData_Groups_CoverMajorHierarchies()
    {
        var hierarchies = _groups.Select(g => g.Name.Split('.')[0]).Distinct().OrderBy(h => h).ToList();

        var big8 = new[] { "comp", "rec", "alt", "sci", "news", "misc", "soc" };
        var covered = big8.Count(h => hierarchies.Contains(h));

        Assert.IsTrue(covered >= 5, $"Expected at least 5 of 7 major hierarchies, got {covered}: {string.Join(", ", hierarchies)}");
    }

    [TestMethod]
    public void BundledData_Groups_IncludesHiveHelp()
    {
        Assert.IsTrue(_groups.Any(g => g.Name == "alt.hive.help"), "Missing alt.hive.help group");
    }

    [TestMethod]
    public void BundledData_Articles_AllGroupsHaveArticles()
    {
        foreach (var group in _groups)
        {
            Assert.IsTrue(_articles.ContainsKey(group.Name), $"No articles for group '{group.Name}'");
            Assert.IsTrue(_articles[group.Name].Count > 0, $"Empty article list for group '{group.Name}'");
        }
    }

    [TestMethod]
    public void BundledData_Articles_AllHaveRequiredFields()
    {
        foreach (var (groupName, articles) in _articles)
        {
            foreach (var article in articles)
            {
                Assert.IsTrue(article.Number > 0, $"Article in '{groupName}' has invalid number {article.Number}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(article.MessageId), $"Article {article.Number} in '{groupName}' has empty MessageId");
                Assert.IsFalse(string.IsNullOrWhiteSpace(article.From), $"Article {article.Number} in '{groupName}' has empty From");
                Assert.IsFalse(string.IsNullOrWhiteSpace(article.Subject), $"Article {article.Number} in '{groupName}' has empty Subject");
                Assert.IsFalse(string.IsNullOrWhiteSpace(article.Date), $"Article {article.Number} in '{groupName}' has empty Date");
                Assert.IsFalse(string.IsNullOrWhiteSpace(article.Body), $"Article {article.Number} in '{groupName}' has empty Body");
            }
        }
    }

    [TestMethod]
    public void BundledData_Articles_MessageIdsAreGloballyUnique()
    {
        var allIds = _articles.Values.SelectMany(a => a).Select(a => a.MessageId).ToList();
        var distinct = allIds.Distinct().ToList();

        Assert.AreEqual(allIds.Count, distinct.Count, "Duplicate message IDs found across groups");
    }

    [TestMethod]
    public void BundledData_Articles_MessageIdsFollowAngleBracketFormat()
    {
        foreach (var (groupName, articles) in _articles)
        {
            foreach (var article in articles)
            {
                Assert.IsTrue(article.MessageId.StartsWith('<'), $"MessageId '{article.MessageId}' in '{groupName}' doesn't start with '<'");
                Assert.IsTrue(article.MessageId.EndsWith('>'), $"MessageId '{article.MessageId}' in '{groupName}' doesn't end with '>'");
                Assert.IsTrue(article.MessageId.Contains('@'), $"MessageId '{article.MessageId}' in '{groupName}' doesn't contain '@'");
            }
        }
    }

    [TestMethod]
    public void BundledData_Articles_NumbersAreSequentialPerGroup()
    {
        foreach (var (groupName, articles) in _articles)
        {
            var sorted = articles.OrderBy(a => a.Number).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                Assert.AreEqual(i + 1, sorted[i].Number, $"Article numbers not sequential in '{groupName}': expected {i + 1}, got {sorted[i].Number}");
            }
        }
    }

    [TestMethod]
    public void BundledData_Articles_ReferencesPointToValidMessageIds()
    {
        var allIds = new HashSet<string>(_articles.Values.SelectMany(a => a).Select(a => a.MessageId));

        foreach (var (groupName, articles) in _articles)
        {
            foreach (var article in articles)
            {
                if (!string.IsNullOrEmpty(article.References))
                {
                    var refs = article.References.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var refId in refs)
                    {
                        if (refId.StartsWith('<') && refId.EndsWith('>'))
                        {
                            Assert.IsTrue(allIds.Contains(refId), $"Article {article.Number} in '{groupName}' references unknown MessageId '{refId}'");
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void BundledData_Articles_DateStringsAreParseable()
    {
        foreach (var (groupName, articles) in _articles)
        {
            foreach (var article in articles)
            {
                var parseable = DateTimeOffset.TryParse(article.Date, out _);

                Assert.IsTrue(parseable, $"Article {article.Number} in '{groupName}' has unparseable date '{article.Date}'");
            }
        }
    }

    [TestMethod]
    public void BundledData_Articles_TotalCountIsReasonable()
    {
        var totalArticles = _articles.Values.SelectMany(a => a).Count();

        Assert.IsTrue(totalArticles >= 7, $"Expected at least 7 total articles, got {totalArticles}");
    }

    [TestMethod]
    public void BundledData_HiveHelp_HasExpectedArticles()
    {
        Assert.IsTrue(_articles.ContainsKey("alt.hive.help"), "Missing alt.hive.help articles");

        var helpArticles = _articles["alt.hive.help"];

        Assert.IsTrue(helpArticles.Count >= 5, $"Expected at least 5 help articles, got {helpArticles.Count}");

        var subjects = helpArticles.Select(a => a.Subject).ToList();

        Assert.IsTrue(subjects.Any(s => s.Contains("Welcome", StringComparison.OrdinalIgnoreCase)), "Missing Welcome article");
    }

    #endregion

    #region NntpResponseCode Tests

    [TestMethod]
    public void ResponseCode_GreetingValues()
    {
        Assert.AreEqual(200, (int)NntpResponseCode.ServerReadyPostingAllowed);
        Assert.AreEqual(201, (int)NntpResponseCode.ServerReadyNoPosting);
        Assert.AreEqual(205, (int)NntpResponseCode.QuitGoodbye);
    }

    [TestMethod]
    public void ResponseCode_SelectionValues()
    {
        Assert.AreEqual(211, (int)NntpResponseCode.GroupSelected);
        Assert.AreEqual(215, (int)NntpResponseCode.ListOfNewsgroupsFollows);
    }

    [TestMethod]
    public void ResponseCode_ArticleRetrievalValues()
    {
        Assert.AreEqual(220, (int)NntpResponseCode.ArticleFollows);
        Assert.AreEqual(221, (int)NntpResponseCode.HeadFollows);
        Assert.AreEqual(222, (int)NntpResponseCode.BodyFollows);
        Assert.AreEqual(223, (int)NntpResponseCode.ArticleExists);
        Assert.AreEqual(224, (int)NntpResponseCode.OverviewFollows);
    }

    [TestMethod]
    public void ResponseCode_ErrorValues()
    {
        Assert.AreEqual(411, (int)NntpResponseCode.NoSuchGroup);
        Assert.AreEqual(412, (int)NntpResponseCode.NoGroupSelected);
        Assert.AreEqual(420, (int)NntpResponseCode.NoCurrentArticle);
        Assert.AreEqual(423, (int)NntpResponseCode.NoSuchArticleNumber);
        Assert.AreEqual(430, (int)NntpResponseCode.NoSuchArticleId);
        Assert.AreEqual(440, (int)NntpResponseCode.PostingNotAllowed);
        Assert.AreEqual(500, (int)NntpResponseCode.CommandNotRecognized);
        Assert.AreEqual(501, (int)NntpResponseCode.SyntaxError);
    }

    [TestMethod]
    public void ResponseCode_AuthValues()
    {
        Assert.AreEqual(281, (int)NntpResponseCode.AuthenticationAccepted);
        Assert.AreEqual(502, (int)NntpResponseCode.AccessDenied);
    }

    #endregion

    #region Protocol Formatting Tests

    [TestMethod]
    public void FormatHeaders_ShouldContainAllRequiredHeaders()
    {
        var article = MakeTestArticle();

        var headers = NntpProxy.FormatHeaders(article);

        Assert.IsTrue(headers.Contains("From: testuser@test.com (Test User)\r\n"));
        Assert.IsTrue(headers.Contains("Subject: Test Subject\r\n"));
        Assert.IsTrue(headers.Contains("Date: Fri, 15 Jan 1999 08:30:12 -0500\r\n"));
        Assert.IsTrue(headers.Contains("Message-ID: <test@test.com>\r\n"));
        Assert.IsTrue(headers.Contains("Newsgroups: comp.test\r\n"));
        Assert.IsTrue(headers.Contains("Bytes: 42\r\n"));
        Assert.IsTrue(headers.Contains("Lines: 3\r\n"));
    }

    [TestMethod]
    public void FormatHeaders_ShouldIncludeReferencesWhenPresent()
    {
        var article = MakeTestArticle();
        article.References = "<parent@test.com>";

        var headers = NntpProxy.FormatHeaders(article);

        Assert.IsTrue(headers.Contains("References: <parent@test.com>\r\n"));
    }

    [TestMethod]
    public void FormatHeaders_ShouldOmitReferencesWhenEmpty()
    {
        var article = MakeTestArticle();
        article.References = "";

        var headers = NntpProxy.FormatHeaders(article);

        Assert.IsFalse(headers.Contains("References:"));
    }

    [TestMethod]
    public void FormatHeaders_AllLinesEndWithCRLF()
    {
        var article = MakeTestArticle();

        var headers = NntpProxy.FormatHeaders(article);
        var lines = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.IsTrue(lines.Length >= 7, $"Expected at least 7 header lines, got {lines.Length}");

        Assert.IsFalse(headers.Replace("\r\n", "").Contains('\n'), "Found bare LF without CR");
    }

    [TestMethod]
    public void DotStuff_ShouldEscapeLeadingDots()
    {
        var input = "Line one\r\n.Line starting with dot\r\nLine three";
        var result = NntpProxy.DotStuff(input);

        Assert.AreEqual("Line one\r\n..Line starting with dot\r\nLine three", result);
    }

    [TestMethod]
    public void DotStuff_ShouldHandleEmptyString()
    {
        Assert.AreEqual("", NntpProxy.DotStuff(""));
    }

    [TestMethod]
    public void DotStuff_ShouldHandleNull()
    {
        Assert.AreEqual("", NntpProxy.DotStuff(null));
    }

    [TestMethod]
    public void DotStuff_ShouldNotEscapeDotsInMiddleOfLine()
    {
        var input = "This has a . in the middle";
        var result = NntpProxy.DotStuff(input);

        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void DotStuff_ShouldHandleMultipleConsecutiveDotLines()
    {
        var input = "Normal\r\n.first dot\r\n.second dot\r\nNormal again";
        var result = NntpProxy.DotStuff(input);

        Assert.AreEqual("Normal\r\n..first dot\r\n..second dot\r\nNormal again", result);
    }

    #endregion

    #region Command Parsing Tests

    [TestMethod]
    public void ParseCommand_SimpleCommand()
    {
        var data = Encoding.ASCII.GetBytes("QUIT\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("QUIT", command);
        Assert.AreEqual("", argument);
    }

    [TestMethod]
    public void ParseCommand_CommandWithArgument()
    {
        var data = Encoding.ASCII.GetBytes("GROUP comp.sys.ibm.pc.hardware\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("GROUP", command);
        Assert.AreEqual("comp.sys.ibm.pc.hardware", argument);
    }

    [TestMethod]
    public void ParseCommand_CaseInsensitive()
    {
        var data = Encoding.ASCII.GetBytes("group comp.test\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("GROUP", command);
    }

    [TestMethod]
    public void ParseCommand_ModeReader()
    {
        var data = Encoding.ASCII.GetBytes("MODE READER\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("MODE", command);
        Assert.AreEqual("READER", argument);
    }

    [TestMethod]
    public void ParseCommand_XoverWithRange()
    {
        var data = Encoding.ASCII.GetBytes("XOVER 1-10\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("XOVER", command);
        Assert.AreEqual("1-10", argument);
    }

    [TestMethod]
    public void ParseCommand_ArticleByMessageId()
    {
        var data = Encoding.ASCII.GetBytes("ARTICLE <test@example.com>\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("ARTICLE", command);
        Assert.AreEqual("<test@example.com>", argument);
    }

    [TestMethod]
    public void ParseCommand_ArticleByNumber()
    {
        var data = Encoding.ASCII.GetBytes("ARTICLE 42\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("ARTICLE", command);
        Assert.AreEqual("42", argument);
    }

    [TestMethod]
    public void ParseCommand_AuthInfoWithMultipleSpaces()
    {
        var data = Encoding.ASCII.GetBytes("AUTHINFO USER testuser\r\n");
        var (command, argument) = NntpProxy.ParseCommand(data, data.Length);

        Assert.AreEqual("AUTHINFO", command);
        Assert.AreEqual("USER testuser", argument);
    }

    #endregion

    #region UsenetGroup Model Tests

    [TestMethod]
    public void UsenetGroup_DefaultPostingStatusIsN()
    {
        var group = new UsenetGroup();

        Assert.AreEqual("n", group.PostingStatus);
    }

    [TestMethod]
    public void UsenetGroup_PropertiesRoundTrip()
    {
        var group = new UsenetGroup
        {
            Name = "comp.test",
            FirstArticle = 1,
            LastArticle = 50,
            ArticleCount = 50,
            PostingStatus = "n"
        };

        Assert.AreEqual("comp.test", group.Name);
        Assert.AreEqual(1, group.FirstArticle);
        Assert.AreEqual(50, group.LastArticle);
        Assert.AreEqual(50, group.ArticleCount);
    }

    #endregion

    #region UsenetArticle Model Tests

    [TestMethod]
    public void UsenetArticle_DefaultReferencesIsEmpty()
    {
        var article = new UsenetArticle();

        Assert.AreEqual("", article.References);
    }

    [TestMethod]
    public void UsenetArticle_PropertiesRoundTrip()
    {
        var article = MakeTestArticle();

        Assert.AreEqual(1, article.Number);
        Assert.AreEqual("<test@test.com>", article.MessageId);
        Assert.AreEqual("testuser@test.com (Test User)", article.From);
        Assert.AreEqual("Test Subject", article.Subject);
        Assert.AreEqual("comp.test", article.Newsgroups);
        Assert.AreEqual(42, article.Bytes);
        Assert.AreEqual(3, article.Lines);
    }

    #endregion

    #region XOVER Format Tests

    [TestMethod]
    public void XoverLine_ShouldBeTabSeparated()
    {
        var article = MakeTestArticle();

        var xoverLine = $"{article.Number}\t{article.Subject}\t{article.From}\t{article.Date}\t{article.MessageId}\t{article.References}\t{article.Bytes}\t{article.Lines}";
        var fields = xoverLine.Split('\t');

        Assert.AreEqual(8, fields.Length, "XOVER line should have exactly 8 tab-separated fields");
        Assert.AreEqual("1", fields[0]);
        Assert.AreEqual("Test Subject", fields[1]);
        Assert.AreEqual("testuser@test.com (Test User)", fields[2]);
        Assert.AreEqual("Fri, 15 Jan 1999 08:30:12 -0500", fields[3]);
        Assert.AreEqual("<test@test.com>", fields[4]);
        Assert.AreEqual("", fields[5]);
        Assert.AreEqual("42", fields[6]);
        Assert.AreEqual("3", fields[7]);
    }

    [TestMethod]
    public void XoverLine_BundledArticlesShouldFormatCorrectly()
    {
        foreach (var (groupName, articles) in _articles)
        {
            foreach (var article in articles)
            {
                var xoverLine = $"{article.Number}\t{article.Subject}\t{article.From}\t{article.Date}\t{article.MessageId}\t{article.References}\t{article.Bytes}\t{article.Lines}";
                var fields = xoverLine.Split('\t');

                Assert.AreEqual(8, fields.Length, $"XOVER line for article {article.Number} in '{groupName}' should have 8 fields");
                Assert.IsFalse(fields[1].Contains('\t'), $"Subject in '{groupName}' article {article.Number} contains a tab");
                Assert.IsFalse(fields[2].Contains('\t'), $"From in '{groupName}' article {article.Number} contains a tab");
            }
        }
    }

    #endregion

    #region LIST Format Tests

    [TestMethod]
    public void ListLine_ShouldFollowNntpFormat()
    {
        foreach (var group in _groups)
        {
            var articles = _articles[group.Name];
            var first = articles.Min(a => a.Number);
            var last = articles.Max(a => a.Number);

            var listLine = $"{group.Name} {last} {first} n";
            var fields = listLine.Split(' ');

            Assert.IsTrue(fields.Length >= 4, $"LIST line for '{group.Name}' should have at least 4 fields");
            Assert.AreEqual(group.Name, fields[0]);
            Assert.AreEqual("n", fields[^1], $"Posting status for '{group.Name}' should be 'n'");
        }
    }

    #endregion

    #region Helper Methods

    private static UsenetArticle MakeTestArticle()
    {
        return new UsenetArticle
        {
            Number = 1,
            MessageId = "<test@test.com>",
            From = "testuser@test.com (Test User)",
            Subject = "Test Subject",
            Date = "Fri, 15 Jan 1999 08:30:12 -0500",
            Newsgroups = "comp.test",
            References = "",
            Bytes = 42,
            Lines = 3,
            Body = "This is line one.\r\nThis is line two.\r\nThis is line three."
        };
    }

    private static Dictionary<string, List<UsenetArticle>> LoadArticles()
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var assembly = Assembly.GetExecutingAssembly();
        var gzResourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("TestData.usenet.articles.json.gz", StringComparison.OrdinalIgnoreCase));

        if (gzResourceName != null)
        {
            using var gzStream = assembly.GetManifestResourceStream(gzResourceName);
            using var decompressed = new GZipStream(gzStream, CompressionMode.Decompress);
            using var ms = new MemoryStream();

            decompressed.CopyTo(ms);

            var json = Encoding.UTF8.GetString(ms.ToArray());

            return JsonSerializer.Deserialize<Dictionary<string, List<UsenetArticle>>>(json, jsonOptions);
        }

        return LoadEmbeddedJson<Dictionary<string, List<UsenetArticle>>>("TestData.usenet.articles.json");
    }

    private static T LoadEmbeddedJson<T>(string resourceSuffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    #endregion

    #region Bundled Data DTO

    private class BundledGroup
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    #endregion
}
