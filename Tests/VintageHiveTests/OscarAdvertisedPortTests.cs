// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

// OSCAR, MMS and PNA were the only TCP servers with hardcoded ports and no Port* config key. OSCAR was the
// dangerous one, because it does not just LISTEN on 5190 - it tells the client to reconnect there, in four
// separate redirect TLVs that each interpolated the literal ":5190". Adding a PortOscar key the ordinary way
// would have moved the listener and left those four redirects pointing at a port nothing was bound to, so
// sign-on would break at the BOS handoff with no error anywhere near the cause.
//
// This pins the property that stops that: the advertised port follows the port the server was constructed
// with. If someone reintroduces a literal, the second assertion fails.

using System.Net;
using VintageHive.Data.Types;
using VintageHive.Proxy.Oscar;

namespace Adversarial5.OscarPort;

[TestClass]
public class OscarAdvertisedPortTests
{
    [TestMethod]
    public void TheAdvertisedPortFollowsTheListenPort()
    {
        _ = new OscarServer(IPAddress.Loopback, 15190);

        Assert.AreEqual(15190, OscarServer.AdvertisedPort, "The redirect TLVs would still point at the old port, so clients could not complete the BOS handoff.");

        // Back to the period default so ordering cannot affect other tests.
        _ = new OscarServer(IPAddress.Loopback, 5190);

        Assert.AreEqual(5190, OscarServer.AdvertisedPort);
    }

    // The new keys must have registered defaults, or ConfigGet returns 0 and the listener binds to an
    // ephemeral port - a service that silently moves rather than one that fails loudly.
    [TestMethod]
    public void TheNewPortKeysHaveRegisteredDefaults()
    {
        Mail.MailTestEnv.Ensure();

        Assert.AreEqual(5190, VintageHive.Mind.Db.ConfigGet<int>(ConfigNames.PortOscar), "PortOscar has no registered default, so OSCAR would bind to an ephemeral port.");
        Assert.AreEqual(1755, VintageHive.Mind.Db.ConfigGet<int>(ConfigNames.PortMms), "PortMms has no registered default.");
        Assert.AreEqual(7070, VintageHive.Mind.Db.ConfigGet<int>(ConfigNames.PortPna), "PortPna has no registered default.");
    }
}
