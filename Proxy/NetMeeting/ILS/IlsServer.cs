// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net;
using System.Text;
using VintageHive.Network;
using VintageHive.Proxy.NetMeeting.Asn1;

namespace VintageHive.Proxy.NetMeeting.ILS;

/// <summary>
/// ILS (Internet Locator Service) server implementing LDAP on TCP.
/// Supports Microsoft NetMeeting 2.x/3.x client registration, directory lookup,
/// and TTL-based entry management per the MS-TAIL specification.
/// </summary>
internal class IlsServer : Listener
{
    private const string LOG_SRC = nameof(IlsServer);

    private readonly IlsDirectory _directory = new();
    private readonly Timer _ttlTimer;

    public IlsServer(IPAddress address, int port)
        : base(address, port, SocketType.Stream, ProtocolType.Tcp)
    {
        _ttlTimer = new Timer(_ => _directory.CleanExpired(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        connection.IsKeepAlive = false;
        var stream = connection.Stream;

        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"ILS connection from {connection.RemoteAddress}", "");

        try
        {
            while (connection.IsConnected)
            {
                var messageBytes = await ReadLdapMessageAsync(stream);
                if (messageBytes == null)
                {
                    break;
                }

                var response = ProcessLdapMessage(messageBytes, connection);
                if (response == null)
                {
                    break; // Unbind
                }

                await stream.WriteAsync(response);
                await stream.FlushAsync();
            }
        }
        catch (IOException) { /* Client disconnected */ }
        catch (Exception ex)
        {
            Log.WriteException(LOG_SRC, ex, "");
        }

        // Shut down the socket so the base class's while(connection.Connected) exits immediately
        try { connection.RawSocket.Shutdown(SocketShutdown.Both); } catch { }
        try { connection.RawSocket.Close(); } catch { }

        return null;
    }

    public override async Task ProcessDisconnection(ListenerSocket connection)
    {
        _directory.RemoveBySession(connection.TraceId);
        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"ILS disconnect from {connection.RemoteAddress}, cleaned up session entries", "");
        await Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────────
    //  LDAP message framing (BER over TCP)
    // ──────────────────────────────────────────────────────────

    private static async Task<byte[]> ReadLdapMessageAsync(NetworkStream stream)
    {
        // Every LDAP message is a BER SEQUENCE: tag 0x30 + length + content.
        // Accumulate raw bytes as we read so the full TLV can be passed to BerDecoder.

        var buffer = new MemoryStream();

        var tagByte = await ReadByteAsync(stream);
        if (tagByte < 0)
        {
            return null;
        }

        if (tagByte != 0x30)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Expected SEQUENCE tag (0x30), got 0x{tagByte:X2}", "");
            return null;
        }
        buffer.WriteByte(0x30);

        // Read length (short or long form)
        var lenByte = await ReadByteAsync(stream);
        if (lenByte < 0)
        {
            return null;
        }
        buffer.WriteByte((byte)lenByte);

        int contentLength;

        if ((lenByte & 0x80) == 0)
        {
            contentLength = lenByte;
        }
        else
        {
            var numLenBytes = lenByte & 0x7F;
            if (numLenBytes == 0 || numLenBytes > 4)
            {
                return null;
            }

            var lenBytes = await ReadExactAsync(stream, numLenBytes);
            if (lenBytes == null)
            {
                return null;
            }
            buffer.Write(lenBytes, 0, numLenBytes);

            contentLength = 0;
            for (var i = 0; i < numLenBytes; i++)
            {
                contentLength = (contentLength << 8) | lenBytes[i];
            }
        }

        if (contentLength > LdapConstants.MAX_MESSAGE_SIZE)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"LDAP message too large: {contentLength} bytes", "");
            return null;
        }

        // Read content
        var content = await ReadExactAsync(stream, contentLength);
        if (content == null)
        {
            return null;
        }
        buffer.Write(content, 0, contentLength);

        return buffer.ToArray();
    }

    private static async Task<int> ReadByteAsync(NetworkStream stream)
    {
        var buf = new byte[1];
        var read = await stream.ReadAsync(buf, 0, 1);
        return read == 0 ? -1 : buf[0];
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, totalRead, count - totalRead);
            if (read == 0)
            {
                return null;
            }
            totalRead += read;
        }
        return buffer;
    }

    // ──────────────────────────────────────────────────────────
    //  LDAP message dispatch
    // ──────────────────────────────────────────────────────────

    private byte[] ProcessLdapMessage(byte[] messageBytes, ListenerSocket connection)
    {
        var decoder = new BerDecoder(messageBytes);
        var msgBody = decoder.ReadSequence();
        var messageId = msgBody.ReadInteger();
        var opTag = msgBody.ReadTag();
        var opLength = msgBody.ReadLength();
        var opBody = msgBody.Slice(opLength);

        switch (opTag.RawByte)
        {
            case LdapConstants.TAG_BIND_REQUEST:
            {
                return HandleBind(messageId, opBody);
            }

            case LdapConstants.TAG_UNBIND_REQUEST:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Unbind (msgId={messageId})", "");
                return null;
            }

            case LdapConstants.TAG_SEARCH_REQUEST:
            {
                return HandleSearch(messageId, opBody);
            }

            case LdapConstants.TAG_ADD_REQUEST:
            {
                return HandleAdd(messageId, opBody, connection);
            }

            case LdapConstants.TAG_MODIFY_REQUEST:
            {
                return HandleModify(messageId, opBody);
            }

            case LdapConstants.TAG_DELETE_REQUEST:
            {
                return HandleDelete(messageId, opBody);
            }

            default:
            {
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Unsupported LDAP operation: {opTag}", "");
                return BuildLdapResult(messageId, opTag.Number + 1, LdapConstants.RESULT_PROTOCOL_ERROR, "", "Unsupported operation");
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Operation handlers
    // ──────────────────────────────────────────────────────────

    private byte[] HandleBind(int messageId, BerDecoder body)
    {
        var version = body.ReadInteger();

        // Read name (LDAPDN as OCTET STRING)
        var name = body.ReadString();

        // Accept any authentication — ILS uses anonymous simple bind
        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Bind: version={version} dn=\"{name}\"", "");

        return BuildLdapResult(messageId, LdapConstants.OP_BIND_RESPONSE, LdapConstants.RESULT_SUCCESS, "", "");
    }

    private byte[] HandleSearch(int messageId, BerDecoder body)
    {
        var baseDn = body.ReadString();
        var scope = body.ReadEnumerated();
        var derefAliases = body.ReadEnumerated();
        var sizeLimit = body.ReadInteger();
        var timeLimit = body.ReadInteger();
        var typesOnly = body.ReadBoolean();

        // Parse search filter
        var filter = LdapFilter.Parse(body);

        // Parse requested attributes
        var attrsSeq = body.ReadSequence();
        var requestedAttrs = new List<string>();
        while (attrsSeq.HasData)
        {
            requestedAttrs.Add(attrsSeq.ReadString());
        }

        // Handle ILS sttl refresh side-effect (MS-TAIL: including sttl in filter resets TTL)
        HandleTtlRefresh(filter);

        // Search directory
        var results = _directory.Search(filter);

        if (sizeLimit > 0 && results.Count > sizeLimit)
        {
            results = results.Take(sizeLimit).ToList();
        }

        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Search: base=\"{baseDn}\" scope={scope} results={results.Count}", "");

        // Build response: one SearchResultEntry per match, then SearchResultDone
        var response = new MemoryStream();

        foreach (var user in results)
        {
            var entry = BuildSearchResultEntry(messageId, user, requestedAttrs);
            response.Write(entry, 0, entry.Length);
        }

        var done = BuildLdapResult(messageId, LdapConstants.OP_SEARCH_RESULT_DONE, LdapConstants.RESULT_SUCCESS, "", "");
        response.Write(done, 0, done.Length);

        return response.ToArray();
    }

    private byte[] HandleAdd(int messageId, BerDecoder body, ListenerSocket connection)
    {
        var dn = body.ReadString();

        var user = new IlsUser
        {
            Dn = dn,
            SessionId = connection.TraceId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(LdapConstants.DEFAULT_TTL_MINUTES)
        };

        // Parse attribute list
        var attrsSeq = body.ReadSequence();
        while (attrsSeq.HasData)
        {
            var attrSeq = attrsSeq.ReadSequence();
            var attrName = attrSeq.ReadString();
            var valuesSet = attrSeq.ReadSet();
            while (valuesSet.HasData)
            {
                user.AddAttributeValue(attrName, valuesSet.ReadString());
            }
        }

        // Ensure objectClass is set
        if (!user.HasAttribute("objectClass"))
        {
            user.SetAttribute("objectClass", "rtPerson");
        }

        _directory.AddOrUpdate(user);

        var cn = IlsDirectory.ExtractCnFromDn(dn);
        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"Add: cn=\"{cn}\" (directory has {_directory.Count} users)", "");

        return BuildLdapResult(messageId, LdapConstants.OP_ADD_RESPONSE, LdapConstants.RESULT_SUCCESS, "", "");
    }

    private byte[] HandleModify(int messageId, BerDecoder body)
    {
        var dn = body.ReadString();
        var user = _directory.FindByDn(dn);

        if (user == null)
        {
            Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Modify: dn=\"{dn}\" not found", "");
            return BuildLdapResult(messageId, LdapConstants.OP_MODIFY_RESPONSE, LdapConstants.RESULT_NO_SUCH_OBJECT, "", "");
        }

        var changesSeq = body.ReadSequence();
        while (changesSeq.HasData)
        {
            var changeSeq = changesSeq.ReadSequence();
            var operation = changeSeq.ReadEnumerated();
            var modSeq = changeSeq.ReadSequence();
            var attrName = modSeq.ReadString();
            var valuesSet = modSeq.ReadSet();
            var values = new List<string>();
            while (valuesSet.HasData)
            {
                values.Add(valuesSet.ReadString());
            }

            switch (operation)
            {
                case LdapConstants.MOD_ADD:
                {
                    foreach (var v in values)
                    {
                        user.AddAttributeValue(attrName, v);
                    }
                }
                break;

                case LdapConstants.MOD_DELETE:
                {
                    user.RemoveAttribute(attrName);
                }
                break;

                case LdapConstants.MOD_REPLACE:
                {
                    user.SetAttributes(attrName, values);
                }
                break;
            }
        }

        Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"Modify: dn=\"{dn}\" applied", "");

        return BuildLdapResult(messageId, LdapConstants.OP_MODIFY_RESPONSE, LdapConstants.RESULT_SUCCESS, "", "");
    }

    private byte[] HandleDelete(int messageId, BerDecoder body)
    {
        // DeleteRequest is APPLICATION PRIMITIVE — the value IS the LDAPDN
        var dn = Encoding.UTF8.GetString(body.ReadRemainingBytes());
        var removed = _directory.RemoveByDn(dn);
        var resultCode = removed
            ? LdapConstants.RESULT_SUCCESS
            : LdapConstants.RESULT_NO_SUCH_OBJECT;

        var cn = IlsDirectory.ExtractCnFromDn(dn);
        Log.WriteLine(Log.LEVEL_INFO, LOG_SRC, $"Delete: cn=\"{cn}\" removed={removed} (directory has {_directory.Count} users)", "");

        return BuildLdapResult(messageId, LdapConstants.OP_DELETE_RESPONSE, resultCode, "", "");
    }

    // ──────────────────────────────────────────────────────────
    //  ILS-specific: TTL refresh via sttl in search filter
    // ──────────────────────────────────────────────────────────

    private void HandleTtlRefresh(LdapFilter filter)
    {
        if (filter is not AndFilter andFilter)
        {
            return;
        }

        string cn = null;
        int? ttlMinutes = null;

        foreach (var child in andFilter.Children)
        {
            if (child is EqualityFilter eq)
            {
                if (string.Equals(eq.Attribute, "cn", StringComparison.OrdinalIgnoreCase))
                {
                    cn = eq.Value;
                }
                else if (string.Equals(eq.Attribute, "sttl", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(eq.Value, out var ttl))
                    {
                        ttlMinutes = ttl;
                    }
                }
            }
        }

        if (cn != null && ttlMinutes != null)
        {
            var user = _directory.Find(cn);
            if (user != null)
            {
                user.ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes.Value);
                Log.WriteLine(Log.LEVEL_DEBUG, LOG_SRC, $"TTL refresh: cn=\"{cn}\" ttl={ttlMinutes}m", "");
            }
        }
    }

    // ──────────────────────────────────────────────────────────
    //  Response builders
    // ──────────────────────────────────────────────────────────

    private static byte[] BuildLdapResult(int messageId, int responseTagNumber, int resultCode, string matchedDn, string diagnosticMessage)
    {
        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationConstructed(responseTagNumber, resp =>
            {
                resp.WriteEnumerated(resultCode);
                resp.WriteString(matchedDn);
                resp.WriteString(diagnosticMessage);
            });
        });
        return enc.ToArray();
    }

    private static byte[] BuildSearchResultEntry(int messageId, IlsUser user, List<string> requestedAttrs)
    {
        var attributes = user.GetSelectedAttributes(requestedAttrs);

        var enc = new BerEncoder();
        enc.WriteSequence(msg =>
        {
            msg.WriteInteger(messageId);
            msg.WriteApplicationConstructed(LdapConstants.OP_SEARCH_RESULT_ENTRY, entry =>
            {
                entry.WriteString(user.Dn);
                entry.WriteSequence(attrs =>
                {
                    foreach (var attr in attributes)
                    {
                        attrs.WriteSequence(partialAttr =>
                        {
                            partialAttr.WriteString(attr.Key);
                            partialAttr.WriteSet(vals =>
                            {
                                foreach (var val in attr.Value)
                                {
                                    vals.WriteString(val);
                                }
                            });
                        });
                    }
                });
            });
        });
        return enc.ToArray();
    }
}
