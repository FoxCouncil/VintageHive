// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

/// <summary>One entry of a member's stored Yahoo! buddy list. Group and Buddy are the strings exactly as
/// the member's client presented them: the group must round-trip verbatim (the Add-a-Friend dialog files
/// the buddy under a group the client knows about, and a server-invented one locks the dialog up), and
/// buddy resolution to an account happens at use, never at storage, so an entry naming nothing is stored
/// and served identically to one naming a real member (the no-oracle rule).</summary>
public sealed record YahooListEntry(string Group, string Buddy);
