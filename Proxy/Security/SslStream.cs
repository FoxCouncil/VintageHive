// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Runtime.InteropServices;

namespace VintageHive.Proxy.Security;

public class SslStream : NativeRef
{
    delegate void SslStreamCallback(IntPtr ssl, int where, int ret);

    public SslContext Context { get; set; }

    internal NetworkStream Stream { get; }

    readonly BasicInputOutput bioInput;

    readonly BasicInputOutput bioOutput;

    HandshakeState handshakeState = HandshakeState.None;

    SslStreamCallback callback;

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

        bioInput = new BasicInputOutput();
        bioInput.SetClosed();

        bioOutput = new BasicInputOutput();
        bioOutput.SetClosed();

        Native.SSL_set_bio(this, bioInput, bioOutput);

        callback = new SslStreamCallback(SslInfoCallback);

        Native.SSL_set_info_callback(this, Marshal.GetFunctionPointerForDelegate(callback));

        var ret = SetAccept();
    }

    public override void Dispose()
    {
        Native.SSL_free(Handle);

        GC.KeepAlive(callback);

        callback = null;
    }

    public void UseCertificate(X509Certificate cert)
    {
        if (Native.SSL_use_certificate(this, cert) != 1)
        {
            throw new OpenSslException();
        }
    }

    public void UseRSAPrivateKey(Rsa key)
    {
        if (Native.SSL_use_RSAPrivateKey(this, key) != 1)
        {
            throw new OpenSslException();
        }
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
            bioInput.Write(buffer.ToArray(), encryptedBytesRead);
        }

        var newBuffer = new byte[4096];

        var unencryptedBytesRead = Native.SSL_read(this, newBuffer, newBuffer.Length);

        if (unencryptedBytesRead == -1)
        {
            Native.CheckResultSuccess(Native.SSL_get_error(this, unencryptedBytesRead));
        }

        newBuffer.CopyTo(buffer);

        return unencryptedBytesRead;
    }

    public async ValueTask<int> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return await WriteRawAsync(buffer.ToArray(), buffer.Length, cancellationToken);
    }

    public async ValueTask<int> WriteRawAsync(byte[] buffer, int length, CancellationToken cancellationToken = default)
    {
        var unencryptedBytesWritten = Native.SSL_write(this, buffer, length);

        if (bioOutput.PendingBytes > 0)
        {
            var newBuffer = new byte[bioOutput.PendingBytes];

            var bytesRead = bioOutput.Read(newBuffer, newBuffer.Length);

            await Stream.WriteAsync(newBuffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return unencryptedBytesWritten;
    }

    void SslInfoCallback(IntPtr ssl, int where, int ret)
    {
        if (where == Native.SSL_CB_HANDSHAKE_START)
        {
            handshakeState = HandshakeState.Started;
        }
        else if (where == Native.SSL_CB_HANDSHAKE_DONE)
        {
            handshakeState = HandshakeState.Done;
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

        while (handshakeState != HandshakeState.Done)
        {
            if (tick == 0)
            {
                throw new OpenSslException("Handshake timeout.");
            }

            HandshakeReadSocket(buffer);

            HandshakeWriteSocket(buffer);

            tick--;
        }

        // System.Diagnostics.Debugger.Break();

        void HandshakeReadSocket(byte[] buffer)
        {
            var read = Stream.Read(buffer, 0, buffer.Length);

            bioInput.Write(buffer, read);

            var ret = Native.SSL_do_handshake(this);

            var error = Native.SSL_get_error(this, ret);

            if (error == Native.SSL_ERROR_SSL)
            {
                throw new OpenSslException();
            }
            else if (error == Native.SSL_ERROR_WANT_READ)
            {
                // System.Diagnostics.Debugger.Break();
            }
            else
            {
                // System.Diagnostics.Debugger.Break();
            }
        }

        void HandshakeWriteSocket(byte[] buffer)
        {
            while (bioOutput.PendingBytes > 0)
            {
                var bytesRead = bioOutput.Read(buffer, buffer.Length);

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
