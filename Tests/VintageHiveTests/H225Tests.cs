// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.H225;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  H225Types tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class H225TransportAddressTests
{
    [TestMethod]
    public void TransportAddress_RoundTrip()
    {
        var ep = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 1720);

        var enc = new PerEncoder();
        H225Types.WriteTransportAddress(enc, ep);
        var bytes = enc.ToArray();

        var dec = new PerDecoder(bytes);
        var result = H225Types.ReadTransportAddress(dec);

        Assert.AreEqual(ep.Address, result.Address);
        Assert.AreEqual(ep.Port, result.Port);
    }

    [TestMethod]
    public void TransportAddress_Loopback()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 0);

        var enc = new PerEncoder();
        H225Types.WriteTransportAddress(enc, ep);

        var dec = new PerDecoder(enc.ToArray());
        var result = H225Types.ReadTransportAddress(dec);

        Assert.AreEqual(IPAddress.Loopback, result.Address);
        Assert.AreEqual(0, result.Port);
    }

    [TestMethod]
    public void TransportAddress_MaxPort()
    {
        var ep = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 65535);

        var enc = new PerEncoder();
        H225Types.WriteTransportAddress(enc, ep);

        var dec = new PerDecoder(enc.ToArray());
        var result = H225Types.ReadTransportAddress(dec);

        Assert.AreEqual(65535, result.Port);
    }

    [TestMethod]
    public void TransportAddresses_RoundTrip()
    {
        var eps = new[]
        {
            new IPEndPoint(IPAddress.Parse("192.168.1.1"), 1720),
            new IPEndPoint(IPAddress.Parse("10.0.0.2"), 1721)
        };

        var enc = new PerEncoder();
        H225Types.WriteTransportAddresses(enc, eps);

        var dec = new PerDecoder(enc.ToArray());
        var result = H225Types.ReadTransportAddresses(dec);

        Assert.AreEqual(2, result.Length);
        Assert.AreEqual(eps[0].Address, result[0].Address);
        Assert.AreEqual(eps[0].Port, result[0].Port);
        Assert.AreEqual(eps[1].Address, result[1].Address);
        Assert.AreEqual(eps[1].Port, result[1].Port);
    }

    [TestMethod]
    public void TransportAddress_Skip()
    {
        var ep = new IPEndPoint(IPAddress.Parse("172.16.0.1"), 5060);

        var enc = new PerEncoder();
        H225Types.WriteTransportAddress(enc, ep);
        enc.WriteBoolean(true); // sentinel

        var dec = new PerDecoder(enc.ToArray());
        H225Types.SkipTransportAddress(dec);
        Assert.IsTrue(dec.ReadBoolean()); // sentinel intact
    }
}

[TestClass]
public class H225AliasAddressTests
{
    [TestMethod]
    public void AliasAddress_H323Id_RoundTrip()
    {
        var enc = new PerEncoder();
        H225Types.WriteAliasAddress(enc, "John Doe");

        var dec = new PerDecoder(enc.ToArray());
        var (alias, isE164) = H225Types.ReadAliasAddress(dec);

        Assert.AreEqual("John Doe", alias);
        Assert.IsFalse(isE164);
    }

    [TestMethod]
    public void AliasAddress_E164_RoundTrip()
    {
        var enc = new PerEncoder();
        H225Types.WriteAliasAddress(enc, "5551234", isE164: true);

        var dec = new PerDecoder(enc.ToArray());
        var (alias, isE164) = H225Types.ReadAliasAddress(dec);

        Assert.AreEqual("5551234", alias);
        Assert.IsTrue(isE164);
    }

    [TestMethod]
    public void AliasAddresses_RoundTrip()
    {
        var aliases = new[] { "Alice", "Bob" };

        var enc = new PerEncoder();
        H225Types.WriteAliasAddresses(enc, aliases);

        var dec = new PerDecoder(enc.ToArray());
        var result = H225Types.ReadAliasAddresses(dec);

        Assert.AreEqual(2, result.Length);
        Assert.AreEqual("Alice", result[0]);
        Assert.AreEqual("Bob", result[1]);
    }

