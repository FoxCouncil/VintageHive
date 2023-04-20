// Copyright (c) 2023 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

public class LogItem
{
    public LogItem() { }

    public LogItem(string level, string system, string message, string traceId)
    {
        Timestamp = DateTimeOffset.UtcNow;
        Level = level;
        System = system;
        Message = message;
        TraceId = string.IsNullOrWhiteSpace(traceId) ? Guid.Empty.ToString() : traceId;
    }

    public DateTimeOffset Timestamp { get; set; }

    public string Level { get; set; }

    public string System { get; set; }

    public string Message { get; set; }

    public string TraceId { get; set; }

    public override string ToString()
    {
        var traceId = "";

        if (TraceId != Guid.Empty.ToString())
        {
            traceId = TraceId;
        }

        return $"[{Timestamp:ddTHH:mm:ss.fffffff}]{(traceId?.Length != 0 ? $"[{traceId}]" : "")}[{Level.ToUpper()}][{System}] {Message}";
    }
}
