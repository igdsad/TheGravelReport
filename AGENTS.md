# GravelReview Repository Working Agreement

This file defines how work is done throughout this repository. It applies to human contributors and coding agents. `DESIGN.md` is the complete architectural contract; this file is the operational checklist that must be read before changing code.

## Product and compatibility identity

- The user-facing product name is **GravelReview**.
- Existing `IncidentReview.*` project, assembly, namespace, database-path, environment-variable, mutex, and UI Automation identities are compatibility contracts. Do not rename them as cosmetic cleanup.
- Product-facing text may say GravelReview. Technical documentation must distinguish the product name from retained internal identities.

## Library boundaries

- Split behavior by capability into distinct projects with their own `.csproj` files.
- Cross-library communication goes through focused interfaces and immutable contract values. Do not reach into another library's implementation, downcast an interface, expose provider handles, or share mutable internals.
- Contract projects contain abstractions and simulator/provider-neutral values. Implementation projects contain technology-specific code.
- `IncidentReview.Application` orchestrates use cases through contracts. It must not reference WPF, SQLite, Dapper, DbUp, Windows messages, or the iRacing SDK representation.
- `IncidentReview.Desktop.Wpf` depends on application contracts and presentation-safe domain/results values only. It must not query the store, read telemetry, or issue replay protocol commands.
- `IncidentReview.Host.Wpf` is the sole composition root. Concrete adapters are selected only there through Microsoft Generic Host dependency injection.
- A future remote store or alternate simulator adapter must be addable without changing domain or UI use-case policy.
- Update `eng/ArchitecturePolicy.props` and architecture tests deliberately when adding a project or permitted dependency edge. Never bypass them.

## Results and errors

- Expected failures cross boundaries as the repository-standard `Result` or `Result<T>` with a stable error code and safe message.
- Exceptions are reserved for cancellation, programmer errors, and violated invariants.
- Infrastructure translates provider exceptions at its boundary. Raw database, SDK, operating-system, or network errors do not escape to application/UI callers.
- Preserve the most specific actionable error. Do not replace a known cause with a list of possible causes.

## Storage and SQL

- `IStore` is the persistence capability. SQLite is one implementation, not the abstraction.
- Every write is an immutable typed `IStoreCommand` with an `OperationId` and executes through `IStore.ExecuteAsync`.
- The store implementation owns the transaction. All effects of one command, including its operation record/fingerprint, commit or roll back together.
- Application and UI code never receive a connection or transaction.
- Dapper runtime values are always parameters. SQL text is adapter-private and compile-time constant; never construct SQL by concatenating runtime input.
- DbUp migrations are ordered, embedded, checksum-pinned, and append-only once released. Schema changes require migration, upgrade, and rollback/failure tests.
- JSON is not a database, configuration requirement, or internal message format. A future JSON export belongs behind a separate export contract.

## One-way UI state

