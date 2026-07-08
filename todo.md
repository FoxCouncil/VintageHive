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
- [ ] Centralize the `hive.com` base domain - deferred. The `[Domain(...)]` attributes need compile-time literals and the domain is spread across ~10 working files; a full refactor is broad and low-value, so left as-is rather than half-centralized
- [ ] `RepoUtils` custom repositories - deferred. Implementing user-added repos (config store + admin UI) is a feature, not cleanup, and outside the chosen scope

## Test coverage

- [x] `InternetArchiveProcessor` - `RewriteToWorker`, `GetArchiveTypeCode`, and `ProcessCDX` made internal and covered by `InternetArchiveTests.cs` (18 tests over the new Edge Worker path)
- [x] Finger `BuildUserList` covered (see above)
- [ ] FTP request parsing, mail/IRC/telnet/streaming/printer protocol flow - still uncovered; needs socket-level harnesses. Left for a dedicated testing pass

## Discovered during manual testing (pre-existing, NOT introduced here - needs a decision)

- [ ] **Template cache collides across domains.** Fluid's template cache (`LocalServerProcessor`) is only disabled when a debugger is attached (`Mind.IsDebug`), so a released/`dotnet run` build caches parsed partials by their bare path (`partials/header.html`, `partials/footer.html`). `hive.com` and `admin.hive.com` each ship *different* files under those same names, so whichever domain renders a shared partial first wins the cache slot for the whole process. Repro: browse `http://hive.com/` then log into the admin panel -> the dashboard 500s with `FileNotFoundException: partials/menu.html.liquid` (admin inherits hive.com's menu-including header). The reverse order silently strips hive.com's menu. Fox never sees this in dev because the debugger disables the cache. Fix options: disable the template cache unconditionally (simple, negligible perf cost at this traffic level) or make the cache key domain-aware. Left unfixed - it's a caching-architecture change outside this pass's scope.
