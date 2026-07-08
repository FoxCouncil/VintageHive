// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// Telnet BBS tests. Telnet here is a raw-ASCII BBS (no RFC 854 option negotiation), so these are
// application-logic tests: the command registry / window routing, the riddle game, and word wrapping.

using System.Reflection;
using VintageHive.Network;
using VintageHive.Proxy.Telnet;
using VintageHive.Proxy.Telnet.Commands;
using VintageHive.Utilities;

namespace Telnet;

[TestClass]
public class TelnetCommandRegistryTests
{
    [TestMethod]
    public void VisibleCommands_AreExactlyTheUserFacingSet()
    {
        var visible = TelnetWindowManager.GetAllCommands(showHidden: false);

        CollectionAssert.AreEquivalent(
            new[] { "weather", "riddle", "lorem", "help", "count", "news" },
            visible.Keys.ToList());
    }

    [TestMethod]
    public void HiddenCommands_AreExcludedWhenNotShowingHidden()
    {
        var visible = TelnetWindowManager.GetAllCommands(showHidden: false);

        Assert.IsFalse(visible.ContainsKey("invalid_cmd"));
        Assert.IsFalse(visible.ContainsKey("image_gallery"));
        Assert.IsFalse(visible.ContainsKey("weather_change_temp"));
    }

    [TestMethod]
    public void ShowHidden_IncludesInternalWindows()
    {
        var all = TelnetWindowManager.GetAllCommands(showHidden: true);

        Assert.IsTrue(all.ContainsKey("invalid_cmd"));
        Assert.IsTrue(all.ContainsKey("image_gallery"));
        Assert.IsTrue(all.Count > TelnetWindowManager.GetAllCommands(showHidden: false).Count);
    }

    [TestMethod]
    public void EveryCommand_HasADescription()
    {
        foreach (var kv in TelnetWindowManager.GetAllCommands(showHidden: true))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(kv.Value), $"'{kv.Key}' has no description");
        }
    }
}

[TestClass]
public class TelnetWindowStackTests
{
    [TestMethod]
    public void AddKnownWindow_PushesIt()
    {
        var wm = new TelnetWindowManager();

        Assert.IsTrue(wm.TryAddWindow("riddle"));
        Assert.AreEqual(1, wm.GetWindowCount());
        Assert.AreEqual("riddle", wm.GetTopWindow().Title);
    }

    [TestMethod]
    public void AddUnknownWindow_Fails()
    {
        var wm = new TelnetWindowManager();

        Assert.IsFalse(wm.TryAddWindow("does_not_exist"));
        Assert.AreEqual(0, wm.GetWindowCount());
    }

    [TestMethod]
    public void CloseTopWindow_PopsIt()
    {
        var wm = new TelnetWindowManager();
        wm.TryAddWindow("riddle");

        wm.CloseTopWindow();

        Assert.AreEqual(0, wm.GetWindowCount());
        Assert.IsNull(wm.GetTopWindow());
    }

    [TestMethod]
    public void Windows_StackAndUnstack()
    {
        var wm = new TelnetWindowManager();

        wm.TryAddWindow("riddle");
        wm.TryAddWindow("help");

        Assert.AreEqual(2, wm.GetWindowCount());
        Assert.AreEqual("help", wm.GetTopWindow().Title);

        wm.CloseTopWindow();

        Assert.AreEqual("riddle", wm.GetTopWindow().Title);
    }
}

[TestClass]
public class TelnetRiddleTests
{
    private static (TelnetRiddleCommand riddle, string answer) NewRiddle()
    {
        var session = new TelnetSession(new ListenerSocket());
        var riddle = new TelnetRiddleCommand();
        riddle.OnAdd(session);

        var answer = (string)typeof(TelnetRiddleCommand)
            .GetField("answer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(riddle)!;

        return (riddle, answer);
    }

    [TestMethod]
    public void WrongAnswer_DecrementsAttempts()
    {
        var (riddle, _) = NewRiddle();

        riddle.ProcessCommand("definitely-not-the-answer");

        StringAssert.Contains(riddle.Text, "2 attempts left");
        Assert.IsFalse(riddle.ShouldRemoveNextCommand);
    }

    [TestMethod]
    public void ThreeWrongAnswers_EndsTheGame()
    {
        var (riddle, _) = NewRiddle();

        riddle.ProcessCommand("nope-one");
        riddle.ProcessCommand("nope-two");
        riddle.ProcessCommand("nope-three");

        Assert.IsTrue(riddle.ShouldRemoveNextCommand);
        StringAssert.Contains(riddle.Text, "ran out of attempts");
    }

    [TestMethod]
    public void CorrectAnswer_Wins()
    {
        var (riddle, answer) = NewRiddle();

        riddle.ProcessCommand(answer);

        Assert.IsTrue(riddle.ShouldRemoveNextCommand);
        StringAssert.Contains(riddle.Text, "Correct");
    }

    [TestMethod]
    public void AnswerAsSubstring_StillWins()
    {
        // The riddle matches on Contains, so "a wolf" counts when the answer is "wolf".
        var (riddle, answer) = NewRiddle();

        riddle.ProcessCommand($"i think it is {answer}!");

        Assert.IsTrue(riddle.ShouldRemoveNextCommand);
        StringAssert.Contains(riddle.Text, "Correct");
    }
}

[TestClass]
public class WordWrapTextTests
{
    [TestMethod]
    public void ShortText_FitsOnOneLine()
    {
        var result = "hello world".WordWrapText(80, 24);

        StringAssert.Contains(result, "hello world");
        Assert.AreEqual(1, result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [TestMethod]
    public void LongText_WrapsToMultipleLines()
    {
        var result = "one two three four five six seven eight nine ten".WordWrapText(12, 24);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.IsTrue(lines.Length > 1, "expected wrapping into multiple lines");
        Assert.IsTrue(lines.All(l => l.Length <= 12), "a wrapped line exceeded the width");
    }
}
