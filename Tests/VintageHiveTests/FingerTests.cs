// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Finger;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Presence;

#pragma warning disable MSTEST0025 // Use Assert.Fail instead of always-failing Assert.AreEqual

namespace Finger;

[TestClass]
public class FingerQueryParsingTests
{
    #region Basic Query Parsing

    [TestMethod]
    public void ParseQuery_SimpleUsername_ReturnsUsername()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("fox");

        Assert.AreEqual("fox", query);
        Assert.IsFalse(verbose);
        Assert.IsFalse(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_EmptyString_ReturnsEmpty()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("");

        Assert.AreEqual("", query);
        Assert.IsFalse(verbose);
        Assert.IsFalse(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_TrailingCrLf_Stripped()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("testuser\r\n");

        Assert.AreEqual("testuser", query);
        Assert.IsFalse(verbose);
        Assert.IsFalse(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_TrailingLf_Stripped()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("testuser\n");

        Assert.AreEqual("testuser", query);
    }

    [TestMethod]
    public void ParseQuery_OnlyCrLf_ReturnsEmpty()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("\r\n");

        Assert.AreEqual("", query);
        Assert.IsFalse(verbose);
        Assert.IsFalse(isForwarding);
    }

    #endregion

    #region Verbose /W Prefix

    [TestMethod]
    public void ParseQuery_VerboseWithUsername_SetsVerbose()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/W fox");

        Assert.AreEqual("fox", query);
        Assert.IsTrue(verbose);
        Assert.IsFalse(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_VerboseAlone_SetsVerboseEmptyQuery()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/W");

        Assert.AreEqual("", query);
        Assert.IsTrue(verbose);
        Assert.IsFalse(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_VerboseLowerCase_SetsVerbose()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/w fox");

        Assert.AreEqual("fox", query);
        Assert.IsTrue(verbose);
    }

    [TestMethod]
    public void ParseQuery_VerboseWithExtraSpaces_TrimsUsername()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/W   fox");

        Assert.AreEqual("fox", query);
        Assert.IsTrue(verbose);
    }

    [TestMethod]
    public void ParseQuery_VerboseWithCrLf_ParsesCorrectly()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/W fox\r\n");

        Assert.AreEqual("fox", query);
        Assert.IsTrue(verbose);
        Assert.IsFalse(isForwarding);
    }

    #endregion

    #region Forwarding Rejection

    [TestMethod]
    public void ParseQuery_AtSign_DetectsForwarding()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("fox@otherhost");

        Assert.IsTrue(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_VerboseWithAtSign_DetectsForwarding()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("/W fox@otherhost");

        Assert.IsTrue(verbose);
        Assert.IsTrue(isForwarding);
    }

    [TestMethod]
    public void ParseQuery_DoubleAtSign_DetectsForwarding()
    {
        var (query, verbose, isForwarding) = FingerServer.ParseQuery("fox@host1@host2");

        Assert.IsTrue(isForwarding);
    }

    #endregion
}

[TestClass]
public class FingerFormatStatusTests
{
    [TestMethod]
    public void FormatStatus_Online()
    {
        Assert.AreEqual("Online", FingerServer.FormatStatus(PresenceStatus.Online));
    }

    [TestMethod]
    public void FormatStatus_Away()
    {
        Assert.AreEqual("Away", FingerServer.FormatStatus(PresenceStatus.Away));
    }

    [TestMethod]
    public void FormatStatus_DoNotDisturb()
    {
        Assert.AreEqual("Do Not Disturb", FingerServer.FormatStatus(PresenceStatus.DoNotDisturb));
    }

    [TestMethod]
    public void FormatStatus_NotAvailable()
    {
        Assert.AreEqual("Not Available", FingerServer.FormatStatus(PresenceStatus.NotAvailable));
    }

    [TestMethod]
    public void FormatStatus_Occupied()
    {
        Assert.AreEqual("Occupied", FingerServer.FormatStatus(PresenceStatus.Occupied));
    }

    [TestMethod]
    public void FormatStatus_FreeToChat()
    {
        Assert.AreEqual("Free to Chat", FingerServer.FormatStatus(PresenceStatus.FreeToChat));
    }