    [TestMethod]
    public void AliasAddress_Skip()
    {
        var enc = new PerEncoder();
        H225Types.WriteAliasAddress(enc, "SkipMe");
        enc.WriteBoolean(true); // sentinel

        var dec = new PerDecoder(enc.ToArray());
        H225Types.SkipAliasAddress(dec);
        Assert.IsTrue(dec.ReadBoolean());
    }
}

[TestClass]
public class H225EndpointTypeTests
{
    [TestMethod]
    public void EndpointType_WriteAndSkip()
    {
        var enc = new PerEncoder();
        H225Types.WriteEndpointType(enc);
        enc.WriteBoolean(true); // sentinel

        var dec = new PerDecoder(enc.ToArray());
        H225Types.SkipEndpointType(dec);
        Assert.IsTrue(dec.ReadBoolean());
    }

    [TestMethod]
    public void EndpointType_NotTerminal()
    {
        var enc = new PerEncoder();
        H225Types.WriteEndpointType(enc, isTerminal: false);
        enc.WriteBoolean(true); // sentinel

        var dec = new PerDecoder(enc.ToArray());
        H225Types.SkipEndpointType(dec);
        Assert.IsTrue(dec.ReadBoolean());
    }
}

// ──────────────────────────────────────────────────────────
//  RasMessage codec tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RasCodecGatekeeperTests
{
    [TestMethod]
    public void GatekeeperConfirm_Encodes()
    {
        var rasAddr = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 1719);
        var bytes = RasCodec.EncodeGatekeeperConfirm(1, rasAddr, "VintageHive");

        Assert.IsNotNull(bytes);
        Assert.IsTrue(bytes.Length > 0);

        // Decode: should be a GCF (CHOICE index 1)
        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_GATEKEEPER_CONFIRM, choice);
    }

    [TestMethod]
    public void GatekeeperReject_Encodes()
    {
        var bytes = RasCodec.EncodeGatekeeperReject(1, H225Constants.GRJ_RESOURCE_UNAVAILABLE);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_GATEKEEPER_REJECT, choice);
    }

    [TestMethod]
    public void GatekeeperRequest_EncodeDecodeRoundTrip()
    {
        // Build a GRQ manually with PER
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_GATEKEEPER_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // GRQ SEQUENCE (extensible)
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, true, true); // nonStandard=no, gkId=yes, aliases=yes

        enc.WriteConstrainedWholeNumber(42, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // rasAddress
        H225Types.WriteTransportAddress(enc, new IPEndPoint(IPAddress.Parse("10.0.0.5"), 5001));

        // endpointType
        H225Types.WriteEndpointType(enc);

        // gatekeeperIdentifier
        enc.WriteBMPString("TestGK", lb: H225Constants.GK_ID_MIN, ub: H225Constants.GK_ID_MAX);

        // aliases
        H225Types.WriteAliasAddresses(enc, new[] { "NetMeetingUser" });

        var data = enc.ToArray();
        var msg = RasCodec.Decode(data);

        Assert.AreEqual(H225Constants.RAS_GATEKEEPER_REQUEST, msg.Type);
        Assert.AreEqual(42, msg.RequestSeqNum);
        Assert.IsNotNull(msg.Grq);
        Assert.AreEqual("TestGK", msg.Grq.GatekeeperIdentifier);
        Assert.AreEqual(1, msg.Grq.Aliases.Length);
        Assert.AreEqual("NetMeetingUser", msg.Grq.Aliases[0]);
        Assert.AreEqual(5001, msg.Grq.RasAddress.Port);
    }
}

