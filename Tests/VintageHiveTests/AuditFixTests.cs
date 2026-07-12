// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using VintageHive;
using VintageHive.Data.Contexts;
using VintageHive.Data.Types;
using VintageHive.Network;
using VintageHive.Proxy.Oscar;
using VintageHive.Proxy.Oscar.Services;
using VintageHive.Utilities;

namespace AuditFixes;

internal static class AuditTestDb
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "vfs", "data"));

            if (Mind.Db == null)
            {
                var setter = typeof(Mind).GetProperty(nameof(Mind.Db))!.GetSetMethod(nonPublic: true)!;
                setter.Invoke(null, new object[] { new HiveDbContext() });
            }

            // "icqaim" exercises the AIM (FNV-hashed) UIN path; "778899" the numeric ICQ path. Names are
            // deliberately not "fox"/"alice"/"bob" so they don't collide with other suites' expected nicks.
            foreach (var user in new[] { "icqaim", "778899" })
            {
                if (!Mind.Db!.UserExistsByUsername(user))
                {
                    Mind.Db.UserCreate(user, "secret");
                }
            }

            _ready = true;
        }
    }
}

[TestClass]
public class OscarIcqUinTests
{
    [TestMethod]
    public void ResolveUin_AimName_RoundTripsThroughFnv()
    {
        AuditTestDb.Ensure();

        var uin = OscarIcqService.ScreenNameToUin("icqaim");

        Assert.AreEqual("icqaim", OscarIcqService.ResolveUinToScreenName(uin), "an AIM name's UIN must resolve back to that name");
    }

    [TestMethod]
    public void ResolveUin_NumericName_IsItsOwnUin()
    {
        AuditTestDb.Ensure();

        Assert.AreEqual("778899", OscarIcqService.ResolveUinToScreenName(778899));
    }

    [TestMethod]
    public void ResolveUin_ZeroAndUnknown_ReturnNull()
    {
        AuditTestDb.Ensure();

        Assert.IsNull(OscarIcqService.ResolveUinToScreenName(0));
        Assert.IsNull(OscarIcqService.ResolveUinToScreenName(4293918720), "an unassigned UIN must not resolve to any account");
    }
}

[TestClass]
public class ConfigDefaultConversionTests
{
    // The units defaults are boxed as enums but read back as strings; the fix converts instead of hard-casting,
    // which used to throw InvalidCastException on the first read against a fresh database.
    [TestMethod]
    public void ConfigGet_TemperatureUnitsDefault_ReadsAsStringWithoutThrowing()
    {
        AuditTestDb.Ensure();

        var expected = WeatherUtils.TemperatureUnits.Celsius.ToString();

        Mind.Db!.ConfigSet<string>(ConfigNames.TemperatureUnits, null); // remove -> force the default branch

        Assert.AreEqual(expected, Mind.Db.ConfigGet<string>(ConfigNames.TemperatureUnits));
    }

    [TestMethod]
    public void ConfigGet_DistanceUnitsDefault_ReadsAsStringWithoutThrowing()
    {
        AuditTestDb.Ensure();

        var expected = WeatherUtils.DistanceUnits.Metric.ToString();

        Mind.Db!.ConfigSet<string>(ConfigNames.DistanceUnits, null);

        Assert.AreEqual(expected, Mind.Db.ConfigGet<string>(ConfigNames.DistanceUnits));
    }
}

[TestClass]
public class OscarInvisibilityTests
{
    private const ushort FamilyBuddy = 0x0003;
    private const ushort SrvUserOnline = 0x000B;
    private const ushort SrvUserOffline = 0x000C;

    // A loopback OscarSession whose Client is the server end; reads on the returned stream see what the server
    // sent to that session.
    private static NetworkStream MakeWatcher(string screenName, out OscarSession session)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var acceptTask = listener.AcceptSocketAsync();

        client.Connect(IPAddress.Loopback, port);