    [TestMethod]
    public void FormatStatus_Invisible()
    {
        Assert.AreEqual("Invisible", FingerServer.FormatStatus(PresenceStatus.Invisible));
    }

    [TestMethod]
    public void FormatStatus_YahooMsnStates()
    {
        Assert.AreEqual("Idle", FingerServer.FormatStatus(PresenceStatus.Idle));
        Assert.AreEqual("Busy", FingerServer.FormatStatus(PresenceStatus.Busy));
        Assert.AreEqual("On the Phone", FingerServer.FormatStatus(PresenceStatus.OnThePhone));
        Assert.AreEqual("Out to Lunch", FingerServer.FormatStatus(PresenceStatus.OutToLunch));
        Assert.AreEqual("Be Right Back", FingerServer.FormatStatus(PresenceStatus.BeRightBack));
    }
}

[TestClass]
public class FingerFormatIdleTests
{
    #region FormatIdle (seconds-based)

    [TestMethod]
    public void FormatIdle_NotIdle_ReturnsDash()
    {
        Assert.AreEqual("-", FingerServer.FormatIdle(0u));
    }

    [TestMethod]
    public void FormatIdle_Idle_FormatsLong()
    {
        Assert.AreEqual("5m", FingerServer.FormatIdle(300u));
    }

    #endregion

    #region FormatIdleLong

    [TestMethod]
    public void FormatIdleLong_Seconds_FormatsWithS()
    {
        Assert.AreEqual("30s", FingerServer.FormatIdleLong(30));
    }

    [TestMethod]
    public void FormatIdleLong_OneSecond()
    {
        Assert.AreEqual("1s", FingerServer.FormatIdleLong(1));
    }

    [TestMethod]
    public void FormatIdleLong_59Seconds()
    {
        Assert.AreEqual("59s", FingerServer.FormatIdleLong(59));
    }

    [TestMethod]
    public void FormatIdleLong_OneMinute()
    {
        Assert.AreEqual("1m", FingerServer.FormatIdleLong(60));
    }

    [TestMethod]
    public void FormatIdleLong_Minutes()
    {
        Assert.AreEqual("5m", FingerServer.FormatIdleLong(300));
    }

    [TestMethod]
    public void FormatIdleLong_59Minutes()
    {
        Assert.AreEqual("59m", FingerServer.FormatIdleLong(3599));
    }

    [TestMethod]
    public void FormatIdleLong_OneHour()
    {
        Assert.AreEqual("1h 0m", FingerServer.FormatIdleLong(3600));
    }

    [TestMethod]
    public void FormatIdleLong_HoursAndMinutes()
    {
        Assert.AreEqual("2h 30m", FingerServer.FormatIdleLong(9000));
    }

    [TestMethod]
    public void FormatIdleLong_ManyHours()
    {
        Assert.AreEqual("24h 0m", FingerServer.FormatIdleLong(86400));
    }

    #endregion
}

[TestClass]
public class FingerFormatRealNameTests
{
    [TestMethod]
    public void FormatRealName_NullProfile_ReturnsUnknown()
    {
        Assert.AreEqual("(unknown)", FingerServer.FormatRealName(null!));
    }

    [TestMethod]
    public void FormatRealName_EmptyProfile_ReturnsUnknown()
    {
        var profile = new OscarUserProfile();

        Assert.AreEqual("(unknown)", FingerServer.FormatRealName(profile));
    }

    [TestMethod]
    public void FormatRealName_FirstAndLast_JoinsWithSpace()
    {
        var profile = new OscarUserProfile { FirstName = "John", LastName = "Carmack" };

        Assert.AreEqual("John Carmack", FingerServer.FormatRealName(profile));
    }

    [TestMethod]
    public void FormatRealName_FirstNameOnly()
    {
        var profile = new OscarUserProfile { FirstName = "John" };

        Assert.AreEqual("John", FingerServer.FormatRealName(profile));
    }

    [TestMethod]
    public void FormatRealName_LastNameOnly()
    {
        var profile = new OscarUserProfile { LastName = "Carmack" };

        Assert.AreEqual("Carmack", FingerServer.FormatRealName(profile));
    }

