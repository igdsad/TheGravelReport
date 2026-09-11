# GravelReview

GravelReview is a local-first Windows companion that helps you find race incidents in iRacing. It saves the time, points, and driver context for each scored incident iRacing exposes, and it lets a driver mark custom replay moments from a keyboard or wheel-mapped button. Pick a row and GravelReview moves the iRacing replay to that spot. Drivers can optionally join a league-review server and send custom markers to one central reviewer after they leave the car.

GravelReview runs on your Windows PC. Your race data stays on your PC. It does not say who was at fault. A driver name tells you which car's score went up, not who caused the crash.

This is an alpha test release. The automated suite passes, but the app still needs broader testing with the live iRacing game.

## How to use it

You need:

- a 64-bit Windows PC;
- iRacing;
- **Max Cars** set to **63** in iRacing, so the game can send as many cars as it can to the app.

Then:

1. Download `GravelReview-win-x64.zip` from the GitHub release.
2. Unzip the file.
3. Open `GravelReview-win-x64.exe`. You do not need to install .NET.
   Windows may warn you because this test app is not signed yet. Only run a file from a release you trust.
4. Start iRacing and join a session. You may also open GravelReview first. It will wait for iRacing.
5. Drive. GravelReview will add a row when it sees scored incident points go up.
6. Get out of the car before you review an incident.
7. Find the row by its **Recorded at** time and **Driver** name.
8. Pick **-2 sec**, **0 sec**, or **+2 sec**. iRacing will move the replay to that car and time.

**Recorded at** shows the full time, down to a tiny part of a second. This keeps close events in the right order. If a driver name is missing, the app shows the team, car number, or **Unknown driver**.

One iRacing event stays in one list. Heats that share one iRacing event ID—or stay on one live game connection when that ID is missing—keep their old rows. Each car gets a fresh start, so a score reset does not make fake points. A league night that uses a different event ID starts a different list. One long story, not a pile of tiny books.

GravelReview saves the list on its own. Keep it open for the whole league night when you can. If iRacing gives the app a lasting event ID, closing and reopening the app will still find that event. If that ID is missing, a restart must begin a new list. The button at the bottom right changes the app between the Windows theme, light mode, and dark mode. Only one copy of GravelReview can run at a time. One pit crew is enough.

GravelReview can only save what iRacing sends to it. iRacing may not send every car in every race. A `0x` touch does not add points, so the app cannot see it.

## Build and run from source

Developers need the .NET SDK selected by [`global.json`](global.json) (`10.0.400` feature band). iRacing is not needed to build or run the automated tests.

From PowerShell in the repository root:

```powershell
.\eng\verify.ps1 -Configuration Release

dotnet run --project .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore --no-build
```

## Field-wide incident tracking

For each non-pace-car, non-spectator entry that iRacing includes in the current `DriverInfo:Drivers` session data with usable identity and counter evidence, GravelReview chooses one scored counter shape. When `CurDriverIncidentCount` is present, it uses that driver counter and keeps team drivers in separate streams. A negative or broken value is still driver-shaped; an opponent is skipped until a good value returns. When the field is absent, GravelReview supports the older cumulative `TeamIncidentCount` shape and keeps one stream for the team car. Team cars add the driver ID when needed. A no-team car is already keyed by its user ID. This keeps counters with different meanings out of the same checkpoint.

For custom markers, set a submitter name and choose a Windows key gesture; `F9` is the default. A steering-wheel button works when its driver or mapping software emits that gesture. Pressing it during a live session immediately saves the session-relative replay position to local SQLite, so a network outage cannot discard the marker. The marker's deterministic ID comes from the iRacing-derived session identity and exact replay position—not the submitter name or local clock.

One user can start a joinable event session locally, or use the address of the shared server, and share the generated code. The code contains both the advertised HTTP(S) server address and the deterministic iRacing session identity; other users paste that one code to join. Pending markers are sent only after authoritative telemetry says the driver is out of the car. The server keeps the first payload received for each deterministic event ID and reports later copies as duplicates; clients mark either an accepted or duplicate response synchronized and surface a duplicate warning.

Join codes are locators, not secrets. The current public alpha endpoint has no authentication and plain HTTP provides no encryption: anyone who learns the address and session ID can submit a claimed name or race to be the first payload for an event ID. Use it only with that limitation understood; an authenticated TLS deployment remains future hardening work.

