# GravelReview — System Design

- **Status:** Accepted architecture; adaptive-themed runnable MVP implemented, distribution and live-simulator acceptance pending
- **Document version:** 1.6
- **Last updated:** 2026-09-06
- **Target platform:** Windows x64
- **Target runtime:** .NET 10 LTS / C# 14

This document is the architectural contract and implementation record for GravelReview, a local iRacing incident-review application. It records the product goal, project boundaries, public contracts, storage and transaction model, error model, startup model, dependency policy, testing philosophy, and the line between the implemented MVP and remaining release work.

The words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative. A departure from a MUST or MUST NOT requires an architecture decision record (ADR), accompanying tests, and explicit review.

[`AGENTS.md`](AGENTS.md) is the mandatory operational summary of these rules for every human or coding-agent change. It MUST remain aligned with this design so future work starts from the repository's established practices rather than reconstructing them from conversation history.

`GravelReview` is the user-visible product name. `IncidentReview.*` remains the internal bounded-context, project, namespace, and assembly identity. The existing `%LOCALAPPDATA%\IncidentReview\incident-review.db` database path, `INCIDENTREVIEW_` environment-variable prefix, `Local\IncidentReview.Host.Wpf` single-instance mutex, and `IncidentReview.*` UI Automation identifiers are compatibility contracts and MUST NOT be renamed as part of product-facing branding. This preserves existing data, deployment configuration, old/new-process exclusion, public library identities, and accessibility automation. A future internal-identity migration requires its own ADR and compatibility plan.

## 1. Executive summary

GravelReview is a local Windows companion for iRacing. It observes the scored incident counters that iRacing exposes for eligible participants in a live field, records participant-scoped incident markers, presents the active event through a review UI, and lets the user review an incident at two seconds before, exactly at, or two seconds after its recorded time while focusing the recorded car and applying the stored playback state. An application `SessionIdentity` is the event container; one event may contain several SDK `SessionNum` heat coordinates.

The WPF experience has three durable theme modes: follow the live Windows application theme, force Light, or force Dark. Native WPF semantic resource dictionaries provide the visual system; the configured preference and concrete resolved palette remain separate values in the same immutable presentation state as the rest of the screen.

The system is built as independent .NET libraries connected through explicit interfaces. Infrastructure details—including iRacing shared memory, Windows replay messages, SQLite, Dapper, DbUp, WPF, and any future server protocol—must remain inside their owning assemblies. The WPF host is the sole composition root that selects concrete implementations.

SQLite is the initial durable store. Every application mutation is an immutable `IStore` command with idempotent committed effects whose implementation owns one transaction; application code never receives a database connection or transaction. Dapper performs parameterized SQL mapping, and DbUp applies immutable, ordered schema migrations before normal startup.

Expected failures cross boundaries through the shared `Result` and `Result<T>` types with stable error codes. Exceptions are retained for cancellation, programmer defects, and broken invariants; provider exceptions are translated and logged at infrastructure boundaries.

Testing follows the philosophy Richard Hipp described in *Reliability Lessons From SQLite* at SSW 2026: design for testability from the beginning, assume untested behavior does not work, test through the same public interfaces used in production, inject faults deterministically, exercise boundary conditions and independent decisions, verify the tests themselves, and test the actual Release deliverable.

### 1.1 Current implementation snapshot

The repository now contains a runnable end-to-end MVP, not only scaffolding. The current production path is:

```text
iRacing shared memory/event
    → IncidentReview.Iracing
    → ITelemetrySource
    → IncidentReview.Application
    → typed IStore commands/queries
    → IncidentReview.Store.Sqlite
    → SQLite

IncidentReview.Iracing → IReplayContextReader
                         → IncidentReview.Application

WPF UI
    → IIncidentReviewService.GetSnapshotAsync
    ← coherent ReviewSnapshot
    → ReviewIncidentAsync(IncidentId, ReplayOffset)
    → IReplayController
    → official iRacing replay broadcast messages
    ← later stable iRacing telemetry confirmation
```

Implemented at this revision:

- validated domain values plus shared `Result`/`Result<T>` failure primitives;
- simulator-neutral telemetry, replay, store, and application contract assemblies;
- field-wide eligible-participant scored incident detection, independent durable participant checkpoints, reconnect/restart session resolution, and indeterminate-command reconciliation;
- the SQLite implementation using parameterized Dapper SQL, serialized bounded execution, per-operation transactions, operation fingerprints, and embedded checksum-pinned DbUp migrations, including the additive theme-preference upgrade;
- a repository-owned adapter transcribed from the official iRacing SDK 1.20 archive, including shared-memory reads, bounded meaningful-event delivery, narrow session/driver/camera metadata extraction, and confirmed replay seek/camera/playback control;
- one coherent `ReviewSnapshot` application read containing revisioned status, the active session and its incidents, stored preferences, and transient driver/camera context;
- a WPF screen centered on the active session's chronological incident log, with a full local recorded timestamp, captured driver context with team/car fallback, `-2 sec`, `0 sec`, and `+2 sec` actions on each row, compact display identifiers backed by full stable IDs, transient active-driver context, a collapsed Advanced panel, GravelReview branding, and a quiet bottom status bar;
- native light/dark WPF palettes plus a status-bar control for durable `FollowDesktop`, `Light`, and `Dark` selection, with live Windows-theme observation and pre-show application of the stored mode;
- a single immutable `MainWindowState` reduced from explicit UI actions and replaced atomically for top-down WPF rendering; it owns the configured and resolved theme values, and its bounded tail-follow event stream and current status share the exact same event object;
- a Generic Host composition root with validated DI, ordered store bootstrap, runtime supervision, a per-user database default, a single-instance guard, and deterministic shutdown;
- automated domain, contract, application, SQLite, architecture, adapter/protocol-simulator, view-model, and Release-host-startup tests.

The official archive is evidence, not a linked native or managed runtime dependency. The pinned `irsdk-1-20.zip` is 102,658 bytes with SHA-256 `af4948cc8efe03fa7c99332a63da1ab9d7b34e5b6107540c176ed682e48a2d79`. It was inspected without adding the upstream source to this repository. Exact reviewed entry hashes, ABI decisions, URLs, and licensing scope are recorded in [`docs/iracing-sdk-baseline.md`](docs/iracing-sdk-baseline.md); the distributable copies [`vendor/third_party/iracing-sdk-1.20/NOTICE.md`](vendor/third_party/iracing-sdk-1.20/NOTICE.md).

Not yet complete or accepted for a public release:

- acceptance against a recorded current real iRacing build, including a permitted redacted session-information fixture and field-wide participant-counter validation with `Max Cars = 63`;
- packaged UI Automation through the independent protocol simulator, broad frame-mutation coverage, crash/power-loss campaigns, mutation/fuzz/stress runs, dependency inventory/SBOM automation, an installer/updater, and a persistent Release log sink;
- WPF controls for annotations, classification, and explicit reviewed/dismissed state, even though annotation application support and reviewed-state store support already exist;
- JSON export, remote storage, and synchronization.

No test result or live-simulator acceptance is implied by this document alone. The exact commands in the root `README.md` produce the evidence for the current checkout.

## 2. Product definition

### 2.1 Primary user outcome

After completing or pausing an iRacing session, a driver can see the scored incidents exposed for eligible participants in the active session, choose `-2 sec`, `0 sec`, or `+2 sec` on one row, and have iRacing move its replay to that explicit offset from the event with the participant recorded on the incident and preferred—or current—camera group selected.

### 2.2 Primary workflow

1. The desktop application starts and validates its configuration.
2. DbUp migrates the local SQLite database to the required schema.
3. The application waits for iRacing telemetry.
4. When iRacing connects, the application identifies the active event from validated SDK evidence. A new SDK heat number does not by itself create another event.
5. The adapter reads eligible `DriverInfo:Drivers` entries. Presence of `CurDriverIncidentCount` selects an independent current-driver counter; its absence selects the legacy team counter. The adapter emits a deterministic participant-counter collection without exposing SDK field names through the telemetry contract.
6. The detector evaluates every participant against an independent persisted checkpoint. A positive increase creates a durable incident record containing that participant's identity and optional driver/team/car context, its replay position, the new total, and the complete observed delta.
7. The application publishes a revision and the UI obtains one coherent `ReviewSnapshot`; one reducer transition replaces the immutable top-level state and shows the incident.
8. The user exits the car; the current policy requires an authoritative `NotOnTrack` sample before replay control.
9. The user chooses `-2 sec`, `0 sec`, or `+2 sec` on an incident row; double-click means `0 sec`.
10. The application applies the selected `ReplayOffset` to the stored incident time, clamping a negative result to zero, and preflights the current simulator/session state and stored playback representation before sending an external command.
11. The replay controller seeks to the explicit target and waits for a later stable telemetry frame to confirm the requested session/time.
12. The controller resolves and focuses the participant stored with the incident using the stored camera-group preference, or preserves the current group when no preference is set, and waits for camera confirmation.
13. The controller applies the stored pause or playback-speed compatibility preference and waits for playback confirmation. A failure identifies the exact precondition or seek, camera, or playback stage that failed.

Classification, notes, explicit reviewed/dismissed actions, and export remain follow-on UI capabilities; they are not steps in the current WPF workflow.

The current WPF incident rows show the full local recorded time through milliseconds and its UTC offset, incident identity, replay/lap context, points, the captured driver name with team/car fallback, and review actions. The captured name remains observation context rather than an assignment of fault. Review status stays in the durable model but is not shown in the primary grid.

### 2.3 Goals

- Detect and persist cumulative scored incident-count increases independently for every eligible participant iRacing exposes in the current field.
- Preserve the iRacing session number and session time required for exact replay seeking.
- Present a fast, accessible Windows UI whose primary log contains only the active session's incidents and whose complete visible state arrives and renders as one coherent top-down value.
- Render a consistent native WPF visual system in Follow desktop, Light, and Dark modes without introducing a third-party theme framework.
- Show the transient local active-driver header and live iRacing camera-group catalog without persisting that live snapshot context; separately show the durable participant display context captured with each incident row.
- Seek iRacing replay through the official local SDK broadcast mechanism.
- Focus replay on the participant recorded with the incident through an optional named camera-group preference.
- Report external replay control as successful only after stable telemetry confirms the requested state.
- Preserve session and incident records across application restarts.
- Make every behavioral dependency replaceable through a focused interface.
- Allow a future remote or synchronized store without changing application use cases.
- Preserve an extension point for a future JSON export without coupling JSON to storage or domain models.
- Make boundary violations, nontransactional writes, and unsafe SQL fail build or CI checks.
- Treat testability and fault injection as product design requirements.

### 2.4 Non-goals for the first release

- Assigning fault or blame to drivers.
- Automatically detecting every contact between every car in the field.
- Claiming visibility into entrants or incidents that iRacing does not expose to the local client, including a `0x` contact that does not change a scored counter.
- Replacing iRacing's replay UI or rendering replay video ourselves.
- Uploading data to a server or providing multi-device synchronization.
- Calling the remote iRacing Data API.
- Recording every 60 Hz telemetry field indefinitely.
- Supporting operating systems other than Windows.
- Supporting simulators other than iRacing, although simulator-neutral contracts should not prevent it.
- Treating JSON as the database or an internal messaging requirement.

## 3. Requirements baseline

Requirements use stable identifiers. Tests MUST reference one or more requirement or invariant identifiers. The identifiers remain stable even if wording improves.

### 3.1 Functional requirements

| ID | Requirement |
|---|---|
| IR-CON-001 | The application detects iRacing connection and disconnection without requiring a restart. |
| IR-SES-001 | The application creates or resumes the correct local event record when telemetry identifies an event. Several SDK heat/session numbers under the same event key share that record and incident list. |
| IR-SES-002 | Valid durable simulator event evidence resolves to the same application session across heat changes, reconnect, and restart; ambiguous evidence is never heuristically merged. |
| IR-SES-003 | A malformed or inconsistent frame is transient unavailability, not an event boundary. Recovery before 30 seconds have elapsed since the last successfully decoded sample retains the same connection-scoped key, active application session, participant checkpoints, and recorded incidents. A real SDK disconnect or expiry of that deadline ends the provisional event; valid evidence of another simulator event selects that event instead. |
| IR-INC-001 | A positive increase in an eligible participant's applicable cumulative scored incident counter records one participant-scoped incident marker. |
| IR-INC-002 | The marker records both the new total and the positive incident-point delta. |
| IR-INC-003 | A participant counter decrease, SDK heat-number change, application-event transition, or reconnect is handled as a state transition and never as a negative incident. A decrease or heat change establishes a new baseline and advances only that participant's counter epoch; the heat change retains the event's earlier incidents. |
| IR-INC-004 | Duplicate or repeated telemetry samples do not create duplicate incident records. |
| IR-INC-005 | Detector progress and its incident record are persisted atomically so failure, retry, or restart cannot silently lose or duplicate an incident. |
| IR-INC-006 | Each eligible scored entry exposed in current `DriverInfo:Drivers` has an independent checkpoint. A current-driver row is keyed by `CarIdx`, positive `UserID`, and positive `TeamID` when present; a legacy team row is keyed by `CarIdx` plus positive `TeamID`, otherwise positive `UserID`. Missing or insufficient identity evidence omits the participant. A team-car driver swap starts or resumes that driver's stream without resetting another driver or car. |
| IR-INC-007 | One observed positive jump creates one marker with the complete delta, while same-update jumps by different participants create independent markers. Presence of `CurDriverIncidentCount`, including an unavailable value, selects driver semantics; only absence selects legacy `TeamIncidentCount`. The matching local `PlayerCarDriverIncidentCount` or `PlayerCarTeamIncidentCount` may substitute only within the selected scope. `PlayerCarMyIncidentCount` and cross-scope substitution are forbidden. A `0x` without a counter change is not observable, and field-wide coverage is bounded by the entries iRacing transmits to this client; intended coverage requires `Max Cars = 63`. |
| IR-RPY-001 | A recorded incident contains a simulator-neutral replay position with session number and session time. |
| IR-RPY-002 | Reviewing an incident accepts an explicit `-2 sec`, `0 sec`, or `+2 sec` offset, applies it to the recorded time with a zero lower bound, focuses the participant recorded with the incident using the preferred/current camera group, and applies the stored playback state in that order. The compatibility overload without an explicit offset continues to use the stored lead-in. |
| IR-RPY-003 | Replay actions unavailable in the current iRacing state are disabled or return a clear structured failure. |
| IR-STR-001 | Sessions and incidents survive application restart. |
| IR-STR-002 | Every application data mutation executes inside a transaction. |
| IR-STR-003 | A failed multi-step write leaves no partial application state. |
| IR-STR-004 | Released schema migrations are applied once and are never edited in place. |
| IR-STR-005 | Re-executing an identical command with the same operation identifier can produce at most one committed effect; a transient pre-commit failure may later succeed. |
| IR-STR-006 | An indeterminate commit can be reconciled by operation identifier before retry. |
| IR-HIS-001 | Past event sessions and incidents remain durably queryable through application/store contracts after restart; the primary WPF UI displays only the active event container and does not expose history navigation. Incidents from all heat numbers grouped into that event are shown together. |
| IR-SET-001 | Mutable durable user preferences, including preferred camera and hidden lead-in/playback compatibility values, are stored through `IStore`; removing controls from the primary UI does not silently discard existing values. |
| IR-SET-002 | The durable theme preference is exactly `FollowDesktop`, `Light`, or `Dark`, defaults to `FollowDesktop` for new and upgraded databases, and is updated atomically with the existing replay preferences through `IStore`. A failed update preserves every previously stored preference. |
| IR-UI-001 | One coherent revisioned `ReviewSnapshot` supplies the active session, chronological incidents, connection/error state, stored preferences, transient active driver, and live camera groups. WPF renders one immutable top-level `MainWindowState`; stale revisions cannot replace newer state and an automatic refresh preserves a dirty camera draft. |
| IR-UI-002 | Every safe actionable UI message is one immutable event appended to the bounded event stream and assigned by reference as `CurrentStatusEvent`. Except while a transient busy label obscures it, the status bar displays that exact event object's message; busy state MUST NOT create or replace a competing status event. |
| IR-UI-003 | The status-bar theme control cycles `FollowDesktop → Light → Dark → FollowDesktop`, applies the newly resolved palette immediately, and persists it. `FollowDesktop` reacts live to Windows application-theme changes; forced modes ignore those changes. Persistence failure restores the complete prior preference and its current resolved palette before publishing one specific error event/status. The stored preference is applied before the main window is shown. |
| IR-UI-004 | Publishing root state MUST NOT reinterpret WPF selector rebinding as new user input. Equivalent incident/camera selections return the existing state instance, selector setters ignore render-time write-back, and unrelated state transitions retain unchanged item-collection identities. |
| IR-UI-005 | Each primary incident row displays its captured driver name, falling back to team name, car number, or `Unknown driver`, and displays an unambiguous local recorded timestamp through milliseconds with its UTC offset. Initial chronology and interactive Recorded-at sorting use the underlying instant with full stored precision rather than formatted display text. Review status remains durable but is not a primary-grid column. |

Deferred requirement: `DR-EXP-001` — a future export operation can serialize selected session data without exposing the SQLite schema.

### 3.2 Quality requirements

| ID | Requirement |
|---|---|
| QR-ARC-001 | Production assemblies reference only dependencies allowed by the architecture matrix. |
| QR-ARC-002 | Cross-assembly behavior is consumed through public contracts; concrete infrastructure implementations remain internal. |
| QR-ARC-003 | Public contracts expose no WPF, SQLite, Dapper, DbUp, Windows-message, HTTP-transport, or raw iRacing SDK types. |
| QR-ARC-004 | The composition root contains wiring and lifetime code only, not business decisions. |
| QR-ERR-001 | Expected operational failures use `Result` or `Result<T>` with stable machine-readable codes. |
| QR-ERR-002 | Cancellation observed before an irreversible commit remains cancellation and is not converted into an ordinary failure result. |
| QR-LIF-001 | Every owned background task is supervised; an unexpected runtime completion shuts down the UI and is observed as a process failure. |
| QR-SQL-001 | Runtime values are always passed to Dapper as parameters and never concatenated or interpolated into SQL. |
| QR-SQL-002 | Dynamic SQL identifiers are selected only from code-owned allowlists. |
| QR-TST-001 | Critical hand-written decision logic has complete branch coverage and independently exercises compound conditions. |
| QR-TST-002 | Each infrastructure implementation passes reusable behavioral contract tests for its public interface. |
| QR-TST-003 | Release validation tests the compiled and packaged deliverable, not only Debug source-level execution. |
| QR-DEP-001 | SDK and package versions are controlled from repository-root files. |
| QR-DEP-002 | Restores are repeatable and CI rejects an unreviewed dependency graph change. |
| QR-VCS-001 | Repository commits follow Conventional Commits and contain one cohesive, reviewable change. |
| QR-VCS-002 | Each commit is independently buildable and includes the tests and documentation required by its behavior. |
| QR-VCS-003 | Generated output, local databases/logs, credentials, and unrelated user changes are never included in a commit. |

