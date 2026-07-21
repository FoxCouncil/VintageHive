// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Imap;

internal class ImapSession
{
    public ImapState State { get; set; } = ImapState.NotAuthenticated;

    public string Username { get; set; } = string.Empty;

    // AUTHENTICATE LOGIN exchange state: the armed stage, the tag to answer with, and the local part
    // resolved from the username challenge (held until the password challenge completes).
    public ImapAuthExchange AuthExchange { get; set; } = ImapAuthExchange.None;

    public string AuthTag { get; set; } = string.Empty;

    public string AuthUsername { get; set; } = string.Empty;

    public string SelectedMailbox { get; set; } = string.Empty;

    public bool SelectedReadOnly { get; set; }

    public int SelectedMailboxId { get; set; }

    public int UidValidity { get; set; }

    public int UidNext { get; set; }

    public List<EmailMessage> Messages { get; set; } = [];

    // In-flight APPEND literal capture: armed by the APPEND command line, drained byte-counted in
    // ProcessRequest BEFORE line splitting (the payload is message data full of CRLFs), finalized
    // when Remaining hits zero.
    public ImapAppendState Append { get; set; }
}

internal class ImapAppendState
{
    public string Tag { get; init; }

    public int MailboxId { get; init; }

    public string Flags { get; init; }

    public DateTime InternalDate { get; init; }

    public int Remaining { get; set; }

    public StringBuilder Data { get; } = new();
}
