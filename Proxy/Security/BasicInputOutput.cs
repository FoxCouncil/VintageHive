// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using static VintageHive.Proxy.Security.Native;
using static System.Text.Encoding;

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
        return BIO_write(Handle, StringEncoding.GetBytes(data), data.Length);
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

    public override void Dispose()
    {
        BIO_free(this);
    }
}
