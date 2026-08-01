// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using static System.Text.Encoding;
using static VintageHive.Proxy.Security.Native;

namespace VintageHive.Proxy.Security;

public class BasicInputOutput : NativeRef
{
    public uint PendingBytes => BIO_ctrl_pending(this);

    public Encoding StringEncoding { get; private set; } = ASCII;

    public BasicInputOutput() : base(BIO_new(BIO_s_mem())) { }

    public BasicInputOutput(string data) : this()
    {
        Write(data);
    }

    public int Read(byte[] buffer, int count)
    {
        return BIO_read(Handle, buffer, count);
    }

    public int Write(string data)
    {
        // Byte count, not character count. data.Length happens to equal the encoded length for the ASCII PEM
        // this handles today, but StringEncoding is a mutable property, so any multibyte encoding would have
        // written a truncated buffer with a mismatched length.
        var bytes = StringEncoding.GetBytes(data);

        return BIO_write(Handle, bytes, bytes.Length);
    }

    public int Write(byte[] buffer, int length)
    {
        return BIO_write(Handle, buffer, length);
    }

    public void SetClosed()
    {
        var ret = BIO_set_close(Handle, BinaryInputOutputClose);

        if (ret != 1)
        {
            throw new OpenSslException("Failed to set BIO close");
        }
    }

    public override string ToString()
    {
        var output = "";

        if (PendingBytes > 0)
        {
            var bytes = new byte[PendingBytes];

            Read(bytes, bytes.Length);

            output = ASCII.GetString(bytes);
        }

        return output;
    }

    protected override void FreeHandle()
    {
        BIO_free(this);
    }
}