The far-right status-bar button cycles the appearance through **Follow desktop → Light → Dark → Follow desktop**. Its monitor, sun, or moon icon shows the configured mode, and its tooltip states both the current mode and what the next click will select. Follow desktop reacts to Windows theme changes while the app is running; forced Light and Dark do not. The choice is applied immediately and saved to SQLite. If saving fails, GravelReview restores the previous appearance and reports that exact failure in the shared status/event stream.

The participant key is opaque outside the iRacing adapter and is derived afresh from current roster evidence. Team-counter rows use `car-index:{CarIdx}:team:{TeamID}` for a positive `TeamID`, otherwise `car-index:{CarIdx}:user:{UserID}`. A current-driver row for a team car adds `:driver-user:{UserID}`. A row whose required identity evidence is missing, malformed, or non-positive is omitted until usable evidence returns; its saved checkpoint stays intact. The incident grid displays the driver name captured with each incident, falling back to team name, car number, or **Unknown driver** when that context is unavailable. **Recorded at** shows the full local time through milliseconds and its UTC offset; sorting that column uses the underlying instant rather than the formatted text. Review status remains durable data but is no longer shown in the primary grid.

`PlayerCarMyIncidentCount` is never used. If the selected row counter is unavailable for the local car, GravelReview may use the matching scalar: `PlayerCarDriverIncidentCount` for a driver stream or `PlayerCarTeamIncidentCount` for a team stream. It never uses one kind as a substitute for the other. If session metadata does not match live `SessionNum`, or no safe identity exists, the adapter emits no local participant observation. When usable roster evidence returns, comparison resumes from that participant's saved checkpoint.

A positive `WeekendInfo:SubSessionID` now creates the event-wide key `v2:event:subsession:{id}`. Heat number stays on every incident's replay position, but it is no longer part of the event key. A heat-number change advances each returning participant's counter epoch and establishes a baseline without an incident. Databases made by an older build remain valid, but their `v1` per-heat rows are not guessed together or rewritten. Upgrading in the middle of an event can look like a one-time new list: the old rows are still saved, but this screen shows the new `v2` list.

