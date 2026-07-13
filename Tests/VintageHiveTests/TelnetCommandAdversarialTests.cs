// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive
//
// ADVERSARIAL, second-pass gap-filling for the Telnet BBS command dispatch. The happy paths
// (visible command set, basic riddle, wordwrap, simple stack push/pop) are already covered by
// TelnetBbsTests.cs. This file hunts the edges: case-sensitivity of the raw dispatcher vs the
// normalizing entry point, empty/whitespace/null command lines, self-purging window stack
// mechanics, the "invalid command" echo formatter (control bytes / IAC 0xFF / line-ending
// stripping), and the fact that hidden internal sub-windows are dispatchable by exact name.
//
// Telnet here is a raw-ASCII BBS with NO RFC 854 IAC option negotiation parser: 0xFF bytes are
// ordinary data, so there is no pure IAC decoder to attack. We instead prove the 0xFF/control-byte
// pass-through behaviour of the one pure text surface that echoes user input (invalid_cmd).
//
// Everything here is pure: no sockets are opened (a bare ListenerSocket with a null RawSocket is
// used only as a data holder), no Mind.Db access, and no window whose OnAdd performs a data callout
// (weather/news/count/gallery) is ever activated. The one intentionally-thrown exception is a
// synchronous NullReferenceException with no I/O behind it.

using VintageHive.Network;
using VintageHive.Proxy.Telnet;
using VintageHive.Proxy.Telnet.Commands;

namespace Adversarial6.Telnet;

[TestClass]
public class CommandRegistryEdgeTests
{
    [TestMethod]
    public void GetAllCommands_ReturnsFreshDictionaryInstanceEachCall()
    {
        var a = TelnetWindowManager.GetAllCommands(true);
        var b = TelnetWindowManager.GetAllCommands(true);

        Assert.IsFalse(ReferenceEquals(a, b), "GetAllCommands should hand back an independent dictionary each call");
        CollectionAssert.AreEquivalent(a.Keys.ToList(), b.Keys.ToList());
    }

    [TestMethod]
    public void GetAllCommands_ShowHidden_IsStrictSupersetOfVisible()
    {
        var visible = TelnetWindowManager.GetAllCommands(false);
        var all = TelnetWindowManager.GetAllCommands(true);

        foreach (var key in visible.Keys)
        {
            Assert.IsTrue(all.ContainsKey(key), $"visible command '{key}' missing from the show-hidden set");
        }

        Assert.IsTrue(all.Count > visible.Count, "show-hidden set must contain more than the visible set");
    }

    [TestMethod]
    public void GetAllCommands_HasExactlyTwelveTotalAndSixVisible()
    {
        // Documents the current registry size. If a window is added/removed this deliberately breaks
        // so the dispatch surface is re-reviewed (12 = 6 user-facing + 6 hidden internal sub-windows).
        Assert.AreEqual(12, TelnetWindowManager.GetAllCommands(true).Count);
        Assert.AreEqual(6, TelnetWindowManager.GetAllCommands(false).Count);
    }

    [TestMethod]
    public void HiddenInternalSubWindows_ArePresentOnlyInShowHiddenSet()
    {
        var visible = TelnetWindowManager.GetAllCommands(false);
        var all = TelnetWindowManager.GetAllCommands(true);

        // These are internal sub-windows the UI is only ever supposed to reach programmatically via
        // ForceAddWindow(...) WITH arguments. They are hidden from `help` yet remain routable by name.
        foreach (var hidden in new[] { "invalid_cmd", "image_gallery", "news_headline_view", "news_article_view", "weather_change_location", "weather_change_temp" })
        {
            Assert.IsFalse(visible.ContainsKey(hidden), $"'{hidden}' should not be user-visible");
            Assert.IsTrue(all.ContainsKey(hidden), $"'{hidden}' should still exist in the show-hidden registry");
        }
    }
}

[TestClass]
public class WindowDispatchEdgeTests
{
    [TestMethod]
    public void TryAddWindow_IsCaseSensitive_UppercaseIsRejected()
    {
        // The raw dispatcher does NOT normalize case; ProcessCommand is what lowercases first.
        var wm = new TelnetWindowManager();

        Assert.IsFalse(wm.TryAddWindow("RIDDLE"));
        Assert.IsFalse(wm.TryAddWindow("Riddle"));
        Assert.AreEqual(0, wm.GetWindowCount());
    }

