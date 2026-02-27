// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Proxy.Imap;

internal class ImapSession
{
    public ImapState State { get; set; } = ImapState.NotAuthenticated;

    public string Username { get; set; } = string.Empty;

    public string SelectedMailbox { get; set; } = string.Empty;

    public bool SelectedReadOnly { get; set; }

    public int SelectedMailboxId { get; set; }

    public int UidValidity { get; set; }

    public int UidNext { get; set; }

    public List<EmailMessage> Messages { get; set; } = [];
}
