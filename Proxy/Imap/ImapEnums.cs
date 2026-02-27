// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Imap;

internal enum ImapState
{
    NotAuthenticated,
    Authenticated,
    Selected,
    Logout
}

internal enum ImapResponseType
{
    OK,
    NO,
    BAD,
    PREAUTH,
    BYE
}

internal static class ImapMessageFlag
{
    public const string Seen = @"\Seen";
    public const string Answered = @"\Answered";
    public const string Flagged = @"\Flagged";
    public const string Deleted = @"\Deleted";
    public const string Draft = @"\Draft";
    public const string Recent = @"\Recent";

    public static readonly string[] AllPermanentFlags = [Seen, Answered, Flagged, Deleted, Draft];
    public static readonly string[] AllFlags = [Seen, Answered, Flagged, Deleted, Draft, Recent];
}

internal static class ImapDefaults
{
    public static readonly string[] DefaultMailboxNames = ["INBOX", "Sent", "Drafts", "Trash"];
}
