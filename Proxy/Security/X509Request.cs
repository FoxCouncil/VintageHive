﻿// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Security;

internal class X509Request : NativeRef
{
    public X509Request() : base(Native.X509_REQ_new()) { }

    public override void Dispose()
    {
        Native.X509_REQ_free(this);
    }
}
