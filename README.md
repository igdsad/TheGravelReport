# iRacing Incident Review

A local Windows companion that watches iRacing's local telemetry, records increases in the local member's incident counter, keeps the resulting review points in SQLite, and lets the user select an incident and seek iRacing replay to it.

The repository contains a runnable MVP. Automated Release startup and protocol-simulator coverage exist, but acceptance against a recorded current real iRacing build is still required before treating it as a public release.

## Quick start

Prerequisites:

- Windows x64;
- the .NET SDK selected by [`global.json`](global.json) (`10.0.400` feature band);
- iRacing for live telemetry/replay use. It is not needed to build or run the automated tests.

From PowerShell in the repository root:

```powershell
.\eng\verify.ps1 -Configuration Release

dotnet run --project .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore --no-build
```

The app can start while iRacing is closed and will wait and reconnect. Once a live session produces an incident-counter increase, select the session and incident. Exit the car so telemetry reports `NotOnTrack`, then choose **Review selected incident**. Review seeks to the stored session time minus the configured lead-in and applies the configured pause/playback setting. It deliberately does not mark the incident reviewed automatically.

Only one app instance may run in the Windows user session.

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

Replay lead-in, pause/playback speed, and preferred camera are durable user preferences stored through `IStore`. Preferred camera is retained for forward compatibility but is not yet applied to iRacing.

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

Publish the framework-dependent Windows x64 app after a successful restore/build:

```powershell
dotnet publish .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore `
    --output .\artifacts\publish\win-x64
```

The target machine needs the matching .NET Desktop Runtime. The output must include `THIRD-PARTY-NOTICES\iracing-sdk-1.20.md`. An installer, updater, and self-contained packaging policy have not been selected.

## Architecture

The application is split by behavioral boundary:

- `Results` and `Domain` own dependency-light primitives and rules.
- `*.Contracts` projects own simulator-neutral store, telemetry, replay, and application interfaces and immutable values.
- `Application` orchestrates use cases through those contracts; it has no SQLite, Dapper, DbUp, WPF, or Windows interop reference.
- `Store.Sqlite` is one `IStore` implementation. It owns Dapper SQL, connections, transactions, row mapping, DbUp, and schema validation.
- `Iracing` is the only production assembly that knows the SDK ABI or replay broadcast protocol.
- `Desktop.Wpf` talks only to `Application.Contracts` plus domain/result values.
- `Host.Wpf` is the sole composition root and selects the concrete adapters using Microsoft Generic Host and its built-in DI container.

Every store mutation is an immutable typed `IStoreCommand` with an `OperationId`. The SQLite adapter executes it in one owned transaction, writes its operation fingerprint in the same transaction, and supports reconciliation after an indeterminate commit. Runtime values are Dapper parameters; raw SQL is private to the adapter. DbUp applies the checksum-pinned embedded migration before the store gate opens.

Boundary rules are defined once in [`eng/ArchitecturePolicy.props`](eng/ArchitecturePolicy.props), enforced during MSBuild, inspected again in compiled-assembly architecture tests, and supplemented by repository analyzers for non-constant Dapper SQL, missing Dapper mutation transactions (including `CommandDefinition`), and nested service-provider construction.

See [`DESIGN.md`](DESIGN.md) for the complete architecture, transaction/error semantics, testing philosophy, dependency policy, and future evolution path.

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
- Determine whether an authoritative SDK signal exists for a replay manually entered and paused outside the app-owned workflow.
- Add package-level WPF UI Automation and an independent replay broadcast receiver to the protocol simulator.
- Complete crash/full/locked/corrupt-database campaigns, coverage enforcement, fuzz/mutation/stress jobs, dependency/license inventory, and SBOM generation.
- Add WPF editing for annotations/classification and explicit reviewed/dismissed actions; add export only as a separate capability.

These gaps do not prevent local MVP use, but they prevent a claim that the current checkout is a fully accepted distributable release.
