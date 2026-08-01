// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// FTP was the one line-based protocol that never received the carry-buffer fix its five siblings all have.
// Its control session is interactive rather than per-read - FtpRequest.Parse consumes the opening command and
// then drives the conversation itself through FetchCommand - so both halves treated one TCP read as exactly
// one command. That broke two ways: a command split across reads was truncated, and a pipelined
// "USER x\r\nPASS y\r\n" had its username parsed as "x\r\nPASS" before FetchCommand blocked forever waiting
// for a PASS that had already arrived and been discarded.
//
// These drive Request's buffered reader directly over a loopback socket pair, which is where the framing
// actually lives now.

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Network;

namespace Adversarial5.FtpFraming;

[TestClass]
public class FtpCommandFramingTests
{
    private sealed class Wire : IDisposable
    {
        public TcpClient Client { get; }

        public Request Request { get; }

        private readonly TcpClient _serverSide;

        public Wire()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            Client = new TcpClient();
            Client.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            _serverSide = listener.AcceptTcpClient();

            listener.Stop();

            Request = new Request
            {
                IsValid = true,
                Encoding = Encoding.ASCII,
                ListenerSocket = new ListenerSocket
                {
                    RawSocket = _serverSide.Client,
                    Stream = _serverSide.GetStream(),
                },
            };
        }

        public void Send(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);

            Client.GetStream().Write(bytes, 0, bytes.Length);
            Client.GetStream().Flush();
        }

        public void Dispose()
        {
            try { Client.Close(); } catch { }
            try { _serverSide.Close(); } catch { }
        }
    }

    // The deadlock case. Both commands arrive in one packet; the second must come out of the buffer rather
    // than off a socket that will never carry it again.
    [TestMethod]
    [Timeout(20000)]
    public async Task TwoPipelinedCommands_AreReadAsTwoCommands()
    {
        using var wire = new Wire();

        wire.Send("USER fox\r\nPASS secret\r\n");

        Assert.AreEqual("USER fox", await wire.Request.ReadCommandLineAsync());
        Assert.AreEqual("PASS secret", await wire.Request.ReadCommandLineAsync(), "The pipelined second command was lost; this is the read that used to block forever.");
    }

    // The split case: one command arriving across two reads must not be truncated at the read boundary.
    [TestMethod]
    [Timeout(20000)]
    public async Task ACommandSplitAcrossReads_IsReassembled()
    {
        using var wire = new Wire();

        wire.Send("RETR some");

        var pending = wire.Request.ReadCommandLineAsync();

        Assert.IsFalse(pending.IsCompleted, "A command with no terminator yet should not have been returned as complete.");

        wire.Send("file.txt\r\n");

        Assert.AreEqual("RETR somefile.txt", await pending);
    }

    // Seeding is how the opening command and anything pipelined behind it enter the same buffer that later
    // reads draw from. Without it the two halves would disagree about what had already been consumed.
    [TestMethod]
    [Timeout(20000)]
    public async Task SeededBytes_AreConsumedBeforeTheSocketIsRead()
    {
        using var wire = new Wire();

        wire.Request.SeedCommandBuffer("OPEN ftp.example.com\r\nCWD /pub\r\n");

        Assert.AreEqual("OPEN ftp.example.com", await wire.Request.ReadCommandLineAsync());
        Assert.AreEqual("CWD /pub", await wire.Request.ReadCommandLineAsync());
    }

    [TestMethod]
    [Timeout(20000)]
    public async Task BareLfIsAcceptedAndCarriageReturnsAreTrimmed()
    {
        using var wire = new Wire();

        wire.Send("NOOP\nQUIT\r\n");

        Assert.AreEqual("NOOP", await wire.Request.ReadCommandLineAsync());
        Assert.AreEqual("QUIT", await wire.Request.ReadCommandLineAsync());
    }

    // A peer that opens a connection and streams without ever sending a terminator must not be able to grow
    // the buffer without bound.
    [TestMethod]
    [Timeout(30000)]
    public async Task AnUnterminatedFlood_IsBoundedRatherThanBufferedForever()
    {
        using var wire = new Wire();

        wire.Send(new string('A', 9 * 1024));

        Assert.AreEqual(string.Empty, await wire.Request.ReadCommandLineAsync(), "An unterminated flood past the line cap should be dropped, not accumulated.");
    }
}
