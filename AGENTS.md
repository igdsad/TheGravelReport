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
- `IncidentReview.Host.Wpf` is the desktop composition root. `IncidentReview.Host.Server` is the headless event-receiver composition root. Concrete adapters are selected only in the host that owns their process through Microsoft Generic Host dependency injection.
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
- JSON is not a database, configuration requirement, or in-process message format. Bounded JSON wire DTOs stay private to `EventSync.Http`; a future JSON export belongs behind a separate export contract.

## Deterministic identities and custom-event synchronization

- Durable simulator sessions use RFC UUIDv5 derived only from the canonical simulator code and exact validated opaque simulator-session key. Connection-scoped provisional sessions remain UUIDv7 and MUST NOT be advertised in a join code.
- Detected incident UUIDv5 identity is derived from session identity, participant identity, counter epoch, and resulting incident-points total. Custom-event UUIDv5 identity is derived from session identity and exact replay position (replay session number plus session time in integer milliseconds).
- Namespaces, component order, canonical formatting, and version labels used for deterministic identities are persisted/protocol contracts. Changing any of them requires a new version, migration/compatibility design, and golden-vector tests; never silently re-key existing rows.
- Submitter name and wall-clock occurrence time are payload, not custom-event identity. Two submissions with the same custom-event ID are duplicates even when those payload values differ; the first committed payload is authoritative and later duplicates are ignored with a safe warning.
- Custom-event capture is local-first. Record one typed store command before attempting network I/O, retain unsynchronized events as a durable SQLite outbox, and never perform HTTP inside a store transaction.
- The application, not the HTTP adapter, owns outbox timing and retry. It may publish only after authoritative telemetry says the driver is out of the car; an accepted or duplicate server response may then mark the local event synchronized.
- Join codes are versioned locators containing the advertised HTTP(S) base URI and deterministic session identity. They are not secrets or credentials. The current local/league-night protocol has no authentication; do not imply confidentiality, authorization, or safe public-Internet exposure.
- Cross-boundary synchronization uses `IncidentReview.EventSync.Contracts`. Wire encoding, HTTP routing, listener mechanics, and transport failures stay in `IncidentReview.EventSync.Http`, selected by `Host.Wpf` for local hosting and by `Host.Server` for the always-on receiver. The server-only SQLite inbox stays in `IncidentReview.EventSync.Sqlite`; UI, domain, application, and general store contracts never depend on either adapter.

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
- WPF may write `SelectedItem` back while rebinding an `ItemsSource`. That render-time write-back is not new user intent: suppress selector setters while publishing root state, make equivalent selection actions return the existing state instance, and preserve unchanged collection identities across unrelated state transitions.
- Run shown-window WPF crash regressions in a child process with a hard timeout. Fatal runtime failures such as stack overflow cannot be contained by an in-process test assertion.
- Keep the event log bounded and tail-following. It is presentation state, not a durable diagnostic sink.
- Busy state may temporarily overlay the status text, but it must not destroy the underlying current notice.

## Desktop theming and branding

- WPF styling uses native resource dictionaries only. `Themes/Base.xaml` owns structure and control styles; `Themes/Light.xaml` and `Themes/Dark.xaml` own matching semantic color-resource sets. Controls consume semantic `DynamicResource` keys rather than literal palette colors.
- The durable `ThemePreference` (`FollowDesktop`, `Light`, or `Dark`) is policy, while `ResolvedTheme` is the concrete palette currently rendered. Never collapse these into one value: following Windows must remain distinguishable from forcing whichever palette Windows currently uses.
- Windows theme detection stays behind `IDesktopThemeSource`; palette resolution/application stays behind `IThemeController`. Ordinary view-model, reducer, application, and domain code must not read the registry or subscribe to static operating-system events.
- `MainWindowState` owns both the configured preference and resolved palette. The window applies the palette represented by that state; it does not resolve an independent theme. `FollowDesktop` reacts to live Windows changes, while forced Light and Dark modes ignore them.
- The status-bar theme control cycles `FollowDesktop → Light → Dark → FollowDesktop`. A selection is rendered immediately, then persisted with all other preferences through the transactional `IStore` path. On persistence failure, restore the complete prior preference and resolve its current palette before publishing the one exact error event/status.
- Apply the stored preference before the main window becomes visible. New and version-1 databases default to `FollowDesktop` through append-only migration `002`; never edit migration `001` or discard existing replay preferences during a theme update.
- Use `assets/branding/logo-simplified.png` for in-app GravelReview branding and `assets/branding/favicon-simplified.ico` for the executable/window icon. Keep the compatibility-stable internal `IncidentReview.*` identities unchanged.

## iRacing boundary

- Only `IncidentReview.Iracing` knows shared-memory layouts, session YAML shape, SDK numeric identifiers, or Windows replay broadcast encoding.
- Prefer the smallest repository-owned transcription of the authenticated official SDK surface. Do not add an unofficial SDK wrapper casually.
- `IReplayController` issues simulator-neutral replay intents. `IReplayContextReader` exposes only transient driver/camera display context.
- Model the telemetry lifecycle as one closed immutable state hierarchy. `IracingTelemetryLifecycleReducer.Reduce(state, input)` is pure and returns the complete replacement state, explicit ordered effects, and the next loop directive; do not reintroduce correlated connection/recovery booleans or mutate lifecycle fields from the shared-memory loop.
- Keep nondeterminism and resources in the telemetry interpreter: it alone opens/closes readers, captures monotonic time, creates provisional identities, waits, delays, writes the event channel, and publishes replay observations. Closing a reader is resource cleanup, not an implicit replay-state mutation; every reducer transition declares replay invalidation explicitly.
- Represent mutually exclusive protocol and replay states with closed variants, not an enum/Boolean plus a nullable payload. Replay availability, frame, and version are one atomic observation under one lock.
- Adapter disposal cancels and joins its registered producer before returning. No reader, telemetry write, or replay publication may outlive that completion boundary.
- Driver and camera parsing fail independently. Missing display metadata must not disable otherwise valid replay control.
- A malformed or inconsistent frame is transient unavailability, not proof of a simulator disconnect. Retain the logical connection/session identity while recovery remains within 30 seconds of the last successfully decoded sample. Only a real SDK disconnect, expiry of that deadline, or valid evidence of a different simulator session may replace the active session.
- Every successfully decoded sample refreshes the logical-connection deadline, including a position-only sample coalesced from the application event stream. Repeated invalid frames emit one diagnostic per degraded interval; one valid recovery rearms that diagnostic.
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