## 4. Architectural principles

1. **Assemblies are boundaries.** Folders and namespaces aid organization but do not enforce isolation.
2. **Depend inward on contracts.** Application policy does not depend on infrastructure mechanisms.
3. **Behavior crosses boundaries through interfaces.** Immutable value records may cross boundaries directly; creating an interface for every DTO does not improve isolation.
4. **Infrastructure types do not leak.** Database connections, SDK buffers, Windows messages, UI classes, and transport DTOs stay in their adapter assemblies.
5. **The composition root is the only concrete meeting point.** It may reference implementations solely to register them.
6. **Make invalid operations difficult to express.** Mutations are typed atomic store commands; transaction-bound handlers remain internal to the SQLite implementation.
7. **Failures are data when callers can reasonably handle them.** Defects and invariant violations remain exceptions.
8. **Test through the surface that production uses.** Test hooks enable conditions; they do not create alternate production behavior.
9. **Prefer a small explicit dependency graph.** Every external package requires a reason, a pinned version, and review of its transitive graph.
10. **Optimize for replacement, not speculation.** Interfaces describe capabilities we use; they do not attempt to predict every future backend.
11. **Render visible state from one root value.** Presentation effects dispatch data-only actions to a pure reducer; no control, handler, collection, or parallel property bag owns an independent copy of UI-visible state.
12. **Represent state combinations honestly.** Mutually exclusive states use closed sum-like record hierarchies with their required data, not correlated Booleans, enums, nullable payloads, or partially synchronized fields. Stateful boundary loops interpret explicit effects produced by pure reducers; resource handles and nondeterminism stay outside those reducers.

## 5. System context

```text
┌──────────────────┐       shared memory / event       ┌────────────────────────┐
│     iRacing      │ ─────────────────────────────────→ │ Telemetry implementation │
│ simulator/replay │                                     └────────────┬───────────┘
│                  │ ←─────────────────────────────────┐              │ contract values
└──────────────────┘    Windows broadcast messages     │              ▼
                                                   ┌───┴─────────────────────────┐
                                                   │ Application + incident logic │
                                                   └───────┬──────────────┬──────┘
                                                           │              │
                                                    IStore │              │ application contracts
                                                           ▼              ▼
                                                   ┌────────────┐   ┌───────────┐
                                                   │   SQLite   │   │  WPF UI   │
                                                   └────────────┘   └───────────┘
```

The application runs locally under the user's Windows account. SQLite is embedded; there is no database server or daemon to start.

## 6. Solution and project structure

Every project below currently exists as a separate SDK-style project with its own `.csproj`.

```text
IncidentReview.slnx

src/
  IncidentReview.Results/
  IncidentReview.Domain/

  IncidentReview.Store.Contracts/
  IncidentReview.Telemetry.Contracts/
  IncidentReview.Replay.Contracts/
  IncidentReview.Application.Contracts/

  IncidentReview.Application/
  IncidentReview.Store.Sqlite/
  IncidentReview.Iracing/
  IncidentReview.Desktop.Wpf/
  IncidentReview.Host.Wpf/

tests/
  IncidentReview.TestKit/
  IncidentReview.Architecture.Tests/
  IncidentReview.Results.Tests/
  IncidentReview.Domain.Tests/
  IncidentReview.Telemetry.Contracts.Tests/
  IncidentReview.Replay.Contracts.Tests/
  IncidentReview.Application.Tests/
  IncidentReview.Store.ContractTests/
  IncidentReview.Store.Sqlite.Tests/
  IncidentReview.Iracing.Tests/
  IncidentReview.Desktop.Wpf.Tests/
  IncidentReview.Deliverable.Tests/
  IncidentReview.Iracing.ProtocolSimulator/
  IncidentReview.Analyzers.Tests/
  IncidentReview.Verification.Tests/

tools/
  IncidentReview.Analyzers/
  IncidentReview.Verification/

eng/
  ArchitecturePolicy.props
  build.ps1
  test.ps1
  verify.ps1
```

`Export.Contracts`, `Export.Json`, `Store.Remote`, and `Store.Sync` are reserved in the architecture policy but have not been created. Dedicated machine-readable requirement/release evidence and reusable fixture directories are also planned, not present. Until those assets exist, `DESIGN.md`, test metadata, the SDK baseline, and the code are the available evidence sources.

### 6.1 Project responsibilities

| Project | Responsibility | Public surface |
|---|---|---|
| `Results` | Shared success/failure primitives | `Result`, `Result<T>`, `Error`, `ErrorKind` |
| `Domain` | Simulator-neutral identities, values, incident rules, and invariants | Domain values and pure services |
| `Store.Contracts` | Store capabilities expressed as typed queries, atomic commands, and host-only initialization | `IStore`, `IStoreInitializer`, store queries, store commands |
| `Telemetry.Contracts` | Stream of simulator-neutral telemetry observations | `ITelemetrySource`, telemetry records |
| `Replay.Contracts` | Replay command and read-only context capabilities in application vocabulary | `IReplayController`, `IReplayContextReader`, `ReplayContext`, replay records |
| `Application.Contracts` | Use cases, runtime lifecycle, and coherent UI-facing state/events | Application service/lifecycle interfaces, `ReviewSnapshot`, and immutable models |
| `Application` | Use-case orchestration and incident detection | Registration module; internal implementations |
| `Store.Sqlite` | Dapper queries, SQLite mappings, transactions, DbUp migrations | Registration/options plus a narrowly named testing registration surface |
| `Iracing` | SDK shared-memory reader, session-info translation, replay broadcasts and telemetry confirmation | Registration/options plus a narrowly named testing registration surface |
| `Desktop.Wpf` | Views, effects, pure UI-state reduction, UI mapping, dispatcher interaction | `MainWindowState`, `MainWindowAction`, reducer, registration module, and WPF application surface |
| `Host.Wpf` | Executable, composition root, startup and shutdown | Process entry point |
| `Analyzers` | Compile-time enforcement that needs semantic source analysis | Roslyn diagnostics only; no runtime API |
| `Verification` | Reserved home for traceability, dependency, migration-manifest, artifact, and release-evidence checks; currently a no-op CLI scaffold | Repository CLI only; never shipped |
| `Iracing.ProtocolSimulator` | Independent out-of-process shared-memory/event simulator used by adapter integration tests | Test executable only; never shipped |

### 6.2 Allowed dependency direction

An arrow means “may reference.” Framework assemblies are omitted.

```text
Domain          ──→ Results
*.Contracts     ──→ Domain + Results
Application     ──→ Application.Contracts + Store.Contracts
                   + Telemetry.Contracts + Replay.Contracts + Domain + Results
Desktop.Wpf     ──→ Application.Contracts + Domain + Results
Store.Sqlite    ──→ Store.Contracts + Domain + Results
Iracing         ──→ Telemetry.Contracts + Replay.Contracts + Domain + Results
Host.Wpf        ──→ Application + Desktop.Wpf + Store.Sqlite + Iracing
                   + Application.Contracts + Store.Contracts + Results
```

Detailed rules:

- `Results` MUST have no runtime or compile-API project/package dependencies. The repository analyzer may be attached build-only with `ReferenceOutputAssembly=false`; it does not appear in the produced assembly graph.
- `Domain` MAY reference `Results` but no infrastructure or presentation assembly.
- A contract assembly MAY reference `Domain`, `Results`, and another lower-level contract only when unavoidable and documented.
- `Application` MAY reference the contract assemblies, `Domain`, and `Results`.
- `Application` MUST NOT reference WPF, iRacing, SQLite, Dapper, DbUp, HTTP implementations, or Windows interop.
- `Desktop.Wpf` MUST consume application-facing interfaces and MUST NOT call `IStore`, Dapper, SQLite, or the iRacing adapter directly.
- `Store.Sqlite` MUST NOT reference application, UI, telemetry, replay, or iRacing assemblies.
- `Iracing` MUST NOT reference application, storage, or UI assemblies.
- Only `Host.Wpf` may reference all concrete production implementations.
- `Host.Wpf` declares direct references to every contract it names in source; it does not rely on accidental transitive project references.
- Tests may reference the implementation they test plus its public contracts; that exception never applies to production assemblies.
- A production assembly MUST NOT reference another assembly's `.Testing` namespace or any test/tool assembly. The owning adapter may reference its own public `.Testing` facade only in its explicitly named testing-registration bridge; normal runtime services depend on an internal probe contract instead.
- The analyzer is attached as a compiler analyzer (`ReferenceOutputAssembly=false`), never as a runtime library reference.
- `Iracing.ProtocolSimulator` MUST NOT reference `IncidentReview.Iracing` or reuse production protocol codecs/constants; its independence is architecture-tested.
- No production assembly may use `InternalsVisibleTo` to reach into another production assembly.
- Dependency cycles are forbidden.

### 6.3 Boundary enforcement

Boundaries are enforced independently at several levels:

1. Minimal `.csproj` project references make most illegal dependencies compile-time errors.
2. `Directory.Build.targets` rejects forbidden project/package combinations and package versions declared in individual project files.
3. `eng/ArchitecturePolicy.props` is the single canonical dependency matrix. Both MSBuild targets and architecture tests MUST consume it; neither may maintain a second allowlist.
4. Architecture tests inspect compiled assembly references against that canonical allowlist.
5. Architecture tests inspect exported public APIs recursively, including base types, attributes, generic arguments, constraints, parameters, and return types.
6. Repository analyzers currently reject nested `BuildServiceProvider`, runtime-constructed Dapper SQL (including supported private forwarding paths), and Dapper mutations without an explicit transaction, including `CommandDefinition` overloads. Broader service-locator detection remains a documented container rule enforced by review until analyzer coverage exists.
7. `IncidentReview.Verification` is the designated future home for requirement/test links, migration manifests, resolved dependency graphs, release hashes, and retained evidence. Its current executable is a scaffold that returns success without performing those checks; it must not be cited as release evidence yet.
8. Code review checks the semantic quality of interfaces, which automated dependency checks cannot determine.

## 7. Core contracts

The signatures below establish direction; exact names may be refined without changing the boundary semantics. They are declaration-only, C#-shaped API sketches: private constructors, factory parameters, and method bodies are intentionally omitted and the snippets are not paste-ready implementations.

### 7.1 Telemetry

```csharp
public interface ITelemetrySource
{
    IAsyncEnumerable<TelemetryEvent> ObserveAsync(
        CancellationToken cancellationToken);
}

public abstract class TelemetryEvent { }
public sealed class TelemetryConnected : TelemetryEvent { }
public sealed class TelemetrySampleObserved : TelemetryEvent
{
    public TelemetrySample Sample { get; }
}
public sealed class TelemetryDisconnected : TelemetryEvent { }
public sealed class TelemetryUnavailable : TelemetryEvent
{
    public Error Error { get; }
}

public sealed class TelemetrySample
{
    public SimulatorSessionDescriptor Session { get; }
    public ReplayPosition Position { get; }
    public IReadOnlyList<ParticipantIncidentCounter> IncidentCounters { get; }
    public OnTrackState OnTrackState { get; }
    public UtcInstant ObservedAt { get; }

    public static Result<TelemetrySample> TryCreate(/* untrusted values */);
}

public sealed class ParticipantIncidentCounter
{
    public IncidentParticipant Participant { get; }
    public IncidentCounter IncidentCounter { get; }
    public LapNumber? Lap { get; }
    public LapDistance? LapDistance { get; }
}
```

The event types have controlled constructors/factories and carry validated values/reasons omitted from the conceptual sketch. `TelemetrySample` is intentionally simulator-neutral and snapshots a bounded, ordered, identity-unique, possibly empty participant-counter collection. Raw variable names such as `DriverInfo:Drivers`, `CurDriverIncidentCount`, `TeamIncidentCount`, `TeamID`, and `UserID`, plus memory offsets, YAML nodes, and SDK buffers, remain internal to `IncidentReview.Iracing`.

Connection, disconnection, transient unavailability, and samples are observable without using exceptions as routine stream messages. The adapter owns reconnection and emits state transitions in order. Cancellation ends enumeration with `OperationCanceledException`; an unexpected defect faults the stream and reaches the application runtime's top-level owner.

### 7.2 Replay

```csharp
public interface IReplayController
{
    Result ValidatePlayback(ReplayPlayback playback);

    ValueTask<Result> SeekAsync(
        ReplayPosition position,
        CancellationToken cancellationToken);

    ValueTask<Result> FocusParticipantAsync(
        IncidentParticipant participant,
        string? preferredCamera,
        CancellationToken cancellationToken);

    ValueTask<Result> SetPlaybackAsync(
        ReplayPlayback playback,
        CancellationToken cancellationToken);
}

public interface IReplayContextReader
{
    Result<ReplayContext> Read();
}

public sealed class ReplayContext
{
    public string? DriverDisplayName { get; }
    public IReadOnlyList<string> CameraGroups { get; }
    public string? CurrentCameraGroup { get; }
}

// Owned by IncidentReview.Domain and created with ReplayPosition.TryCreate(...).
public sealed class ReplayPosition
{
    public SessionNumber SessionNumber { get; }
    public SessionTime SessionTime { get; }
}
```

`ReplayPosition` has one authoritative definition in `IncidentReview.Domain`, because both telemetry and replay contracts consume it. `ReplayOffset` is a separately validated signed duration used for explicit relative navigation; applying it to `SessionTime` has a zero lower bound. The replay contract speaks in simulator-neutral intent and does not expose iRacing broadcast enums, Windows message packing, camera numbers, or raw session-information fields. A camera preference is an optional group name; resolving the incident's stored participant identity to the current car index/raw car number and resolving the group/camera numbers are the adapter's responsibility.

`IReplayContextReader` is a read-only, synchronous snapshot boundary over context already copied from the latest stable simulator frame. It MUST NOT issue commands, block waiting for telemetry, expose mutable adapter state, or leak raw iRacing YAML/indices. Its immutable `ReplayContext` contains only a validated optional driver display name, an ordered case-insensitively unique camera-group catalog, and an optional current group that is a member of that catalog. Unavailability is a structured `Result` failure. The application may deliberately substitute empty transient context while preserving durable incident/session state; the presentation never reaches into `IncidentReview.Iracing`.

`ValidatePlayback` is a pure capability preflight. The application calls it before seeking so a persisted playback preference that cannot be represented by the pinned iRacing protocol cannot produce a half-completed “seek succeeded, playback failed validation” workflow.

The three asynchronous operations are applied-result boundaries, not transport acknowledgements. Each adapter sends its native command and then waits for a later stable simulator observation that confirms the requested state. Cancellation remains `OperationCanceledException`; unavailable metadata, a missing named camera group, native delivery rejection, disconnection, and separate seek/camera/playback confirmation timeouts return stable `Result` failures. Another simulator adapter may confirm equivalent state through a different mechanism without changing `Application` or the UI.

### 7.3 Store

```csharp
public interface IStore
{
    Task<Result<T>> QueryAsync<T>(
        IStoreQuery<T> query,
        CancellationToken cancellationToken)
        where T : notnull;

    Task<Result> ExecuteAsync(
        IStoreCommand command,
        CancellationToken cancellationToken);
}

public interface IStoreQuery<out T> where T : notnull
{
}

public interface IStoreCommand
{
    OperationId OperationId { get; }
}

public interface IStoreInitializer
{
    Task<Result> InitializeAsync(CancellationToken cancellationToken);
}
```

Queries and commands are immutable, capability-focused records declared in `Store.Contracts`. Examples include `ListSessions`, `GetSession`, `GetSessionBySimulatorKey`, `GetIncidents`, `GetIncidentCheckpoints`, `GetPreferences`, `GetOperationOutcome`, `EnsureSession`, `EstablishIncidentCheckpoint`, `RecordDetectedIncident`, `AnnotateIncident`, `MarkIncidentReviewed`, and `UpdatePreferences`. They contain application/domain values only—never functions or provider objects.

This RPC-shaped boundary is deliberate. One `ExecuteAsync` call is one coarse-grained atomic operation, whether it is handled locally by SQLite or sent to a future server. Arbitrary callbacks are forbidden because they cannot be transported honestly and would require a remote transaction to remain open while client code runs. A query executes against one consistent snapshot for its entire operation and returns a complete immutable result; callers do not assemble one logical read from a sequence of independently timed store calls.

Every command carries an `OperationId` generated before execution by a controlled command factory. Retrying the identical immutable command with that identifier can produce at most one committed effect. If it already committed, execution verifies the stored kind/version/fingerprint and returns success without repeating the effect. A failure or cancellation proven to occur before commit is non-terminal and leaves no durable `StoreOperation`, so the same command may later succeed; the API does not promise memoization of transient failure results.

Public factories do not permit callers to pair an existing identifier with a different payload. The store rejects different kind/version/fingerprint reuse when that identifier is in flight or committed. Because a rolled-back attempt deliberately leaves no durable claim, misuse after such an attempt is a caller contract violation rather than something SQLite can discover historically; tests and analyzers keep ordinary application code on the factories. Command handlers and their transaction context are private to each implementation; application code cannot begin, retain, nest, commit, or roll back a transaction.

The store contract MUST NOT expose:

- `DbConnection`, `IDbConnection`, `DbTransaction`, or `IDbTransaction`;
- Dapper types;
- SQL, expression trees, or `IQueryable`;
- SQLite row models or migration APIs;
- HTTP messages or remote transport DTOs.

The shared store contract suite defines atomicity, snapshot consistency, idempotency, cancellation, error, and concurrency behavior. It is the behavioral specification every implementation must pass; it does not require implementations to share code.

`IStoreInitializer` is a lifecycle contract consumed only by the composition root's bootstrap coordinator. For SQLite it initializes the native provider, migrates, validates the schema/capabilities, and opens the execution gate; for a remote implementation it can perform compatibility/capability negotiation. Ordinary application use cases receive only `IStore`.

### 7.4 Application

UI-facing use cases are expressed through application contracts such as:

```csharp
public interface IIncidentReviewService
{
    Task<Result<ReviewServiceStatus>> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<Result<ReviewSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken);

    Task<Result<ReviewSession>> GetCurrentSessionAsync(
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken);

    Task<Result<ReviewSession>> GetSessionAsync(
        SessionIdentity session,
        CancellationToken cancellationToken);

    Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        ReplayOffset offset,
        CancellationToken cancellationToken);

    // Compatibility path: uses the stored lead-in preference.
    Task<Result> ReviewIncidentAsync(
        IncidentId incidentId,
        CancellationToken cancellationToken);

    Task<Result> AnnotateIncidentAsync(
        IncidentId incidentId,
        IncidentAnnotation annotation,
        CancellationToken cancellationToken);

    Task<Result<UserPreferences>> GetPreferencesAsync(
        CancellationToken cancellationToken);

    Task<Result> UpdatePreferencesAsync(
        UserPreferences preferences,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ReviewUpdate> ObserveUpdatesAsync(
        CancellationToken cancellationToken);
}

public sealed class ReviewSnapshot
{
    public long Revision { get; }
    public ReviewServiceStatus Status { get; }
    public Error? StatusError { get; }
    public ReviewSession? ActiveSession { get; }
    public UserPreferences Preferences { get; }
    public string? DriverDisplayName { get; }
    public IReadOnlyList<string> CameraGroups { get; }
    public string? CurrentCameraGroup { get; }
}

public interface IApplicationRuntime
{
    Task Completion { get; }
    Task<Result> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`GetSnapshotAsync` is the primary presentation read. It captures a revisioned application anchor, reads the active event session and stored preferences, reads transient replay context through `IReplayContextReader`, and verifies that the anchor is still current before constructing one immutable `ReviewSnapshot`. If application state changes during those reads it retries instead of returning a torn mixture. The snapshot validates that `Unavailable` has exactly one structured status error and that the current camera belongs to the bounded camera catalog. The active event session—not an independently selected historical session—is the sole source for the primary incident log, and it may contain incidents whose replay positions name several heat numbers. Existing granular session/status/query methods remain application capabilities for non-primary consumers and compatibility; WPF MUST NOT assemble its visible root by calling them independently.

Every in-memory application-anchor change advances a monotonically increasing snapshot revision. A presentation MUST ignore a snapshot older than the revision it has already rendered. Equal revisions MAY be reapplied for manual-refresh messaging, a separately persisted preference update, or refreshed transient context, but cannot regress application-owned state. Transient driver/camera context is not written to SQLite. Stored lead-in, playback speed, and pause remain durable compatibility inputs even though the current primary WPF surface exposes only the camera preference and explicit row offsets. The stored `ThemePreference` also travels through this same preference value and coherent snapshot; it is not loaded through an independent UI configuration source.

`ReviewUpdate` is an application-owned notification that tells the UI to obtain a fresh coherent snapshot; it does not expose telemetry frames, store commands, threads, dispatchers, or infrastructure events. `IApplicationRuntime` gives the composition root an explicit lifecycle for the telemetry/processing loop without exposing its implementation. `Completion` remains incomplete while the runtime is healthy, completes normally only after requested stop, and faults on an unexpected worker defect; an unrequested normal completion is also treated as a runtime failure. `StopAsync` is idempotent, cancels and awaits all owned workers, always observes their completion, and rethrows a worker fault that occurred before expected stop rather than converting it into successful shutdown. View models do not coordinate stores, replay-context readers, or replay controllers themselves. They invoke application use cases and marshal returned data/actions through a presentation-owned dispatcher abstraction.

## 8. Domain model

Initial concepts:

- `SessionIdentity`: application-owned stable identifier, generated before the session-creation command.
- `SimulatorSessionDescriptor`: validated simulator-owned identity evidence, session number/mode, and whether that evidence is durable or connection-scoped.
- `ParticipantIdentity`: bounded opaque identity scoped to one simulator session; only the owning adapter interprets how it was formed.
- `IncidentParticipant`: a participant identity plus optional bounded driver, team, and car-number context captured with an observation/incident.
- `IncidentId`: client-generated stable identity, preferably UUIDv7.
- `Incident`: immutable core record plus controlled annotation/status transitions.
- `ReplayPosition`: session number and session-relative time.
- `ReplayOffset`: validated signed relative seek duration, with application to session time clamped at zero.
- `IncidentPoints`: non-negative total and positive delta.
- `IncidentClassification`: optional user classification.
- `IncidentReviewStatus`: pending, reviewed, or dismissed.
- `IncidentAnnotation`: user notes and classification changes.
- `UserPreferences`: validated durable review behavior such as lead-in, playback, optional camera preference, and the configured `ThemePreference` policy.

Domain values validate themselves at creation through private constructors and `TryCreate`/factory methods returning `Result<T>`. This applies at every untrusted boundary: SDK decoding, database mapping, command-line/configuration input, and future wire decoding. Invalid session times, negative counters, NaN/out-of-range percentages, non-UTC instants, invalid lap numbers, empty participant identities, and oversized/malformed display text do not circulate through the system. Optional SDK data is represented explicitly—such as `LapNumber?`, `LapDistance?`, and participant display context—rather than by magic sentinel values.

The domain does not know how telemetry arrived, how replay commands are transmitted, how records are stored, or how views render them.

### 8.1 Session identity resolution

The iRacing adapter cannot manufacture an application `SessionIdentity` because it has no store dependency. It instead emits a `SimulatorSessionDescriptor`. For the pinned SDK, a positive validated `WeekendInfo:SubSessionID` creates durable event key `v2:event:subsession:{id}`. The SDK `SessionNum` is retained in the descriptor and every `ReplayPosition`, but it is a heat/replay coordinate rather than part of event identity. Raw field names never become a public contract.

The acquisition, provenance, licensing, and evidence gate for that pinned artifact is maintained in [`docs/iracing-sdk-baseline.md`](docs/iracing-sdk-baseline.md). ABI constants and replay ordinals do not enter production from unofficial mirrors.

The application resolves the descriptor before incident detection: query `GetSessionBySimulatorKey`; if found, reuse its `SessionIdentity`; otherwise generate one UUIDv7 and execute `EnsureSession` with the proposed identity and descriptor. A concurrent unique-key conflict is resolved by querying the winner, never by creating a second logical session. SQLite enforces a partial unique key over `(simulator, simulator_session_key)` when the durable key is non-null.

| Observation | Session identity action | Detector action |
|---|---|---|
| Repeated sample with the same validated simulator event key and heat number | Reuse current identity | Continue from current/persisted participant checkpoints |
| Disconnect/reconnect in the same app run with the same durable key | Reuse identity | Reload/confirm participant checkpoints; do not synthesize an incident |
| App restart while iRacing reports the same durable key | Resolve existing identity from SQLite | Resume from persisted participant checkpoints |
| SDK session/heat number changes under the same event key | Retain the event identity and earlier incident list | Advance each returning participant's epoch and establish a heat baseline without an incident |
| Durable `SubSessionID` event evidence changes | Resolve/create a new identity | Establish a new baseline |
| Required identity evidence is missing, malformed, or contradictory | Create a clearly marked connection-scoped provisional identity; never guess a cross-restart match | Establish a baseline and surface degraded identity state |
| Replay matches the same provisional connection key, including another heat number | Retain the active provisional identity for that logical connection only; mode and heat are not identity evidence | Pause incident detection; replay seeking remains available |
| Malformed or inconsistent frame followed by recovery before the source's 30-second last-successfully-decoded-sample deadline | Report transient unavailability and retain the logical connection key and active application session | Retain persisted participant checkpoints and incidents; resume from the next valid sample |
| Real SDK disconnect or expiry of the source's last-successfully-decoded-sample deadline, followed by reconnect with only provisional evidence | Create a new provisional identity unless future verified evidence proves continuity | Establish a baseline; do not merge records heuristically |
| Replay mode matches an existing durable key | Reuse identity for review only | Pause incident detection; replay seeking remains available |
| Standalone/unmatched replay playback | Use an ephemeral replay context | Do not persist inferred incidents or create a durable session automatically |

Changing heat number, track/car display metadata, or mode does not change event identity. Once a provisional session later gains trustworthy durable evidence, merging or re-keying records is not automatic; that workflow requires a separately tested command so uniqueness and user history cannot be corrupted silently. Likewise, historical `v1` keys remain valid per-heat records. The first `v2` observation creates or resumes a separate event-wide record; old rows are neither rewritten nor guessed into it, so upgrading during an event can cause one visible list break.

## 9. Incident detection

After Section 8.1 resolves the simulator descriptor, `Application` maps each member of the telemetry sample's ordered participant-counter collection to a domain `IncidentObservation` carrying the application `SessionIdentity` and `IncidentParticipant`. The detector evaluates every eligible participant independently.

```text
first observation for participant in session
    → establish that participant's baseline; do not emit incident

same session/participant and current count == prior count
    → no change

same session/participant and current count > prior count
    → emit one marker with delta = current - prior

participant omitted from one or more samples
    → retain its checkpoint; do not synthesize a reset or incident

same session/participant and current count < prior count
    → treat as that participant's reset/discontinuity; advance its epoch and establish a new baseline

same event/participant and replay session number changes
    → treat as a new heat; advance its epoch and establish a new baseline even if the counter is equal or higher

session identity changes
    → finalize all old detector state; establish independent baselines in the new session
```

The iRacing adapter derives an opaque participant identity independently from each authoritative roster row. When `CurDriverIncidentCount` is present, a positive-team row uses `car-index:{CarIdx}:team:{TeamID}:driver-user:{UserID}`; a no-team row uses `car-index:{CarIdx}:user:{UserID}`. When the field is absent, the legacy team counter uses `car-index:{CarIdx}:team:{TeamID}`, otherwise the same positive-user form. A team driver swap therefore establishes or resumes the correct driver's independent stream. Missing or insufficient identity evidence omits the row rather than inventing an alias or consulting process memory. Team and driver counters never share an incompatible checkpoint. Exact identity reuse is intentionally indistinguishable and resumes its checkpoint. Driver display text is incident context, not proof of causality. Only the iRacing adapter interprets these formats.

The marker time is initially the first observed sample containing the increased counter. A jump of multiple scored points creates one marker with the complete positive delta; the detector does not invent timestamps for unobserved intermediate values. Multiple participants whose counters rise in the same sample create independent markers in deterministic participant-identity order. A small in-memory telemetry ring buffer MAY later refine the event context, but that refinement must not change the persistence or replay interfaces.

The detector MUST be deterministic and pure with respect to an ordered input sequence. Connection management, persistence, and UI notification surround it but are not part of its decision logic. Its durable progress follows this protocol:

1. On first observation or restart, load all persisted `IncidentCheckpoint` values for the event before evaluating new samples; checkpoints are keyed by participant identity.
2. For a new participant, counter reset, or heat change, create one `EstablishIncidentCheckpoint` command. A reset or heat change advances that participant's monotonically increasing counter epoch without changing another participant or deleting earlier incidents.
3. For an increase, generate the `IncidentId` and `OperationId` once, then create one `RecordDetectedIncident` command containing the expected checkpoint, the complete incident, and the next checkpoint.
4. The store compares the expected checkpoint, inserts the incident, updates the checkpoint, and records the completed operation in one transaction.
5. The in-memory detector state advances only after success or after reconciliation proves an indeterminate commit succeeded. Until then, the same command remains pending; later telemetry cannot silently skip it.
6. On restart or retry, the operation identifier and unique incident key make the transition idempotent. A genuine checkpoint mismatch returns a conflict for explicit reconciliation rather than guessing.

The deterministic incident uniqueness key is `(session_id, participant_identity, counter_epoch, incident_points_total)`. Temporary roster omission never deletes or rewrites a participant checkpoint. On reappearance, the next value is compared with the retained value: equality is unchanged, an increase records the complete delta, and a decrease establishes a reset baseline without an incident.

The adapter never uses `PlayerCarMyIncidentCount`. Presence of `CurDriverIncidentCount`, including a negative or malformed value, selects driver scope and never falls through to team points; absence selects legacy team scope. When the row is the matching local car but its selected value is unavailable, the adapter may use `PlayerCarDriverIncidentCount` for driver scope or `PlayerCarTeamIncidentCount` for team scope. Cross-scope substitution is forbidden. If current-session or identity evidence is unavailable, the adapter emits no local observation. A sample may therefore have no counters while still updating session, position, on-track state, health, and replay context. Later reappearance resumes the persisted checkpoint without an in-memory alias table.

Known limitation: this is field-wide tracking of *eligible scored counters exposed to this local client*, not direct contact detection and not proof of causality. Pace-car and spectator entries are excluded. A `0x` changes neither supported scored counter and is invisible. [iRacing's documented incident-visibility policy](https://www.iracing.com/2016-season-3-release-notes/) limits what ordinary clients see while admins can see more. Session type, admin/broadcaster context, counter-shape stability, and connection/server transmission still determine available evidence. Intended use requires **Max Cars = 63**, but [iRacing explicitly warns](https://support.iracing.com/support/solutions/articles/31000149355-connection-type-max-cars) that 63 cannot guarantee every car. Current-build live validation remains required; the release MUST NOT promise data that iRacing omits or withholds.

## 10. iRacing integration

`IncidentReview.Iracing` is the only project that understands the local SDK protocol.

The implementation source of truth is iRacing SDK 1.20, acquired through iRacing's authenticated official member-forum distribution. The downloaded archive is `irsdk-1-20.zip`, 102,658 bytes, SHA-256 `af4948cc8efe03fa7c99332a63da1ab9d7b34e5b6107540c176ed682e48a2d79`. The upstream C/C++ archive was inspected as protocol evidence and is not extracted, compiled, linked, or redistributed by this repository. Exact source URLs, reviewed-entry hashes, ABI layouts, and the limited upstream licensing conclusion are maintained in [`docs/iracing-sdk-baseline.md`](docs/iracing-sdk-baseline.md). Public GitHub mirrors may help discovery, but they are not authoritative and cannot silently update the protocol baseline.

The initial adapter is repository-owned and uses .NET/Windows interop directly; it does not add an unreviewed iRacing wrapper package. Named memory mappings/events use BCL primitives where they match the protocol. Required User32 calls use source-generated `LibraryImport` declarations with fixed-width validated packing. `unsafe` code, if unavoidable for copied frame decoding, is enabled only in `IncidentReview.Iracing`, kept in small reviewed methods, and covered by malformed-buffer and bounds tests.

Responsibilities:

- Open and monitor the iRacing shared-memory mapping and synchronization event.
- Copy telemetry frames promptly before processing to avoid holding SDK-owned buffers.
- Parse only the session metadata needed for eligible participant counters, identity/display context, and replay focus.
- Translate raw telemetry into immutable `TelemetrySample` values.
- Detect connection, disconnection, moving or paused replay, and on-track state.
- Resolve the local player's transient header context, every focusable recorded participant, and named camera groups from current session information, then expose only validated simulator-neutral display context through public contracts.
- Encode replay commands for session-time search, recorded-participant/camera focus, pause, and playback speed.
- Convert the fire-and-forget Windows transport into applied-result semantics by confirming each command against a later stable telemetry frame.
- Translate expected integration failures into stable `Result` errors.

### 10.1 Telemetry lifecycle reducer

The shared-memory source has one immutable lifecycle value. `IracingTelemetryLifecycleReducer.Reduce(state, input)` is a synchronous pure function returning a complete next state, an ordered collection of explicit effects, and one loop directive. It performs no shared-memory access, clock read, delay, identifier generation, channel write, replay publication, or mutation. The source loop is the effect interpreter and sole owner of those operations.

The closed lifecycle variants are:

| State | Reader | Retained evidence | Meaning |
|---|---|---|---|
| `AwaitingEndpoint` | closed | none | no endpoint; the next open failure may publish the generic unavailable diagnostic |
| `AwaitingEndpointAfterDiagnostic` | closed | none | the same open failure is coalesced until progress occurs |
| `Priming` | open | provisional connection identity | endpoint opened, but no sample has decoded successfully |
| `PrimingDegraded` | open | provisional connection identity | a semantic rejection was reported before the first accepted sample |
| `ReopeningBeforeFirstSample` | closed | provisional connection identity | a structurally invalid frame forced an internal reopen before an accepted sample |
| `Active` | open | logical identity, last-success timestamp, last published projection | accepted telemetry and replay context are available |
| `Degraded` | open | logical identity and last-success timestamp | a semantic rejection invalidated replay context while preserving logical continuity |
| `Reopening` | closed | logical identity and last-success timestamp | a structurally invalid frame forced an internal reopen while preserving logical continuity |

State leaves declare whether the interpreter must own an open reader and whether provisional or established identity evidence exists. This makes a newly added state supply those facts instead of silently falling through a helper switch. Logical age is itself a closed value—`NotEstablished` or non-negative `Measured`—and reducer validation rejects an age inconsistent with its state. Monotonic timestamps are opaque values captured and compared only through the configured `TimeProvider`.

Reducer effects are likewise closed values: publish a replay frame, invalidate replay, publish a telemetry event, or close the reader. Every reducer-driven close explicitly invalidates replay first. The close operation itself only releases the OS resource, so it cannot hide another state transition. Loop directives are checked against the next state's declared reader ownership, and the interpreter independently asserts that the actual reader sidecar matches. The mutable OS handle deliberately remains outside immutable reducer state.

Raw SDK reads and replay observations do not use a status/Boolean paired with a nullable frame. Each is one closed variant whose available/snapshot case necessarily contains its frame. Replay availability, frame, metadata projection, and monotonically increasing version are replaced atomically under one lock, preventing replay control from observing availability from one frame and content from another.

Observation admission atomically installs a producer-completion barrier before production can begin. Cancellation, early enumeration disposal, and adapter disposal close the active reader and join that producer. `DisposeAsync` performs a final replay invalidation after the join, so it cannot return while a late open, channel write, or replay-frame publication remains possible.

Frame reads follow the official buffer-generation/tick protocol: copy the selected frame into app-owned memory, prove the header did not change during the copy, and retry a bounded number of times on a torn read. Every count, offset, element size, index, and string length is range-checked before slicing. Unknown variables, SDK-version drift, and malformed session information yield typed unavailability/errors rather than unchecked memory access. A rejected frame also clears transient replay confirmation state, but it does not emit `TelemetryDisconnected` or rotate the connection-scoped session key. A later valid frame before the source deadline restores availability under that same logical connection. A real SDK disconnect or 30 seconds without a successfully decoded sample ends the provisional connection.

iRacing session information is YAML-like text, not JSON. The adapter exposes only the few validated fields required by the application. A narrowly scoped decoder may be repository-owned and fixture/fuzz tested against the recorded official samples; it must not pretend to be a general YAML parser or use substring/line-splitting that ignores escaping and structure. If official fixtures demonstrate that a conforming YAML library is necessary, that library requires its own dependency ADR and supply-chain review before use.

The telemetry reader and downstream processing are decoupled by a bounded single-reader channel. Its production capacity is 256 and validated configuration permits 2 through 4,096 pending meaningful events. The source publishes connection transitions and samples only when session, mode, on-track state, or the identity/counter projection of the participant set changes; position-only frames are coalesced. Roster omission or reappearance is therefore meaningful even when remaining counts are unchanged, including transitions to or from an empty participant set; repeated identical omissions coalesce. A distinct transition is never silently dropped: capacity exhaustion drains already accepted events, reports the stable buffer-overflow error, and ends that observation. The producer is canceled and joined on enumeration cancellation, early consumer disposal, and adapter disposal.

Replay review behavior:

1. Confirm that iRacing is connected, the SDK reports `NotOnTrack`, and no review command is already in flight.
2. Load the incident and preferences through application use cases. For the primary UI, apply its explicit `ReplayOffset` (`-2 sec`, `0 sec`, or `+2 sec`) to the recorded incident time and clamp below zero; the compatibility overload instead subtracts the stored lead-in.
3. Preflight pause or playback speed through `ValidatePlayback`; an unrepresentable preference fails before any external command.
4. Recheck that the incident's application event session is loaded. Its stored replay session number is the seek target and may name an earlier heat in that event; a connection-scoped event must still have the same logical connection key.
5. Call `SeekAsync` and wait for a later stable frame whose replay session and time confirm the target. The iRacing adapter accepts a time within 250 milliseconds because search and frame publication are asynchronous.
6. Call `FocusParticipantAsync` with the `IncidentParticipant` stored on the incident. The adapter requires metadata `CurrentSessionNum` to match raw live `SessionNum`; active `ReplaySessionNum` may name the earlier heat just reached by seek and must remain stable through confirmation. A stored `:driver-user:` team identity is validated and reduced to its stable canonical team/car identity, then exact-matched against current roster metadata to obtain `CarNumberRaw`; there is no car-index fallback. The adapter resolves the camera group and confirms the same live metadata, replay heat, canonical car/team, raw number, camera car, and group in a later frame. A TV group may advance its sub-camera. With no preference, the current group/camera is preserved. Missing, changed, or ambiguous canonical evidence fails closed. Migrated `local-player` incidents retain their narrow compatibility path.
7. Call `SetPlaybackAsync` and confirm the requested speed and slow-motion flag in a later stable frame.
8. Return one structured, actionable failure for the exact failed precondition or stage. Seek, camera, and playback confirmation have independent ten-second default windows and distinct codes: `iracing.replay.seek-timeout`, `iracing.replay.camera-timeout`, and `iracing.replay.playback-timeout`. Missing/ambiguous participant or camera metadata and a named group absent from the current session are separate failures.

`ReviewIncidentAsync` does not automatically mutate `review_status`; handing a replay command to Windows and committing SQLite cannot form one atomic transaction. Marking reviewed/dismissed or changing notes/classification must be a separate explicit operation, so the user is never told a cross-system action was atomic when it was not. The current WPF UI does not initiate those status/annotation operations.

Replay context reads are independent of replay commands. After each accepted stable frame, the adapter retains an immutable, bounded projection of the active driver's display name, ordered camera-group names, and the current group. `IReplayContextReader.Read` returns that latest projection immediately or a typed unavailability result. The application includes it in `ReviewSnapshot`; a malformed or temporarily unavailable display context degrades to empty transient context and MUST NOT corrupt or hide durable incidents. Camera choices shown by WPF therefore come from the live iRacing session, while the explicit “current iRacing camera” choice represents a null stored preference. A previously stored camera that is absent from the live catalog remains visible as unavailable so an automatic refresh cannot silently rewrite the user's durable choice.

The current application prevents detection during its own replay-navigation command and keeps that suppression active until telemetry reports the car back on track. The adapter classifies a frame as replay when either `IsReplayPlaying` is true or the official `CamCameraState` bitfield includes `IsSessionScreen`. This keeps a paused session-screen replay in replay mode even though `IsReplayPlaying` becomes false. Compatibility of that official signal across every supported real-iRacing session type remains part of live acceptance rather than an unimplemented behavior.

Replay command registration, command identifiers, field widths, signedness, and parameter packing are derived from the pinned official header and protected by golden-vector tests. SDK broadcasts remain fire-and-forget at the operating-system boundary: `SendNotifyMessageW` returning success proves only that Windows accepted the notification for delivery. `IReplayController` does not expose that transport outcome as success. It awaits a stable observation newer than the pre-command baseline and returns success only when iRacing reports the corresponding seek, camera, or playback state. A disconnect while waiting returns replay unavailable, caller cancellation propagates, and an expired stage returns its exact timeout error. Confirmation remains private to the adapter; it does not call back into application policy or leak protocol state through the interface.

## 11. Storage design

### 11.1 Storage abstraction

`IStore` is the application storage capability. SQLite is its first implementation, not part of its identity. Future implementations may include:

- `Store.Remote`: sends store operations to a service over a versioned protocol.
- `Store.Sync`: composes a local store and remote store for offline-first synchronization.
- `Store.Memory`: deterministic implementation used by tests and development scenarios.

The interface describes required behavior and consistency, not SQLite syntax. Any new implementation must pass the shared store contract suite.

### 11.2 SQLite implementation

`IncidentReview.Store.Sqlite` owns:

- connection creation and disposal;
- transaction creation, commit, rollback, and disposal;
- Dapper SQL and row mapping;
- SQLite-specific row types;
- DbUp configuration and embedded migration scripts;
- schema validation;
- SQLite provider error translation;
- tested database pragmas and locking policy.

SQLite stores durable application state. Ephemeral process objects—windows, cancellation tokens, open connections, replay handles, and transient view selection—are not database records. High-rate raw telemetry is not persisted by default.

Operational constraints:

- SQLite permits one writer at a time. The current implementation uses one bounded, single-consumer executor for all queries and commands, keeping transactions short and behavior deterministic. Its default queue capacity is 64 and validated range is 1 through 1,024.
- Network calls and replay commands MUST NOT execute inside a database transaction.
- Enable and verify foreign-key enforcement on every connection.
- Use WAL only after tests confirm the desired local read/write behavior; never combine it with shared-cache mode.
- Use durability-oriented synchronous settings. Relaxing durability requires measurements and an ADR.
- Define finite busy/command timeouts and map exhaustion to the common error model.
- Store the database in a per-user local application-data directory, never the installation directory or a network share.
- Use a process-level single-instance guard until multi-process database access is designed and tested.
- Treat the database as plaintext at rest; Windows `winsqlite3.dll` does not add transparent encryption.
- A consistent backup uses the provider's online backup API; it does not copy only the main file while WAL may be active.
- `Microsoft.Data.Sqlite` asynchronous ADO.NET methods execute synchronously. SQLite work therefore runs through that dedicated executor and never blocks the WPF dispatcher. The executor owns queue-capacity/backpressure, cancellation-before-start, shutdown draining, and its worker lifetime. The public contract remains asynchronous because dispatch is asynchronous and a future remote store performs real network I/O.

The implemented connection policy uses private cache, disables pooling, enables foreign keys, uses `journal_mode=DELETE` and `synchronous=FULL`, and applies a five-second default busy/command timeout. Initialization validates the engine version, schema version, required tables/columns/indexes/foreign keys, pragmas, and `PRAGMA integrity_check` before opening the store gate. Changing these values is a measured storage decision, not a host/UI concern.

### 11.3 Initial logical schema

The physical schema is introduced through migrations. Its initial logical records are:

```text
Session
  session_id                 text primary key
  simulator                  text
  simulator_session_key      text/null
  identity_kind              integer
  simulator_session_number   integer
  session_mode               integer
  started_at_utc_ms          integer
  ended_at_utc_ms            integer/null
  track_id / track_name      text/null
  car_id / car_name          text/null
  created_at_utc_ms          integer
  updated_at_utc_ms          integer

