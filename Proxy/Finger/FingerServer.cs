// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using VintageHive.Network;
using VintageHive.Proxy.Oscar;

namespace VintageHive.Proxy.Finger;

internal class FingerServer : Listener
{
    public FingerServer(IPAddress listenAddress, int port) : base(listenAddress, port, SocketType.Stream, ProtocolType.Tcp, false) { }

    public override async Task<byte[]> ProcessConnection(ListenerSocket connection)
    {
        var traceId = connection.TraceId.ToString();

        Log.WriteLine(Log.LEVEL_INFO, nameof(FingerServer), $"Client connected from {connection.RemoteAddress}", traceId);

        try
        {
            var buffer = new byte[512];
            var read = await connection.Stream.ReadAsync(buffer);

            if (read == 0)
            {
                return null;
            }

            var query = Encoding.ASCII.GetString(buffer, 0, read).TrimEnd('\r', '\n');

            Log.WriteLine(Log.LEVEL_INFO, nameof(FingerServer), $"Query: \"{query}\"", traceId);

            var (parsedQuery, verbose, isForwarding) = ParseQuery(query);

            if (isForwarding)
            {
                await WriteLineAsync(connection, "Finger forwarding is not supported.");
                return null;
            }

            query = parsedQuery;

            string response;

            if (string.IsNullOrEmpty(query))
            {
                response = BuildUserList();
            }
            else
            {
                response = BuildUserInfo(query, verbose);
            }

            await WriteLineAsync(connection, response);
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(FingerServer), ex, traceId);
        }

        Log.WriteLine(Log.LEVEL_INFO, nameof(FingerServer), $"Client disconnected from {connection.RemoteAddress}", traceId);

