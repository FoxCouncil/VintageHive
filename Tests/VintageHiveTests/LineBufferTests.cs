// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// POP3, SMTP, IMAP, NNTP and IRC each carried their own copy of the CRLF carry-buffer, differing only in the
// DataBag key and the cap. That is where the pipelining and split-command bugs lived, and it had to be fixed
// five separate times - while FTP, the sixth line-based protocol, never got the fix at all. LineBuffer owns
// the mechanics now so the next fix lands once.
//
// These test the primitive directly rather than through five protocols, which is the point of extracting it.

using System.Text;
using VintageHive.Network;

namespace Adversarial5.LineBuffering;

[TestClass]
public class LineBufferTests
{
    private static ListenerSocket NewSocket() => new();

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [TestMethod]
    public void PipelinedCommandsAreAllReturned()
    {
        var socket = NewSocket();

        var bytes = Ascii("ONE\r\nTWO\r\nTHREE\r\n");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length);

        Assert.IsTrue(buffer.TryReadLine(out var first));
        Assert.AreEqual("ONE", first);
        Assert.IsTrue(buffer.TryReadLine(out var second));
        Assert.AreEqual("TWO", second);
        Assert.IsTrue(buffer.TryReadLine(out var third));
        Assert.AreEqual("THREE", third);
        Assert.IsFalse(buffer.TryReadLine(out _));
    }

    // The split-command case: the remainder has to survive to the next read, which is what Save/Open do.
    [TestMethod]
    public void APartialLineCarriesToTheNextRead()
    {
        var socket = NewSocket();

        var firstRead = Ascii("RETR some");

        var buffer = LineBuffer.Open(socket, "k", firstRead, firstRead.Length);

        Assert.IsFalse(buffer.TryReadLine(out _), "A line with no terminator must not be returned as complete.");

        buffer.Save();

        var secondRead = Ascii("file.txt\r\n");

        var resumed = LineBuffer.Open(socket, "k", secondRead, secondRead.Length);

        Assert.IsTrue(resumed.TryReadLine(out var line));
        Assert.AreEqual("RETR somefile.txt", line, "The carried remainder was lost between reads.");
    }

    [TestMethod]
    public void BareLfIsAcceptedAndCarriageReturnsAreTrimmed()
    {
        var socket = NewSocket();

        var bytes = Ascii("NOOP\nQUIT\r\n");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length);

        Assert.IsTrue(buffer.TryReadLine(out var first));
        Assert.AreEqual("NOOP", first);
        Assert.IsTrue(buffer.TryReadLine(out var second));
        Assert.AreEqual("QUIT", second);
    }

    // The cap is what stops a peer that never sends a terminator from growing this without bound.
    [TestMethod]
    public void AnUnterminatedFloodIsDroppedAtTheCap()
    {
        var socket = NewSocket();

        var bytes = Ascii(new string('A', 128));

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length, maxLineBytes: 64);

        buffer.Save();

        Assert.AreEqual(string.Empty, socket.DataBag["k"], "An unterminated line past the cap should be dropped rather than accumulated.");
    }

    [TestMethod]
    public void AnUnterminatedLineUnderTheCapIsKept()
    {
        var socket = NewSocket();

        var bytes = Ascii("PARTIAL");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length, maxLineBytes: 64);

        buffer.Save();

        Assert.AreEqual("PARTIAL", socket.DataBag["k"]);
    }

    // IMAP's APPEND literal is byte-counted message data full of CRLFs, so it has to come off the buffer
    // WITHOUT line splitting.
    [TestMethod]
    public void RawTakeBypassesLineSplitting()
    {
        var socket = NewSocket();

        var bytes = Ascii("AB\r\nCD\r\nTAG OK\r\n");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length);

        var literal = buffer.TakeRaw(8);

        Assert.AreEqual("AB\r\nCD\r\n", literal, "A byte-counted literal must come back verbatim, CRLFs included.");

        Assert.IsTrue(buffer.TryReadLine(out var line));
        Assert.AreEqual("TAG OK", line, "Line reading must resume correctly after a raw take.");
    }

    // SMTP's DATA handover: everything still buffered stops being commands and becomes body.
    [TestMethod]
    public void TakeRestDrainsTheRemainder()
    {
        var socket = NewSocket();

        var bytes = Ascii("DATA\r\nbody line one\r\nbody line two\r\n");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length);

        Assert.IsTrue(buffer.TryReadLine(out var command));
        Assert.AreEqual("DATA", command);

        Assert.AreEqual("body line one\r\nbody line two\r\n", buffer.TakeRest());
        Assert.AreEqual(string.Empty, buffer.Pending);
    }

    // IRC decodes UTF-8 while the mail and news protocols are ASCII, so the encoding is a parameter.
    [TestMethod]
    public void TheEncodingIsHonoured()
    {
        var socket = NewSocket();

        var bytes = Encoding.UTF8.GetBytes("PRIVMSG #hive :café ☃\r\n");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length, encoding: Encoding.UTF8);

        Assert.IsTrue(buffer.TryReadLine(out var line));
        Assert.AreEqual("PRIVMSG #hive :café ☃", line, "UTF-8 content was mangled, which would corrupt IRC messages.");
    }

    [TestMethod]
    public void ClearDropsEverything()
    {
        var socket = NewSocket();

        var bytes = Ascii("LEFTOVER");

        var buffer = LineBuffer.Open(socket, "k", bytes, bytes.Length);

        buffer.Clear();

        Assert.AreEqual(string.Empty, buffer.Pending);
        Assert.AreEqual(string.Empty, socket.DataBag["k"]);
    }
}