        var serverSocket = acceptTask.GetAwaiter().GetResult();

        listener.Stop();

        session = new OscarSession(new ListenerSocket { RawSocket = serverSocket, Stream = new NetworkStream(serverSocket) })
        {
            ScreenName = screenName,
        };

        return new NetworkStream(client) { ReadTimeout = 3000 };
    }

    private static (ushort family, ushort subtype) ReadSnacHeader(NetworkStream stream)
    {
        var flapHeader = ReadExact(stream, 6); // '*', type, seq(2), datalen(2)
        var dataLen = (flapHeader[4] << 8) | flapHeader[5];
        var data = ReadExact(stream, dataLen);

        return ((ushort)((data[0] << 8) | data[1]), (ushort)((data[2] << 8) | data[3]));
    }

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);

            if (read <= 0)
            {
                throw new IOException("connection closed mid-frame");
            }

            offset += read;
        }

        return buffer;
    }

    private static async Task WithCleanSessions(Func<Task> body)
    {
        var snapshot = OscarServer.Sessions.ToArray();

        OscarServer.Sessions.Clear();

        try
        {
            await body();
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
    [Timeout(15000)]
    public async Task Broadcast_InvisibleUser_SendsOfflineToWatchers()
    {
        await WithCleanSessions(async () =>
        {
            var read = MakeWatcher("bob", out var bob);
            bob.Buddies = new List<string> { "fox" };
            OscarServer.Sessions[bob.ID] = bob;

            var fox = new OscarSession { ScreenName = "fox", Status = OscarSessionOnlineStatus.Invisible };

            await fox.BroadcastStatusToWatchers();

            var (family, subtype) = ReadSnacHeader(read);

            Assert.AreEqual(FamilyBuddy, family);
            Assert.AreEqual(SrvUserOffline, subtype, "an invisible user must be broadcast as SRV_USER_OFFLINE");

            read.Dispose();
            bob.Client.RawSocket.Dispose();
        });
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task Broadcast_VisibleUser_SendsOnlineToWatchers()
    {
        await WithCleanSessions(async () =>
        {
            var read = MakeWatcher("bob", out var bob);
            bob.Buddies = new List<string> { "fox" };
            OscarServer.Sessions[bob.ID] = bob;

            var fox = new OscarSession { ScreenName = "fox", Status = OscarSessionOnlineStatus.Online };

            await fox.BroadcastStatusToWatchers();

            var (family, subtype) = ReadSnacHeader(read);

            Assert.AreEqual(FamilyBuddy, family);
            Assert.AreEqual(SrvUserOnline, subtype, "a visible user must be broadcast as SRV_USER_ONLINE");

            read.Dispose();
            bob.Client.RawSocket.Dispose();
        });
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task SendUserOnline_SuppressesInvisibleButAnnouncesOnline()
    {
        await WithCleanSessions(async () =>
        {
            var read = MakeWatcher("bob", out var bob);

            var invisible = new OscarSession { ScreenName = "fox", Status = OscarSessionOnlineStatus.Invisible };

            await OscarBuddyListService.SendUserOnline(bob, invisible);

            read.ReadTimeout = 400;

            var suppressed = false;

            try
            {
                read.ReadByte();
            }
            catch (IOException)
            {
                suppressed = true;
            }

            Assert.IsTrue(suppressed, "an invisible user must not be announced online on the buddy-list sync path");

            var online = new OscarSession { ScreenName = "fox", Status = OscarSessionOnlineStatus.Online };

            await OscarBuddyListService.SendUserOnline(bob, online);

            read.ReadTimeout = 3000;
            var (family, subtype) = ReadSnacHeader(read);

            Assert.AreEqual(FamilyBuddy, family);
            Assert.AreEqual(SrvUserOnline, subtype, "an online user must still be announced");

            read.Dispose();
            bob.Client.RawSocket.Dispose();
        });
    }
}
