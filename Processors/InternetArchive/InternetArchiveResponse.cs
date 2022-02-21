using System.Text.Json.Serialization;

namespace VintageHive.Processors.InternetArchive;

public class InternetArchiveResponse
{
    public string url { get; set; }

    public ArchivedSnapshots archived_snapshots { get; set; }

    public string timestamp { get; set; }
}

public class ArchivedSnapshots
{
    public Closest closest { get; set; }
}

public class Closest
{
    public string status { get; set; }

    public bool available { get; set; }

    public string url { get; set; }

    public string timestamp { get; set; }
}