        return null;
    }

    internal static string BuildUserList()
    {
        var sb = new StringBuilder();

        sb.AppendLine("VintageHive Finger Server");
        sb.AppendLine(new string('-', 60));

        var sessions = OscarServer.Sessions.ToList();

        if (sessions.Count == 0)
        {
            sb.AppendLine("No users currently online.");
        }
        else
        {
            sb.AppendLine($"{"User",-20} {"Status",-15} {"Idle",-10} {"On Since"}");
            sb.AppendLine($"{new string('-', 20)} {new string('-', 15)} {new string('-', 10)} {new string('-', 20)}");

            foreach (var session in sessions)
            {
                if (string.IsNullOrEmpty(session.ScreenName))
                {
                    continue;
                }

                var status = FormatStatus(session);
                var idle = FormatIdle(session);
                var signOn = session.SignOnTime.LocalDateTime.ToString("ddd MMM dd HH:mm");

                sb.AppendLine($"{session.ScreenName,-20} {status,-15} {idle,-10} {signOn}");
            }
        }

        return sb.ToString();
    }

    private static string BuildUserInfo(string username, bool verbose)
    {
        var sb = new StringBuilder();

        if (!Mind.Db.UserExistsByUsername(username))
        {
            sb.AppendLine($"finger: {username}: no such user.");
            return sb.ToString();
        }

        // Check online status
        var session = OscarServer.Sessions.GetByScreenName(username);
        var isOnline = session != null;

        // Get profile from DB
        var profile = Mind.Db.OscarGetProfile(username);

        sb.AppendLine($"Login: {username,-30} Name: {FormatRealName(profile)}");

        if (isOnline)
        {
            sb.AppendLine($"On since {session.SignOnTime.LocalDateTime:ddd MMM dd HH:mm} on hive");

            var idle = session.GetCurrentIdleSeconds();

            if (idle > 0)
            {
                sb.AppendLine($"Idle {FormatIdleLong(idle)}");
            }

            sb.AppendLine($"Status: {FormatStatus(session)}");

            if (!string.IsNullOrEmpty(session.AwayMessage))
            {
                sb.AppendLine($"Away: {session.AwayMessage}");
            }
        }
        else
        {
            sb.AppendLine("Not currently logged in.");
        }

        if (profile != null)
        {
            if (!string.IsNullOrEmpty(profile.Email))
            {
                sb.AppendLine($"Mail: {profile.Email}");
            }

            if (!string.IsNullOrEmpty(profile.Homepage))
            {
                sb.AppendLine($"Homepage: {profile.Homepage}");
            }

            if (verbose)
            {
                if (!string.IsNullOrEmpty(profile.HomeCity) || !string.IsNullOrEmpty(profile.HomeState))
                {
                    sb.AppendLine($"Location: {FormatLocation(profile.HomeCity, profile.HomeState)}");
                }

                if (!string.IsNullOrEmpty(profile.WorkCompany))
                {
                    var work = profile.WorkCompany;

                    if (!string.IsNullOrEmpty(profile.WorkPosition))
                    {
                        work = $"{profile.WorkPosition} at {work}";
                    }

                    sb.AppendLine($"Work: {work}");
                }
            }
        }

        // .plan - we use the Oscar profile text as the plan
        if (isOnline && !string.IsNullOrEmpty(session.Profile))
        {
            sb.AppendLine("Plan:");
            sb.AppendLine(session.Profile);
        }
        else if (profile != null && !string.IsNullOrEmpty(profile.Notes))
        {
            sb.AppendLine("Plan:");
            sb.AppendLine(profile.Notes);
        }
        else
        {
            sb.AppendLine("No Plan.");
        }

        return sb.ToString();
    }

    internal static (string query, bool verbose, bool isForwarding) ParseQuery(string raw)
    {
        var query = raw.TrimEnd('\r', '\n');
        var verbose = false;

        // RFC 1288: /W prefix requests "verbose" output
        if (query.StartsWith("/W ", StringComparison.OrdinalIgnoreCase) || query.Equals("/W", StringComparison.OrdinalIgnoreCase))
        {
            verbose = true;
            query = query.Length > 3 ? query[3..].TrimStart() : "";
        }

        // Reject forwarding requests (user@host@host) per RFC 1288 security
        var isForwarding = query.Contains('@');

        return (query, verbose, isForwarding);
    }

    internal static string FormatRealName(OscarUserProfile profile)
    {
        if (profile == null)
        {
            return "(unknown)";
        }

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(profile.FirstName))
        {
            parts.Add(profile.FirstName);
        }

        if (!string.IsNullOrEmpty(profile.LastName))
        {
            parts.Add(profile.LastName);
        }

        if (parts.Count == 0 && !string.IsNullOrEmpty(profile.Nickname))
        {
            return profile.Nickname;
        }

        return parts.Count > 0 ? string.Join(" ", parts) : "(unknown)";
    }

    internal static string FormatStatus(OscarSession session)
    {
        return session.Status switch
        {
            OscarSessionOnlineStatus.Online => "Online",
            OscarSessionOnlineStatus.Away => "Away",
            OscarSessionOnlineStatus.DoNotDisturb => "Do Not Disturb",
            OscarSessionOnlineStatus.NotAvailable => "Not Available",
            OscarSessionOnlineStatus.Occupied => "Occupied",
            OscarSessionOnlineStatus.FreeToChat => "Free to Chat",
            OscarSessionOnlineStatus.Invisible => "Invisible",
            _ => "Online"
        };
    }

    internal static string FormatIdle(OscarSession session)
    {
        var seconds = session.GetCurrentIdleSeconds();

        if (seconds == 0)
        {
            return "-";
        }

        return FormatIdleLong(seconds);
    }

    internal static string FormatIdleLong(uint seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        if (seconds < 3600)
        {
            return $"{seconds / 60}m";
        }

        return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
    }

    internal static string FormatLocation(string city, string state)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(city))
        {
            parts.Add(city);
        }

        if (!string.IsNullOrEmpty(state))
        {
            parts.Add(state);
        }

        return string.Join(", ", parts);
    }

    private static async Task WriteLineAsync(ListenerSocket connection, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text.Replace("\n", "\r\n"));

        await connection.Stream.WriteAsync(bytes);
    }
}
