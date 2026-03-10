// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Net.Sockets;
using System.Text;
using VintageHive.Proxy.NetMeeting.Asn1;
using VintageHive.Proxy.NetMeeting.ILS;

namespace VintageHiveTests;

// ──────────────────────────────────────────────────────────
//  LDAP Filter Parser Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class LdapFilterParserTests
{
    private static BerDecoder BuildFilterBytes(Action<BerEncoder> writeFilter)
    {
        var enc = new BerEncoder();
        writeFilter(enc);
        return new BerDecoder(enc.ToArray());
    }

    [TestMethod]
    public void ParsePresentFilter()
    {
        // (objectClass=*) → present filter: tag 0x87
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteTag(BerTag.Context(7, constructed: false)); // 0x87
            var attr = Encoding.UTF8.GetBytes("objectClass");
            enc.WriteLength(attr.Length);
            enc.WriteRawBytes(attr);
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<PresentFilter>(filter);
        Assert.AreEqual("objectClass", ((PresentFilter)filter).Attribute);
    }

    [TestMethod]
    public void ParseEqualityFilter()
    {
        // (cn=john) → equality filter: tag 0xA3
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteContextConstructed(3, eq =>
            {
                eq.WriteString("cn");
                eq.WriteString("john");
            });
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<EqualityFilter>(filter);
        var eq = (EqualityFilter)filter;
        Assert.AreEqual("cn", eq.Attribute);
        Assert.AreEqual("john", eq.Value);
    }

    [TestMethod]
    public void ParseAndFilter_WithChildren()
    {
        // (&(objectClass=rtPerson)(cn=%)) → AND with equality + equality
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteContextConstructed(0, and =>
            {
                // equality: objectClass=rtPerson
                and.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("objectClass");
                    eq.WriteString("rtPerson");
                });
                // equality: cn=%
                and.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("cn");
                    eq.WriteString("%");
                });
            });
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<AndFilter>(filter);
        var and = (AndFilter)filter;
        Assert.AreEqual(2, and.Children.Count);
        Assert.IsInstanceOfType<EqualityFilter>(and.Children[0]);
        Assert.IsInstanceOfType<EqualityFilter>(and.Children[1]);
    }

    [TestMethod]
    public void ParseOrFilter()
    {
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteContextConstructed(1, or =>
            {
                or.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("cn");
                    eq.WriteString("alice");
                });
                or.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("cn");
                    eq.WriteString("bob");
                });
            });
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<OrFilter>(filter);
        Assert.AreEqual(2, ((OrFilter)filter).Children.Count);
    }

    [TestMethod]
    public void ParseNotFilter()
    {
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteContextConstructed(2, not =>
            {
                not.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("cn");
                    eq.WriteString("admin");
                });
            });
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<NotFilter>(filter);
        var inner = ((NotFilter)filter).Child;
        Assert.IsInstanceOfType<EqualityFilter>(inner);
    }

    [TestMethod]
    public void ParseSubstringFilter_InitialOnly()
    {
        // (cn=abc*) → substrings with initial "abc"
        var decoder = BuildFilterBytes(enc =>
        {
            enc.WriteContextConstructed(4, sub =>
            {
                sub.WriteString("cn");
                sub.WriteSequence(subSeq =>
                {
                    subSeq.WriteContextPrimitive(0, Encoding.UTF8.GetBytes("abc"));
                });
            });
        });

        var filter = LdapFilter.Parse(decoder);
        Assert.IsInstanceOfType<SubstringFilter>(filter);
        var sf = (SubstringFilter)filter;
        Assert.AreEqual("cn", sf.Attribute);
        Assert.AreEqual("abc", sf.Initial);
        Assert.IsNull(sf.Final);
    }
}