    [TestMethod]
    public void TryAddWindow_DoesNotTrim_PaddedNameIsRejected()
    {
        var wm = new TelnetWindowManager();

        Assert.IsFalse(wm.TryAddWindow(" riddle"));
        Assert.IsFalse(wm.TryAddWindow("riddle "));
        Assert.IsFalse(wm.TryAddWindow("\triddle"));
        Assert.AreEqual(0, wm.GetWindowCount());
    }

    [TestMethod]
    public void TryAddWindow_EmptyOrWhitespace_IsRejected()
    {
        var wm = new TelnetWindowManager();

        Assert.IsFalse(wm.TryAddWindow(string.Empty));
        Assert.IsFalse(wm.TryAddWindow("   "));
        Assert.IsFalse(wm.TryAddWindow("\r\n"));
        Assert.AreEqual(0, wm.GetWindowCount());
    }

    [TestMethod]
    public void TryAddWindow_Null_ThrowsArgumentNullException()
    {
        // Dictionary.ContainsKey(null) throws. Not reachable through ProcessCommand (which always
        // supplies a trimmed, lowercased, non-null string), so documented as behaviour only.
        var wm = new TelnetWindowManager();

        Assert.ThrowsExactly<ArgumentNullException>(() => wm.TryAddWindow(null!));
    }

    [TestMethod]
    public void TryAddWindow_HiddenInternalName_RejectedByDefault_AcceptedWhenAllowed()
    {
        // Hidden windows are no longer routable by default (that let a typed name run OnAdd with null
        // args and crash); they are only added with allowHidden: true via ForceAddWindow.
        var wm = new TelnetWindowManager();

        Assert.IsFalse(wm.TryAddWindow("invalid_cmd"), "a hidden window must not be dispatchable by default");
        Assert.AreEqual(0, wm.GetWindowCount());

        Assert.IsTrue(wm.TryAddWindow("invalid_cmd", allowHidden: true), "allowHidden must permit the programmatic add");
        Assert.AreEqual("invalid_cmd", wm.GetTopWindow().Title);
    }

    [TestMethod]
    public void TryAddWindow_SameTitleAsTop_ReplacesTheInstance()
    {
        var wm = new TelnetWindowManager();

        Assert.IsTrue(wm.TryAddWindow("riddle"));
        var first = wm.GetTopWindow();

        Assert.IsTrue(wm.TryAddWindow("riddle"));
        var second = wm.GetTopWindow();

        Assert.AreEqual(1, wm.GetWindowCount());
        Assert.IsFalse(ReferenceEquals(first, second), "re-adding the same top window should close the old instance and push a fresh one");
    }

    [TestMethod]
    public void TryAddWindow_ReAddingSelfPurgingWindow_StaysCountOne()
    {
        // help has ShouldRemoveNextCommand == true, so the second add's RemoveDeadWindows pass purges
        // the first BEFORE the same-title check runs; a fresh help is then pushed. Net count is 1.
        var wm = new TelnetWindowManager();

        Assert.IsTrue(wm.TryAddWindow("help"));
        var first = wm.GetTopWindow();

        Assert.IsTrue(wm.TryAddWindow("help"));

        Assert.AreEqual(1, wm.GetWindowCount());
        Assert.AreEqual("help", wm.GetTopWindow().Title);
        Assert.IsFalse(ReferenceEquals(first, wm.GetTopWindow()));
    }

    [TestMethod]
    public void TryAddWindow_DistinctWindows_Stack()
    {
        var wm = new TelnetWindowManager();

        Assert.IsTrue(wm.TryAddWindow("riddle"));
        Assert.IsTrue(wm.TryAddWindow("count"));

        Assert.AreEqual(2, wm.GetWindowCount());
        Assert.AreEqual("count", wm.GetTopWindow().Title);

        // count owns an unstarted System.Timers.Timer; Destroy disposes it (OnAdd was never called so
        // it never started, but this keeps the test tidy).
        wm.Destroy();
    }
}