Incident
  incident_id                text primary key
  session_id                 text foreign key
  participant_identity       text
  driver_name                text/null
  team_name                  text/null
  car_number                 text/null
  replay_session_number      integer
  replay_session_time_ms     integer
  observed_at_utc_ms         integer
  incident_points_delta      integer
  incident_points_total      integer
  counter_epoch              integer
  lap                        integer/null
  lap_distance_percent       real/null
  review_status              integer
  classification             integer/null
  notes                      text/null
  created_at_utc_ms          integer
  updated_at_utc_ms          integer

IncidentCheckpoint
  session_id                 text foreign key
  participant_identity       text
  counter_epoch              integer
  last_incident_points_total integer
  last_replay_session_number integer
  last_replay_session_time_ms integer
  updated_at_utc_ms          integer
  primary key (session_id, participant_identity)

StoreOperation
  operation_id               text primary key
  command_kind               text
  command_version            integer
  payload_fingerprint_sha256 blob
  committed_at_utc_ms        integer

ApplicationPreferences
  preferences_id             integer primary key, constrained to singleton
  replay_lead_in_ms          integer
  auto_pause                 integer
  playback_speed             real
  preferred_camera           text/null
  theme_preference           integer, constrained FollowDesktop/Light/Dark
  updated_at_utc_ms          integer
```

Identifiers use canonical lowercase UUID text initially; participant identities are bounded opaque text; timestamps use UTC Unix milliseconds in `INTEGER` columns; booleans use constrained `0`/`1`; enums use explicitly assigned stable integer codes. Each command has a stable `command_kind` and positive schema version. The operation fingerprint is SHA-256 over that kind/version plus a handler-owned canonical, length-delimited binary encoding of the persisted fields—it does not introduce JSON into the store path. Encoding, field order, normalization, and version are release contracts protected by golden vectors. Existing command versions remain readable/reconcilable for every supported database upgrade; changing their encoding in place is forbidden, and a new shape receives a new version. `Incident` has a unique constraint on `(session_id, participant_identity, counter_epoch, incident_points_total)` in addition to its primary key. `IncidentCheckpoint` has one row per `(session_id, participant_identity)`. Check constraints enforce non-negative counters/times, positive deltas, bounded participant/display text, valid percentages, and known status/preference values. Foreign keys specify deliberate delete behavior rather than relying on provider defaults.

Client-generated identifiers, operation identifiers, and explicit timestamps preserve a credible path to remote synchronization. Application records remain separate from SQLite row models and future wire DTOs. Mutable user preferences—including theme policy—are durable application state and go through `IStore`; host/deployment settings such as database path, logging level, and diagnostic switches remain validated startup configuration and are not mixed with user preferences. A theme change rebuilds the complete validated `UserPreferences` value and executes one `UpdatePreferences` command, so replay lead-in, pause, speed, and camera cannot be lost by a partial preference write.

### 11.4 Transaction guarantee

Every application mutation, including a single-statement write, enters through `IStore.ExecuteAsync(command)`.

```text
enqueue immutable command on serialized store executor
    → open connection
        → begin transaction
            → reject in-flight/committed OperationId reuse with a different payload
            → return success without effects if the identical operation already committed
            → dispatch to an internal transaction-bound handler
            → write StoreOperation in the same transaction
                ├─ handler success → commit
                ├─ handler failure Result → rollback
                ├─ expected provider error → rollback + translated failure
                ├─ cancellation → rollback + rethrow cancellation
                └─ unexpected exception → rollback + rethrow/log at owner
        → dispose handler context, transaction, and connection on every path
```

Every Dapper mutation MUST receive the active transaction explicitly. Mutation methods exist only on internal transaction-bound handlers, so an application consumer cannot request a nontransactional write. Nested root transactions are not part of the public contract; all steps required by one command execute in its single owned transaction.

`StoreOperation` records successful commits only; rolled-back validation/provider failures and cancellations are attempts, not durable outcomes. `Commit` can fail without proving whether SQLite committed. Such a case returns the distinct stable error `store.commit.indeterminate` with `ErrorKind.Indeterminate`; it MUST NOT be translated to an ordinary persistence failure or blindly retried under a new identifier. The caller queries `GetOperationOutcome(OperationId)` on a fresh connection. If the operation is recorded, it succeeded; if a reachable, validated store reports it absent, the exact same immutable command may be retried with the same identifier. The committed `StoreOperation` payload fingerprint prevents repeating or changing an operation that took effect. Unique business constraints provide a second defense against duplicate incidents.

Cancellation is observed before queuing, before beginning work, within handler steps, and immediately before commit. Once the synchronous commit call begins it is treated as a non-cancellable resolution point: a confirmed commit returns success even if cancellation was requested concurrently; a thrown/unknown outcome returns `store.commit.indeterminate`. Rollback is best-effort cleanup, and a rollback failure is logged and attached to diagnostics without erasing the primary safe error. `StoreOperation` records are not automatically pruned in the first release because they are the reconciliation evidence; a future retention policy must preserve the idempotency guarantee.

Each `QueryAsync` call opens one connection and read transaction/snapshot for the whole internal query handler. A logical aggregate—such as a session and all incidents for export—is returned by one typed query so concurrent commits cannot produce a torn cross-query view. A remote store provides the equivalent snapshot in one response.

The initial implementation MUST preserve this single-consumer policy. Introducing concurrent read lanes or multiple processes requires an ADR, performance evidence, and new consistency/locking tests. Busy/locked handling, retry limits, journal mode, synchronous mode, foreign-key enforcement, and timeouts must be explicit, documented, and fault-tested rather than inherited accidentally from provider defaults.

### 11.5 SQL safety

Dapper is a mapper, not an automatic defense against unsafe SQL. Therefore:

- SQL text is owned only by `Store.Sqlite`.
- Runtime values always use named Dapper parameters.
- String concatenation and interpolation into SQL are forbidden.
- Dynamic identifiers cannot be parameters; they map from closed application enums to hard-coded SQL fragments.
- Provider messages are never returned directly to UI or contract callers.
- Integration tests run every query against a real temporary SQLite database.

### 11.6 Schema migration

DbUp runs before the store is available to application use cases. The store may already be registered in DI for container validation, but its execution gate remains closed until provider initialization, migration, and schema validation complete; an accidental early call fails deterministically and performs no data operation.

- Migrations are embedded resources in `Store.Sqlite`.
- Filenames are monotonically ordered, for example `001_CreateSessions.sql`.
- Only scripts named in an exact embedded migration manifest are loaded; wildcard discovery alone is insufficient.
- A migration committed to a release is immutable.
- Fixes and roll-forwards use a new migration.
- DbUp variable substitution, preprocessing, code-based migrations, and `RunAlways` scripts are disabled. Enabling any of them requires a separate ADR and threat/reliability tests.
- DbUp's journal records applied scripts.
- DbUp defaults to no migration transaction. The migrator MUST explicitly configure `WithTransaction()`. Integration tests must prove that both pending scripts and journal writes roll back together with the pinned SQLite provider; if they do not, startup must fail until a verified atomic strategy is selected.
- A failed migration prevents normal startup and produces a structured startup failure plus diagnostic logging.
- Tests cover new, old, partially migrated, incompatible, and deliberately failing databases.
- Released migration resources are covered by a checksum manifest so accidental edits fail CI.

DbUp is the deliberate migration path outside normal `IStore.ExecuteAsync`; it runs before application writes and owns its own transaction semantics.

The current manifest contains checksum-pinned `001_InitialSchema.sql`, `002_AddThemePreference.sql`, and `003_AddIncidentParticipants.sql`. Migration `002` adds the constrained, non-null `theme_preference` column with numeric default `0` (`FollowDesktop`) and advances `PRAGMA user_version` to 2. Migration `003` adds incident participant identity plus optional driver/team/car context, replaces the old session-only checkpoint with a composite `(session_id, participant_identity)` checkpoint, replaces incident uniqueness with the participant-scoped key, maps existing incidents/checkpoints to the compatibility identity `local-player`, and advances `PRAGMA user_version` to 3. Neither migration rewrites an earlier script; upgrades preserve existing incident, checkpoint, and replay-preference data. DbUp is configured with `WithVariablesDisabled()`, `WithTransaction()`, and the same finite execution timeout as the SQLite busy timeout. Cancellation is checked before and after migration and throughout schema validation; the native synchronous DbUp/provider call itself is not preemptible. A timeout or cancellation therefore prevents the execution gate from opening, but the host must not claim a hard wall-clock interruption inside an in-progress native call.

### 11.7 Export

Export is a separate application capability, not a method on `IStore` and not a copy of database tables.

```csharp
public interface IIncidentExporter
{
    Task<Result> ExportAsync(
        IncidentExport export,
        Stream destination,
        CancellationToken cancellationToken);
}
```

A future `Export.Json` implementation maps stable export models to JSON. JSON is not required for domain communication, configuration, or storage.

## 12. Result and error model

`IncidentReview.Results` is a tiny runtime/compile-API dependency-free primitives library. Repository analyzers may inspect it during the build but do not become an assembly dependency. It must not become a miscellaneous `Common` project.

Conceptual public surface:

```csharp
public readonly record struct ErrorCode
{
    public static ErrorCode Define(string namespacedCode);
}

public sealed class Error
{
    public ErrorCode Code { get; }
    public ErrorKind Kind { get; }
    public string Message { get; }

    public static Error Create(
        ErrorCode code,
        ErrorKind kind,
        string safeMessage);
}

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unavailable,
    Persistence,
    Integration,
    Indeterminate
}

public sealed class Result
{
    public bool IsSuccess { get; }
    public Error? Error { get; }

    public static Result Success();
    public static Result Failure(Error error);
}

