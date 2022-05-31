using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VintageHive.Proxy.Security;

internal class BasicInputOutput : NativeRef
{
    public uint PendingBytes => Native.BIO_ctrl_pending(this);

    public BasicInputOutput() : base(Native.BIO_new(Native.BIO_s_mem())) { }

    public int Read(byte[] buffer, int count)
    {
        return Native.BIO_read(Handle, buffer, count);
    }

    public int Write(byte[] buffer, int length)
    {
        return Native.BIO_write(Handle, buffer, length);
    }

    public void SetClosed()
    {
        var ret = Native.BIO_set_close(Handle, Native.BinaryInputOutputClose);

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

            output = Encoding.ASCII.GetString(bytes);
        }

        return output;
    }

    public override void Dispose()
    {
        Native.BIO_free(this);
    }
}
