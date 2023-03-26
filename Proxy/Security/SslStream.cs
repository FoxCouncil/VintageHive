using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

public class SslStream : NativeRef
{
    delegate void SslStreamCallback(IntPtr ssl, int where, int ret);

    public SslContext Context { get; set; }

    internal NetworkStream Stream { get; }

    readonly BasicInputOutput _bioInput;

    readonly BasicInputOutput _bioOutput;

    HandshakeState _handshakeState = HandshakeState.None;

    SslStreamCallback _callback;

    public SslStream(SslContext context, NetworkStream stream) : base(Native.SSL_new(context))
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));

        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead || !stream.CanWrite)
        {
            throw new ArgumentException($"{nameof(stream)} must be have read and write enabled.", nameof(stream));
        }

        Stream = stream;

        SetAcceptState();

        _bioInput = new BasicInputOutput();
        _bioInput.SetClosed();

        _bioOutput = new BasicInputOutput();
        _bioOutput.SetClosed();

        Native.SSL_set_bio(this, _bioInput, _bioOutput);

        _callback = new SslStreamCallback(SslInfoCallback);

        Native.SSL_set_info_callback(this, Marshal.GetFunctionPointerForDelegate(_callback));

        var ret = SetAccept();
    }

    public override void Dispose()
    {
        Native.SSL_free(Handle);

        GC.KeepAlive(_callback);

        _callback = null;
    }

    public void SetAcceptState()
    {
        Native.SSL_set_accept_state(this);
    }

    public int SetAccept()
    {
        return Native.SSL_accept(this);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var encryptedBytesRead = await Stream.ReadAsync(buffer, cancellationToken);

        if (encryptedBytesRead != 0)
        {
            _bioInput.Write(buffer.ToArray(), encryptedBytesRead);
        }

        var newBuffer = new byte[4096];

        var unencryptedBytesRead = Native.SSL_read(this, newBuffer, newBuffer.Length);

        newBuffer.CopyTo(buffer);

        return unencryptedBytesRead;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var unencryptedBytesWritten = Native.SSL_write(this, buffer.ToArray(), buffer.Length);

        if (_bioOutput.PendingBytes > 0)
        {
            var newBuffer = new byte[_bioOutput.PendingBytes];

            var bytesRead = _bioOutput.Read(newBuffer, newBuffer.Length);

            await Stream.WriteAsync(newBuffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    void SslInfoCallback(IntPtr ssl, int where, int ret)
    {
        if (where == Native.SSL_CB_HANDSHAKE_START)
        {
            _handshakeState = HandshakeState.Started;
        }
        else if (where == Native.SSL_CB_HANDSHAKE_DONE)
        {
            _handshakeState = HandshakeState.Done;
        }
        else if (where == Native.SSL_CB_READ_ALERT)
        {
            //Display.WriteLog("Read alert: {0}", ret);
        }
        else if (where == Native.SSL_CB_WRITE_ALERT)
        {
            //Display.WriteLog("Write alert: {0}", ret);
        }
        else if (where == Native.SSL_CB_EXIT)
        {
            //Display.WriteLog("Exit: {0}", ret);
        }
        else if (where == Native.SSL_CB_ACCEPT_LOOP)
        {
            //Display.WriteLog($"Accept loop: {ret}");
        }
        else if (where == Native.SSL_CB_ACCEPT_EXIT)
        {
            //Display.WriteLog($"Accept exit: {ret}");
        }
        else if (where == Native.SSL_CB_CONNECT_LOOP)
        {
            //Display.WriteLog($"Connect loop: {ret}");
        }
        else if (where == Native.SSL_CB_CONNECT_EXIT)
        {
            //Display.WriteLog($"Connect exit: {ret}");
        }

        // Display.WriteLog($"SSL_INFO_CB: {where} {ret}");
    }

    internal void AuthenticateAsServer()
    {
        var buffer = new byte[1024];
        var tick = 5;

        while (_handshakeState != HandshakeState.Done)
        {
            if (tick == 0)
            {
                throw new OpenSslException("Handshake timeout.");
            }

            HandshakeReadSocket(buffer);

            HandshakeWriteSocket(buffer);

            tick--;
        }

        // Debugger.Break();

        void HandshakeReadSocket(byte[] buffer)
        {
            var read = Stream.Read(buffer, 0, buffer.Length);

            _bioInput.Write(buffer, read);

            var ret = Native.SSL_do_handshake(this);

            var error = Native.SSL_get_error(this, ret);

            if (error == Native.SSL_ERROR_SSL)
            {
                throw new OpenSslException();
            }
            else if (error == Native.SSL_ERROR_WANT_READ)
            {
            }
            else
            {
                // Debugger.Break();
            }
        }

        void HandshakeWriteSocket(byte[] buffer)
        {
            while (_bioOutput.PendingBytes > 0)
            {
                var bytesRead = _bioOutput.Read(buffer, buffer.Length);

                Stream.Write(buffer, 0, bytesRead);
            }
        }
    }

    public enum HandshakeState
    {
        None,
        Started,
        Done
    }
}
