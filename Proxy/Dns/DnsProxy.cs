// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;

namespace VintageHive.Proxy.Dns;

/// <summary>
/// Minimal authoritative DNS responder: answers every A query with one configured address.
/// </summary>
/// <remarks>
/// Derives from <see cref="UdpListener"/> rather than hand-rolling a Thread + UdpClient receive loop, which is
/// exactly what that base class documents itself as existing for. Beyond removing the duplicate loop, this is
/// what puts DNS into the admin dashboard's live-listeners grid: that panel is built from the listener
/// registries, so a service outside them was invisible no matter how much traffic it was serving.
/// </remarks>
public class DnsProxy : UdpListener
{
    private const int DNS_HEADER_SIZE = 12;
    private const ushort QTYPE_A = 1;
    private const ushort QCLASS_IN = 1;
    private static readonly uint TTL_SECONDS = 300;

    private readonly IPAddress _responseAddress;

    public DnsProxy(IPAddress listenAddress, int port, IPAddress responseAddress) : base(listenAddress, port)
    {
        _responseAddress = responseAddress;
    }

    public override Task<byte[]> ProcessDatagram(IPEndPoint remoteEndPoint, byte[] data, int length)
    {
        var query = data;

        if (length < DNS_HEADER_SIZE)
        {
            return Task.FromResult<byte[]>(null);
        }

        // -- Parse header -----------------------------------------------
        var transactionId = (ushort)((query[0] << 8) | query[1]);
        var questionCount = (ushort)((query[4] << 8) | query[5]);

        if (questionCount == 0)
        {
            return Task.FromResult<byte[]>(null);
        }

        // -- Parse first question ---------------------------------------
        var offset = DNS_HEADER_SIZE;
        var domainName = ParseDomainName(query, ref offset);

        if (domainName == null || offset + 4 > length)
        {
            return Task.FromResult<byte[]>(null);
        }

        var qtype = (ushort)((query[offset] << 8) | query[offset + 1]);
        var qclass = (ushort)((query[offset + 2] << 8) | query[offset + 3]);

        offset += 4;

        Log.WriteLine(Log.LEVEL_DEBUG, nameof(DnsProxy), $"Query: {domainName} type={qtype} class={qclass} from {remoteEndPoint}", "");

        // -- Build response ---------------------------------------------
        // Copy original question section (header through end of question)
        var questionSection = query[..offset];

        // Only the FIRST question is parsed and echoed, so both builders force QDCOUNT to 1 rather than
        // inheriting a count that would promise more questions than the response carries.
        var response = qtype == QTYPE_A && qclass == QCLASS_IN
            ? BuildAResponse(transactionId, questionSection, offset)
            : BuildEmptyResponse(transactionId, questionSection, offset);

        return Task.FromResult(response);
    }

    private byte[] BuildAResponse(ushort transactionId, byte[] questionSection, int questionEnd)
    {
        // Response = header (12) + question section + answer RR
        // Answer RR: name pointer (2) + type (2) + class (2) + TTL (4) + rdlength (2) + rdata (4) = 16 bytes
        var response = new byte[questionEnd + 16];

        // Copy question section (includes the 12-byte header)
        Buffer.BlockCopy(questionSection, 0, response, 0, questionEnd);

        // Patch header flags: QR=1, AA=1, RD=1, RA=1 -> 0x8580
        response[2] = 0x85;
        response[3] = 0x80;

        // QDCOUNT must be forced to 1, not inherited. Only the FIRST question is parsed and copied, so a
        // query carrying QDCOUNT > 1 produced a response promising N questions while containing one -
        // unparseable to the resolver that sent it.
        response[4] = 0x00;
        response[5] = 0x01;

        // ANCOUNT = 1
        response[6] = 0x00;
        response[7] = 0x01;

        // NSCOUNT = 0, ARCOUNT = 0. These are NOT already zeroed - the header above was copied
        // verbatim from the query, so an EDNS0 client's ARCOUNT=1 (its OPT record) survives the copy
        // and promises an additional RR the response never writes. Omitting OPT entirely is a valid
        // non-EDNS answer (RFC 6891); the counts just have to tell the truth about it.
        response[8] = 0x00;
        response[9] = 0x00;
        response[10] = 0x00;
        response[11] = 0x00;

        // -- Answer RR --------------------------------------------------
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

        // Patch header flags: QR=1, RD=1, RA=1 -> 0x8180
        response[2] = 0x81;
        response[3] = 0x80;

        // QDCOUNT forced to 1 for the same reason as the A-record path: only one question is copied.
        response[4] = 0x00;
        response[5] = 0x01;

        // ANCOUNT/NSCOUNT/ARCOUNT must be cleared, not
        // assumed zero - the copied header still carries the query's counts, including an EDNS0
        // client's ARCOUNT=1 for an OPT record this response deliberately drops.
        response[6] = 0x00;
        response[7] = 0x00;
        response[8] = 0x00;
        response[9] = 0x00;
        response[10] = 0x00;
        response[11] = 0x00;

        return response;
    }

    // Iterative, not recursive: a crafted compression pointer that points to itself (or forms a
    // cycle) would otherwise recurse until StackOverflowException - which is uncatchable and kills
    // the whole process. Pointers here must jump strictly backward AND strictly decrease on each
    // follow (backward-only alone still permits a revisit cycle), with a hard hop budget as backstop.
    private static string ParseDomainName(byte[] data, ref int offset)
    {
        var labels = new List<string>();
        var terminated = false;

        var current = offset;
        var followedPointer = false;
        var minPointer = data.Length;
        var jumps = 0;
        var totalLength = 0;

        while (current < data.Length)
        {
            var labelLen = data[current];

            if (labelLen == 0)
            {
                current++; // Skip the null terminator

                // Only the pre-pointer bytes belong to this name in the original stream
                if (!followedPointer)
                {
                    offset = current;
                }

                terminated = true;

                break;
            }

            // Compression pointer (top two bits set) - shouldn't appear in queries, but handle it
            if ((labelLen & 0xC0) == 0xC0)
            {
                if (current + 1 >= data.Length)
                {
                    return null;
                }

                var pointer = ((labelLen & 0x3F) << 8) | data[current + 1];

                // Reject forward/self pointers, non-decreasing jumps (cycles), and runaway chains
                if (pointer >= current || pointer >= minPointer || ++jumps > 128)
                {
                    return null;
                }

                minPointer = pointer;

                // The name's own bytes end just past this first pointer
                if (!followedPointer)
                {
                    offset = current + 2;

                    followedPointer = true;
                }

                current = pointer;

                continue;
            }

            current++;

            if (current + labelLen > data.Length)
            {
                return null;
            }

            totalLength += labelLen + 1;

            if (totalLength > 255) // RFC 1035 max name length
            {
                return null;
            }

            labels.Add(Encoding.ASCII.GetString(data, current, labelLen));

            current += labelLen;
        }

        return terminated && labels.Count > 0 ? string.Join(".", labels) : null;
    }
}
