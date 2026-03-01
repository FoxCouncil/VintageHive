// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Net.Sockets;

namespace VintageHive.Proxy.Dns;

internal class DnsProxy
{
    private const int DNS_HEADER_SIZE = 12;
    private const ushort QTYPE_A = 1;
    private const ushort QCLASS_IN = 1;
    private static readonly uint TTL_SECONDS = 300;

    private readonly IPAddress _listenAddress;
    private readonly int _port;
    private readonly IPAddress _responseAddress;

    private Thread _thread;
    private volatile bool _running;

    public DnsProxy(IPAddress listenAddress, int port, IPAddress responseAddress)
    {
        _listenAddress = listenAddress;
        _port = port;
        _responseAddress = responseAddress;
    }

    public void Start()
    {
        _running = true;

        _thread = new Thread(Run)
        {
            Name = nameof(DnsProxy)
        };

        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
    }

    private async void Run()
    {
        using var udp = new UdpClient(new IPEndPoint(_listenAddress, _port));

        Log.WriteLine(Log.LEVEL_INFO, nameof(DnsProxy), $"Starting DNS Proxy...{_listenAddress}:{_port}", "");

        while (_running)
        {
            UdpReceiveResult result;

            try
            {
                result = await udp.ReceiveAsync();
            }
            catch (SocketException) when (!_running)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.WriteException(nameof(DnsProxy), ex, "");

                continue;
            }

            var query = result.Buffer;
            var remote = result.RemoteEndPoint;

            if (query.Length < DNS_HEADER_SIZE)
            {
                continue;
            }

            // ── Parse header ───────────────────────────────────────────────
            var transactionId = (ushort)((query[0] << 8) | query[1]);
            var questionCount = (ushort)((query[4] << 8) | query[5]);

            if (questionCount == 0)
            {
                continue;
            }

            // ── Parse first question ───────────────────────────────────────
            var offset = DNS_HEADER_SIZE;
            var domainName = ParseDomainName(query, ref offset);

            if (domainName == null || offset + 4 > query.Length)
            {
                continue;
            }

            var qtype = (ushort)((query[offset] << 8) | query[offset + 1]);
            var qclass = (ushort)((query[offset + 2] << 8) | query[offset + 3]);

            offset += 4;

            Log.WriteLine(Log.LEVEL_DEBUG, nameof(DnsProxy), $"Query: {domainName} type={qtype} class={qclass} from {remote}", "");

            // ── Build response ─────────────────────────────────────────────
            // Copy original question section (header through end of question)
            var questionSection = query[..offset];

            byte[] response;

            if (qtype == QTYPE_A && qclass == QCLASS_IN)
            {
                response = BuildAResponse(transactionId, questionSection, offset);
            }
            else
            {
                // Non-A query: return NOERROR with zero answers
                response = BuildEmptyResponse(transactionId, questionSection, offset);
            }

            try
            {
                await udp.SendAsync(response, response.Length, remote);
            }
            catch (Exception ex)
            {
                Log.WriteLine(Log.LEVEL_DEBUG, nameof(DnsProxy), $"Failed to send response to {remote}: {ex.Message}", "");
            }
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(DnsProxy), "Stopping DNS Proxy...", "");
    }

    private byte[] BuildAResponse(ushort transactionId, byte[] questionSection, int questionEnd)
    {
        // Response = header (12) + question section + answer RR
        // Answer RR: name pointer (2) + type (2) + class (2) + TTL (4) + rdlength (2) + rdata (4) = 16 bytes
        var response = new byte[questionEnd + 16];

        // Copy question section (includes the 12-byte header)
        Buffer.BlockCopy(questionSection, 0, response, 0, questionEnd);

        // Patch header flags: QR=1, AA=1, RD=1, RA=1 → 0x8580
        response[2] = 0x85;
        response[3] = 0x80;

        // QDCOUNT = 1 (already set from query)
        // ANCOUNT = 1
        response[6] = 0x00;
        response[7] = 0x01;

        // NSCOUNT = 0, ARCOUNT = 0 (already zeroed)

        // ── Answer RR ──────────────────────────────────────────────────
        var pos = questionEnd;

        // Name: pointer to offset 12 (start of question name)
        response[pos++] = 0xC0;
        response[pos++] = 0x0C;

        // Type: A (1)
        response[pos++] = 0x00;
        response[pos++] = 0x01;

        // Class: IN (1)
        response[pos++] = 0x00;
        response[pos++] = 0x01;

        // TTL: 300 seconds
        response[pos++] = (byte)(TTL_SECONDS >> 24);
        response[pos++] = (byte)(TTL_SECONDS >> 16);
        response[pos++] = (byte)(TTL_SECONDS >> 8);
        response[pos++] = (byte)(TTL_SECONDS);

        // RDLENGTH: 4 (IPv4)
        response[pos++] = 0x00;
        response[pos++] = 0x04;

        // RDATA: the VintageHive server IP
        var addrBytes = _responseAddress.MapToIPv4().GetAddressBytes();

        Buffer.BlockCopy(addrBytes, 0, response, pos, 4);

        return response;
    }

    private static byte[] BuildEmptyResponse(ushort transactionId, byte[] questionSection, int questionEnd)
    {
        // Response = just the question section with patched header (zero answers)
        var response = new byte[questionEnd];

        Buffer.BlockCopy(questionSection, 0, response, 0, questionEnd);

        // Patch header flags: QR=1, RD=1, RA=1 → 0x8180
        response[2] = 0x81;
        response[3] = 0x80;

        // QDCOUNT = 1 (already set), ANCOUNT = 0, NSCOUNT = 0, ARCOUNT = 0

        return response;
    }

    private static string ParseDomainName(byte[] data, ref int offset)
    {
        var labels = new List<string>();

        while (offset < data.Length)
        {
            var labelLen = data[offset];

            if (labelLen == 0)
            {
                offset++; // Skip the null terminator

                break;
            }

            // Compression pointer (top two bits set) — shouldn't appear in queries, but handle it
            if ((labelLen & 0xC0) == 0xC0)
            {
                if (offset + 1 >= data.Length)
                {
                    return null;
                }

                var pointer = ((labelLen & 0x3F) << 8) | data[offset + 1];
                var pointerOffset = pointer;

                // Follow the pointer to read the rest of the name
                var rest = ParseDomainName(data, ref pointerOffset);

                if (rest != null && rest.Length > 0)
                {
                    labels.Add(rest);
                }

                offset += 2;

                return string.Join(".", labels);
            }

            offset++;

            if (offset + labelLen > data.Length)
            {
                return null;
            }

            labels.Add(Encoding.ASCII.GetString(data, offset, labelLen));

            offset += labelLen;
        }

        return labels.Count > 0 ? string.Join(".", labels) : null;
    }
}
