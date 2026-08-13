// Copyright (c) 2026 Fox Council - VintageHive - https://github.com/FoxCouncil/VintageHive

namespace VintageHive.Data.Types;

/// <summary>One entry of a member's stored MSN contact lists (FL/AL/BL/RL). Contact is the string exactly
/// as the member's client presented it - resolution to an account happens at use, never at storage, so an
/// entry naming nothing is stored and served identically to one naming a real member (the no-oracle rule).
/// DisplayName is stored DECODED (wire encoding is applied exactly once, on the way out); GroupId only
/// means anything on the FL, where MSNP7's trailing group field carries it.</summary>
public sealed record MsnListEntry(string Contact, string DisplayName, int GroupId);

/// <summary>Per-owner MSN list metadata: the list serial the client versions its cache with, and the
/// GTC/BLP behavior settings SYN serves back. A member who has never edited has no row and gets these
/// defaults - the exact constants the pre-persistence SYN always answered.</summary>
public sealed record MsnListState(int Serial, string Gtc, string Blp);