public sealed class Result<T> where T : notnull
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public Error? Error { get; }

    public static Result<T> Success(T value);
    public static Result<T> Failure(Error error);
}
```

Required semantics:

- Construction is controlled by factories/private constructors.
- Success cannot contain an error.
- Failure cannot expose a usable value; accessing `Value` on failure throws `InvalidOperationException`.
- `Result<T>` never represents success with `null`. Optional success data is represented by an explicit domain value, while absence that prevents satisfying the request is `NotFound`.
- `default` cannot manufacture an invalid state; therefore the primary types are reference types rather than defaultable record structs.
- `ErrorCode.Define` accepts only a documented lowercase namespaced syntax and rejects empty/malformed constants; `Error.Create` also rejects `default(ErrorCode)`. Each boundary owns a static error catalog; verification scans catalogs for duplicate codes and undocumented additions. Examples include `store.sqlite.busy` and `store.commit.indeterminate`.
- Error messages are safe diagnostics, not localized UI copy and not raw provider messages.
- UI maps error codes/kinds to user-facing actions and wording.
- Expected validation, not-found, conflict, unavailable, persistence, integration, and indeterminate-outcome failures return results.
- Broken invariants, programmer defects, and genuinely unexpected exceptions throw; there is deliberately no catch-all `Unexpected` result kind.
- `OperationCanceledException` propagates when cancellation is observed before an irreversible commit and is never converted into an ordinary failure; once commit has begun, the resolution rule in Section 11.4 applies.
- Infrastructure logs exception details with appropriate context before returning a safe translated error.
- Result composition helpers such as `Map`, `Bind`, and `Match` MAY be provided, but the library must remain small and unsurprising.

## 13. Dependency injection and application lifetime

### 13.1 Generic Host

The WPF executable uses Microsoft's Generic Host with `Host.CreateEmptyApplicationBuilder`. Starting from the empty builder makes every configuration/logging provider deliberate and avoids the default `appsettings.json` pipeline. The host owns:

- dependency injection;
- configuration providers;
- structured logging;
- process lifetime and framework-hosted services;
- application lifetime and graceful shutdown;
- options validation.

`Host.Wpf` is the sole composition root:

```csharp
builder.Services
    .AddIncidentReviewApplication()
    .AddSqliteStore()
    .AddIracingIntegration()
    .AddIncidentReviewDesktop();
```

Concrete implementations are `internal sealed`. Each implementation assembly exposes a small registration extension and any validated options type needed by the host. Adapter assemblies may additionally expose one clearly named `.Testing` registration API as specified in Section 15.6; that is the only approved public testing surface.

The host explicitly adds code defaults, the `INCIDENTREVIEW_`-prefixed environment provider, and command-line configuration, then binds and validates typed options. It registers those options before calling implementation modules; adapter assemblies do not accept a general-purpose `IConfiguration` object or select configuration sections themselves.

WPF requires an STA entry thread. The host executable owns that constraint. Container validation, store initialization, migration, and schema/capability validation finish before entering the WPF dispatcher. Session/participant-checkpoint state is loaded lazily as telemetry and UI queries require it. Synchronous joins needed to preserve STA startup are confined to the composition root before a UI synchronization context exists.

### 13.2 Container rules

- Enable `ValidateOnBuild` and `ValidateScopes` in every environment.
- Bind typed options and call `ValidateOnStart` for settings needed during startup.
- Do not use a static/global service locator.
- Do not inject `IServiceProvider` into ordinary application, domain, UI, or adapter services.
- Do not call `BuildServiceProvider()` inside registration methods.
- Do not perform I/O or asynchronous initialization in constructors.
- Let the container own disposal of services it constructs.
- Create explicit scopes for scoped workflows; WPF has no automatic per-request scope.
- SQLite connections and transactions are per-operation resources owned internally by the store, not singleton DI services.
- Singleton services containing mutable state must own and test their synchronization.

### 13.3 Startup sequence

Startup uses a host-owned internal `BootstrapCoordinator` plus the `IApplicationRuntime` contract with named phases; it does not depend accidentally on registration order or unexplained integer priorities. `BootstrapCoordinator` orchestrates public lifecycle contracts such as `IStoreInitializer` and never reaches into an adapter implementation. The application telemetry loop is intentionally not registered as `IHostedService`, because `Host.StartAsync` must be able to validate the container and options without beginning external work before the database is ready.

```csharp
[STAThread]
public static int Main(string[] args)
{
    using var singleInstance = SingleInstanceGuard.TryAcquire();
    if (!singleInstance.IsAcquired) return ExitCodes.AlreadyRunning;

    IHost host = BuildHost(args); // ValidateOnBuild + ValidateScopes
    IApplicationRuntime runtime;
    try
    {
        host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        using (var startup = new CancellationTokenSource(ValidatedStartupTimeout))
        {
            var bootstrap = host.Services.GetRequiredService<BootstrapCoordinator>();
            RequireSuccess(bootstrap.InitializeAsync(startup.Token).GetAwaiter().GetResult());
            // create/validate directories → initialize SQLitePCL once → migrate →
            // validate schema/capabilities and open the store execution gate

            runtime = host.Services.GetRequiredService<IApplicationRuntime>();
            RequireSuccess(runtime.StartAsync(startup.Token).GetAwaiter().GetResult());
            // Start the supervised telemetry/application worker only after the store gate opens.

            if (!VerifyStartupWasRequested(args))
            {
                var preferences = RequireSuccess(
                    host.Services.GetRequiredService<IIncidentReviewService>()
                        .GetPreferencesAsync(startup.Token).GetAwaiter().GetResult());
                var preparedApp = host.Services
                    .GetRequiredService<IncidentReviewDesktopApplication>();
                preparedApp.PrepareForStartup(preferences);
                // Resolve and apply the stored theme while the window is still hidden.
            }
        }

        if (VerifyStartupWasRequested(args))
        {
            StopRuntimeAndHost(runtime, host);
            return ExitCodes.Success;
        }

        var app = host.Services.GetRequiredService<IncidentReviewDesktopApplication>();
        using var supervisor = RuntimeSupervisor.Attach(runtime, app.Dispatcher);
        var uiExitCode = app.Run();

        supervisor.BeginExpectedStop();
        StopRuntimeAndHost(runtime, host);
        return uiExitCode;
    }
    finally
    {
        // The implementation independently attempts runtime stop/completion,
        // host stop, and async host disposal even when an earlier phase fails.
    }
}
```

The code is condensed pseudocode matching the implemented phase order; detailed failure preservation is omitted. The single-instance guard is acquired before any database open or migration. `Host.StartAsync` starts only the deliberately registered framework services; no application service observes telemetry, opens the database, or mutates state during that call. `BootstrapCoordinator` owns ordered store initialization and is the only gate to `IApplicationRuntime.StartAsync`. For an interactive start, the host reads the complete stored `UserPreferences` through `IIncidentReviewService` after store initialization and passes it into the desktop startup surface before `Application.Run` can show the main window. Headless `--verify-startup` does not construct the UI. This composition-root-only synchronous bridge occurs before a WPF synchronization context exists and prevents a default-palette flash without allowing WPF to query `IStore`.

The validated startup timeout defaults to 30 seconds and permits 1 through 120 seconds. It bounds the cancellable phase as a whole and is checked between native migration/schema operations. It cannot forcibly preempt a synchronous native call that is already in progress. Process-wide Ctrl+C/session-ending cancellation is not currently wired into WPF startup and remains lifecycle hardening work.

`RuntimeSupervisor` observes `IApplicationRuntime.Completion`. An unexpected fault or unrequested completion posts shutdown to the WPF dispatcher, causing `App.Run` to return. On every normal return from `App.Run`, the host first marks shutdown as expected, then calls and awaits `StopAsync`, awaits/inspects `Completion`, stops the Generic Host, and disposes it before returning the UI exit code. Thus a worker fault in the race before or during stop is still surfaced through the outer fatal boundary.

On an exceptional path, `ExceptionDispatchInfo` preserves the primary stack. Cleanup still attempts runtime stop/completion observation, host stop, and disposal. Cleanup diagnostics are attached/logged without replacing an existing primary exception; when there is no primary exception, cleanup/worker failure becomes the thrown failure. No background `Task` is fire-and-forgotten or left unobserved.

Each expected startup step returns `Result`; `RequireSuccess` converts a failed startup result into one host-owned fatal-startup path without discarding its safe message. SQLitePCL initialization occurs before any `SqliteConnection` or DbUp use and is exercised by the Release-host startup smoke test. Failure stops later phases, traces diagnostics, presents one safe fatal-startup message when not in headless verification mode, and shuts the host down cleanly. Unexpected exceptions are caught only at this process boundary.

Shutdown reverses ownership:

```text
disable/close UI
    → cancel telemetry processing
    → await application runtime stop
    → finish or roll back active store operations
    → stop Generic Host
    → dispose host
```

JSON configuration is not used in the current application. Deployment settings use code defaults, the compatibility-stable `INCIDENTREVIEW_`-prefixed environment variables, and command-line arguments. The default database remains `%LOCALAPPDATA%\IncidentReview\incident-review.db`; `--database-path <absolute-path>` and `--startup-timeout-seconds <1..120>` override the two startup values. The product-facing host assembly metadata is `GravelReview`, while its assembly/namespace identity remains `IncidentReview.Host.Wpf`. The existing database location, environment prefix, `Local\IncidentReview.Host.Wpf` mutex, and `IncidentReview.*` Automation IDs MUST remain unchanged during this rebrand. `--verify-startup` is a headless smoke-test switch that initializes the real host/store/runtime and then shuts down. Durable user preferences—including theme policy—belong to SQLite and are read through application use cases. Adding a JSON configuration provider later requires an explicit use case and dependency/configuration review; `System.Text.Json` remains reserved initially for the deferred export feature.

## 14. UI design boundary

The implemented GravelReview UI contains:

- the `assets/branding/logo-simplified.png` GravelReview mark, product name, and transient active-driver display name in a compact raised header/toolbar; the host executable/window uses `assets/branding/favicon-simplified.ico`;
- one primary chronological incident log derived only from `ReviewSnapshot.ActiveSession`, with its own horizontal and vertical scrolling and no historical-session selector;
- full local recorded time through milliseconds with its UTC offset, a compact `…xxxxxxxx` suffix for the UUIDv7 incident identity, replay time, lap, point delta/total, and captured driver context for each incident; the display falls back from driver to team, car number, then `Unknown driver`, while the full stable incident ID remains the command identity and is available as a tooltip;
- `-2 sec`, `0 sec`, and `+2 sec` actions on each incident row; double-click dispatches the `0 sec` action;
- no Notes column and no annotation/classification controls in the primary UI, while those durable domain/store capabilities remain intact for future workflows;
- a collapsed-by-default **Advanced** panel containing a timestamped bounded event stream with horizontal/vertical scrolling and tail-follow behavior, plus a live iRacing camera-group dropdown and Save action;
- no visible lead-in, playback-speed, or pause fields; those stored values remain compatibility behavior for playback and the legacy review overload rather than being silently deleted;
- a quiet bottom status bar containing iRacing connection state, current event/work state, live-update state, and a far-right icon-only theme control;
- explicit unavailable state when replay control cannot be used.

The camera dropdown is populated from the simulator-neutral camera names in `ReviewSnapshot`. It always offers “current iRacing camera” as the null preference. The selected or saved name is retained as an unavailable choice when it is absent from a later live catalog; only a successful explicit Save writes the preference. Driver and current-camera display values are transient and disappear when the application snapshot no longer supplies them. WPF MUST NOT parse SDK session information or invent a second default-camera catalog.

The WPF state model deliberately adapts the top-down, data-driven ideas described by [Replicant](https://replicant.fun/) without adding Clojure, ClojureScript, a browser runtime, or a Replicant dependency:

```text
ReviewSnapshot + local UiAction (implemented as MainWindowAction)
    → pure MainWindowReducer.Reduce(previousState, action)
    → replacement immutable MainWindowState
    → one State property notification
    → XAML binds State.* from the window root
```

`MainWindowState` is the sole owner of UI-visible data: snapshot revision/status/error, active session, incidents, selected incident identity, saved preferences, configured theme preference, resolved palette, transient driver/current-camera context, camera choices and dirty draft, busy/monitoring state, event log, and current status event. The view model MUST NOT maintain independent visible fields or mutable observable collections that can disagree with this root. It may own effect machinery—commands, cancellation sources, gates, worker tasks, and the dispatcher—but effects occur outside the reducer. Service calls and update observation produce data-only actions; time needed by an action is captured before reduction. `MainWindowReducer.Reduce` performs no I/O, reads no clock/service/control, mutates no prior value, and returns either the unchanged instance for an ignored action or a complete replacement state.

Applying a snapshot is atomic. The reducer derives the incident list from the active session, retains selection only while its full ID remains present, builds camera choices, and then replaces the root. A snapshot with a revision lower than the rendered revision is stale and MUST return the existing state instance. During an automatic refresh, an unsaved camera draft MUST survive application preference/context updates; a manual refresh may deliberately accept the stored preference. Busy and monitoring transitions also flow through actions instead of separate bindable properties.

WPF selector bindings can synchronously write `SelectedItem` back when an `ItemsSource` is rebound during root-state publication. That framework write-back is a render consequence, not a second user action. The view model therefore suppresses selector setters while it publishes state, equivalent selector actions are reducer identity transitions, and the state copier reuses incident, camera, and event collection objects when a transition carries them forward unchanged. This prevents presentation feedback loops and needless item-container regeneration while preserving real user selection as an explicit action.

Every actionable notice is constructed once as an immutable `EventLogItem`. Appending it evicts the oldest entries above the 200-entry bound, stores it as the event-log tail, and assigns that exact object reference to `CurrentStatusEvent`. Whenever current-event text is displayed, `ReferenceEquals(State.EventLog[^1], State.CurrentStatusEvent)` MUST hold; a temporary busy label may obscure its text but MUST NOT create or replace a competing status event. Errors, successful replay navigation, refreshes, monitoring transitions, and simulator-status transitions use this same path. The list tail-follows after replacement-state notification as a view-only WPF effect; the running event stream is not a substitute for a future durable diagnostic sink.

View models depend only on `Application.Contracts`, domain values intended for presentation, `Results`, and presentation-owned abstractions such as a dispatcher or dialog service. They do not query the store, read telemetry/replay context, or encode replay commands. UI event handlers contain presentation mechanics only. Use-case decisions live in `Application`, pure business rules live in `Domain`, and pure presentation transitions live in the reducer.

The presentation layer uses WPF/BCL `INotifyPropertyChanged`, `ICommand`, data binding, read-only snapshots, resource dictionaries, and accessibility automation peers directly. No MVVM framework, theme framework, mediator, event-bus, immutable-collection package, or reactive package is added before a concrete requirement justifies it. Long-running actions expose busy/cancellation state, disable duplicate commands, and never block the dispatcher; replacement state is published only on the dispatcher through the presentation abstraction.

User-visible state and interactive elements have stable, documented Windows UI Automation names/`AutomationId` values. Keyboard navigation, focus order, screen-reader names, scaling, high contrast, scrolling/tail-follow, all three row offsets, and selection/review behavior MUST be acceptance-tested; automation identifiers remain the compatibility-stable `IncidentReview.*` contracts and do not depend on product branding or localized display text.

### 14.1 Native WPF theme system

Theme structure and palette are deliberately separated:

```text
Themes/Base.xaml       structural control styles using DynamicResource
Themes/Light.xaml      complete light semantic brush set
Themes/Dark.xaml       matching complete dark semantic brush set

ThemePreference        FollowDesktop | Light | Dark (durable policy)
ResolvedTheme          Light | Dark (concrete palette in MainWindowState)
```

`Base.xaml` defines shared typography, spacing, focus visuals, raised surfaces, button hierarchy, `DataGrid` header/alternate/hover/selection states, input/list styling, separators, scrollbars, the Advanced disclosure, tooltips, and the status bar. The palette dictionaries contain no behavior and expose matching semantic resources, including `WindowBackgroundBrush`, `SurfaceBrush`, `RaisedSurfaceBrush`, `BorderBrush`, `PrimaryTextBrush`, `SecondaryTextBrush`, `AccentBrush`, `DangerBrush`, and `SelectionBrush`. Additional `App.*` semantic keys MAY refine component states, but control XAML MUST use semantic `DynamicResource` references and MUST NOT duplicate light/dark literal colors. Palette replacement changes only the active Light/Dark dictionary and preserves `Base.xaml` plus unrelated resources.

This native resource-dictionary design keeps palette policy explicit, inspectable, testable, and dependency-free while retaining normal WPF dynamic-resource propagation and accessibility behavior. The visual direction is compact and information-first: layered surfaces, quiet separators, clear focus/selection, restrained GravelReview orange accent, and dense controls without obscuring the incident log.

The durable `ThemePreference` and presentation-only `ResolvedTheme` are intentionally different types. Selecting Follow desktop does not persist “Light” or “Dark”; it persists `FollowDesktop`, resolves the current Windows application palette, and can therefore continue responding to later desktop changes. `IDesktopThemeSource` is the only surface that interprets Windows' `AppsUseLightTheme` value and owns the `Microsoft.Win32.SystemEvents` subscription. `IThemeController` maps policy to a concrete palette and replaces WPF resources. Registry and static-event details do not enter domain, application, reducer, or XAML code. A missing/inaccessible registry value or unavailable event subscription degrades safely to Light. Both operating-system surfaces are already provided by the Microsoft desktop framework; this feature adds no package dependency.

The theme button is an accessible icon-only control at the far right of the status bar. Monitor, sun, and moon vector glyphs represent Follow desktop, Light, and Dark; the Automation name/tooltip always states the configured mode and the next click's result. Keyboard activation and the exact cycle `FollowDesktop → Light → Dark → FollowDesktop` are public interaction behavior. A desktop-theme event becomes a typed presentation action; the reducer updates `ResolvedTheme` only when `ThemePreference` is `FollowDesktop`, so forced Light or Dark cannot be overwritten by an operating-system event. The window applies exactly `State.ResolvedTheme`; it MUST NOT independently resolve Windows state and create a second source of truth.

Theme selection is an optimistic presentation effect with explicit compensation:

```text
capture prior complete UserPreferences
    → reduce next ThemePreference + resolved palette immediately
    → rebuild UserPreferences while preserving replay/camera fields
    → IIncidentReviewService.UpdatePreferencesAsync
    → typed UpdatePreferences command
    → one IStore-owned transaction
        ├─ success: keep selected state and refresh the coherent snapshot
        └─ failure: restore prior preferences/current resolution, then append one exact error event
```

The rollback action occurs before the error notice is reduced, so status and event history cannot claim a palette that was not durably accepted. If Windows changes while a failed save temporarily previews a forced mode, restoring `FollowDesktop` resolves the newest desktop palette instead of reviving a stale one. Startup follows the same ownership boundary: after migration/store initialization, the host obtains `UserPreferences` through `IIncidentReviewService`, seeds the desktop state, resolves/applies the palette while the window is hidden, and only then runs WPF. Subsequent snapshots remain authoritative. Neither the host nor the view reads or writes the preference row directly.

## 15. Testing philosophy and strategy

### 15.1 Governing philosophy

The project follows the principles described by Richard Hipp at SSW 2026:

- If behavior is not tested, assume it does not work.
- Testability must be designed into the product rather than added afterward.
- Verify tests cover requirements and verify the tests themselves.
- Exercise every meaningful decision in both directions and independently vary compound conditions.
- Inject failures deterministically at controllable boundaries.
- Use boundary, stress, configuration, static-analysis, and platform tests together.
- Test the code and artifacts actually delivered.
- Do not fear test code being substantially larger than production code.
- Use checklists because a large reliability process cannot live in memory.
- Use mutation testing to assess test sensitivity, while recognizing that universal mutation completeness is not a practical initial gate.

This is an engineering philosophy, not a claim of DO-178B certification.

### 15.2 Test layers

```text
Requirements and invariants
    ↓