[TestClass]
public class WindowLifecycleEdgeTests
{
    [TestMethod]
    public void CloseTopWindow_OnEmptyStack_IsNoOp()
    {
        var wm = new TelnetWindowManager();

        wm.CloseTopWindow();

        Assert.AreEqual(0, wm.GetWindowCount());
        Assert.IsNull(wm.GetTopWindow());
    }

    [TestMethod]
    public void RemoveDeadWindows_OnEmptyStack_IsNoOp()
    {
        var wm = new TelnetWindowManager();

        wm.RemoveDeadWindows();

        Assert.AreEqual(0, wm.GetWindowCount());
    }

    [TestMethod]
    public void RemoveDeadWindows_PurgesSelfPurgingTop_ButKeepsLiveWindowBelow()
    {
        var wm = new TelnetWindowManager();

        // riddle stays resident (ShouldRemoveNextCommand starts false); lorem self-purges.
        Assert.IsTrue(wm.TryAddWindow("riddle"));
        Assert.IsTrue(wm.TryAddWindow("lorem"));
        Assert.AreEqual(2, wm.GetWindowCount());
        Assert.AreEqual("lorem", wm.GetTopWindow().Title);

        wm.RemoveDeadWindows();

        Assert.AreEqual(1, wm.GetWindowCount());
        Assert.AreEqual("riddle", wm.GetTopWindow().Title);
    }

    [TestMethod]
    public void TryAddWindow_AfterManualClose_ReturnsToEmptyCleanly()
    {
        var wm = new TelnetWindowManager();

        wm.TryAddWindow("riddle");
        wm.CloseTopWindow();

        Assert.AreEqual(0, wm.GetWindowCount());
        Assert.IsNull(wm.GetTopWindow());
        Assert.IsTrue(wm.TryAddWindow("riddle"), "stack should be reusable after emptying");
    }
}

[TestClass]
public class InvalidCommandFormatterTests
{
    // Exercises TelnetInvalidCommand.OnAdd directly: the pure "what did you type" echo. It reads only
    // session.InputBuffer and touches no socket, so a bare ListenerSocket data holder is sufficient.
    private static string Format(string inputBuffer)
    {
        var session = new TelnetSession(new ListenerSocket())
        {
            InputBuffer = inputBuffer
        };

        var cmd = new TelnetInvalidCommand();
        cmd.OnAdd(session);

        return cmd.Text;
    }

    [TestMethod]
    public void EmptyInputBuffer_ReportsEmptyOrWhitespace()
    {
        Assert.AreEqual("Invalid command: Empty or whitespace\r\n", Format(string.Empty));
    }

    [TestMethod]
    public void WhitespaceOnlyInputBuffer_ReportsEmptyOrWhitespace()
    {
        Assert.AreEqual("Invalid command: Empty or whitespace\r\n", Format("      "));
    }

    [TestMethod]
    public void NewlineOnlyInputBuffer_IsStrippedToEmptyOrWhitespace()
    {
        // ReplaceLineEndings("") collapses the CRLFs, then Trim leaves nothing.
        Assert.AreEqual("Invalid command: Empty or whitespace\r\n", Format("\r\n\r\n"));
    }

    [TestMethod]
    public void PlainCommand_IsEchoedVerbatim()
    {
        Assert.AreEqual("Invalid command: foobar\r\n", Format("foobar"));
    }

    [TestMethod]
    public void SurroundingSpaces_AreTrimmed()
    {
        Assert.AreEqual("Invalid command: foobar\r\n", Format("   foobar   "));
    }

    [TestMethod]
    public void EmbeddedControlChars_ArePreservedNotSanitized()
    {
        // SOH (0x01) is a control char but neither whitespace nor a line-ending, so it survives intact.
        // The BBS echoes arbitrary control bytes back to the terminal without sanitization.
        var soh = ((char)0x01).ToString();
        var input = "ab" + soh + "cd";

        var result = Format(input);

        Assert.AreEqual("Invalid command: ab" + soh + "cd\r\n", result);
    }

