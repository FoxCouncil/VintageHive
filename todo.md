# TODO

- [ ] **SOCKS5 auth** - authentication for the SOCKS proxy
- [ ] **T.128 app-sharing server** - NetMeeting application/desktop sharing
- [ ] **Gopher** - Gopher protocol proxy
- [ ] **Yahoo!/MSN** - Yahoo! Messenger / MSN Messenger protocols

Each is a net-new protocol implementation (a new listener + protocol stack), not an audit defect - tracked here as future work. Completed audit/bug-fix work has moved to [DONE.md](DONE.md).

## New-protocol groundwork (SOCKS5 auth / T.128 app-sharing / Gopher / Yahoo!·MSN)

_Seam map from the audit - reference example is Finger (commit `5bb2471`), the newest end-to-end service. Adding a protocol service touches these 12 places, in wiring order:_

1. **`Proxy/<Name>/<Name>Server.cs`** - subclass `Network.Listener`; ctor `base(addr, port, SocketType.Stream, ProtocolType.Tcp, false)`. Own-the-connection (Finger/SOCKS: do it all in `ProcessConnection`, return `null`, wrap body in try/catch) or request/response (mail/IRC: implement `ProcessRequest`). Call `Mind.Db.RequestsTrack(...)` for dashboard visibility.
2. **`Data/Types/ConfigNames.cs`** - add `Port<Name>` + `Service<Name>` consts (lowercase values).
3. **`Data/Contexts/HiveDbContext.cs` -> `kDefaultGlobalSettings`** - add the port default AND `{ Service<Name>, true }`. Existing installs auto-pick-up via the fallback write-back; no migration.
4. **`Mind.cs`** - three spots: static field, construction in `Init()` reading `ConfigGet<int>(Port<Name>)`, and `StartService(Service<Name>, "<Display>", () => ...Start())` in `Start()`. Toggles apply on restart.
5. **`Processors/LocalServer/Controllers/AdminController.cs` -> `ToggleableServices`** - one `{ "<key>", Service<Name> }` entry feeds both `/api/servicetoggle` and `/api/status`.
6. **`Statics/controllers/admin.hive.com/index.html` -> `serviceLabels`** - add `<key>: "<Display>"`. **Easy to miss (drift trap below).**
7. **`Dockerfile`** - `EXPOSE <port>`. **Currently missed for Finger.**
8. **`README.md`** - four spots: Service Ports row, `### <Name>` feature section, `docker run -p`, and the Service Toggles sentence.
9. **`Tests/VintageHiveTests/<Name>Tests.cs`** - MSTest; extract pure parse/format as `internal static` and write RFC-conformance tests; document intentional deviations. Auto-runs in `ci.yml` - no CI edit.
10. **`todo.md`** - log the integration as checked items under Uncommitted work.
11. *(If it dials out)* respect the processor-chain / DialNine boundary - outbound connects go straight to the live internet by design.
12. *(If it needs per-user auth)* reuse `HiveDbContext.UserFetch` / `UserExistsByUsername` (same store OSCAR/mail/Telnet use) - don't invent a credential table.

**Drift traps (these ARE gotchas):**
- The port registry is copied in **four** places (ConfigNames, kDefaultGlobalSettings, Dockerfile EXPOSE, README) with no single source of truth - Finger already drifted (`EXPOSE 79` was missing; since fixed).
- Admin Services grid **silently drops** any service whose key isn't in BOTH `ToggleableServices` and JS `serviceLabels` (`renderServices` `continue`s) - no error, no warning.
- A missing `kDefaultGlobalSettings` entry fails silent + dangerous: forgotten `Service*` reads `false` (never starts, only INFO log); forgotten `Port*` reads `0` -> `Listener.Run` binds a **random ephemeral port** and nothing throws.
- Port config is **not** universal: OSCAR hardcodes 5190 (incl. in a TLV), MMS/PNA take no port config - copy Finger's ctor, not those. The "core services" block in `Mind.Start()` (HTTP/HTTPS/FTP/Telnet/SOCKS/OSCAR/MMS/PNA) is intentionally non-toggleable - decide which block a new service joins.
- `Listener.Run` is `async void` with floating per-connection `Task.Run` - a new service MUST wrap its handler body in a catch-all (see the fix-first items).

**Per-protocol groundwork that already exists:**
- **SOCKS5 auth**: method-negotiation phase already parses/selects offered methods (`Socks5Handler.cs:19-53`, "NO AUTH only for now"); `Socks5AuthType` enum exists. Work = accept `UsernamePassword (0x02)`, RFC 1929 sub-negotiation, validate via `UserFetch`, add a "require auth" knob. Extend `SocksTests.cs`. (SOCKS4/5 share one port via first-byte dispatch.)
- **T.128 app-sharing**: transport is done - `T120Server` is a working MCS top provider (channel joins, `RelayToChannel`). S20 layer is parse-only (`AppSharing/AppSharingMessage.cs` + `AppSharingConstants.cs`, tests in `AppSharingTests.cs`) and `T120Server` never references it - S20 PDUs are blind-relayed today. Work = S20 session/host logic hooked into the relay path (`Omnet/`/`Chat/`/`Whiteboard/` show the layering). **No new listener/config/toggle** - rides `ServiceT120`/port 1503.
- **Gopher**: nothing exists (only a README link entry). Full 12-step checklist. Closest template is `FingerServer` (one-shot query->response, selector strings are query-shaped); could consume the HTTP processor chain (InternetArchive/ProtoWeb) like `FtpProxy` does if it's meant to proxy.
- **Yahoo!/MSN Messenger**: nothing exists. Template is `Proxy/Oscar/` (server + per-family `Services/`, `oscar_*` tables). Share the presence model - `FingerServer.BuildUserList` reads `OscarServer.Sessions` directly, so a second messenger must feed a common presence abstraction or Finger/cross-messenger presence silently shows only AIM/ICQ. Give it port config from day one (unlike OSCAR's hardcoded 5190).