// ──────────────────────────────────────────────────────────
//  LDAP Filter Evaluation Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class LdapFilterEvalTests
{
    private static IlsUser MakeUser(string cn, params (string key, string val)[] attrs)
    {
        var user = new IlsUser
        {
            Dn = $"cn={cn}, o=Microsoft, objectClass=rtPerson"
        };
        user.SetAttribute("objectClass", "rtPerson");
        user.SetAttribute("cn", cn);
        foreach (var (key, val) in attrs)
        {
            user.AddAttributeValue(key, val);
        }
        return user;
    }

    [TestMethod]
    public void EqualityFilter_ExactMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new EqualityFilter("cn", "alice@test.com");
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void EqualityFilter_CaseInsensitive()
    {
        var user = MakeUser("Alice@Test.com");
        var filter = new EqualityFilter("cn", "alice@test.com");
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void EqualityFilter_NoMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new EqualityFilter("cn", "bob@test.com");
        Assert.IsFalse(filter.Evaluate(user));
    }

    [TestMethod]
    public void EqualityFilter_PercentWildcard_MatchesAny()
    {
        var user = MakeUser("anyone@test.com");
        var filter = new EqualityFilter("cn", "%");
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void EqualityFilter_PercentWildcard_NoAttribute_Fails()
    {
        var user = MakeUser("test@test.com");
        var filter = new EqualityFilter("sappid", "%");
        Assert.IsFalse(filter.Evaluate(user));
    }

    [TestMethod]
    public void EqualityFilter_MultiValuedAttribute()
    {
        var user = MakeUser("alice@test.com", ("sprotid", "T120"), ("sprotid", "H323"));
        var filter = new EqualityFilter("sprotid", "H323");
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void PresentFilter_AttributeExists()
    {
        var user = MakeUser("alice@test.com");
        Assert.IsTrue(new PresentFilter("cn").Evaluate(user));
        Assert.IsTrue(new PresentFilter("objectClass").Evaluate(user));
    }

    [TestMethod]
    public void PresentFilter_AttributeMissing()
    {
        var user = MakeUser("alice@test.com");
        Assert.IsFalse(new PresentFilter("sappid").Evaluate(user));
    }

    [TestMethod]
    public void AndFilter_AllMatch()
    {
        var user = MakeUser("alice@test.com", ("sappid", "ms-netmeeting"));
        var filter = new AndFilter(new List<LdapFilter>
        {
            new EqualityFilter("objectClass", "rtPerson"),
            new EqualityFilter("cn", "%"),
            new EqualityFilter("sappid", "ms-netmeeting")
        });
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void AndFilter_OneFails()
    {
        var user = MakeUser("alice@test.com");
        var filter = new AndFilter(new List<LdapFilter>
        {
            new EqualityFilter("objectClass", "rtPerson"),
            new EqualityFilter("sappid", "ms-netmeeting") // user doesn't have sappid
        });
        Assert.IsFalse(filter.Evaluate(user));
    }

    [TestMethod]
    public void OrFilter_OneMatches()
    {
        var user = MakeUser("alice@test.com");
        var filter = new OrFilter(new List<LdapFilter>
        {
            new EqualityFilter("cn", "bob"),
            new EqualityFilter("cn", "alice@test.com")
        });
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void NotFilter_Inverts()
    {
        var user = MakeUser("alice@test.com");
        Assert.IsTrue(new NotFilter(new EqualityFilter("cn", "bob")).Evaluate(user));
        Assert.IsFalse(new NotFilter(new EqualityFilter("cn", "alice@test.com")).Evaluate(user));
    }

    [TestMethod]
    public void SubstringFilter_InitialMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new SubstringFilter("cn", "alice", new List<string>(), null!);
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void SubstringFilter_FinalMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new SubstringFilter("cn", null!, new List<string>(), "test.com");
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void SubstringFilter_AnyMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new SubstringFilter("cn", null!, new List<string> { "@test" }, null!);
        Assert.IsTrue(filter.Evaluate(user));
    }

    [TestMethod]
    public void SubstringFilter_NoMatch()
    {
        var user = MakeUser("alice@test.com");
        var filter = new SubstringFilter("cn", "bob", new List<string>(), null!);
        Assert.IsFalse(filter.Evaluate(user));
    }
}

// ──────────────────────────────────────────────────────────
//  IlsDirectory Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class IlsDirectoryTests
{
    [TestMethod]
    public void AddAndFind()
    {
        var dir = new IlsDirectory();
        var user = new IlsUser
        {
            Dn = "cn=alice@test.com, o=Microsoft, objectClass=rtPerson",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        user.SetAttribute("cn", "alice@test.com");

        dir.AddOrUpdate(user);
        Assert.AreEqual(1, dir.Count);

        var found = dir.Find("alice@test.com");
        Assert.IsNotNull(found);
        Assert.AreEqual("alice@test.com", found.GetAttribute("cn"));
    }

    [TestMethod]
    public void AddOrUpdate_ReplaceExisting()
    {
        var dir = new IlsDirectory();

        var user1 = new IlsUser { Dn = "cn=alice, objectClass=rtPerson" };
        user1.SetAttribute("cn", "alice");
        user1.SetAttribute("comment", "first");

        var user2 = new IlsUser { Dn = "cn=alice, objectClass=rtPerson" };
        user2.SetAttribute("cn", "alice");
        user2.SetAttribute("comment", "second");

        dir.AddOrUpdate(user1);
        dir.AddOrUpdate(user2);

        Assert.AreEqual(1, dir.Count);
        Assert.AreEqual("second", dir.Find("alice").GetAttribute("comment"));
    }

    [TestMethod]
    public void Remove()
    {
        var dir = new IlsDirectory();
        var user = new IlsUser { Dn = "cn=alice, objectClass=rtPerson" };
        user.SetAttribute("cn", "alice");
        dir.AddOrUpdate(user);

        Assert.IsTrue(dir.Remove("alice"));
        Assert.AreEqual(0, dir.Count);
        Assert.IsFalse(dir.Remove("alice")); // already gone
    }

    [TestMethod]
    public void RemoveByDn_IlsFormat()
    {
        var dir = new IlsDirectory();
        var user = new IlsUser { Dn = "c=-,o=Microsoft, cn=alice@test.com, objectClass=rtPerson" };
        user.SetAttribute("cn", "alice@test.com");
        dir.AddOrUpdate(user);

        Assert.IsTrue(dir.RemoveByDn("c=-,o=Microsoft, cn=alice@test.com, objectClass=rtPerson"));
        Assert.AreEqual(0, dir.Count);
    }

    [TestMethod]
    public void RemoveBySession()
    {
        var dir = new IlsDirectory();
        var session1 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        var user1 = new IlsUser { Dn = "cn=alice", SessionId = session1 };
        user1.SetAttribute("cn", "alice");
        var user2 = new IlsUser { Dn = "cn=bob", SessionId = session1 };
        user2.SetAttribute("cn", "bob");
        var user3 = new IlsUser { Dn = "cn=carol", SessionId = session2 };
        user3.SetAttribute("cn", "carol");

        dir.AddOrUpdate(user1);
        dir.AddOrUpdate(user2);
        dir.AddOrUpdate(user3);
        Assert.AreEqual(3, dir.Count);

        dir.RemoveBySession(session1);
        Assert.AreEqual(1, dir.Count);
        Assert.IsNotNull(dir.Find("carol"));
    }

    [TestMethod]
    public void Search_WithFilter()
    {
        var dir = new IlsDirectory();

        var alice = new IlsUser { Dn = "cn=alice" };
        alice.SetAttribute("cn", "alice");
        alice.SetAttribute("objectClass", "rtPerson");
        alice.SetAttribute("sappid", "ms-netmeeting");

        var bob = new IlsUser { Dn = "cn=bob" };
        bob.SetAttribute("cn", "bob");
        bob.SetAttribute("objectClass", "rtPerson");
        // bob has no sappid

        dir.AddOrUpdate(alice);
        dir.AddOrUpdate(bob);

        var filter = new AndFilter(new List<LdapFilter>
        {
            new EqualityFilter("objectClass", "rtPerson"),
            new EqualityFilter("sappid", "ms-netmeeting")
        });

        var results = dir.Search(filter);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("alice", results[0].GetAttribute("cn"));
    }

    [TestMethod]
    public void CleanExpired()
    {
        var dir = new IlsDirectory();

        var alive = new IlsUser
        {
            Dn = "cn=alive",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        alive.SetAttribute("cn", "alive");

        var expired = new IlsUser
        {
            Dn = "cn=expired",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
        };
        expired.SetAttribute("cn", "expired");

        dir.AddOrUpdate(alive);
        dir.AddOrUpdate(expired);
        Assert.AreEqual(2, dir.Count);

        dir.CleanExpired();
        Assert.AreEqual(1, dir.Count);
        Assert.IsNotNull(dir.Find("alive"));
        Assert.IsNull(dir.Find("expired"));
    }

    [TestMethod]
    public void ExtractCnFromDn_IlsFormat()
    {
        Assert.AreEqual("alice@test.com",
            IlsDirectory.ExtractCnFromDn("c=-,o=Microsoft, cn=alice@test.com, objectClass=rtPerson"));
    }

    [TestMethod]
    public void ExtractCnFromDn_StandardFormat()
    {
        Assert.AreEqual("alice",
            IlsDirectory.ExtractCnFromDn("cn=alice, ou=Dynamic, o=Intranet"));
    }

    [TestMethod]
    public void ExtractCnFromDn_NoCn_ReturnsFull()
    {
        Assert.AreEqual("objectClass=rtPerson",
            IlsDirectory.ExtractCnFromDn("objectClass=rtPerson"));
    }

    [TestMethod]
    public void ExtractCnFromDn_Empty()
    {
        Assert.AreEqual("", IlsDirectory.ExtractCnFromDn(""));
        Assert.AreEqual("", IlsDirectory.ExtractCnFromDn(null!));
    }
}

// ──────────────────────────────────────────────────────────
//  ILS Server Integration Tests
// ──────────────────────────────────────────────────────────

[TestClass]
public class IlsServerIntegrationTests
{
    private const int TEST_PORT = 21002;

    private static IlsServer _server = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        _server = new IlsServer(IPAddress.Loopback, TEST_PORT);
        _server.Start();
        await WaitForServerAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        // Server runs on background thread; will be collected
    }

    private static async Task WaitForServerAsync()
    {
        // Probe until the server accepts connections
        for (var i = 0; i < 30; i++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, TEST_PORT);
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }
        throw new TimeoutException("ILS server did not start in time");
    }

    private static async Task<TcpClient> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, TEST_PORT);
        return client;
    }

    private static byte[] BuildBindRequest(int messageId, int version = 2)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationConstructed(LdapConstants.OP_BIND_REQUEST, bind =>
            {
                bind.WriteInteger(version);
                bind.WriteString(""); // anonymous
                bind.WriteContextPrimitive(0, Array.Empty<byte>()); // simple auth, empty password
            });
        });
        return enc.ToArray();
    }

    private static byte[] BuildAddRequest(int messageId, string dn, params (string name, string[] values)[] attributes)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationConstructed(LdapConstants.OP_ADD_REQUEST, add =>
            {
                add.WriteString(dn);
                add.WriteSequence(attrs =>
                {
                    foreach (var (name, values) in attributes)
                    {
                        attrs.WriteSequence(partialAttr =>
                        {
                            partialAttr.WriteString(name);
                            partialAttr.WriteSet(vals =>
                            {
                                foreach (var v in values)
                                {
                                    vals.WriteString(v);
                                }
                            });
                        });
                    }
                });
            });
        });
        return enc.ToArray();
    }

    private static byte[] BuildSearchRequest(int messageId, string baseDn,
        Action<BerEncoder> writeFilter, params string[] requestedAttrs)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationConstructed(LdapConstants.OP_SEARCH_REQUEST, search =>
            {
                search.WriteString(baseDn);
                search.WriteEnumerated(0);  // scope: baseObject
                search.WriteEnumerated(0);  // derefAliases: never
                search.WriteInteger(0);     // sizeLimit: unlimited
                search.WriteInteger(0);     // timeLimit: unlimited
                search.WriteBoolean(false); // typesOnly
                writeFilter(search);        // filter
                search.WriteSequence(attrs =>
                {
                    foreach (var attr in requestedAttrs)
                    {
                        attrs.WriteString(attr);
                    }
                });
            });
        });
        return enc.ToArray();
    }

    private static byte[] BuildDeleteRequest(int messageId, string dn)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationPrimitive(LdapConstants.OP_DELETE_REQUEST,
                Encoding.UTF8.GetBytes(dn));
        });
        return enc.ToArray();
    }

    private static byte[] BuildUnbindRequest(int messageId)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationPrimitive(LdapConstants.OP_UNBIND_REQUEST, Array.Empty<byte>());
        });
        return enc.ToArray();
    }

    private static async Task<byte[]> SendAndReceiveAsync(NetworkStream stream, byte[] request,
        int timeoutMs = 2000)
    {
        await stream.WriteAsync(request);
        await stream.FlushAsync();

        var buffer = new byte[4096];
        var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (read == 0)
            {
                return null!;
            }
            var result = new byte[read];
            Array.Copy(buffer, result, read);
            return result;
        }
        catch (OperationCanceledException)
        {
            return null!;
        }
    }

    private static (int messageId, byte opTag, int resultCode) ParseLdapResult(byte[] data)
    {
        var dec = new BerDecoder(data);
        var msg = dec.ReadSequence();
        var messageId = msg.ReadInteger();
        var opTag = msg.ReadTag();
        var opLength = msg.ReadLength();
        var body = msg.Slice(opLength);
        var resultCode = body.ReadEnumerated();
        return (messageId, opTag.RawByte, resultCode);
    }

    [TestMethod]
    public async Task Bind_ReturnsSuccess()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, BuildBindRequest(1));
        Assert.IsNotNull(response);

        var (msgId, opTag, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(1, msgId);
        Assert.AreEqual(LdapConstants.TAG_BIND_RESPONSE, opTag);
        Assert.AreEqual(LdapConstants.RESULT_SUCCESS, resultCode);
    }

    [TestMethod]
    public async Task Bind_LDAPv3_ReturnsSuccess()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        var response = await SendAndReceiveAsync(stream, BuildBindRequest(1, version: 3));
        Assert.IsNotNull(response);

        var (_, _, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(LdapConstants.RESULT_SUCCESS, resultCode);
    }

    [TestMethod]
    public async Task Add_ReturnsSuccess()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        // Bind first
        await SendAndReceiveAsync(stream, BuildBindRequest(1));

        // Add
        var addReq = BuildAddRequest(2, "c=-,o=Microsoft, cn=test-add@hive.com, objectClass=rtPerson", ("cn", new[] { "test-add@hive.com" }), ("objectClass", new[] { "rtPerson" }), ("sipaddress", new[] { "16777343" }), ("sflags", new[] { "1" }));

        var response = await SendAndReceiveAsync(stream, addReq);
        Assert.IsNotNull(response);

        var (msgId, opTag, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(2, msgId);
        Assert.AreEqual(LdapConstants.TAG_ADD_RESPONSE, opTag);
        Assert.AreEqual(LdapConstants.RESULT_SUCCESS, resultCode);
    }

    [TestMethod]
    public async Task AddThenSearch_FindsUser()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        // Bind
        await SendAndReceiveAsync(stream, BuildBindRequest(1));

        // Add user
        var addReq = BuildAddRequest(2, "c=-,o=Microsoft, cn=searcher@hive.com, objectClass=rtPerson", ("cn", new[] { "searcher@hive.com" }), ("objectClass", new[] { "rtPerson" }), ("sappid", new[] { "ms-netmeeting" }), ("sflags", new[] { "1" }));
        await SendAndReceiveAsync(stream, addReq);

        // Search with NetMeeting-style filter
        var searchReq = BuildSearchRequest(3, "objectClass=rtPerson",
            filter =>
            {
                // (&(objectClass=rtPerson)(cn=%)(sappid=ms-netmeeting))
                filter.WriteContextConstructed(0, and =>
                {
                    and.WriteContextConstructed(3, eq =>
                    {
                        eq.WriteString("objectClass");
                        eq.WriteString("rtPerson");
                    });
                    and.WriteContextConstructed(3, eq =>
                    {
                        eq.WriteString("cn");
                        eq.WriteString("%");
                    });
                    and.WriteContextConstructed(3, eq =>
                    {
                        eq.WriteString("sappid");
                        eq.WriteString("ms-netmeeting");
                    });
                });
            },
            "cn", "sipaddress"
        );

        var response = await SendAndReceiveAsync(stream, searchReq);
        Assert.IsNotNull(response);

        // Response contains SearchResultEntry + SearchResultDone
        // Parse the first LDAP message (SearchResultEntry)
        var dec = new BerDecoder(response);
        var msg = dec.ReadSequence();
        var msgId = msg.ReadInteger();
        var opTag = msg.ReadTag();

        Assert.AreEqual(3, msgId);
        Assert.AreEqual(LdapConstants.TAG_SEARCH_RESULT_ENTRY, opTag.RawByte);
    }

    [TestMethod]
    public async Task SearchEmpty_ReturnsOnlyDone()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        await SendAndReceiveAsync(stream, BuildBindRequest(1));

        // Search for a non-existent appid
        var searchReq = BuildSearchRequest(2, "objectClass=rtPerson",
            filter =>
            {
                filter.WriteContextConstructed(3, eq =>
                {
                    eq.WriteString("sappid");
                    eq.WriteString("nonexistent-app-xyz");
                });
            }
        );

        var response = await SendAndReceiveAsync(stream, searchReq);
        Assert.IsNotNull(response);

        // Should be just SearchResultDone (no entries)
        var (msgId, opTag, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(2, msgId);
        Assert.AreEqual(LdapConstants.TAG_SEARCH_RESULT_DONE, opTag);
        Assert.AreEqual(LdapConstants.RESULT_SUCCESS, resultCode);
    }

    [TestMethod]
    public async Task Delete_RemovesUser()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        await SendAndReceiveAsync(stream, BuildBindRequest(1));

        // Add
        var dn = "c=-,o=Microsoft, cn=to-delete@hive.com, objectClass=rtPerson";
        var addReq = BuildAddRequest(2, dn, ("cn", new[] { "to-delete@hive.com" }), ("objectClass", new[] { "rtPerson" }));
        await SendAndReceiveAsync(stream, addReq);

        // Delete
        var delReq = BuildDeleteRequest(3, dn);
        var response = await SendAndReceiveAsync(stream, delReq);
        Assert.IsNotNull(response);

        var (msgId, opTag, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(3, msgId);
        Assert.AreEqual(LdapConstants.TAG_DELETE_RESPONSE, opTag);
        Assert.AreEqual(LdapConstants.RESULT_SUCCESS, resultCode);
    }

    [TestMethod]
    public async Task Delete_NonExistent_ReturnsNoSuchObject()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        await SendAndReceiveAsync(stream, BuildBindRequest(1));

        var delReq = BuildDeleteRequest(2, "cn=nobody-here@hive.com, objectClass=rtPerson");
        var response = await SendAndReceiveAsync(stream, delReq);
        Assert.IsNotNull(response);

        var (_, _, resultCode) = ParseLdapResult(response);
        Assert.AreEqual(LdapConstants.RESULT_NO_SUCH_OBJECT, resultCode);
    }

    [TestMethod]
    public async Task Unbind_ClosesConnection()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        await SendAndReceiveAsync(stream, BuildBindRequest(1));
        await stream.WriteAsync(BuildUnbindRequest(2));
        await stream.FlushAsync();

        // Server should close the connection — read should return 0
        await Task.Delay(200);
        var buf = new byte[16];
        var read = await stream.ReadAsync(buf, 0, buf.Length);
        Assert.AreEqual(0, read);
    }

    [TestMethod]
    public async Task MessageId_PreservedInResponse()
    {
        using var client = await ConnectAsync();
        var stream = client.GetStream();

        // Use a high message ID
        var response = await SendAndReceiveAsync(stream, BuildBindRequest(42));
        Assert.IsNotNull(response);

        var (msgId, _, _) = ParseLdapResult(response);
        Assert.AreEqual(42, msgId);
    }
}
