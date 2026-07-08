# TODO

## Uncommitted work (finish before commit)

- [x] Finger: `ServiceFinger` on/off config flag added (`ConfigNames.cs`, default true in `HiveDbContext.cs`) and gated in `Mind.cs`
- [x] Finger: admin panel toggle - new Services section in `admin.hive.com/index.html` driven by `/api/servicetoggle` and `/api/status`
- [x] Finger: README entries - Service Ports row (79/TCP), feature section, Docker port, config toggle
- [x] Finger: tests for `BuildUserList` (empty, populated, skips blank screen names) added to `FingerTests.cs`
- [x] IA worker: documented the Edge Worker toggle + URL in the README Configuration section
- [x] `wayback-proxy-test/` scratch scaffold deleted

## Quick wins

- [x] NU1903 cleared - pinned `SQLitePCLRaw.lib.e_sqlite3` 3.53.3 (patched SQLite ≥ 3.50.2, CVE-2025-6965); `dotnet list package --vulnerable` is clean for both projects
- [x] Removed the `[PROXY-DEBUG]` stderr spam in `HttpProxy.cs` (and the matching `[LISTENER]` keep-alive spam in `Listener.cs`)
- [x] ICQ banner URL `192.168.69.1` -> `api.hive.com` (`OscarIcqService.cs`)
- [x] FTP proxy placeholder password gate (`"PENIS"`) removed (`FtpRequest.cs`); real FTP auth stays roadmap
- [x] Dockerfile installs ffmpeg; `FfmpegUtils` falls back to PATH ffmpeg when the bundled binary is absent; bundled ffmpeg binaries dropped from the Linux image (fixes arm64 + saves ~240MB)
- [x] README `docker run` example gains SOCKS `1996` and Finger `79`
- [x] `HiveController` download path traversal hardened - `file` param resolved with `Path.GetFullPath` and confirmed inside the repo root
- [x] CI: new `.github/workflows/ci.yml` builds + tests on push/PR (ubuntu + windows matrix)
- [x] `actions/setup-dotnet@v3` -> v4 in `ci-release.yml`

## Bigger gaps

- [x] Service toggles made functional - `Mind.Start()` gates each listener on its `Service*` flag (SMTP, POP3, IMAP, IRC, Usenet, DNS, Printer, ILS, RAS, H323, T120, Finger); intranet gated in `LocalServerProcessor` with `admin.hive.com` always exempt so the panel can re-enable it. Listener changes apply on restart, intranet applies immediately
- [x] README aligned - Contributing no longer claims debug builds enable extra services; Configuration documents the runtime toggles and restart semantics
- [x] `LocalServerFileProvider.GetDirectoryContents` returns `NotFoundDirectoryContents.Singleton` instead of throwing
- [x] FTP response caching dead block removed from `FtpProxy.cs` (documented why it's not implemented)
- [x] `tools/UsenetCurator/README.md` written - pipeline phases, modes, CLI flags, and how the output feeds `NntpProxy`
- [ ] HTTPS proxy (9999) - still local + DialNine only, no ProtoWeb/IA passthrough. Out of scope this pass (roadmap: HTTPS re-enablement)
- [ ] SOCKS5 auth, T.128 app-sharing server, Gopher, Yahoo!/MSN - out of scope this pass (new-protocol roadmap items)

## Cleanup

- [x] H.323 stack: the ~27 bare `catch {}` around `ReadExtensionAdditions` replaced with `PerDecoder.TryReadExtensionAdditions()`, which catches the specific decode exceptions and documents why the best-effort swallow is correct
- [x] `PrinterProxy` unknown IPP operations now return a well-formed `ServerErrorOperationNotSupported` response instead of throwing
- [x] `HttpProxy` request tracking now skips all `*.hive.com` intranet hosts (was a hardcoded `admin.com`/`hive.com` check)
- [x] `SCUtils.GetStationById` - removed the stale cache TODO (caching is already implemented directly below it)
- [x] `.editorconfig` expanded with the Allman / always-braces / no-line-limit style rules
- [x] Centralize the `hive.com` base domain - done. All subdomains, host checks, and email addresses derive from `HiveDomains.Base`; const concatenation (`"admin." + Base`) keeps it usable in `[Domain(...)]` attributes.
- [x] `RepoUtils` custom download repos - done. Repos are stored in config and managed from the Local Server admin page (add with validation, delete); the built-in downloads folder is reserved and can't be removed.

## Test coverage

- [x] `InternetArchiveProcessor` - `RewriteToWorker`, `GetArchiveTypeCode`, and `ProcessCDX` made internal and covered by `InternetArchiveTests.cs` (18 tests over the new Edge Worker path)
- [x] Finger `BuildUserList` covered (see above)
- [~] Protocol-flow coverage - in progress. IRC done: parser, reply wire-formatting, nick validation, and channel logic (`IrcProtocolTests.cs`, 46 tests). Still to cover: FTP request parsing, mail (SMTP/POP3/IMAP), Telnet BBS, MMS/PNA streaming, printer.

## Discovered during manual testing (pre-existing, NOT introduced here)

- [x] **Template cache collides across domains - FIXED.** Fluid's template cache was only disabled when a debugger was attached (`Mind.IsDebug`), so released/`dotnet run` builds cached parsed partials by bare path (`partials/header.html`). `hive.com` and `admin.hive.com` ship different files under those names, so whichever domain rendered a shared partial first won the slot process-wide - browse `http://hive.com/` then open the admin dashboard and it 500'd with `FileNotFoundException: partials/menu.html.liquid`; the reverse silently stripped hive.com's menu. Fox never saw it in dev because the debugger disabled the cache. Fixed by disabling `TemplateOptions.Default.TemplateCache` unconditionally in `LocalServerProcessor` (negligible cost at this traffic; every include now re-resolves through the per-request FileProvider). Verified live: hive.com->admin and admin->hive.com both render 200.
- [x] **Flaky RTP relay test - FIXED.** `RtpRelayManagerTests.GetStatistics_WithActiveRelays` intermittently threw `SocketException: Only one usage of each socket address` from `RtpRelay`'s bind. Root cause was a TOCTOU race in `RtpRelayManager.AllocateEvenPort`: it probe-bound a port, released it, then rounded to an even port and bound `evenPort`/`evenPort+1` - neither of which was reliably verified free - so the RTCP port (`evenPort+1`) could collide with a port another test held. Fixed by dropping the misleading test-binds and making the real bind authoritative: `CreateRelay` now retries with a fresh pair on `SocketException` (up to 10 attempts). Hardens the production NetMeeting relay too, not just the test. Verified: 25 iterations under heavy ephemeral-port contention + 45 full-suite runs, zero failures.
- [x] **Related - shared FileProvider mutated per request - FIXED.** `new TemplateContext(model)` uses the static `TemplateOptions.Default` (where the custom filters/`UnsafeMemberAccessStrategy` live), and `ControllerManager` used to assign `Context.Options.FileProvider` on it per request, which raced across concurrent cross-domain requests (one render could pick up another domain's FileProvider). Fixed by registering a single shared `LocalServerFileProvider` on `TemplateOptions.Default` once, and having it read the per-request controller root from an `AsyncLocal` (`SetCurrentRoot` called in `ControllerManager`) so each async request flow is isolated. Verified with a concurrency stress test: ~760 interleaved hive.com/admin.hive.com requests at up to 40-way concurrency, zero cross-domain contamination.
