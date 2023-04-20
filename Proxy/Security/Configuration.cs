// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Proxy.Security;

using static VintageHive.Proxy.Security.Native;

internal class Configuration : NativeRef
{
    public Configuration() : base(NCONF_new(IntPtr.Zero)) { }

    public void Load(string filename)
    {
        var eline = 0;

        CheckResultSuccess(NCONF_load(this, filename, ref eline));
    }

    public override void Dispose()
    {
        NCONF_free(this);
    }
}