“Field-wide” is deliberately bounded. GravelReview can record only eligible entries and counter changes that iRacing exposes and transmits to this client. [iRacing's documented incident-visibility policy](https://www.iracing.com/2016-season-3-release-notes/) restricts ordinary clients in a live race to their own team's count, while admins can see the field; non-race and completed-race visibility is broader. Current-build validation remains pending, and session type, client visibility, admin/broadcaster context, connection settings, and server transmission may all affect the available data. The scored counter cannot reveal a `0x` contact because no value changes. If one observed update jumps by several incident points, GravelReview records one marker containing the complete delta rather than inventing separate event times for intermediate points. If multiple entrants increase in the same update, each gets its own marker.

## Data and configuration

SQLite is embedded and needs no service or daemon. The default database path is:

```text
%LOCALAPPDATA%\IncidentReview\incident-review.db
```

Override startup configuration with command-line arguments:

```powershell
dotnet run --project .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore --no-build -- `
    --database-path "C:\IncidentReviewData\incident-review.db" `
    --startup-timeout-seconds 30
```

The database path must be an absolute local file path. Startup timeout accepts 1 through 120 seconds. The equivalent environment variables are `INCIDENTREVIEW_DatabasePath` and `INCIDENTREVIEW_StartupTimeoutSeconds`; command-line values take precedence. There is no JSON configuration file in the current runtime.

Replay pause/playback behavior, preferred camera group, theme mode, custom-event submitter name, custom-event key gesture, and optional join code are durable user preferences stored together through `IStore`; older lead-in settings remain compatible, while the primary UI supplies an explicit row-relative offset. Available camera names come from the active iRacing session. A named camera preference is matched case-insensitively against that catalog; no preference preserves the currently reported camera group while focusing the participant stored with the selected incident. Existing databases migrate to **Follow desktop**, an `F9` custom-event gesture, no submitter, and no joined session without changing any replay preference; the stored theme mode is applied before the main window becomes visible.

## Appearance and branding

GravelReview's light and dark visual system uses only native WPF resources. [`Base.xaml`](src/IncidentReview.Desktop.Wpf/Themes/Base.xaml) owns shared control structure, while [`Light.xaml`](src/IncidentReview.Desktop.Wpf/Themes/Light.xaml) and [`Dark.xaml`](src/IncidentReview.Desktop.Wpf/Themes/Dark.xaml) provide matching semantic palettes. The UI includes [`logo-simplified.png`](assets/branding/logo-simplified.png), and the Windows executable/window uses [`favicon-simplified.ico`](assets/branding/favicon-simplified.ico). Product-facing surfaces say GravelReview; compatibility-stable `IncidentReview.*` projects, namespaces, database paths, environment variables, mutex, and Automation IDs intentionally retain their existing names.

## Build, test, and publish

The repository scripts use locked NuGet restore by default:

```powershell
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release
.\eng\verify.ps1 -Configuration Release
```

`verify.ps1` runs the Release build and test suite, then invokes the repository verification executable. That executable is currently a no-op scaffold; coverage aggregation, traceability checks, dependency inventory, and artifact-hash enforcement remain release work.

Only an intentional dependency change should regenerate lock files:

```powershell
.\eng\build.ps1 -Configuration Release -UpdateLockFiles
```

Review every resulting `packages.lock.json` change before committing it.

Create the Windows x64 release artifacts with the repository-owned packaging script:

```powershell
.\eng\publish.ps1
```

The script performs a locked restore, publishes the untrimmed self-contained single-file host, validates the exact output, and smoke-tests the renamed executable with an isolated temporary database. It leaves `artifacts\release\win-x64\GravelReview-win-x64.exe` for local use and `artifacts\release\win-x64\GravelReview-win-x64.zip` for a GitHub release. The ZIP is the distributable asset because it contains both the executable and the required `THIRD-PARTY-NOTICES\iracing-sdk-1.20.md`; the target machine does not need a separately installed .NET Desktop Runtime. The external filename uses the GravelReview product name while the internal `IncidentReview.Host.Wpf` assembly identity remains unchanged. An installer and updater have not been selected.

The repository also contains a self-contained Linux x64 event server. It accepts the same versioned custom-event POSTs and stores the first payload for each deterministic event ID in its own SQLite inbox. See [`deploy/server/README.md`](deploy/server/README.md) for publish, systemd installation, configuration, and health-check instructions; the target server does not need a system-wide .NET runtime.

## Architecture

The application is split by behavioral boundary:

- `Results` and `Domain` own dependency-light primitives and rules.
- `*.Contracts` projects own simulator-neutral store, telemetry, replay, event-sync, and application interfaces and immutable values.
- `Application` orchestrates use cases through those contracts; it owns local-first custom-event/outbox policy but has no SQLite, Dapper, DbUp, WPF, Kestrel, HTTP wire, or Windows interop reference.
- `Store.Sqlite` is one `IStore` implementation. It owns Dapper SQL, connections, transactions, row mapping, DbUp, schema validation, and the durable custom-event outbox rows.
- `EventSync.Contracts` defines versioned join-code, submission, publisher, receiver, and joinable-session-host capabilities. `EventSync.Http` is the replaceable `HttpClient`/Kestrel adapter and is the only event-sync assembly that owns JSON wire DTOs and HTTP routes. `EventSync.Sqlite` is the cross-platform first-write-wins server inbox.
- `Iracing` is the only production assembly that knows the SDK ABI or replay broadcast protocol. It exposes both the meaningful telemetry stream and a read-only latest-frame snapshot, so a hotkey marker uses the freshest accepted replay position even when position-only stream updates are coalesced.
- `Desktop.Wpf` talks only to `Application.Contracts` plus domain/result values. It renders one immutable root presentation state; UI events are reduced into a replacement state and external effects remain outside the reducer. That root also keeps configured theme policy separate from the resolved Light/Dark palette. Windows detection and palette application are isolated behind desktop interfaces.
- `Host.Wpf` is the Windows desktop composition root. `Host.Server` is the independent headless Linux-capable composition root for `EventSync.Http` and `EventSync.Sqlite`. Both select concrete adapters using Microsoft hosting and its built-in DI container; neither owns business rules.

Every store mutation is an immutable typed `IStoreCommand` with an `OperationId`. The SQLite adapter executes it in one owned transaction, writes its operation fingerprint in the same transaction, and supports reconciliation after an indeterminate commit. Runtime values are Dapper parameters; raw SQL is private to the adapter. DbUp applies the checksum-pinned embedded migrations before the store gate opens.

Durable sessions, detected incidents, and custom events use namespaced RFC UUIDv5 identities. A durable session is derived from canonical simulator code plus the exact validated opaque iRacing event key; an incident is derived from session, participant identity, counter epoch, and resulting incident total; a custom event is derived from session, replay session number, and integer session-time milliseconds. Existing UUIDv7 session/incident rows remain readable, while connection-scoped provisional identities continue to use UUIDv7 and cannot be hosted or joined.

Boundary rules are defined once in [`eng/ArchitecturePolicy.props`](eng/ArchitecturePolicy.props), enforced during MSBuild, inspected again in compiled-assembly architecture tests, and supplemented by repository analyzers for non-constant Dapper SQL, missing Dapper mutation transactions (including `CommandDefinition`), and nested service-provider construction.

See [`DESIGN.md`](DESIGN.md) for the complete architecture, transaction/error semantics, testing philosophy, dependency policy, and future evolution path. [`AGENTS.md`](AGENTS.md) is the mandatory day-to-day working agreement for contributors and coding agents, so these patterns do not have to be re-established in each task.

## Official iRacing SDK baseline

The adapter was transcribed from the official iRacing SDK 1.20 archive obtained through iRacing's authenticated member-forum distribution:

- archive: `irsdk-1-20.zip`;
- size: 102,658 bytes;
- SHA-256: `af4948cc8efe03fa7c99332a63da1ab9d7b34e5b6107540c176ed682e48a2d79`.

The upstream archive is not a build input, NuGet package, or installed library. The application uses repository-owned .NET code over memory-mapped files, the SDK event, and narrowly scoped User32 replay broadcasts. No unofficial iRacing wrapper is used. The upstream source itself is not vendored; only provenance and the applicable reviewed notice are retained.

See [`docs/iracing-sdk-baseline.md`](docs/iracing-sdk-baseline.md) for exact source URLs, reviewed file hashes, ABI layouts, field choices, broadcast packing, licensing scope, and test evidence.

## Dependencies and supply chain

Dependency versions are centralized in [`Directory.Packages.props`](Directory.Packages.props), the SDK/test SDK is controlled by [`global.json`](global.json), and each consuming project commits a NuGet lock file. [`NuGet.config`](NuGet.config) clears inherited sources, maps packages to HTTPS NuGet.org, requires trusted repository signatures, and enables audit through root build policy.

The runtime is intentionally small but is not Microsoft-only. Dapper and DbUp are approved third-party exceptions, and SQLite/SQLitePCLRaw are third-party components. The SQLite provider is Microsoft's `Microsoft.Data.Sqlite.Core`; `SQLitePCLRaw.bundle_winsqlite3` binds to the Windows-serviced `winsqlite3.dll` rather than shipping a separate SQLite engine.

JSON is not part of storage, configuration, or in-process application contracts. `System.Text.Json` is used only inside the versioned HTTP event-sync adapter's bounded wire protocol; a future user-requested export may use it independently behind a separate export contract.

## Current acceptance gaps

- Run and record the real-iRacing acceptance checklist against a current simulator build.
- Capture a permitted redacted session-information fixture and expand variable-header mutation coverage.
- Validate field-wide `DriverInfo:Drivers[].CurDriverIncidentCount` and legacy `TeamIncidentCount` behavior in current solo, team, hosted, and admin/broadcaster contexts with **Max Cars = 63**, including absent, negative, malformed, or changing source fields; matching local scalars; multi-heat league nights; driver swaps; omissions; resets; simultaneous or batched deltas; and the expected `0x` limitation.
- Validate paused-replay classification through `CamCameraState.IsSessionScreen`, camera-group selection, and all replay confirmations across the supported session types in a recorded current-iRacing acceptance run.
- Confirm that replay focus uses the saved car/team across heat numbers. A saved driver-scoped team identity is reduced to the stable team car for focus; changed, missing, or ambiguous car/team evidence must fail clearly.
- Add package-level WPF UI Automation that combines the independent shared-memory protocol simulator with the existing hidden native Windows broadcast receiver; those mechanisms are currently tested separately.
- Complete packaged visual acceptance for both palettes, all interaction states, common scaling levels, Windows high contrast, live Follow-desktop switching, and restart persistence.
- Harden the deployed public-alpha event-sync server for production use, including authentication/authorization, TLS termination, discovery, abuse controls, retention, and container-hosting operations. The current endpoint is unauthenticated and unencrypted, and the join code deliberately grants no security property.
- Complete crash/full/locked/corrupt-database campaigns, coverage enforcement, fuzz/mutation/stress jobs, dependency/license inventory, and SBOM generation.
- Add WPF editing for annotations/classification and explicit reviewed/dismissed actions; add export only as a separate capability.

These gaps do not prevent local MVP use, but they prevent a claim that the current checkout is a fully accepted distributable release.
