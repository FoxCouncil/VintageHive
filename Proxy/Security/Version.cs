namespace VintageHive.Proxy.Security;

public class Version
{
    const byte PatchCharAlignmentNumber = 97;

    const string VersionStringFormat = "{0}.{1}.{2}{3} {4} (0x{5:x8})";

    public uint Raw { get; }

    public uint Major { get; }

    public uint Minor { get; }

    public uint Fix { get; }

    public uint Patch { get; }

    public char PatchChar { get; }

    public uint Status { get; }

    public OpenSSLVersionStatus StatusEnum { get; }

    public Version(uint rawVersion)
    {
        Raw = rawVersion;
        Major = (rawVersion & 0xF0000000) >> 28;
        Minor = (rawVersion & 0x0FF00000) >> 20;
        Fix = (rawVersion & 0x000FF000) >> 12;
        Patch = (rawVersion & 0x00000FF0) >> 4;
        PatchChar = Patch == 0 ? (char)Patch : (char)(PatchCharAlignmentNumber + (Patch - 1));
        Status = rawVersion & 0x0000000F;

        if (Status == 0)
        {
            StatusEnum = OpenSSLVersionStatus.Development;
        }
        else if (Status == 0xF)
        {
            StatusEnum = OpenSSLVersionStatus.Release;
        }
        else
        {
            StatusEnum = OpenSSLVersionStatus.Beta;
        }
    }

    public override string ToString()
    {
        return string.Format(VersionStringFormat, Major, Minor, Fix, PatchChar, StatusEnum, Raw);
    }
}

public enum OpenSSLVersionStatus
{
    Development,
    Beta,
    Release
}