    [TestMethod]
    public void IacByte_IsTreatedAsOrdinaryData_NoNegotiation()
    {
        // There is no RFC 854 IAC parser. A 0xFF (IAC) char in the buffer is echoed as data, proving
        // the absence of option negotiation at this pure layer.
        var iac = ((char)0xFF).ToString();
        var input = "x" + iac + "y";

        var result = Format(input);

        Assert.AreEqual("Invalid command: x" + iac + "y\r\n", result);
    }

    [TestMethod]
    public void FormFeedStripped_VerticalTabPreserved_ByLineEndingNormalization()
    {
        // Observed on net10.0: ReplaceLineEndings recognizes FF (U+000C) as a line ending and removes
        // it, but does NOT recognize VT (U+000B), which passes through as ordinary data. This documents
        // the exact (and slightly surprising) set of chars the command echo silently drops.
        var vt = ((char)0x0B).ToString();
        var ff = ((char)0x0C).ToString();

        Assert.AreEqual("Invalid command: a" + vt + "b\r\n", Format("a" + vt + "b"));
        Assert.AreEqual("Invalid command: cd\r\n", Format("c" + ff + "d"));
    }
}

[TestClass]
public class ProcessCommandDispatchTests
{
    // Drives the real entry point TelnetSession.ProcessCommand on paths that never touch the socket
    // (non-exit commands whose windows have side-effect-free OnAdd). A bare ListenerSocket is a pure
    // data holder here; Client is never dereferenced on these paths.
    private static TelnetSession NewSession(string inputBuffer = "")
    {
        return new TelnetSession(new ListenerSocket())
        {
            InputBuffer = inputBuffer
        };
    }

    [TestMethod]
    public async Task UnknownCommand_RoutesToInvalidWindow_EchoingInputBuffer()
    {
        var session = NewSession("zork zork");

        await session.ProcessCommand("zork zork");

        var top = session.WindowManager.GetTopWindow();
        Assert.IsNotNull(top);
        Assert.AreEqual("invalid_cmd", top.Title);
        Assert.AreEqual("Invalid command: zork zork\r\n", top.Text);
    }

    [TestMethod]
    public async Task EmptyCommand_RoutesToInvalidWindow()
    {
        var session = NewSession(string.Empty);

        await session.ProcessCommand(string.Empty);

        var top = session.WindowManager.GetTopWindow();
        Assert.IsNotNull(top);
        Assert.AreEqual("invalid_cmd", top.Title);
        Assert.AreEqual("Invalid command: Empty or whitespace\r\n", top.Text);
    }

    [TestMethod]
    public async Task WhitespaceCommand_RoutesToInvalidWindow()
    {
        var session = NewSession("    ");

        await session.ProcessCommand("    ");

        Assert.AreEqual("invalid_cmd", session.WindowManager.GetTopWindow().Title);
    }

    [TestMethod]
    public async Task UppercaseKnownCommand_IsNormalizedAndDispatched()
    {
        // Contrast with TryAddWindow: ProcessCommand lowercases, so the raw-case rejection does not
        // apply at this layer.
        var session = NewSession();

        await session.ProcessCommand("LOREM");

        Assert.AreEqual(1, session.WindowManager.GetWindowCount());
        Assert.AreEqual("lorem", session.WindowManager.GetTopWindow().Title);
    }

    [TestMethod]
    public async Task PaddedKnownCommand_IsTrimmedAndDispatched()
    {
        var session = NewSession();

        await session.ProcessCommand("   help   ");

        Assert.AreEqual(1, session.WindowManager.GetWindowCount());
        Assert.AreEqual("help", session.WindowManager.GetTopWindow().Title);
    }

    [TestMethod]
    public async Task HiddenInternalWindowName_TypedByUser_RoutesToInvalidNotCrash()
    {
        // A hidden internal sub-window name typed at the prompt must NOT be routable as a top-level
        // command: doing so ran OnAdd with null args -> NullReferenceException (and for the gallery, an
        // async-void unhandled exception that crashed the whole server process). Hidden windows are now
        // only reachable programmatically via ForceAddWindow, so a typed name routes to invalid_cmd.
        var session = NewSession("news_headline_view");

        await session.ProcessCommand("news_headline_view");

        Assert.AreEqual(1, session.WindowManager.GetWindowCount());
        Assert.AreEqual("invalid_cmd", session.WindowManager.GetTopWindow().Title);
    }
}