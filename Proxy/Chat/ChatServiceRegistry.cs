// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

using System.Collections.Concurrent;

namespace VintageHive.Proxy.Chat;

/// <summary>
/// Named chat services an embedding host answers for - "YahooHelper" and friends. Deliberately NOT user
/// rows: a service has nothing to sign on as, nothing to impersonate, and must never surface as a member in
/// rosters, presence or the directory. Because those surfaces are all built from the user store or from live
/// sessions, keeping services out of both is the whole exclusion mechanism.
/// <para>
/// Each message path consults this AFTER its live-session relay and BEFORE its "unknown recipient" branch,
/// so a real signed-on member always wins over a service holding the same name.
/// </para>
/// </summary>
public static class ChatServiceRegistry
{
    static readonly ConcurrentDictionary<string, Func<ChatServiceMessage, Task<IReadOnlyList<string>>>> Services = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string name, Func<ChatServiceMessage, Task<IReadOnlyList<string>>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);

        Services[name] = handler;
    }

    public static void Unregister(string name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            Services.TryRemove(name, out _);
        }
    }

    /// <summary>
    /// Runs the handler registered for <paramref name="recipient"/>, if any. Returns null when the name is
    /// not a service (the caller proceeds to its unknown-recipient handling), otherwise the reply lines to
    /// send back as the service - an empty list means "handled, say nothing".
    /// <para>
    /// Matching is case-insensitive, but the caller must attribute replies to the recipient string the
    /// client actually sent, not the registered casing, or the member's IM window may not thread them.
    /// </para>
    /// <para>
    /// A handler fault counts as handled-and-silent: the name IS a service, so falling through would drop
    /// the message as an unknown recipient anyway, and an embedder bug must never take down a member's
    /// session mid-conversation.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> TryHandleAsync(string recipient, string network, string sender, string text)
    {
        if (string.IsNullOrEmpty(recipient) || !Services.TryGetValue(recipient, out var handler))
        {
            return null;
        }

        try
        {
            var replies = await handler(new ChatServiceMessage { SenderUsername = sender, Network = network, Text = text }) ?? Array.Empty<string>();

            Log.WriteLine(Log.LEVEL_DEBUG, nameof(ChatServiceRegistry), $"Service '{recipient}' handled a message from '{sender}' on {network} ({replies.Count} reply line(s))", string.Empty);

            return replies;
        }
        catch (Exception ex)
        {
            Log.WriteException(nameof(ChatServiceRegistry), ex, string.Empty);

            return Array.Empty<string>();
        }
    }
}
