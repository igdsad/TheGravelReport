# GravelReview

GravelReview helps you find race incidents in iRacing. It saves the time, points, and driver name for each scored incident it can see. Pick a row, and GravelReview moves the iRacing replay to that spot. No more hunting through a long replay like a lost squirrel.

GravelReview runs on your Windows PC. Your race data stays on your PC. It does not say who was at fault. A driver name tells you which car's score went up, not who caused the crash.

This is a test release. The app has passed 615 tests. It still needs more tests with the live iRacing game.

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

GravelReview saves the list on its own. The button at the bottom right changes the app between the Windows theme, light mode, and dark mode. Only one copy of GravelReview can run at a time. One pit crew is enough.

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

For each non-pace-car, non-spectator entry that iRacing includes in the current `DriverInfo:Drivers` session data with usable deterministic identity and counter evidence, GravelReview observes the cumulative scored `TeamIncidentCount`. Every entrant has an independent durable checkpoint. A temporary omission leaves that checkpoint intact, a later reappearance continues from it, and a counter decrease establishes a new baseline only for that entrant. A team-car driver swap therefore continues the same team/car counter and identity rather than creating a false incident merely because the displayed driver changed. The driver name captured at observation time is context, not an assertion that the named driver caused the points.

The participant key is opaque outside the iRacing adapter and is derived afresh from current roster evidence, so it is identical after an application restart: `car-index:{CarIdx}:team:{TeamID}` for a positive `TeamID`, otherwise `car-index:{CarIdx}:user:{UserID}` when the row explicitly has no positive team and does have a positive user. A row whose required identity evidence is missing, malformed, or non-positive is omitted until usable evidence returns; the durable checkpoint remains intact. Changed positive evidence produces a different identity, while an exact A→B→A reuse of the same car index and canonical ID is inherently indistinguishable from the original entrant and reuses its checkpoint. The incident grid displays the driver name captured with each incident, falling back to team name, car number, or **Unknown driver** when that context is unavailable. **Recorded at** shows the full local time through milliseconds and its UTC offset; sorting that column uses the underlying instant rather than the formatted text. Review status remains durable data but is no longer shown in the primary grid.

`PlayerCarMyIncidentCount` is never used because it is a personal counter and cannot safely share a checkpoint with the team/car counter. If the current-session player row supplies the same deterministic identity but its row-level counter is unavailable, GravelReview may use `PlayerCarTeamIncidentCount`, which has the same whole-team semantics. If session metadata does not match the live `SessionNum`, or no safe current player identity exists, the adapter emits no local participant observation; a sample with no counters still keeps connection, position, and on-track state current. When usable roster evidence returns, comparison resumes against the durable participant checkpoint without any process-memory alias or counter-source mixing.

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

Replay pause/playback behavior, preferred camera group, and theme mode are durable user preferences stored together through `IStore`; older lead-in settings remain compatible, while the primary UI supplies an explicit row-relative offset. Available camera names come from the active iRacing session. A named camera preference is matched case-insensitively against that catalog; no preference preserves the currently reported camera group while focusing the participant stored with the selected incident. Existing databases migrate to **Follow desktop** without changing any replay preference, and the stored mode is applied before the main window becomes visible.

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

## Architecture

The application is split by behavioral boundary:

- `Results` and `Domain` own dependency-light primitives and rules.
- `*.Contracts` projects own simulator-neutral store, telemetry, replay, and application interfaces and immutable values.
- `Application` orchestrates use cases through those contracts; it has no SQLite, Dapper, DbUp, WPF, or Windows interop reference.
- `Store.Sqlite` is one `IStore` implementation. It owns Dapper SQL, connections, transactions, row mapping, DbUp, and schema validation.
- `Iracing` is the only production assembly that knows the SDK ABI or replay broadcast protocol.
- `Desktop.Wpf` talks only to `Application.Contracts` plus domain/result values. It renders one immutable root presentation state; UI events are reduced into a replacement state and external effects remain outside the reducer. That root also keeps configured theme policy separate from the resolved Light/Dark palette. Windows detection and palette application are isolated behind desktop interfaces.
- `Host.Wpf` is the sole composition root and selects the concrete adapters using Microsoft Generic Host and its built-in DI container.

Every store mutation is an immutable typed `IStoreCommand` with an `OperationId`. The SQLite adapter executes it in one owned transaction, writes its operation fingerprint in the same transaction, and supports reconciliation after an indeterminate commit. Runtime values are Dapper parameters; raw SQL is private to the adapter. DbUp applies the checksum-pinned embedded migrations before the store gate opens.

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

JSON is not part of storage, internal messaging, or current configuration. A future user-requested export may use `System.Text.Json` behind a separate export contract.

## Current acceptance gaps

- Run and record the real-iRacing acceptance checklist against a current simulator build.
- Capture a permitted redacted session-information fixture and expand variable-header mutation coverage.
- Validate field-wide `DriverInfo:Drivers[].TeamIncidentCount` behavior in current solo, team, hosted, and admin/broadcaster contexts with **Max Cars = 63**, including participant visibility, driver swaps, temporary omissions, counter resets, simultaneous changes, batched deltas, and the expected `0x` limitation.
- Validate paused-replay classification through `CamCameraState.IsSessionScreen`, camera-group selection, and all replay confirmations across the supported session types in a recorded current-iRacing acceptance run.
- Confirm that replay focus selects the participant stored with each incident, including team-car driver swaps, and returns the expected clear failure when that participant is no longer present in current session metadata.
- Add package-level WPF UI Automation that combines the independent shared-memory protocol simulator with the existing hidden native Windows broadcast receiver; those mechanisms are currently tested separately.
- Complete packaged visual acceptance for both palettes, all interaction states, common scaling levels, Windows high contrast, live Follow-desktop switching, and restart persistence.
- Complete crash/full/locked/corrupt-database campaigns, coverage enforcement, fuzz/mutation/stress jobs, dependency/license inventory, and SBOM generation.
- Add WPF editing for annotations/classification and explicit reviewed/dismissed actions; add export only as a separate capability.

These gaps do not prevent local MVP use, but they prevent a claim that the current checkout is a fully accepted distributable release.