[TestClass]
public class RasCodecRegistrationTests
{
    [TestMethod]
    public void RegistrationConfirm_Encodes()
    {
        var callSignal = new[] { new IPEndPoint(IPAddress.Loopback, 1720) };
        var bytes = RasCodec.EncodeRegistrationConfirm(1, callSignal, "VintageHive", "EP0001", 300);

        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_REGISTRATION_CONFIRM, choice);
    }

    [TestMethod]
    public void RegistrationReject_Encodes()
    {
        var bytes = RasCodec.EncodeRegistrationReject(5, H225Constants.RRJ_DUPLICATE_ALIAS);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_REGISTRATION_REJECT, choice);
    }

    [TestMethod]
    public void RegistrationRequest_EncodeDecodeRoundTrip()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_REGISTRATION_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // RRQ SEQUENCE (extensible) — we need extensions for timeToLive
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, true, false); // nonStandard=no, aliases=yes, gkId=no

        enc.WriteConstrainedWholeNumber(7, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        // discoveryComplete
        enc.WriteBoolean(true);

        // callSignalAddress
        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(IPAddress.Parse("192.168.1.50"), 1720) });

        // rasAddress
        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5001) });

        // terminalType (EndpointType)
        H225Types.WriteEndpointType(enc);

        // aliases
        H225Types.WriteAliasAddresses(enc, new[] { "Alice" });

        // endpointVendor (VendorIdentifier) — minimal
        enc.WriteExtensionBit(false); // no extensions
        enc.WriteOptionalBitmap(false, false); // no productId, no versionId
        // H221NonStandard
        enc.WriteConstrainedWholeNumber(181, 0, 255); // t35CountryCode (US=181)
        enc.WriteConstrainedWholeNumber(0, 0, 255);   // t35Extension
        enc.WriteConstrainedWholeNumber(21324, 0, 65535); // manufacturerCode (Microsoft)

        var data = enc.ToArray();
        var msg = RasCodec.Decode(data);

        Assert.AreEqual(H225Constants.RAS_REGISTRATION_REQUEST, msg.Type);
        Assert.AreEqual(7, msg.RequestSeqNum);
        Assert.IsNotNull(msg.Rrq);
        Assert.IsTrue(msg.Rrq.DiscoverComplete);
        Assert.AreEqual(1, msg.Rrq.CallSignalAddresses.Length);
        Assert.AreEqual(1720, msg.Rrq.CallSignalAddresses[0].Port);
        Assert.AreEqual(1, msg.Rrq.Aliases.Length);
        Assert.AreEqual("Alice", msg.Rrq.Aliases[0]);
    }
}

[TestClass]
public class RasCodecAdmissionTests
{
    [TestMethod]
    public void AdmissionConfirm_Encodes()
    {
        var dest = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 1720);
        var bytes = RasCodec.EncodeAdmissionConfirm(3, 640, dest);

        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_ADMISSION_CONFIRM, choice);
    }

    [TestMethod]
    public void AdmissionReject_Encodes()
    {
        var bytes = RasCodec.EncodeAdmissionReject(3, H225Constants.ARJ_CALLED_PARTY_NOT_REGISTERED);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_ADMISSION_REJECT, choice);
    }
}

[TestClass]
public class RasCodecDisengageTests
{
    [TestMethod]
    public void DisengageConfirm_Encodes()
    {
        var bytes = RasCodec.EncodeDisengageConfirm(10);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_DISENGAGE_CONFIRM, choice);
    }

    [TestMethod]
    public void DisengageRequest_EncodeDecodeRoundTrip()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_DISENGAGE_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // DRQ SEQUENCE (extensible)
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false); // nonStandardData

        enc.WriteConstrainedWholeNumber(55, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // endpointIdentifier
        enc.WriteBMPString("EP0001", lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        // conferenceID OCTET STRING (SIZE 16)
        enc.WriteOctetString(new byte[16], lb: 16, ub: 16);

        // callReferenceValue
        enc.WriteConstrainedWholeNumber(1, 0, 65535);

        // disengageReason CHOICE (3 root, extensible): 1 = normalDrop
        enc.WriteChoiceIndex(1, 3, extensible: true);

        var data = enc.ToArray();
        var msg = RasCodec.Decode(data);

        Assert.AreEqual(H225Constants.RAS_DISENGAGE_REQUEST, msg.Type);
        Assert.AreEqual(55, msg.RequestSeqNum);
        Assert.IsNotNull(msg.Drq);
        Assert.AreEqual("EP0001", msg.Drq.EndpointIdentifier);
        Assert.AreEqual(1, msg.Drq.DisengageReason); // normalDrop
    }
}

