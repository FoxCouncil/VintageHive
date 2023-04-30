// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Utilities;

internal class HttpFixerDelegatingStream : DelegatingStream
{
    byte[] icyHeaderSignature = Encoding.ASCII.GetBytes("ICY");

    internal HttpFixerDelegatingStream(Stream innerStream) : base(innerStream) { }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await base.ReadAsync(buffer, cancellationToken);        

        if (buffer.Span[0] == icyHeaderSignature[0] && buffer.Span[1] == icyHeaderSignature[1] && buffer.Span[2] == icyHeaderSignature[2])
        {
            var incomingHeader = Encoding.ASCII.GetString(buffer.ToArray(), 0, read);

            var replacedHeader = incomingHeader.Replace("ICY 200 OK", "HTTP/1.0 200 OK");

            var replacedHeaderBytes = Encoding.ASCII.GetBytes(replacedHeader);

            for (var i = 0; i < replacedHeaderBytes.Length; i++)
            {
                buffer.Span[i] = replacedHeaderBytes[i];
            }

            read = replacedHeaderBytes.Length;
        }

        return read;
    }
}