GravelReview uses a reducer-style, top-down presentation architecture inspired by [Replicant's](https://replicant.fun/) “UI is a function of application state” model, adapted to WPF without adding ClojureScript or a UI framework.

- Each screen has one immutable root presentation state. `MainWindowState` is the only source for main-window display values.
- XAML renders from that root (`State.*`). It must not assemble a screen by querying multiple services or unrelated mutable view-model fields.
- User input and effect results become typed `MainWindowAction` values.
- `MainWindowReducer.Reduce(oldState, action)` is pure: no I/O, clocks, dispatchers, service calls, or mutation. It returns a replacement state.
- The view model owns effects—application calls, cancellation, monitoring, and UI-thread dispatch—then sends their results through the reducer.
- Application data enters through one coherent, revisioned `ReviewSnapshot`. Do not rebuild the same screen with independent status/session/preferences/context queries.
- A status notice and its event-log entry are the same `EventLogItem` instance. Never maintain separate warning strings for the status bar and event stream.
- Ignore lower snapshot revisions. Equal revisions may refresh transient simulator context. Automatic refresh preserves valid in-progress selection/drafts; a manual refresh deliberately reports its result.
- Keep the event log bounded and tail-following. It is presentation state, not a durable diagnostic sink.
- Busy state may temporarily overlay the status text, but it must not destroy the underlying current notice.

## Desktop theming and branding

- WPF styling uses native resource dictionaries only. `Themes/Base.xaml` owns structure and control styles; `Themes/Light.xaml` and `Themes/Dark.xaml` own matching semantic color-resource sets. Controls consume semantic `DynamicResource` keys rather than literal palette colors.
- The durable `ThemePreference` (`FollowDesktop`, `Light`, or `Dark`) is policy, while `ResolvedTheme` is the concrete palette currently rendered. Never collapse these into one value: following Windows must remain distinguishable from forcing whichever palette Windows currently uses.
- Windows theme detection stays behind `IDesktopThemeSource`; palette resolution/application stays behind `IThemeController`. Ordinary view-model, reducer, application, and domain code must not read the registry or subscribe to static operating-system events.
- `MainWindowState` owns both the configured preference and resolved palette. The window applies the palette represented by that state; it does not resolve an independent theme. `FollowDesktop` reacts to live Windows changes, while forced Light and Dark modes ignore them.
- The status-bar theme control cycles `FollowDesktop → Light → Dark → FollowDesktop`. A selection is rendered immediately, then persisted with all other preferences through the transactional `IStore` path. On persistence failure, restore the complete prior preference and resolve its current palette before publishing the one exact error event/status.
- Apply the stored preference before the main window becomes visible. New and version-1 databases default to `FollowDesktop` through append-only migration `002`; never edit migration `001` or discard existing replay preferences during a theme update.
- Use `assets/branding/logo.png` for in-app GravelReview branding and `assets/branding/favicon.ico` for the executable/window icon. Keep the compatibility-stable internal `IncidentReview.*` identities unchanged.

## iRacing boundary

- Only `IncidentReview.Iracing` knows shared-memory layouts, session YAML shape, SDK numeric identifiers, or Windows replay broadcast encoding.
- Prefer the smallest repository-owned transcription of the authenticated official SDK surface. Do not add an unofficial SDK wrapper casually.
- `IReplayController` issues simulator-neutral replay intents. `IReplayContextReader` exposes only transient driver/camera display context.
- Driver and camera parsing fail independently. Missing display metadata must not disable otherwise valid replay control.
- Report seek, camera, and playback success only after later stable telemetry confirms the requested state; accepting a Windows message is not success.

## Dependencies and build configuration

- Prefer BCL and official Microsoft dependencies. Dapper, DbUp, SQLitePCLRaw, and SQLite are reviewed exceptions already recorded in `DESIGN.md`.
- Centralize package versions in `Directory.Packages.props`, SDK selection in `global.json`, and repository-wide compiler/analyzer policy in `Directory.Build.props`/`Directory.Build.targets`.
- Project files declare package use but never individual versions.
- Keep NuGet lock files committed. Regenerate them only for an intentional dependency change and review the complete graph.
- Do not add a package when a small, clear implementation using the existing stack suffices.

## Testing philosophy

- Follow the Richard Hipp/SQLite reliability philosophy recorded in `DESIGN.md`: design test seams up front, assume untested behavior is broken, and test actual public interfaces and Release artifacts.
- Every behavior change includes focused tests with requirement/invariant IDs. Test independent conditions and boundaries, not only happy paths.
- Use deterministic fakes at contracts, real SQLite for adapter behavior, independent protocol peers for iRacing integration, and fault injection for failure/recovery paths.
- A bug fix adds a regression test that would have failed before the fix.
- Production code builds with warnings as errors. Before handoff, run focused tests, `git diff --check`, and the full Release verification gate when practical.

## Git workflow

- Make small, reviewable Conventional Commits. Use the scopes and types defined in `DESIGN.md`.
- Each commit should represent one coherent boundary change and be green on its relevant tests. Do not hide feature, migration, dependency, or architecture changes in `chore`.
- Preserve unrelated user changes. Never rewrite published history or use destructive reset/checkout commands without explicit authorization.
- Pull/rebase, push, publish, or tag only when authorized for the current repository workflow.

## Change checklist

Before considering work complete:

1. Identify the owning library and public interface.
2. Confirm dependency direction remains legal.
3. Model expected failures with `Result`/`Result<T>`.
4. Route persistence writes through one typed transactional command.
5. Route presentation changes through the immutable root-state reducer.
6. Add focused regression/contract tests and requirement IDs.
7. Run focused Release tests, repository verification, and `git diff --check`.
8. Update `DESIGN.md` and user documentation when behavior or an architectural rule changes.
9. Commit the change as a small Conventional Commit.