[TestClass]
public class RasCodecUnregistrationTests
{
    [TestMethod]
    public void UnregistrationConfirm_Encodes()
    {
        var bytes = RasCodec.EncodeUnregistrationConfirm(20);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_UNREGISTRATION_CONFIRM, choice);
    }

    [TestMethod]
    public void UnregistrationRequest_EncodeDecodeRoundTrip()
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_UNREGISTRATION_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        // URQ SEQUENCE (extensible)
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false, true); // aliases=no, nonStandard=no, endpointId=yes

        enc.WriteConstrainedWholeNumber(33, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        // callSignalAddress
        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(IPAddress.Loopback, 1720) });

        // endpointIdentifier
        enc.WriteBMPString("EP0002", lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        var data = enc.ToArray();
        var msg = RasCodec.Decode(data);

        Assert.AreEqual(H225Constants.RAS_UNREGISTRATION_REQUEST, msg.Type);
        Assert.AreEqual(33, msg.RequestSeqNum);
        Assert.IsNotNull(msg.Urq);
        Assert.AreEqual("EP0002", msg.Urq.EndpointIdentifier);
    }
}

[TestClass]
public class RasCodecMiscTests
{
    [TestMethod]
    public void UnknownMessageResponse_Encodes()
    {
        var bytes = RasCodec.EncodeUnknownMessageResponse(99);
        Assert.IsNotNull(bytes);

        var dec = new PerDecoder(bytes);
        var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
        Assert.AreEqual(H225Constants.RAS_UNKNOWN_MESSAGE_RESPONSE, choice);
    }

    [TestMethod]
    public void GatekeeperConfirm_WithoutGkId()
    {
        var rasAddr = new IPEndPoint(IPAddress.Loopback, 1719);
        var bytes = RasCodec.EncodeGatekeeperConfirm(1, rasAddr, null!);
        Assert.IsNotNull(bytes);
        Assert.IsTrue(bytes.Length > 0);
    }
}

