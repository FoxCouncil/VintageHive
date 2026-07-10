// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Gopher;

internal class GopherMenuItem
{
    public char Type { get; set; }

    public string Display { get; set; } = string.Empty;

    public string Selector { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 70;

    internal static GopherMenuItem Parse(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var fields = line[1..].Split('\t');

        return new GopherMenuItem
        {
            Type = line[0],
            Display = fields[0],
            Selector = fields.Length > 1 ? fields[1] : string.Empty,
            Host = fields.Length > 2 ? fields[2] : string.Empty,
            Port = fields.Length > 3 && int.TryParse(fields[3].Trim(), out var port) ? port : 70
        };
    }

    internal string Serialize()
    {
        return $"{Type}{Display}\t{Selector}\t{Host}\t{Port}";
    }
}
