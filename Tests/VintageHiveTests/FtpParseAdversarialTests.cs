// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Ftp;

#pragma warning disable MSTEST0025

namespace Adversarial3.Ftp;

// Adversarial coverage for the pure FTP control-line parse/format helpers on FtpRequest and the
// FtpCommand verb constants. Nothing here opens sockets, touches Mind.Db, or drives a live handler:
// only ParseCommandLine, FormatResponse, FormatFeatureList, and the command-string constants are
// exercised. All asserted values were observed by running the real methods first, then pinned.
[TestClass]
public class FtpParseCommandLineTests
{
    #region Empty / whitespace-only -> InvalidOperationException

    [TestMethod]
    public void ParseCommandLine_EmptyString_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine(""));
    }

    [TestMethod]
    public void ParseCommandLine_SingleSpace_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine(" "));
    }

    [TestMethod]
    public void ParseCommandLine_ManySpaces_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine("        "));
    }

    [TestMethod]
    public void ParseCommandLine_TabOnly_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine("\t"));
    }

    [TestMethod]
    public void ParseCommandLine_CrLfOnly_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine("\r\n"));
    }

    [TestMethod]
    public void ParseCommandLine_FormFeedOnly_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine("\f"));
    }

    [TestMethod]
    public void ParseCommandLine_MixedWhitespaceRun_Throws()
    {
        // Space, tab, CR, LF, form feed are the AngleSharp "space characters"; a line made only of
        // them collapses to zero tokens and is rejected as garbage.
        Assert.ThrowsExactly<InvalidOperationException>(() => FtpRequest.ParseCommandLine(" \t \r \n \f "));
    }

    #endregion

    #region Null -> NullReferenceException (SplitSpaces dereferences before the null guard)

    [TestMethod]
    public void ParseCommandLine_Null_ThrowsNullReference()
    {
        // The method has a `parsedResponse == null` guard, but SplitSpaces() dereferences the string
        // first, so a null line never reaches the intended InvalidOperationException path. Callers
        // (FetchCommand) pre-filter null/whitespace, so this is a documented dead branch, not a live
        // crash vector.
        Assert.ThrowsExactly<NullReferenceException>(() => FtpRequest.ParseCommandLine(null));
    }

    #endregion

    #region Verb-only (no args) -> empty args

    [TestMethod]
    public void ParseCommandLine_VerbOnly_EmptyArgs()
    {
        var result = FtpRequest.ParseCommandLine("USER");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_VerbOnlyWithTrailingCrLf_EmptyArgs()
    {
        var result = FtpRequest.ParseCommandLine("QUIT\r\n");

        Assert.AreEqual("QUIT", result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    #endregion

    #region Verb uppercasing (RFC 959 4.1 commands are case-insensitive)

    [TestMethod]
    public void ParseCommandLine_LowerCaseVerb_UpperCased()
    {
        var result = FtpRequest.ParseCommandLine("user fox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_MixedCaseVerb_UpperCased()
    {
        var result = FtpRequest.ParseCommandLine("UsEr fox");

        Assert.AreEqual("USER", result.Item1);
    }

    [TestMethod]
    public void ParseCommandLine_ArgumentCaseIsPreserved()
    {
        // Only the verb is folded; the argument keeps its original casing.
        var result = FtpRequest.ParseCommandLine("RETR MixedCase.TXT");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("MixedCase.TXT", result.Item2);
    }

    #endregion

    #region Single argument, simple happy path

    [TestMethod]
    public void ParseCommandLine_VerbAndArg_Splits()
    {
        var result = FtpRequest.ParseCommandLine("USER fox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    #endregion

    #region Multiple / duplicate delimiters collapse (documents silent whitespace loss)

    [TestMethod]
    public void ParseCommandLine_MultipleSpacesBetweenVerbAndArg_Collapsed()
    {
        var result = FtpRequest.ParseCommandLine("USER      fox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_TabBetweenVerbAndArg_TreatedAsDelimiter()
    {
        var result = FtpRequest.ParseCommandLine("USER\tfox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_FormFeedBetweenTokens_TreatedAsDelimiter()
    {
        var result = FtpRequest.ParseCommandLine("USER\ffox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_BareCrBetweenTokens_TreatedAsDelimiter()
    {
        var result = FtpRequest.ParseCommandLine("USER\rfox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_BareLfBetweenTokens_TreatedAsDelimiter()
    {
        var result = FtpRequest.ParseCommandLine("USER\nfox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_MultiArgWithDoubleSpaces_CollapsesToSingleSpace()
    {
        // This is the silent-data-loss defect: an argument that legitimately contains a run of
        // spaces is rejoined with exactly one space, so "my  file.txt" becomes "my file.txt".
        var result = FtpRequest.ParseCommandLine("RETR my  file.txt");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("my file.txt", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_ArgumentEmbeddedTab_LostAsSpace()
    {
        // A tab embedded inside a pathname is destroyed the same way (converted to a single space).
        var result = FtpRequest.ParseCommandLine("RETR a\tb.txt");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("a b.txt", result.Item2);
    }

    #endregion

    #region Leading / trailing junk

    [TestMethod]
    public void ParseCommandLine_LeadingSpaces_Trimmed()
    {
        var result = FtpRequest.ParseCommandLine("   USER fox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_TrailingSpaces_Trimmed()
    {
        var result = FtpRequest.ParseCommandLine("USER fox     ");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_TrailingCrLf_Trimmed()
    {
        var result = FtpRequest.ParseCommandLine("USER fox\r\n");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_LeadingCrLf_Trimmed()
    {
        var result = FtpRequest.ParseCommandLine("\r\nUSER fox");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    #endregion

    #region Multi-token arguments rejoined with a single space

    [TestMethod]
    public void ParseCommandLine_ThreeArgTokens_RejoinedWithSpace()
    {
        var result = FtpRequest.ParseCommandLine("RETR a b c");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("a b c", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_SiteCommandWithSubArgs_Rejoined()
    {
        var result = FtpRequest.ParseCommandLine("SITE  CHMOD   777   file");

        Assert.AreEqual("SITE", result.Item1);
        Assert.AreEqual("CHMOD 777 file", result.Item2);
    }

    #endregion

    #region Args containing response-code-like and control text

    [TestMethod]
    public void ParseCommandLine_ArgsLookLikeResponseCodes_PreservedVerbatim()
    {
        // Response-code-like text in the argument is data, not interpreted; it survives unchanged
        // (aside from the whitespace collapse) inside the arguments string.
        var result = FtpRequest.ParseCommandLine("RETR 220 500 EVIL");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("220 500 EVIL", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_EmbeddedCrLfSmuggle_FlattenedNotPreserved()
    {
        // A CRLF-smuggled second command is NOT preserved as a newline: the CR and LF act as token
        // delimiters, so the smuggled verb ends up as extra whitespace-joined argument tokens on the
        // first command's argument line rather than a distinct control line.
        var result = FtpRequest.ParseCommandLine("USER fox\r\nRETR /etc/passwd");

        Assert.AreEqual("USER", result.Item1);
        Assert.AreEqual("fox RETR /etc/passwd", result.Item2);
        Assert.IsFalse(result.Item2.Contains('\r'));
        Assert.IsFalse(result.Item2.Contains('\n'));
    }

    #endregion

    #region Non-HTML-whitespace is NOT a delimiter (VTAB, NUL, NBSP, unicode spaces)

    [TestMethod]
    public void ParseCommandLine_VerticalTab_NotADelimiter()
    {
        // Vertical tab (0x0B) is not in AngleSharp's space set, so the whole run stays one token and
        // no argument is produced.
        var result = FtpRequest.ParseCommandLine("USER\vfox");

        Assert.AreEqual("USER\vfox".ToUpper(), result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_EmbeddedNul_NotADelimiter()
    {
        // An embedded NUL does not split, so a hostile NUL-bearing token flows through as one verb.
        var result = FtpRequest.ParseCommandLine("USER\0fox");

        Assert.AreEqual("USER\0fox".ToUpper(), result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
        Assert.IsTrue(result.Item1.Contains('\0'));
    }

    [TestMethod]
    public void ParseCommandLine_LoneNul_DoesNotThrowAndYieldsNulVerb()
    {
        // A line that is only a NUL is a single (non-space) token, so it does NOT hit the "garbage"
        // throw: it returns a one-character verb of NUL with empty args.
        var result = FtpRequest.ParseCommandLine("\0");

        Assert.AreEqual("\0", result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_NonBreakingSpace_NotADelimiter()
    {
        // U+00A0 (NBSP) looks like a space but is not a delimiter, so "USER<NBSP>fox" is one token.
        var result = FtpRequest.ParseCommandLine("USER fox");

        Assert.AreEqual("USER fox".ToUpper(), result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_UnicodeEmSpace_NotADelimiter()
    {
        // U+2003 (EM SPACE) is likewise not split.
        var result = FtpRequest.ParseCommandLine("USER fox");

        Assert.AreEqual("USER fox".ToUpper(), result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    #endregion

    #region Unicode verbs and arguments

    [TestMethod]
    public void ParseCommandLine_UnicodeVerb_UpperCasedByCulture()
    {
        // Compare against the same ToUpper() the implementation uses so the assertion is culture
        // agnostic; the point is that a non-ASCII verb is accepted and folded, not rejected.
        var result = FtpRequest.ParseCommandLine("café fox");

        Assert.AreEqual("café".ToUpper(), result.Item1);
        Assert.AreEqual("fox", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_UnicodeArgument_Preserved()
    {
        var result = FtpRequest.ParseCommandLine("RETR café.txt");

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual("café.txt", result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_EmojiArgument_Preserved()
    {
        var result = FtpRequest.ParseCommandLine("STOR \U0001F98A.dat");

        Assert.AreEqual("STOR", result.Item1);
        Assert.AreEqual("\U0001F98A.dat", result.Item2);
    }

    #endregion

    #region Length / boundary

    [TestMethod]
    public void ParseCommandLine_VeryLongArgument_ReturnedIntact()
    {
        var bigArg = new string('a', 5000);
        var result = FtpRequest.ParseCommandLine("RETR " + bigArg);

        Assert.AreEqual("RETR", result.Item1);
        Assert.AreEqual(bigArg, result.Item2);
        Assert.AreEqual(5000, result.Item2.Length);
    }

    [TestMethod]
    public void ParseCommandLine_VeryLongVerb_UpperCasedIntact()
    {
        var bigVerb = new string('x', 4096);
        var result = FtpRequest.ParseCommandLine(bigVerb);

        Assert.AreEqual(bigVerb.ToUpper(), result.Item1);
        Assert.AreEqual(string.Empty, result.Item2);
    }

    [TestMethod]
    public void ParseCommandLine_NumericVerb_PassesThrough()
    {
        var result = FtpRequest.ParseCommandLine("1234 payload");

        Assert.AreEqual("1234", result.Item1);
        Assert.AreEqual("payload", result.Item2);
    }

    #endregion

    #region Verb matches FtpCommand constants after folding

    [TestMethod]
    public void ParseCommandLine_LowerCaseKnownVerbs_MatchConstants()
    {
        Assert.AreEqual(FtpCommand.AuthenticationUsername, FtpRequest.ParseCommandLine("user fox").Item1);
        Assert.AreEqual(FtpCommand.Quit, FtpRequest.ParseCommandLine("quit").Item1);
        Assert.AreEqual(FtpCommand.Open, FtpRequest.ParseCommandLine("open ftp://host.example").Item1);
        Assert.AreEqual(FtpCommand.RetrieveFile, FtpRequest.ParseCommandLine("retr file").Item1);
        Assert.AreEqual(FtpCommand.PrintWorkingDirectory, FtpRequest.ParseCommandLine("pwd").Item1);
        Assert.AreEqual(FtpCommand.Bark, FtpRequest.ParseCommandLine("arf").Item1);
    }

    #endregion
}

[TestClass]
public class FtpFormatResponseTests
{
    [TestMethod]
    public void FormatResponse_SimpleText_CodeSpaceTextCrLf()
    {
        Assert.AreEqual("200 OK\r\n", FtpRequest.FormatResponse(FtpResponseCode.CommandSuccess, "OK"));
    }

    [TestMethod]
    public void FormatResponse_UsesNumericEnumValue()
    {
        Assert.AreEqual("220 Welcome\r\n", FtpRequest.FormatResponse(FtpResponseCode.ServiceReadyForNewUser, "Welcome"));
        Assert.AreEqual("550 No\r\n", FtpRequest.FormatResponse(FtpResponseCode.RequestedActionNotTaken, "No"));
        Assert.AreEqual("331 Password required\r\n", FtpRequest.FormatResponse(FtpResponseCode.UsernameOkay, "Password required"));
    }

    [TestMethod]
    public void FormatResponse_EmptyArgs_KeepsSeparatorSpace()
    {
        // Note the space between code and CRLF even with empty text: "200 \r\n".
        Assert.AreEqual("200 \r\n", FtpRequest.FormatResponse(FtpResponseCode.CommandSuccess, ""));
    }

    [TestMethod]
    public void FormatResponse_NullArgs_RendersAsEmpty()
    {
        Assert.AreEqual("200 \r\n", FtpRequest.FormatResponse(FtpResponseCode.CommandSuccess, null));
    }

    [TestMethod]
    public void FormatResponse_AlwaysTerminatesWithCrLf()
    {
        Assert.IsTrue(FtpRequest.FormatResponse(FtpResponseCode.SystemStatus, "anything").EndsWith("\r\n"));
    }

    [TestMethod]
    public void FormatResponse_UnicodeText_Preserved()
    {
        Assert.AreEqual("214 héllo\r\n", FtpRequest.FormatResponse(FtpResponseCode.HelpMessage, "héllo"));
    }

    [TestMethod]
    public void FormatResponse_UndefinedEnumValue_CastsRawInteger()
    {
        // A value with no enum member still casts to its underlying int; no validation is performed.
        Assert.AreEqual("999 x\r\n", FtpRequest.FormatResponse((FtpResponseCode)999, "x"));
    }

    [TestMethod]
    public void FormatResponse_CrLfInArgs_IsStrippedToSingleLine()
    {
        // FormatResponse now strips embedded CR/LF, so a CRLF-bearing argument can no longer forge a
        // second control line: the whole thing collapses onto one response line.
        var result = FtpRequest.FormatResponse(FtpResponseCode.ServiceReadyForNewUser, "hi\r\n500 EVIL");

        Assert.AreEqual("220 hi500 EVIL\r\n", result);

        var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(1, lines.Length);
    }

    [TestMethod]
    public void FormatResponse_BareLfInArgs_IsStripped()
    {
        Assert.AreEqual("200 ab\r\n", FtpRequest.FormatResponse(FtpResponseCode.CommandSuccess, "a\nb"));
    }
}

[TestClass]
public class FtpFormatFeatureListTests
{
    [TestMethod]
    public void FormatFeatureList_TwoFeatures_ExactShape()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new[] { "UTF8", "MLST" });

        Assert.AreEqual("211-Features\r\n UTF8\r\n MLST\r\n211 End\r\n", result);
    }

    [TestMethod]
    public void FormatFeatureList_SingleFeature_ExactShape()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new[] { "SIZE" });

        Assert.AreEqual("211-Features\r\n SIZE\r\n211 End\r\n", result);
    }

    [TestMethod]
    public void FormatFeatureList_EmptyArray_HeaderThenEnd()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, System.Array.Empty<string>());

        Assert.AreEqual("211-Features\r\n211 End\r\n", result);
    }

    [TestMethod]
    public void FormatFeatureList_NullArray_ThrowsNullReference()
    {
        Assert.ThrowsExactly<NullReferenceException>(() => FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, null));
    }

    [TestMethod]
    public void FormatFeatureList_NullElement_RendersAsBlankFeatureLine()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new string[] { null });

        Assert.AreEqual("211-Features\r\n \r\n211 End\r\n", result);
    }

    [TestMethod]
    public void FormatFeatureList_UsesNumericEnumValueForHeaderAndFooter()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.HelpMessage, new[] { "A" });

        Assert.IsTrue(result.StartsWith("214-Features\r\n"));
        Assert.IsTrue(result.EndsWith("214 End\r\n"));
    }

    [TestMethod]
    public void FormatFeatureList_CrLfInFeature_IsStripped()
    {
        // Feature strings are sanitized too, so a CRLF inside a feature token no longer forges extra
        // control lines inside the FEAT reply.
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new[] { "UTF8\r\n230 Logged in" });

        Assert.AreEqual("211-Features\r\n UTF8230 Logged in\r\n211 End\r\n", result);
        Assert.IsFalse(result.Contains("\r\n230 Logged in\r\n"));
    }

    [TestMethod]
    public void FormatFeatureList_UnicodeFeature_Preserved()
    {
        var result = FtpRequest.FormatFeatureList(FtpResponseCode.SystemStatus, new[] { "ünïcödé" });

        Assert.AreEqual("211-Features\r\n ünïcödé\r\n211 End\r\n", result);
    }
}

[TestClass]
public class FtpCommandConstantTests
{
    [TestMethod]
    public void CommandConstants_HaveExpectedRfcValues()
    {
        Assert.AreEqual("ABOR", FtpCommand.AbortActiveFileTransfer);
        Assert.AreEqual("USER", FtpCommand.AuthenticationUsername);
        Assert.AreEqual("SITE", FtpCommand.Site);
        Assert.AreEqual("OPEN", FtpCommand.Open);
        Assert.AreEqual("SYST", FtpCommand.SystemType);
        Assert.AreEqual("FEAT", FtpCommand.FeatureList);
        Assert.AreEqual("PWD", FtpCommand.PrintWorkingDirectory);
        Assert.AreEqual("TYPE", FtpCommand.TransferMode);
        Assert.AreEqual("PASV", FtpCommand.PassiveMode);
        Assert.AreEqual("LIST", FtpCommand.ListInfo);
        Assert.AreEqual("QUIT", FtpCommand.Quit);
        Assert.AreEqual("CWD", FtpCommand.ChangeWorkingDirectory);
        Assert.AreEqual("MKD", FtpCommand.MakeDirectory);
        Assert.AreEqual("RETR", FtpCommand.RetrieveFile);
        Assert.AreEqual("STOR", FtpCommand.StoreFile);
        Assert.AreEqual("REST", FtpCommand.RestartTransfer);
        Assert.AreEqual("DELE", FtpCommand.DeleteFile);
        Assert.AreEqual("RNFR", FtpCommand.RenameFrom);
        Assert.AreEqual("RNTO", FtpCommand.RenameTo);
        Assert.AreEqual("ARF", FtpCommand.Bark);
        Assert.AreEqual("RMD", FtpCommand.DeleteDirectory);
    }

    [TestMethod]
    public void CommandConstants_AreAllUpperCase_SoFoldedVerbsCanMatch()
    {
        // ParseCommandLine upper-cases the verb, so every command constant must already be upper case
        // for the equality dispatch in the processor to work.
        var commands = new[]
        {
            FtpCommand.AbortActiveFileTransfer, FtpCommand.AuthenticationUsername, FtpCommand.Site,
            FtpCommand.Open, FtpCommand.SystemType, FtpCommand.FeatureList, FtpCommand.PrintWorkingDirectory,
            FtpCommand.TransferMode, FtpCommand.PassiveMode, FtpCommand.ListInfo, FtpCommand.Quit,
            FtpCommand.ChangeWorkingDirectory, FtpCommand.MakeDirectory, FtpCommand.RetrieveFile,
            FtpCommand.StoreFile, FtpCommand.RestartTransfer, FtpCommand.DeleteFile, FtpCommand.RenameFrom,
            FtpCommand.RenameTo, FtpCommand.Bark, FtpCommand.DeleteDirectory
        };

        foreach (var command in commands)
        {
            Assert.AreEqual(command.ToUpper(), command, $"Command constant '{command}' is not upper case");
        }
    }
}

#pragma warning restore MSTEST0025