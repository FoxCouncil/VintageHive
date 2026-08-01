// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Security;

public abstract class NativeRef : IDisposable
{
    public static implicit operator IntPtr(NativeRef obj) => obj.Handle;

    public IntPtr Handle { get; set; }

    public bool IsOwner { get; private set; }

    public NativeRef(IntPtr handle, bool owner = true)
    {
        Handle = handle;

        IsOwner = owner;

        if (Handle == IntPtr.Zero)
        {
            throw new OpenSslException("NativeRef object failed to be created");
        }
    }

    /// <summary>
    /// Releases the native handle, but only when this wrapper actually owns it.
    /// </summary>
    /// <remarks>
    /// The IsOwner check used to live here while every concrete subclass OVERRODE Dispose outright and called
    /// its native free unconditionally - so the guard was dead code and a wrapper built over a borrowed
    /// pointer (Asn1Integer, Asn1DateTime and CryptoKey all have owner:false constructors) would free memory
    /// it did not own. Subclasses now supply FreeHandle instead, which is only reached when ownership is real,
    /// and the handle is zeroed afterwards so a double dispose is a no-op rather than a double free.
    /// </remarks>
    public virtual void Dispose()
    {
        if (!IsOwner || Handle == IntPtr.Zero)
        {
            return;
        }

        FreeHandle();

        Handle = IntPtr.Zero;

        GC.SuppressFinalize(this);
    }

    /// <summary>Release the native handle. Called only when this wrapper owns it and it is non-null.</summary>
    protected virtual void FreeHandle()
    {
        throw new NotImplementedException($"FreeHandle not implemented for {GetType().Name}");
    }
}