Pure domain/result tests
    ↓
Application use-case tests through contracts
    ↓
Pure presentation reducer/state tests
    ↓
Reusable implementation contract suites
    ↓
Real SQLite and binary/protocol integration tests
    ↓
Fault, cancellation, locking, and recovery tests
    ↓
Release-compiled architecture and system tests
    ↓
Packaged-deliverable smoke tests
    ↓
Real-iRacing acceptance checklist
```

The repository currently implements the source/contract/integration layers for selected behaviors, plus a Release-compiled host-startup smoke test. Specifically, it has value/transition tests, public contract-shape tests, coherent `ReviewSnapshot` construction and retry tests, explicit replay-offset workflow tests, real temporary-SQLite tests and deterministic commit/migration seams, compiled-assembly architecture tests, analyzer tests, out-of-process shared-memory protocol tests, replay-context-reader tests, confirmed replay-controller/golden-vector tests, a focused hidden-native-window broadcast test, pure `MainWindowReducer`/immutable-state tests, and WPF effect/view-model tests. The final package/UI-automation and real-iRacing layers remain release gates, not completed evidence.

### 15.3 Requirements traceability

Every externally observable requirement and critical invariant receives a stable ID. Its record includes observable acceptance criteria, failure behavior, boundaries, criticality, owning contract, and implementation/verification status. MSTest cases currently attach IDs with `TestProperty`. The planned repository verifier/CI matrix will show:

- requirements with no tests;
- tests with no requirement/invariant rationale;
- requirements whose tests did not execute;
- release-checklist items and evidence.

Traceability does not replace assertions. A test must fail for a meaningful violation of its requirement.

### 15.4 Public-interface and contract testing

Production behavior is tested through the same contracts used by consumers where the current suite provides that coverage.

- `Store.ContractTests` validates the closed immutable contract surface; the SQLite implementation has real-database behavioral tests. A reusable backend-independent behavioral harness is still required before a second `IStore` implementation can claim conformance.
- The SQLite suite validates observable on-disk behavior against real temporary databases, including event sessions containing incidents/checkpoints from several heat numbers, independent participant epochs and uniqueness, historical migrations, preferences, command fingerprints, and replay coordinates.
- The iRacing suite exercises connection/recovery boundaries, replay classification, eligible participant filtering, current-driver and legacy team counter shapes, source-matched local scalar substitution, opponent increases, driver swaps, roster omission/reorder, event-wide durable keys across heat numbers, provisional keys, and bounded-buffer behavior against an independent process.
- Replay tests exercise intent validation, availability, canonical participant resolution, driver-scoped team-car normalization, opponent and cross-heat focus, explicit identity-change refusal, stable live/replay metadata, delivery outcomes, later-frame confirmation, cancellation, and distinct seek/camera/playback timeouts. Independently authored packed-message vectors cover seek, focus, and playback.
- Application identity/workflow tests cover durable and provisional event identity, one incident list across heats, heat baselines, independent participant checkpoints, counter transitions/reset/omission, simultaneous/batched increases, replay suppression, reconciliation, recorded-participant review, explicit offsets, coherent snapshots, revisions, and lifecycle. The full real-store and real-simulator matrix remains an acceptance goal.
- Presentation tests call the pure reducer directly to verify full replacement state, ignored stale revisions, equal-revision handling, selection retention, selector idempotence, unchanged collection identity, dirty camera-draft preservation across automatic refresh, bounded eviction, reference identity between `CurrentStatusEvent` and the event-log tail, all theme mappings/cycle transitions, live desktop changes in Follow desktop, and ignored desktop changes in forced modes. Theme-controller/source tests independently cover registry interpretation, safe fallback, subscription disposal, resolution, and single-palette resource replacement. View-model/desktop-startup tests verify that stored policy seeds state before display, render-time selector write-back cannot feed back into state, an isolated shown window survives populated incident/camera refresh with Advanced expanded, successful persistence preserves replay preferences, and persistence failure restores the prior state before emitting its exact error, without constructing a second visible state. Shown-window crash probes run in a child process with a hard timeout because fatal runtime failures such as stack overflow cannot be caught by the test host.
- Tests do not use reflection to invoke private business logic; reflection is reserved for architecture inspection.
- Implementation-specific tests may reference their implementation project but must not teach production consumers to bypass its contract.

### 15.5 Deterministic test implementations

The current application and presentation suites use narrow suite-local `IStore`, `ITelemetrySource`, `IReplayController`, `IReplayContextReader`, `IDesktopThemeSource`, dispatcher, and service fakes. Reducer tests need no fake, dispatcher, window, or clock because every input—including occurrence time and a resolved palette—is action data. Adapter tests use production-owned explicit `.Testing` facades; SQLite tests use real temporary databases, deterministic commit/cleanup outcomes, an operation checkpoint, and a migration gate; iRacing tests use unique kernel-object names, an independent subprocess, a manual `TimeProvider`, controlled stable-frame observations, a recording replay sender, and one hidden top-level native window that receives the real registered Windows broadcast.

`IncidentReview.TestKit` currently establishes only the permitted public-contract dependency direction; it does not yet contain shared implementations. Reusable `ScriptedTelemetrySource`, `RecordingReplayController`, `InMemoryStore`, manual time/identity helpers, scheduler controls, and a general occurrence-based fault injector remain planned. When duplication or a second adapter/store makes them useful, they move into `TestKit` without privileged implementation access.

Randomized, property, and fuzz-style tests must record the seed and minimized input required to reproduce a failure. Those campaigns are not implemented yet. Existing concurrency tests prefer deterministic barriers and bounded outer timeouts over sleep-based assertions.

Current lifecycle tests cover application start/stop/completion, early telemetry completion/faults, cancellation, WPF monitoring, stale-snapshot rejection, dirty camera-draft/incident-selection preservation, live desktop-theme dispatch, static-event unsubscription, serialized effect dispatch, and view-model disposal while an operation is active. The complete host/UI race matrix—faulting between UI return, expected-stop marking, runtime stop, host stop, and disposal—remains release-hardening work.

### 15.6 Fault injection

The target reliability design uses narrow fault probes at meaningful state transitions, for example:

```text
Sqlite.BeforeBegin
Sqlite.AfterBegin
Sqlite.AfterSessionWrite
Sqlite.AfterIncidentWrite
Sqlite.BeforeCommit
Sqlite.AfterConfirmedCommit
Iracing.BeforeSharedMemoryRead
Iracing.AfterSharedMemoryRead
Replay.BeforeBroadcast
Replay.AfterBroadcast
```

The current SQLite `.Testing` facade can select normal, indeterminate-before-commit, indeterminate-after-commit, and cleanup-failure outcomes; cancel after operation lookup; inject test migrations; and coordinate a blocked migration. The current iRacing `.Testing` facade creates a real reader over isolated object names, opens a focused frame-copy probe, supplies controlled replay-delivery and stable-frame outcomes, invokes the production Windows sender, and exposes only narrow session-key/replay-metadata observations. These facades exercise the compiled implementation and are inaccessible to ordinary production consumers under the architecture policy. There is no user-activatable test mode.

A general named-probe controller and occurrence sweep across every critical I/O point do not exist yet. Reliability work MUST add them only through similarly explicit implementation-owned testing surfaces, keep normal registration on no-op/production behavior, and prove that test controls do not replace the code being tested.

These coordination probes verify our state machine around provider calls; they do not claim to reproduce a native filesystem or SQLite VFS failure inside `fsync`/commit. Existing real-provider tests cover invalid paths, migration failure/rollback, newer or incompatible schema, initialization gating, provider/pragma/schema validation, idempotency, cancellation, conflicts, cleanup, and indeterminate reconciliation. Locks, read-only files, `max_page_count`/full conditions, corruption fixtures, and abrupt child-process termination remain in the required reliability matrix.

The internal commit-boundary wrapper has two test-controller outcomes for the otherwise hard-to-reproduce ambiguous branch: report indeterminate without invoking native commit, and invoke the real native commit then withhold its outcome from the transaction executor. Both return `store.commit.indeterminate` to the caller and force reconciliation on a fresh connection, covering respectively absent and present operation records. This is a state-machine simulation of an unknown outcome, not evidence of physical power-loss behavior. A separately named `AfterConfirmedCommit` occurrence probe is part of the future general fault sweep, not current evidence.

For each injected store failure, tests close and reopen the database. Failure before commit must leave the pre-transaction state; success after a confirmed commit must leave the complete post-transaction state. An indeterminate commit is covered by present/absent reconciliation and idempotent-retry tests. Partial state is never acceptable.

### 15.7 Coverage and independent conditions

Critical hand-written logic requires complete decision/branch coverage:

- domain invariants and incident detection;
- `Result` state and composition;
- application orchestration and error propagation;
- transaction commit/rollback selection;
- SQLite error translation;
- iRacing frame/metadata decoding, replay command encoding, and applied-state confirmation;
- startup phase success/failure decisions.
- runtime worker completion/fault supervision and WPF shutdown routing.
- configured/resolved theme mapping, forced/follow mode decisions, and persistence compensation.

Compound conditions receive decision-table tests showing that each condition can independently affect the outcome. A line percentage alone is insufficient. Generated WPF code and trivial generated boilerplate are excluded transparently; exclusions cannot hide application decisions.

`Microsoft.Testing.Extensions.CodeCoverage` is the selected collector and is directly pinned in every MSTest project. The current `eng/verify.ps1` performs a locked build, runs the test suite, and invokes a no-op verification scaffold; it does not yet collect, merge, or enforce coverage. The required future pipeline creates an empty run-ID-specific output directory and manifest, enumerates every expected test application, and invokes Microsoft Testing Platform with `dotnet test --no-restore --no-build --coverage --coverage-output-format cobertura`. `IncidentReview.Verification` will accept only reports named in the current successful run manifest, read every resulting Cobertura file, apply an explicit reliability-kernel assembly include list, and fail the build below complete reachable branch coverage. It must union branch identities across reports only when invocation ID, module identity, PDB identity, and the recorded binary hash match; it must never scan an ambient directory or average percentages. A missing test application/required assembly, stale or unmanifested report, mismatched binary, unsuccessful producer process, unreadable report, or branch with no executed outcome fails closed. Every source/document exclusion is reviewed. Source/IL branch coverage plus decision tables is evidence of independent-condition testing; it is not proof of machine-code MC/DC under C# async lowering, JIT compilation, or other code generation. Coverage scenarios are rerun against the uninstrumented Release artifact because instrumented code is not the deliverable.

### 15.8 Transaction and recovery matrix

SQLite integration tests include at least:

- successful single and multi-record writes;
- failure `Result` before and after each mutation;
- provider exception before and after each mutation;
- cancellation before begin, during work, and before commit;
- cancellation racing with a confirmed commit returns the committed success outcome;
- uniqueness, foreign-key, and check-constraint failures;
- busy and locked database behavior;
- database open failure, read-only database, disk-full/I/O failure, and corruption;
- failed sync/commit and an explicitly ambiguous commit outcome;
- refusal of an unsupported newer schema;
- verification of production pragmas, provider initialization, engine version, and required capabilities;
- deterministic rejection of queries/commands before successful store initialization;
- process restart/reopen following unsuccessful work;
- migration from every supported historical schema;
- version-1 preferences migrating to `FollowDesktop` without changing lead-in, pause, speed, or camera;
- deliberately failing migration;
- repeated migration execution;
- rollback failure while preserving both primary and cleanup diagnostics;
- rejection of in-flight or committed `OperationId` reuse with a different command/payload and factory prevention of reuse after a rolled-back attempt;
- duplicate execution of an already committed identical command producing no second effect;
- a transient pre-commit failure followed by successful retry of the same immutable command;
- golden command fingerprints and same-`OperationId` reconciliation across every supported command/database version upgrade;
- snapshot-consistent aggregate queries during concurrent reads/writes;
- checkpoint conflict, persistence failure, restart, and retry without a lost or duplicate incident;
- independent participant omission, reappearance, reset, driver-swap, batched-delta, and same-sample multi-participant transitions without cross-contamination;
- hostile-looking values containing quotes, semicolons, comments, SQL keywords, and Unicode, stored verbatim through parameters;
- large note text and boundary numeric values.

The current suite covers the core successful/rollback/idempotency/indeterminate/cancellation/constraint/migration/schema/hostile-value cases, including focused participant-scoped storage cases. It does not yet satisfy every item above—especially OS locking, read-only/full/corrupt storage, every occurrence sweep, all historical multi-version paths, concurrent snapshot stress, or process termination—so this remains the release matrix rather than a claim of completion.

For the transaction reliability kernel, process-termination tests MUST use a child-process harness in Release verification so abrupt termination is not simulated merely by throwing an exception. The explicit crash matrix terminates at least at these named checkpoints:

- between each pair of business mutations in a multi-step command;
- after all business mutations but before inserting `StoreOperation`;
- after inserting `StoreOperation` but before commit;
- immediately before native commit and immediately after confirmed native commit;
- in a repeated race where the parent terminates the child concurrently with a real native commit call;
- after native commit returns but before the caller receives acknowledgement;
- between each DbUp script effect and its journal update, and after the journal update but before the migration transaction commits.

For each point, the parent terminates the child, reopens the real database, runs `PRAGMA integrity_check`, starts the normal recovery/migration path, and asserts the exact allowed state. Before commit is the complete pre-state; after confirmed commit is the complete post-state; a race termination that overlaps the opaque native call may be either complete pre-state or complete post-state, never a mixture. There is deliberately no claim of a deterministic checkpoint *inside* `sqlite3` without a controllable VFS/native hook. Command cases then reconcile and retry with the same `OperationId` and prove duplicate prevention. Migration cases prove schema and journal are both old or both advanced and that rerunning startup converges safely.

Process termination does not independently prove physical power-loss safety or sector-write ordering. These tests verify our transaction usage, pragmas, recovery, and migration integration. A stronger physical-loss claim would require a controllable SQLite VFS or dedicated hardware testing and a separate requirement.

### 15.9 Mutation testing

Planned curated mutations will verify that important tests detect:

- reversed comparisons in incident detection;
- removed rollback or commit calls;
- ignored failed results;
- altered explicit replay-offset or compatibility lead-in arithmetic;
- swapped session/time fields;
- removed SQL transaction arguments;
- unsafe acceptance of reset counters;
- suppressed cancellation.

A mutation run/catalog is not implemented yet. A third-party mutation tool such as Stryker.NET is not added without an explicit dependency decision. Mutation results begin as diagnostic evidence. Every surviving behavior-changing mutant in the reliability kernel is a test defect; equivalent and performance-only mutants require recorded classification, with benchmark evidence for performance-only behavior.

### 15.10 Test the deliverable

The current deliverable tests launch the Release host executable with `--verify-startup` and a unique temporary database and inspect its compiled identity metadata. They exercise the real Generic Host registrations, SQLitePCL initialization, DbUp migration, schema validation, application runtime start/stop, host shutdown, database creation, copied iRacing third-party notice, `GravelReview` product/title metadata, and preservation of the `IncidentReview.Host.Wpf` assembly identity. They assert a successful exit and clean only their owned temporary directory. The release packaging script adds a same-bytes packaged smoke: after a locked restore it publishes the host through its Windows x64 profile, rejects unexpected loose files, externally renames the single executable to `GravelReview-win-x64.exe`, builds a ZIP containing exactly that executable plus the reviewed notice, records both SHA-256 hashes, and runs the exact renamed executable against an isolated temporary database while pointing runtime-discovery environment variables at a nonexistent directory. This is useful packaged startup/identity evidence, but it is not a clean-machine install test and does not drive the WPF UI.

`IncidentReview.Iracing.ProtocolSimulator` is an independent child process with no production-project reference. It creates uniquely named shared memory and an event, then independently writes the pinned header layout, variable table, participant-bearing session-information region, frame values, tick transitions, disconnect/reconnect, malformed input, and torn-read states. Adapter integration tests use the real production shared-memory reader against that process, including opponent-only participant changes and a coalesced telemetry frame that confirms an outstanding seek. Separately, replay tests verify packing through the implementation's recording-sender testing facade and independent golden vectors, and a Windows-only test sends the production broadcast to a hidden native top-level receiver and asserts the exact delivered `wParam`/`lParam`. The protocol simulator and native receiver are not yet combined into one packaged end-to-end peer.

The package-level system test remains a Release gate beyond that packaged startup smoke. It must run the shipped host's real iRacing adapter, application, SQLite store, and production registration against one OS-level protocol peer; publish independent participant-counter changes; use compatibility-stable WPF `IncidentReview.*` Automation IDs to invoke each of the `-2 sec`, `0 sec`, and `+2 sec` actions; independently validate the corresponding seek offset plus recorded-participant camera/playback broadcasts; and publish later confirming telemetry for each stage. It must also verify the active-session-only incident log, captured driver/team/car fallback, full-precision recorded timestamp display and underlying-instant sorting, compact/full incident identity pair, transient driver header, live camera dropdown, collapsed Advanced panel, tail-follow behavior, and same-object event-tail/current-status invariant. Theme acceptance MUST cover keyboard activation and automation naming, all three persisted modes across restart, both palettes and interaction states, live desktop switching only in Follow desktop, persistence-failure rollback, common Windows scaling levels, and high-contrast behavior. It must isolate data using `--database-path`, prove real iRacing is not being disturbed, and run in an interactive Windows user session at compatible integrity levels. Coverage scenarios should also run against the uninstrumented artifact. Clean-machine Windows acceptance, signature verification, and testing the ZIP after transfer remain required release evidence.

Real iRacing acceptance follows a versioned checklist with captured Windows, simulator, SDK-baseline, and app versions. It supplements automated protocol tests; it does not replace them, and it has not yet been completed for this revision.

### 15.11 Test tiers

| Tier | Intended trigger | Contents | Expected role |
|---|---|---|---|
| Fast | Every local build/change | Results, domain, application, architecture, deterministic contracts | Immediate feedback |
| Integration | Every CI change | Real SQLite, migrations, protocol fixtures, concurrency/fault cases | Merge gate |
| Reliability | Scheduled and release | Fault sweeps, fixed-budget fuzzing, randomized seeds, stress, child-process termination, repeated runs | Deep defect discovery |
| Deliverable | Release candidate | Packaged artifact, clean-machine smoke, real-iRacing checklist | Release gate |

Flaky tests are defects. A failing test is not retried until green without preserving and investigating the first failure evidence.

Every reported defect begins with a failing regression test. The repository preserves the smallest useful reproducer and records why the existing suite failed to expose it.

Future nightly fuzzing targets raw SDK buffers, session metadata, telemetry event sequences, notes/Unicode, and store query boundaries for a fixed recorded time budget. Failures retain the seed and input, are minimized, and enter the permanent regression corpus.

No hosted CI, scheduled reliability job, or nightly fuzz job is present yet. The table defines the intended delivery gates; local `eng/*.ps1` scripts currently provide the executable build/test/verification path.

### 15.12 Assertions and explanatory comments

Assertions are executable statements of programmer invariants, not substitutes for validating external input. External invalid data returns a defined failure; broken internal assumptions assert and, where data safety requires it, remain checked in Release. Dedicated diagnostic/invariant-build coverage remains a reliability target; ordinary Release builds are part of the current test path. Concise comments explain why a non-obvious invariant or branch exists rather than merely restating the code.

## 16. Technology stack and dependency policy

### 16.1 Selected baseline

| Area | Selection | Rationale |
|---|---|---|
| Runtime/language | .NET 10 LTS / C# 14 | Current Microsoft LTS baseline installed on the development machine |
| Desktop UI | WPF | Windows-native, mature, Microsoft-supported |
| Desktop theming | Native WPF `ResourceDictionary`/`DynamicResource` plus `Microsoft.Win32.SystemEvents` | Semantic light/dark palettes and live Windows application-theme observation without a theme framework or added package |
| Hosting/DI | `Microsoft.Extensions.Hosting` 10.0.11 and built-in container | Standard composition, logging, options, and lifetime model |
| Testing | `MSTest.Sdk` 4.4.0, Microsoft Testing Platform, explicit Microsoft coverage extension | Microsoft-supported test stack with no implicit extension profile |
| SQL mapping | Dapper 2.1.79 | Explicit parameterized SQL and lightweight mapping |
| Migrations | `dbup-core` 6.1.1 and `dbup-sqlite` 6.0.4 | Ordered, journaled, embedded schema migrations |
| Database | SQLite | Embedded durable store with transactions; no server process |
| SQLite ADO.NET provider | `Microsoft.Data.Sqlite.Core` 10.0.11 | Microsoft-published provider and Dapper compatibility |
| SQLite native binding | `SQLitePCLRaw.bundle_winsqlite3` 2.1.11 | Uses the SQLite engine serviced with Windows |
| Serialization | `System.Text.Json` only for the deferred approved export use case | Included Microsoft implementation; not an internal messaging/storage/configuration dependency |
| Logging | `Microsoft.Extensions.Logging` | Standard structured logging abstraction |
| Repository analysis | Repository Roslyn analyzers matching compiler 5.9.0 | Semantic architecture/SQL enforcement without a runtime dependency |

### 16.2 Approved exceptions and review

The default is Microsoft platform/framework dependencies plus the official iRacing protocol. Theme dictionaries, WPF resource lookup, Windows registry access, and user-preference notifications use framework-provided WPF/BCL/Microsoft desktop APIs; the adaptive theme feature adds no package. Dapper and DbUp are explicitly approved third-party exceptions. SQLite itself and SQLitePCLRaw are also third-party code and must be acknowledged, pinned, licensed, and audited rather than described as Microsoft-only. This stack therefore cannot truthfully be called “Microsoft-only.” The control objective is a minimal, explicit, locked, reviewed graph—not reliance on publisher identity alone.

The Roslyn analysis packages and code-coverage extension are Microsoft-published and prefix-reserved. The coverage extension is closed-source under Microsoft's free-to-use .NET library license; it satisfies an official-Microsoft-publisher policy but not an all-source-auditable policy. That distinction is recorded in the dependency inventory rather than hidden.

The implemented provider stack is `Microsoft.Data.Sqlite.Core` plus `SQLitePCLRaw.bundle_winsqlite3`. It uses Windows' `winsqlite3.dll` instead of shipping the convenience `Microsoft.Data.Sqlite` package's bundled `e_sqlite3` engine. SQLitePCLRaw remains a reviewed third-party shim. This reduces shipped native code but ties available SQLite features to the supported Windows version, so startup validates the engine version and required features and Release acceptance must cover the minimum supported Windows build.

The resolved NuGet graph is locked per package-consuming project, and the pinned Dapper/DbUp/provider combination is exercised by real SQLite integration tests and the Release-host startup smoke. A formal release dependency/license inventory and minimum-Windows compatibility record are still required. `dbup-sqlite` trails `dbup-core` in versioning; its pinned combination remains protected by those tests. Any provider change requires an ADR and must not leak beyond `Store.Sqlite`.

No additional runtime or build package is added merely for convenience. A dependency proposal states:

- capability provided;
- why BCL/repository code is insufficient;
- owner/publisher and repository;
- direct and transitive packages;
- license;
- update history and maintenance status;
- runtime/build-only scope;
- removal/replacement plan;
- tests protecting the boundary.

### 16.3 Current direct package ledger

These are the direct versions currently pinned in repository-root policy. Versions are recorded centrally, not copied into individual projects. Lock files record each consuming project's resolved graph.

| Package | Version | Classification |
|---|---:|---|
| `Microsoft.Extensions.Hosting` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.Configuration.Binder` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.Configuration.CommandLine` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.11 | Microsoft |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.11 | Microsoft |
| `Microsoft.Data.Sqlite.Core` | 10.0.11 | Microsoft-published provider |
| `Dapper` | 2.1.79 | Approved third-party exception |
| `dbup-core` | 6.1.1 | Approved third-party exception |
| `dbup-sqlite` | 6.0.4 | Approved third-party exception |
| `SQLitePCLRaw.bundle_winsqlite3` | 2.1.11 | Approved third-party binding exception |
| `MSTest.Sdk` | 4.4.0 | Microsoft project/test SDK |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.11.0 | Microsoft test-only extension; closed-source license reviewed separately |
| `Microsoft.CodeAnalysis.CSharp` | 5.9.0 | Microsoft build-only dependency; private assets |
| `Microsoft.CodeAnalysis.Analyzers` | 5.9.0 | Microsoft build-only dependency; private assets |

Exact pins are a reproducible starting point, not permission to update automatically or a claim of final platform/release acceptance. Every version change follows the dependency review and lock-file process.

### 16.4 Development prerequisites and verified workstation baseline

The following command-line tools were installed and verified on the initial Windows x64 workstation on 2026-09-05:

| Tool | Verified version/use |
|---|---|
| .NET SDK | `10.0.400` |
| .NET runtime / Windows Desktop runtime | `10.0.11` |
| MSBuild | `18.9.6` |
| Git | `2.53.0` |
| WinGet | `1.29.290` |

An IDE is optional; the repository build, test, verification, and packaging paths must work from PowerShell and `dotnet`. Real integration/acceptance testing additionally requires iRacing and a recorded official local SDK/header artifact. Workstation versions are evidence, not substitutes for `global.json`, lock files, or clean CI images.

## 17. Repository-wide SDK, build, and package management

Each project has a small SDK-style `.csproj`. Shared choices live at the repository root:

```text
global.json
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
NuGet.config
.editorconfig
.gitattributes
.gitignore
IncidentReview.slnx
```

### 17.1 `global.json`

Pins the .NET 10.0.4xx SDK feature band at `10.0.400`, disallows previews, and permits a later installed security patch in that feature band through `latestPatch`. This is deliberate patch roll-forward, not bit-for-bit SDK pinning. Feature-band upgrades are reviewed root changes; Release evidence records the actual SDK version used.

It also selects Microsoft Testing Platform and centrally versions the MSTest project SDK:

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "MSTest.Sdk": "4.4.0"
  }
}
```

### 17.2 `Directory.Build.props`

Defines common defaults:

- target framework where project-specific Windows targeting is not required;
- nullable reference types;
- implicit usings;
- deterministic builds;
- .NET analyzers and code-style enforcement;
- warnings as errors;
- package lock-file generation;
- `TestingExtensionsProfile=None` for every MSTest project so the SDK cannot inject an evolving implicit extension set;
- consistent generated/intermediate output locations if needed.

Do not set `LangVersion=latest`; use the language version associated with the pinned SDK/target framework unless an explicit language upgrade is reviewed.

### 17.3 `Directory.Packages.props`

Enables NuGet Central Package Management. It is the only place `PackageReference` versions appear; MSBuild project SDK versions such as `MSTest.Sdk` live in `global.json`. Project files declare only the packages they consume:

```xml
<PackageReference Include="Dapper" />
```

Floating versions and project-level version overrides are forbidden. Central transitive pinning is enabled only after reviewing its behavior for produced libraries and the resolved graph; `CentralPackageVersionOverrideEnabled` remains false.

### 17.4 `Directory.Build.targets`

Adds repository-owned enforcement after project/package evaluation:

- forbidden project references;
- infrastructure packages outside their owning project;
- WPF outside desktop/host projects;
- versions in project-level `PackageReference` items;
- production `InternalsVisibleTo` relationships;
- other narrowly defined architecture violations that MSBuild can determine reliably.

`Directory.Build.targets` imports `eng/ArchitecturePolicy.props`. Architecture tests parse that same file. This sharing is mandatory so two hand-maintained allowlists cannot silently drift.

The policy is declarative MSBuild XML with stable project/package identities, for example:

```xml
<ItemGroup>
  <AllowedProjectReference Include="IncidentReview.Application">
    <To>IncidentReview.Store.Contracts</To>
  </AllowedProjectReference>
  <PackageOwner Include="Dapper">
    <Project>IncidentReview.Store.Sqlite</Project>
  </PackageOwner>
  <ForbiddenProductionNamespace Include="*.Testing" />
  <TestingFacadeOwner Include="IncidentReview.Store.Sqlite.Testing">
    <Project>IncidentReview.Store.Sqlite</Project>
    <BridgeFile>SqliteTestingRegistration.cs</BridgeFile>
  </TestingFacadeOwner>
</ItemGroup>
```

The real file enumerates every production edge and infrastructure package owner explicitly. Unknown production edges fail closed. Tests/tools have separate explicit rules; wildcard convenience cannot grant production assemblies access to implementation projects.

### 17.5 Repository analyzers and verification tooling

`IncidentReview.Analyzers` targets `netstandard2.0`, sets `IsRoslynComponent=true` and `EnforceExtendedAnalyzerRules=true`, and uses the pinned `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.Analyzers` packages with `PrivateAssets=all`. Version 5.9.0 matches the compiler in SDK 10.0.400; SDK and Roslyn package updates are reviewed together. The root build attaches the project to production consumers as:

```xml
<ProjectReference Include=".../IncidentReview.Analyzers.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

The analyzer project itself and its tests are excluded from that injected reference; analyzer tests use a normal project reference. Code-fix/workspace packages are not added unless a real code-fix requirement is approved. Diagnostics have stable IDs, documented examples, unit tests, and `error` severity for the forbidden patterns named in Section 6.3.

`IncidentReview.Verification` is a normal repository console tool invoked by `eng/verify.ps1`, but its current `Main` is an intentional no-op scaffold. It does not yet consume evidence or enforce policy, and a zero exit from it is not release evidence. When implemented, it must consume explicit machine-readable inputs, exit nonzero on policy failure, and avoid scraping human prose when a schema can be used. Both tools are restored under the same source, lock, audit, and license rules as production dependencies.

### 17.6 Explicit test extensions

Every MSTest project sets `TestingExtensionsProfile=None` and directly references `Microsoft.Testing.Extensions.CodeCoverage` with `PrivateAssets=all`. Direct reference makes the exact extension visible in `Directory.Packages.props`, lock files, audit output, and the license inventory. Do not also enable the SDK's implicit code-coverage switch.

### 17.7 Restore reproducibility

- Commit `packages.lock.json` for package-consuming projects.
- CI uses `dotnet restore --locked-mode`.
- CI/Release also sets `RestoreLockedMode=true` defensively. Every subsequent `build`, `test`, and `publish` command uses `--no-restore`; stages reusing the approved Release build additionally use `--no-build` so no implicit restore or rebuild can change the graph or bytes.
- Dependency-update changes intentionally regenerate and review lock files.
- `NuGet.config` clears inherited sources and declares approved HTTPS feeds/source mapping.
- `NuGet.config` requires the trusted NuGet.org repository signer; author signatures are additional evidence, not a prerequisite for repository-signature enforcement. Any proven incompatibility requires a documented, narrowly tested exception.
- NuGet audit explicitly checks direct and transitive packages; high and critical findings fail CI, while lower findings require recorded triage.
- Because warnings are errors globally, `NU1901` and `NU1902` are explicitly classified as visible triage warnings rather than accidental build failures. `NU1903` and `NU1904` fail CI. Audit-source failures such as `NU1900`/`NU1905` fail closed in CI and Release builds.
- High-trust and CI restores use a clean repository-controlled package cache so an ambient cache cannot bypass source policy.
- Produce a dependency/license inventory for releases. The exact Microsoft-supported SBOM command or tool is selected and pinned in the packaging ADR before the first distributable release; until then the document does not claim SBOM automation exists.

### 17.8 Git and commit strategy

Codex owns routine repository hygiene for work it performs: inspect the worktree before and after each change, stage explicit paths/hunks, run proportionate verification, create the commit, and report its hash. The user does not need to remind Codex to commit completed work. This authority covers the current task and checked-out branch only; it does not authorize pushing, merging, tagging, publishing, or rewriting history. Existing user changes are never reformatted, staged, reverted, or absorbed merely because they are present in the same worktree.

The repository begins on `main`. Work may remain directly on the current user-selected branch; short-lived branches use `type/short-description` when isolation or a future remote/PR workflow requires them. Codex does not create a branch, rewrite published history, force-push, amend an existing user commit, squash the small commits, tag a release, or push to a remote unless the user asks or an agreed workflow explicitly requires it.

Every commit follows Conventional Commits:

```text
<type>(<scope>)!: <imperative summary>

optional body explaining why, invariants, and non-obvious tradeoffs

optional Refs: requirement/invariant identifiers
optional Test: exact verification command, or "not run (specific reason)"
optional BREAKING CHANGE: description
```

Allowed types are `feat`, `fix`, `refactor`, `test`, `docs`, `build`, `ci`, `perf`, `chore`, and `revert`. Scopes normally name the owning boundary—`results`, `domain`, `store-contracts`, `telemetry-contracts`, `replay-contracts`, `app-contracts`, `app`, `sqlite`, `iracing`, `desktop`, `host`, `architecture`, `analyzers`, `verification`, or `deliverable`; cross-cutting scopes include `deps`, `build`, `ci`, `docs`, and `release`. Subjects are imperative, lowercase after the colon, specific, without a trailing period, and normally no more than 72 characters. `chore` must not hide a feature, fix, migration, or dependency change. Breaking public-contract, persisted-data, CLI, or protocol behavior uses `!` and a `BREAKING CHANGE` footer; an ADR and migration/compatibility tests are still required.

A small commit is an independently understandable change with one reason to revert. It is never an intentionally red/WIP checkpoint: it keeps the repository buildable and all previously passing tests green, and includes the tests, requirement links, migration, and documentation needed for that behavior. It should normally be reviewable in about ten minutes and stay below roughly 250 changed hand-written lines; this is a review heuristic, never a reason to split an invariant, migration, test, or compatibility unit. Production code and its direct tests normally belong in the same commit; a standalone `test` commit is appropriate for test infrastructure, a regression that demonstrates an already-known defect, or additional coverage with no production change. Mechanical renames/reformatting, dependency changes, generated lock-file changes, migrations, and behavior changes are separated unless splitting them would make an intermediate commit invalid.

Before each commit, Codex MUST:

1. Re-read `git status` and the staged/unstaged diff so unrelated changes stay untouched.
2. Stage only explicit intended files or hunks; blanket staging such as `git add -A` is forbidden in a dirty worktree and is never the default.
3. Run the smallest sufficient build/tests plus architecture/verification checks affected by the change.
4. Confirm no secret, user telemetry, database/log, generated output, or unexpected dependency/lock-file delta is staged.
5. Create the Conventional Commit and inspect the resulting commit summary/hash.

The commit body/footer records relevant `Refs:` identifiers and the actual `Test:` command. If a check is not applicable or cannot run, it says `Test: not run (specific reason)` rather than implying verification. The full `eng/verify.ps1` plus applicable Release checks run before each milestone handoff even when smaller commits used narrower tests.

The normal cadence is several green, reviewable commits per vertical slice, not one commit per arbitrary file and not one end-of-feature dump. Representative history:

```text
build(repo): establish centralized sdk and package policy
feat(results): add validated result primitives
feat(store-contracts): define atomic command and query contracts
feat(sqlite): persist incident checkpoint transactionally
test(sqlite): cover indeterminate commit reconciliation
feat(iracing): decode validated telemetry observations
feat(wpf): add incident review interaction
docs(design): record replay confirmation semantics
```

Dependency additions/upgrades use a dedicated `build(deps)` commit containing the central version change, reviewed lock files, inventory/license changes, and compatibility evidence. A database migration ships in the same atomic commit as the code and tests that require it; released migration files are never rewritten in a later commit. CI/release configuration changes use dedicated `ci`/`build` commits. Fixes include the smallest reproducer/regression test and use `fix(<scope>)`.

Commit signing and remote branch protection are configured only after the repository host and signing identity are selected. Until then, repository-local author identity must be explicit and must never be fabricated from guessed personal details.

### 17.9 Build, run, and publish commands

Commands are run from the repository root in PowerShell. Normal restore is locked:

```powershell
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release
.\eng\verify.ps1 -Configuration Release
```

`verify.ps1` already performs the Release build and tests; the separate commands are useful while iterating. Only an intentional, reviewed dependency update uses `.\eng\build.ps1 -Configuration Release -UpdateLockFiles`, followed by inspection of every changed `packages.lock.json`.

After a Release build, launch the existing bytes without another restore/build:

```powershell
dotnet run --project .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore --no-build
```

Create and smoke-test the Windows x64 release artifacts:

```powershell
.\eng\publish.ps1
```

The script cleans only its validated `artifacts\release\win-x64` directory, performs a locked restore, and applies the host-local `win-x64-single-file` publish profile. That profile is self-contained, bundles native libraries for extraction, enables single-file compression, disables trimming, and suppresses loose PDBs. The direct executable is `artifacts\release\win-x64\GravelReview-win-x64.exe`; it does not require a separately installed .NET Desktop Runtime. The GitHub-ready `artifacts\release\win-x64\GravelReview-win-x64.zip` contains exactly that executable and `THIRD-PARTY-NOTICES\iracing-sdk-1.20.md`. The script validates that inventory and product metadata, smoke-tests the exact renamed executable with `--verify-startup` and an isolated database, and reports byte counts and SHA-256 hashes. Native runtime components may extract to the user's temporary directory when the application starts. The external release filename is product-facing only: the internal assembly, namespace, mutex, environment-variable, database-path, and UI Automation identities remain compatibility-stable `IncidentReview.*` values.

Startup overrides follow the executable after `--` when using `dotnet run`, for example:

```powershell
dotnet run --project .\src\IncidentReview.Host.Wpf\IncidentReview.Host.Wpf.csproj `
    --configuration Release --no-restore --no-build -- `
    --database-path "C:\IncidentReviewData\incident-review.db" `
    --startup-timeout-seconds 30
```

The database path must be an absolute local file path. The default remains `%LOCALAPPDATA%\IncidentReview\incident-review.db`.

## 18. Observability, privacy, and security

### 18.1 Logging

Logs use structured templates and event IDs. Expected categories include startup, migration, store, iRacing connection, telemetry processing, incident detection, replay control, and shutdown.

Logs MUST NOT contain raw database connection details, arbitrary session-info documents, secrets, or user notes by default. Error results contain safe summaries; detailed exceptions remain in diagnostic logs at the owning boundary.

The Release sink is intentionally an open packaging decision, not an implicit Serilog/NLog dependency. Before the first distributable build, its ADR must define persistence, bounded size/rotation, concurrent writes, crash tolerance, user access, redaction, and failure behavior. Logging failure must not recurse through `IStore` or corrupt application state.

### 18.2 Local data and privacy

The initial release is local-only. It must not transmit telemetry, identifiers, notes, or usage data. Any future server/export flow is explicit and user-initiated or governed by a separately accepted synchronization design.

The database resides in an application-specific per-user data directory. Diagnostics may expose its location without offering unsafe direct mutation. A future export feature chooses a separate user destination. Deletion/retention behavior requires a later product decision.

### 18.3 SQL and input security

- All Dapper values are parameters.
- Notes and imported values are untrusted data.
- Export escapes/serializes data using the selected serializer, never hand-built JSON.
- File destinations are validated and normal file-picker safety is preserved.
- Dynamic SQL identifiers are closed mappings, never user text.

### 18.4 Supply chain

- Restore only from approved sources.
- Pin direct and transitive versions through central management and lock files.
- Review package ownership, signatures, hashes, release notes, licenses, and transitive changes.
- Run vulnerability scanning and produce an SBOM in CI.
- Do not auto-merge dependency updates.
- Build releases from clean, locked restores.

## 19. Evolution paths

### 19.1 Remote store

A future `Store.Remote` maps each immutable store query or command to one request and one response over a versioned protocol. It owns HTTP/gRPC DTOs, authentication, transport retry policy, and server error translation; wire DTOs do not escape the adapter. Application and UI projects remain unchanged.

The server executes one command atomically and honors its `OperationId`; it never attempts to run a caller delegate or keep a transaction open across client round trips. Queries return one server-side snapshot. Transport failure after submission maps to the same indeterminate-outcome and reconciliation contract used locally. If a backend cannot provide these semantics, it cannot advertise itself as an `IStore` implementation.

### 19.2 Offline synchronization

A future `Store.Sync` may compose local SQLite and remote stores. This requires a dedicated design for outbox records, revisions, idempotency, conflict handling, deletion, privacy, and authentication. It must not be approximated by writing to two stores sequentially inside one local transaction.

### 19.3 Additional simulators or frontends

Simulator-neutral telemetry/replay contracts permit another adapter only if its semantics fit honestly. A web or alternate desktop UI consumes `Application.Contracts`; it does not reuse WPF view models by force.

## 20. Delivery plan

Current status: Milestone 0 is implemented except for substantive verification/CI/evidence tooling; Milestones 1 and 2 have working vertical slices, including coherent revisioned presentation snapshots and explicit replay offsets, with remaining reliability-matrix work; Milestone 3 has the official SDK adapter, transient replay-context reader, and independent-process tests but not real-simulator acceptance for this revision; Milestone 4 has the runnable GravelReview WPF workflow, immutable reducer-driven state, semantic light/dark visual system, persisted three-mode theme policy, and pre-show/live Windows theme integration, but not annotation/state editing or package-level UI Automation; Milestone 5 remains largely pending. The checklists below describe the remaining definition of done as well as completed scope.

### Milestone 0 — Repository foundation

- Initialize Git with a .NET/Windows `.gitignore`; do not commit generated output, local databases, logs, credentials, or user telemetry.
- Create `.slnx`, projects, root build/package files, and `.editorconfig`.
- Pin SDK 10.0.400 in `global.json`.
- Add `eng/ArchitecturePolicy.props`, architecture tests, repository analyzers, and verification CLI.
- Disable the implicit MSTest extension profile and pin explicit coverage tooling.
- Implement `Result`/`Result<T>` and their exhaustive tests.
- Establish requirements traceability and CI scripts.

### Milestone 1 — Store vertical slice

- Finalize provider dependency ADR.
- Define immutable `IStore` queries/commands, idempotency, and snapshot semantics.
- Implement SQLite transaction executor with Dapper.
- Add DbUp migrations for sessions, incidents, detector checkpoints, operations, and preferences.
- Build shared store contract tests and SQLite fault/recovery tests.

### Milestone 2 — Simulator-neutral application slice

- Implement domain identities, incident detector, and application use cases.
- Use scripted multi-participant telemetry, recording replay, deterministic IDs/clocks, and in-memory store.
- Verify the complete participant-scoped detect → store → list → review workflow without iRacing.
- Supply presentation consumers with one revisioned `ReviewSnapshot`; retry reads when their application anchor changes and support explicit signed `ReplayOffset` navigation.

### Milestone 3 — iRacing adapter

- Implement and fixture-test shared-memory decoding plus bounded current-driver/legacy-team scored-counter extraction, source-compatible participant identity, source-matched local scalar fallback, event-wide heat grouping, and eligible-entry filtering.
- Implement replay seek, recorded-participant camera focus, and playback encoding with later stable-telemetry confirmation.
- Implement `IReplayContextReader` over the latest stable frame for bounded simulator-neutral active-driver and live camera-group context.
- Build the external protocol simulator used by packaged-deliverable tests.
- Validate connection/reconnect and simulator-state behavior.
- Run the real-iRacing acceptance checklist.

### Milestone 4 — GravelReview WPF experience

- Render only `MainWindowState` from the root; route data-only actions through a pure reducer and replace the complete immutable state on the dispatcher.
- Show the active-session incident log, per-row captured driver context, full-precision local recorded time, transient local-driver header, compact/full incident identity pair, and per-row `-2 sec`, `0 sec`, and `+2 sec` actions.
- Provide the collapsed Advanced panel with bounded tail-follow events and the live iRacing camera selector; derive current status from the exact event-log-tail object.
- Preserve dirty camera drafts across automatic snapshots, reject stale revisions, and retain hidden playback/pause/lead-in compatibility values.
- Keep session history and Notes out of the primary UI while retaining their lower-layer contracts and durable records.
- Use native `Base`/`Light`/`Dark` semantic resource dictionaries and GravelReview branding; keep configured `ThemePreference` and concrete `ResolvedTheme` separate in `MainWindowState`.
- Provide the status-bar `FollowDesktop → Light → Dark` cycle, live Windows changes only while following, pre-show stored-theme application, and transactional save with exact presentation rollback on failure.
- Add reducer, theme source/controller, migration/round-trip, persistence compensation, accessibility, scrolling, dispatcher, and lifetime tests.
- Package and UI-automation-test the Release deliverable; the current host-startup smoke is necessary but not sufficient.

### Milestone 5 — Reliability hardening

- Complete fault-injection sweep and child-process termination harness.
- Add stress/randomized runs with reproducible seeds.
- Review decision tables and surviving curated mutations.
- Produce release checklist, SBOM, dependency inventory, and acceptance evidence.

## 21. Definition of done for any feature

A feature is not done until:

1. Its requirement and relevant failure behavior are documented.
2. Its owning assembly and contract boundary are clear.
3. No forbidden dependency or public API leak is introduced.
4. Success, expected failure, cancellation, and meaningful boundary cases are tested.
5. Compound decisions have independent-condition tests.
6. Store commands are atomic, have at-most-once committed effects by `OperationId`, and their commit/rollback/reconciliation behavior is demonstrated.
7. SQL values are parameterized.
8. New migrations are immutable, repeatable, and migration-tested.
9. Errors use stable result codes and safe messages.
10. Architecture, contract, integration, and Release tests pass.
11. Dependency and lock-file changes are explicitly reviewed.
12. User-visible behavior is validated in the packaged artifact or documented acceptance environment.
13. The work is recorded in one or more small green Conventional Commits and their hashes are reported.
14. A UI feature has one immutable visible-state owner, pure reducer coverage, stale-update behavior, and proof that each displayed status/event derives from its documented source object.
15. A palette-aware UI change uses semantic resources in both Light and Dark, preserves the configured/resolved theme distinction, and is checked for keyboard, automation, scaling, and high-contrast behavior.

### 21.1 Database-change checklist

- Add a new forward-only migration; do not edit a released migration.
- Update and verify the released-migration checksum manifest.
- Run fresh-create and every supported historical upgrade path.
- Prove preservation of existing committed data.
- Exercise migration failure and child-process termination/recovery.
- Verify scripts and journal changes share the intended atomic outcome.
- Validate the resulting schema, pragmas, and supported engine capabilities.
- Document the stable error and recovery behavior for failure.

### 21.2 Defect-response checklist

- Assign a stable defect identifier.
- Preserve the smallest reproducer and first-failure evidence.
- Add a failing regression test before applying the fix.
- Record why existing tests missed the defect.
- Review adjacent decisions, conditions, and boundary values.
- Add the input to the fault/fuzz/mutation corpus where applicable.
- Update release acceptance when a human-process gap contributed.

### 21.3 Release checklist and retained evidence

- Bidirectional requirement/test traceability report.
- Branch/condition coverage report and reviewed exclusions.
- Mutation catalogue results and classifications.
- Fuzz budgets, seeds, minimized failures, and permanent corpus changes.
- Fault-occurrence sweep matrix.
- Child-process crash logs, database snapshots, and integrity results.
- Migration compatibility matrix and script checksums.
- Architecture and resolved-dependency allowlist results.
- Locked restore, vulnerability audit, licenses, and SBOM.
- Hashes proving the tested and shipped artifacts are the same bytes.
- Real-iRacing acceptance checklist with Windows, iRacing, SDK, and app versions; `Max Cars = 63`; solo/team/hosted/admin visibility; both counter shapes and source stability; matching local driver/team scalars; multi-heat event continuity and replay; identity loss/change; omission/reappearance; counter reset; driver swap/return; batched and simultaneous deltas; `0x` non-detection; and canonical recorded-car focus/refusal evidence.

## 22. Initial architecture decisions

| Decision | Status | Summary |
|---|---|---|
| ADR-0001 | Accepted | Windows desktop application using .NET 10 LTS, C# 14, and WPF. |
| ADR-0002 | Accepted; minimum-Windows acceptance pending | SQLite is the initial `IStore`; use `Microsoft.Data.Sqlite.Core` with `SQLitePCLRaw.bundle_winsqlite3`, audit/pin the full graph, and validate Windows engine capabilities at startup. |
| ADR-0003 | Accepted | Dapper performs parameterized SQL mapping; raw SQL remains private to `Store.Sqlite`. |
| ADR-0004 | Accepted | DbUp performs ordered, journaled, embedded migrations before application startup. |
| ADR-0005 | Accepted | Every application mutation is one typed command with at-most-once committed effects, executed atomically through transaction-owning `IStore.ExecuteAsync`. |
| ADR-0006 | Accepted | `Result` and `Result<T>` are shared dependency-free failure primitives. |
| ADR-0007 | Accepted | Microsoft Generic Host is the composition/lifetime mechanism; `Host.Wpf` is the sole composition root. |
| ADR-0008 | Accepted | Assemblies, root policy, analyzers, and architecture tests enforce contracts; runtime implementation classes remain internal, with only approved `.Testing` registration seams public. |
| ADR-0009 | Accepted | Testing follows Hipp's reliability philosophy, including designed-in fault injection and deliverable testing. |
| ADR-0010 | Deferred | JSON is an optional export format, not an internal storage or communication requirement. |
| ADR-0011 | Accepted | Codex creates small, green Conventional Commits for completed work; push/merge/tag/history rewriting remain separately authorized actions. |
| ADR-0012 | Accepted; live acceptance pending | Transcribe only the used protocol surface from the pinned official iRacing SDK 1.20 artifact; do not depend on an unofficial wrapper or redistribute the upstream archive. |
| ADR-0013 | Accepted | `GravelReview` is the product-facing name and assembly metadata; `IncidentReview.*` code/assembly identity plus the legacy database path, environment prefix, mutex, and Automation IDs remain compatibility-stable. |
| ADR-0014 | Accepted | `IReplayContextReader` exposes only a bounded immutable simulator-neutral projection of active driver and camera context; `ReviewSnapshot` is the coherent revisioned application read for presentation. |
| ADR-0015 | Accepted | WPF follows a top-down immutable-state model: data-only UI action → pure reducer → replacement `MainWindowState`; effects stay outside the reducer and current status references the event-log tail object. |
| ADR-0016 | Accepted | WPF theming uses native semantic `Base`/`Light`/`Dark` resource dictionaries. Durable `ThemePreference` remains distinct from `ResolvedTheme`; Windows observation is isolated behind `IDesktopThemeSource`, and theme changes persist through the existing transactional preference command with reducer compensation on failure. |
| ADR-0017 | Accepted; installer/updater deferred | Windows x64 releases use the host-local, untrimmed, self-contained single-file profile. The product-facing executable is `GravelReview-win-x64.exe`, while internal `IncidentReview.Host.Wpf` identity is preserved. The GitHub release asset is a ZIP containing that executable and the external reviewed iRacing notice. |
| ADR-0018 | Accepted; live acceptance pending | An application session is an iRacing event container keyed durably by positive `SubSessionID` alone, or provisionally by one logical connection. SDK `SessionNum` remains the per-incident heat/replay coordinate. Heat changes retain the list and establish new participant epochs without incidents; historical v1 per-heat rows are not heuristically merged. |

## 23. Open questions requiring evidence or product decisions

1. In current real iRacing builds, which eligible `CurDriverIncidentCount` and legacy `TeamIncidentCount` values are transmitted in solo, team, hosted, admin, and broadcaster contexts with `Max Cars = 63`, and is counter-field presence stable for an event?
2. How much delay exists between the physical event and incident-counter update, and should the marker time be refined from a telemetry ring buffer?
3. Which preferred camera and hidden playback/pause compatibility defaults should ship, and when may the legacy configured-lead-in review overload be retired?
4. Do live current builds validate the ten-second per-stage timeout and 250-millisecond seek tolerance across short and long replay searches?
5. What is the minimum supported Windows build, and does its serviced `winsqlite3.dll` pass the existing startup/schema/migration/transaction suite and release acceptance matrix?
6. Which SQLite pragmas provide the desired durability/concurrency balance on supported Windows filesystems?
7. What retention, deletion, backup, and database-corruption recovery experience should the UI provide?
8. Should classification categories be fixed, user-configurable, or versioned?
9. What installer and update mechanism, if any, should complement the accepted self-contained Windows x64 ZIP release?
10. What real-iRacing scenarios and versions form the first release acceptance matrix?
11. Do live samples confirm that the narrow repository-owned `WeekendInfo:SubSessionID` extractor remains sufficient, or does permitted real-world evidence justify a separately reviewed YAML dependency?
12. Does `SubSessionID` alone span all intended heats while separating unrelated events, and can one league night legitimately cross several subsessions?
13. Does the official `CamCameraState.IsSessionScreen` bit unambiguously identify paused replay across every supported live, offline, team, and hosted session state?
14. Which persistent Release diagnostic sink and rotation policy meet support needs without adding an unjustified logging package?

Open questions are resolved with experiments, fixtures, or ADRs—not assumptions embedded silently in implementation code.

## 24. References

- Richard Hipp, [*Reliability Lessons From SQLite* — SSW 2026](https://www.youtube.com/watch?v=V_qzqY1bb7I)
- Replicant, [*Simpler, more testable UIs with pure functions and data*](https://replicant.fun/) — architectural inspiration for top-down immutable UI state only; GravelReview does not depend on Clojure, ClojureScript, or Replicant
- [GravelReview WPF theme design brief](https://chatgpt.com/s/cx_6a9d90b776808191af847f296ebcbe0d) — product-design objectives adapted to this repository's immutable-state, interface, and persistence boundaries
- iRacing Support, [distinction between the local simulator SDK and remote Data API](https://support.iracing.com/support/solutions/articles/31000177790-oauth-client-credentials)
- iRacing, [official member SDK discussion and distribution](https://forums.iracing.com/discussion/62/iracing-sdk/p1)
- Repository evidence gate, [`docs/iracing-sdk-baseline.md`](docs/iracing-sdk-baseline.md)
- Microsoft, [.NET Generic Host](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- Microsoft, [.NET dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines)
- Microsoft, [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)
- Microsoft, [`Directory.Build.props` and `Directory.Build.targets`](https://learn.microsoft.com/visualstudio/msbuild/customize-by-directory)
- Microsoft, [custom SQLite provider/bundle choices](https://learn.microsoft.com/dotnet/standard/data/sqlite/custom-versions)
- Microsoft, [SQLite asynchronous limitations](https://learn.microsoft.com/dotnet/standard/data/sqlite/async)
- Microsoft, [NuGet secure supply-chain guidance](https://learn.microsoft.com/nuget/concepts/security-best-practices)
- Microsoft, [.NET 10 `.slnx` default](https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default)
- Microsoft, [Roslyn analyzer guidance](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
- Microsoft, [Microsoft Testing Platform code coverage](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-code-coverage)
- Dapper, [official parameterized-query guidance](https://github.com/DapperLib/Dapper/blob/main/Readme.md#parameterized-queries)
- DbUp, [official documentation](https://dbup.readthedocs.io/)
- DbUp, [transaction strategies](https://dbup.readthedocs.io/en/latest/more-info/transactions/)