    [TestMethod]
    public void FormatRealName_NicknameOnly_FallsBackToNickname()
    {
        var profile = new OscarUserProfile { Nickname = "JC" };

        Assert.AreEqual("JC", FingerServer.FormatRealName(profile));
    }

    [TestMethod]
    public void FormatRealName_FirstNameAndNickname_PrefersRealName()
    {
        var profile = new OscarUserProfile { FirstName = "John", Nickname = "JC" };

        Assert.AreEqual("John", FingerServer.FormatRealName(profile));
    }
}

[TestClass]
public class FingerFormatLocationTests
{
    [TestMethod]
    public void FormatLocation_CityAndState()
    {
        Assert.AreEqual("Mesquite, Texas", FingerServer.FormatLocation("Mesquite", "Texas"));
    }

    [TestMethod]
    public void FormatLocation_CityOnly()
    {
        Assert.AreEqual("Dallas", FingerServer.FormatLocation("Dallas", ""));
    }

    [TestMethod]
    public void FormatLocation_StateOnly()
    {
        Assert.AreEqual("Texas", FingerServer.FormatLocation("", "Texas"));
    }

    [TestMethod]
    public void FormatLocation_BothEmpty()
    {
        Assert.AreEqual("", FingerServer.FormatLocation("", ""));
    }

    [TestMethod]
    public void FormatLocation_NullCity()
    {
        Assert.AreEqual("Texas", FingerServer.FormatLocation(null!, "Texas"));
    }

    [TestMethod]
    public void FormatLocation_NullState()
    {
        Assert.AreEqual("Dallas", FingerServer.FormatLocation("Dallas", null!));
    }

    [TestMethod]
    public void FormatLocation_BothNull()
    {
        Assert.AreEqual("", FingerServer.FormatLocation(null!, null!));
    }
}

[TestClass]
public class FingerUserListTests
{
    // OscarServer.Sessions is shared static state; snapshot and restore it around each test so
    // Finger list assertions don't leak into (or out of) the OSCAR tests.
    private static void WithSessions(Action<Action<OscarSession>> body)
    {
        // Finger now reads presence through the shared registry; ensure the OSCAR provider is wired so
        // sessions added here surface in BuildUserList (registration is idempotent by network key).
        PresenceRegistry.Register(new OscarPresenceProvider());

        var snapshot = OscarServer.Sessions.ToArray();

        OscarServer.Sessions.Clear();

        try
        {
            body(session => OscarServer.Sessions[session.ID] = session);
        }
        finally
        {
            OscarServer.Sessions.Clear();

            foreach (var kvp in snapshot)
            {
                OscarServer.Sessions[kvp.Key] = kvp.Value;
            }
        }
    }

    [TestMethod]
    public void BuildUserList_NoSessions_ReportsNoUsersOnline()
    {
        WithSessions(_ =>
        {
            var output = FingerServer.BuildUserList();

            Assert.IsTrue(output.Contains("VintageHive Finger Server"), output);
            Assert.IsTrue(output.Contains("No users currently online."), output);
        });
    }

    [TestMethod]
    public void BuildUserList_WithOnlineUser_ListsScreenNameAndStatus()
    {
        WithSessions(add =>
        {
            add(new OscarSession { ScreenName = "FoxRunner", Status = OscarSessionOnlineStatus.Away });

            var output = FingerServer.BuildUserList();

            Assert.IsTrue(output.Contains("FoxRunner"), output);
            Assert.IsTrue(output.Contains("Away"), output);
            Assert.IsFalse(output.Contains("No users currently online."), output);
        });
    }

    [TestMethod]
    public void BuildUserList_SkipsSessionsWithoutScreenName()
    {
        WithSessions(add =>
        {
            add(new OscarSession { ScreenName = "", Status = OscarSessionOnlineStatus.Online });
            add(new OscarSession { ScreenName = "RealUser", Status = OscarSessionOnlineStatus.Online });

            var output = FingerServer.BuildUserList();

            Assert.IsTrue(output.Contains("RealUser"), output);
        });
    }
}

#pragma warning restore MSTEST0025