// ──────────────────────────────────────────────────────────
//  RasRegistry tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class RasRegistryTests
{
    [TestMethod]
    public void Register_And_FindById()
    {
        var registry = new RasRegistry();
        var id = registry.GenerateEndpointId();

        var ep = new RasEndpoint
        {
            EndpointId = id,
            CallSignalAddresses = new[] { new IPEndPoint(IPAddress.Loopback, 1720) },
            RasAddresses = new[] { new IPEndPoint(IPAddress.Loopback, 5001) },
            Aliases = new[] { "TestUser" },
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };

        registry.Register(ep);

        Assert.AreEqual(1, registry.Count);
        Assert.IsNotNull(registry.FindById(id));
        Assert.AreEqual("TestUser", registry.FindById(id).Aliases[0]);
    }

    [TestMethod]
    public void FindByAlias_CaseInsensitive()
    {
        var registry = new RasRegistry();

        registry.Register(new RasEndpoint
        {
            EndpointId = registry.GenerateEndpointId(),
            Aliases = new[] { "Alice" },
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });

        Assert.IsNotNull(registry.FindByAlias("alice"));
        Assert.IsNotNull(registry.FindByAlias("ALICE"));
        Assert.IsNull(registry.FindByAlias("Bob"));
    }

    [TestMethod]
    public void Unregister_RemovesEndpoint()
    {
        var registry = new RasRegistry();
        var id = registry.GenerateEndpointId();

        registry.Register(new RasEndpoint
        {
            EndpointId = id,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        });

        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.Unregister(id));
        Assert.AreEqual(0, registry.Count);
        Assert.IsNull(registry.FindById(id));
    }

    [TestMethod]
    public void CleanExpired_RemovesOldEntries()
    {
        var registry = new RasRegistry();

        registry.Register(new RasEndpoint
        {
            EndpointId = registry.GenerateEndpointId(),
            ExpiresAt = DateTime.UtcNow.AddSeconds(-1) // Already expired
        });

        registry.Register(new RasEndpoint
        {
            EndpointId = registry.GenerateEndpointId(),
            ExpiresAt = DateTime.UtcNow.AddMinutes(5) // Still valid
        });

        Assert.AreEqual(2, registry.Count);
        var removed = registry.CleanExpired();
        Assert.AreEqual(1, removed);
        Assert.AreEqual(1, registry.Count);
    }

    [TestMethod]
    public void GenerateEndpointId_IsUnique()
    {
        var registry = new RasRegistry();
        var id1 = registry.GenerateEndpointId();
        var id2 = registry.GenerateEndpointId();
        var id3 = registry.GenerateEndpointId();

        Assert.AreNotEqual(id1, id2);
        Assert.AreNotEqual(id2, id3);
    }

    [TestMethod]
    public void GetAll_ReturnsSnapshot()
    {
        var registry = new RasRegistry();

        registry.Register(new RasEndpoint { EndpointId = registry.GenerateEndpointId(), ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        registry.Register(new RasEndpoint { EndpointId = registry.GenerateEndpointId(), ExpiresAt = DateTime.UtcNow.AddMinutes(5) });

        var all = registry.GetAll();
        Assert.AreEqual(2, all.Length);
    }
}

// ──────────────────────────────────────────────────────────
//  RasServer integration tests (UDP round-trip)
// ──────────────────────────────────────────────────────────

[TestClass]
public class RasServerTests
{
    private static readonly IPAddress Loopback = IPAddress.Loopback;

    [TestMethod]
    public async Task RasServer_GRQ_Returns_GCF()
    {
        var server = new RasServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            var grqBytes = BuildGrq(1, new IPEndPoint(Loopback, 5001));

            using var client = new System.Net.Sockets.UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEp = new IPEndPoint(Loopback, server.BoundPort);

            await client.SendAsync(grqBytes, grqBytes.Length, serverEp);
            var result = await client.ReceiveAsync();

            // Verify it's a GCF
            var dec = new PerDecoder(result.Buffer);
            var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
            Assert.AreEqual(H225Constants.RAS_GATEKEEPER_CONFIRM, choice);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task RasServer_RRQ_Registers_Endpoint()
    {
        var server = new RasServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            var rrqBytes = BuildRrq(1, "TestUser");

            using var client = new System.Net.Sockets.UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEp = new IPEndPoint(Loopback, server.BoundPort);

            await client.SendAsync(rrqBytes, rrqBytes.Length, serverEp);
            var result = await client.ReceiveAsync();

            // Verify it's an RCF
            var dec = new PerDecoder(result.Buffer);
            var choice = dec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);
            Assert.AreEqual(H225Constants.RAS_REGISTRATION_CONFIRM, choice);

            // Verify endpoint was registered
            Assert.AreEqual(1, server.Registry.Count);
        }
        finally
        {
            server.Stop();
        }
    }

    [TestMethod]
    public async Task RasServer_Full_Lifecycle_GRQ_RRQ_URQ()
    {
        var server = new RasServer(Loopback, 0);

        try
        {
            server.Start();
            Assert.IsTrue(server.WaitForStart());

            using var client = new System.Net.Sockets.UdpClient();
            client.Client.ReceiveTimeout = 3000;
            var serverEp = new IPEndPoint(Loopback, server.BoundPort);

            // 1. Discovery (GRQ → GCF)
            var grq = BuildGrq(1, new IPEndPoint(Loopback, 5001));
            await client.SendAsync(grq, grq.Length, serverEp);
            var gcfResult = await client.ReceiveAsync();

            var gcfDec = new PerDecoder(gcfResult.Buffer);
            Assert.AreEqual(H225Constants.RAS_GATEKEEPER_CONFIRM,
                gcfDec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true));

            // 2. Registration (RRQ → RCF)
            var rrq = BuildRrq(2, "NetMeetingUser");
            await client.SendAsync(rrq, rrq.Length, serverEp);
            var rcfResult = await client.ReceiveAsync();

            var rcfDec = new PerDecoder(rcfResult.Buffer);
            Assert.AreEqual(H225Constants.RAS_REGISTRATION_CONFIRM,
                rcfDec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true));

            Assert.AreEqual(1, server.Registry.Count);

            // Extract endpoint ID from RCF for URQ
            // Skip: extensionBit, optionals, seqNum, protocolId, callSignalAddress
            // For now, just get it from the registry directly
            var registeredEp = server.Registry.GetAll()[0];

            // 3. Unregistration (URQ → UCF)
            var urq = BuildUrq(3, registeredEp.EndpointId);
            await client.SendAsync(urq, urq.Length, serverEp);
            var ucfResult = await client.ReceiveAsync();

            var ucfDec = new PerDecoder(ucfResult.Buffer);
            Assert.AreEqual(H225Constants.RAS_UNREGISTRATION_CONFIRM,
                ucfDec.ReadChoiceIndex(H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true));

            Assert.AreEqual(0, server.Registry.Count);
        }
        finally
        {
            server.Stop();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Test message builders
    // ──────────────────────────────────────────────────────────

    private static byte[] BuildGrq(int seqNum, IPEndPoint rasAddress)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_GATEKEEPER_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false, false); // nonStandard, gkId, aliases

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        H225Types.WriteTransportAddress(enc, rasAddress);
        H225Types.WriteEndpointType(enc);

        return enc.ToArray();
    }

    private static byte[] BuildRrq(int seqNum, string alias)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_REGISTRATION_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, true, false); // nonStandard=no, aliases=yes, gkId=no

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);
        enc.WriteObjectIdentifier(H225Constants.ProtocolOid);

        enc.WriteBoolean(true); // discoveryComplete

        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(Loopback, 1720) });
        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(Loopback, 5001) });

        H225Types.WriteEndpointType(enc);

        H225Types.WriteAliasAddresses(enc, new[] { alias });

        // endpointVendor (VendorIdentifier) — minimal
        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false);
        enc.WriteConstrainedWholeNumber(181, 0, 255);
        enc.WriteConstrainedWholeNumber(0, 0, 255);
        enc.WriteConstrainedWholeNumber(21324, 0, 65535);

        return enc.ToArray();
    }

    private static byte[] BuildUrq(int seqNum, string endpointId)
    {
        var enc = new PerEncoder();
        enc.WriteChoiceIndex(H225Constants.RAS_UNREGISTRATION_REQUEST, H225Constants.RAS_ROOT_ALTERNATIVES, extensible: true);

        enc.WriteExtensionBit(false);
        enc.WriteOptionalBitmap(false, false, true); // aliases=no, nonStandard=no, endpointId=yes

        enc.WriteConstrainedWholeNumber(seqNum, H225Constants.SEQ_NUM_MIN, H225Constants.SEQ_NUM_MAX);

        H225Types.WriteTransportAddresses(enc, new[] { new IPEndPoint(Loopback, 1720) });

        enc.WriteBMPString(endpointId, lb: H225Constants.EP_ID_MIN, ub: H225Constants.EP_ID_MAX);

        return enc.ToArray();
    }
}
