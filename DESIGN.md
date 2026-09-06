# iRacing Incident Review — System Design

- **Status:** Accepted implementation baseline; implementation in progress
- **Document version:** 1.1
- **Last updated:** 2026-09-06
- **Target platform:** Windows x64
- **Target runtime:** .NET 10 LTS / C# 14

This document is the architectural contract for the iRacing Incident Review application. It records the product goal, project boundaries, public contracts, storage and transaction model, error model, startup model, dependency policy, and testing philosophy agreed before implementation.

The words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative. A departure from a MUST or MUST NOT requires an architecture decision record (ADR), accompanying tests, and explicit review.

## 1. Executive summary

The application is a local Windows companion for iRacing. It observes live iRacing telemetry, records incident markers, presents them in a review UI, and lets the user select an incident to seek the iRacing replay to the relevant session time.

The system will be built as independent .NET libraries connected through explicit interfaces. Infrastructure details—including iRacing shared memory, Windows replay messages, SQLite, Dapper, DbUp, WPF, and any future server protocol—must remain inside their owning assemblies. The WPF host is the sole composition root that selects concrete implementations.

SQLite is the initial durable store. Every application mutation is an immutable `IStore` command with idempotent committed effects whose implementation owns one transaction; application code never receives a database connection or transaction. Dapper performs parameterized SQL mapping, and DbUp applies immutable, ordered schema migrations before normal startup.

Expected failures cross boundaries through the shared `Result` and `Result<T>` types with stable error codes. Exceptions are retained for cancellation, programmer defects, and broken invariants; provider exceptions are translated and logged at infrastructure boundaries.

Testing follows the philosophy Richard Hipp described in *Reliability Lessons From SQLite* at SSW 2026: design for testability from the beginning, assume untested behavior does not work, test through the same public interfaces used in production, inject faults deterministically, exercise boundary conditions and independent decisions, verify the tests themselves, and test the actual Release deliverable.

## 2. Product definition

### 2.1 Primary user outcome

After completing or pausing an iRacing session, a driver can see the incidents observed during that session, select one, and have iRacing move its replay to a configurable lead-in before the event.

### 2.2 Primary workflow

1. The desktop application starts and validates its configuration.
2. DbUp migrates the local SQLite database to the required schema.
3. The application waits for iRacing telemetry.
4. When iRacing connects, the application identifies the active session and local driver/team.
5. The incident detector observes changes to the applicable incident counter.
6. A counter increase creates a durable incident record containing its replay position and useful session context.
7. The UI updates to show the incident.
8. The user exits the car or otherwise enters a state in which replay commands are accepted.
9. The user selects an incident and chooses Review.
10. The application asks the replay controller to seek to the incident time minus the configured lead-in.
11. The user may classify the incident, add notes, or dismiss it. Export is a post-MVP extension.

### 2.3 Goals

- Detect and persist incident-count increases for the local driver/team.
- Preserve the iRacing session number and session time required for exact replay seeking.
- Present a fast, accessible Windows UI with connection, session, and replay state.
- Seek iRacing replay through the official local SDK broadcast mechanism.
- Preserve session and incident records across application restarts.
- Make every behavioral dependency replaceable through a focused interface.
- Allow a future remote or synchronized store without changing application use cases.
- Preserve an extension point for a future JSON export without coupling JSON to storage or domain models.
- Make boundary violations, nontransactional writes, and unsafe SQL fail build or CI checks.
- Treat testability and fault injection as product design requirements.

### 2.4 Non-goals for the first release

- Assigning fault or blame to drivers.
- Automatically detecting every contact between every car in the field.
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
| IR-SES-001 | The application creates or resumes the correct local session record when telemetry identifies a session. |
| IR-SES-002 | Valid durable simulator identity evidence resolves to the same application session across reconnect/restart; ambiguous evidence is never heuristically merged. |
| IR-INC-001 | A positive increase in the applicable incident counter records one incident marker. |
| IR-INC-002 | The marker records both the new total and the positive incident-point delta. |
| IR-INC-003 | A counter decrease, session transition, or reconnect is handled as a state transition and never as a negative incident. |
| IR-INC-004 | Duplicate or repeated telemetry samples do not create duplicate incident records. |
| IR-INC-005 | Detector progress and its incident record are persisted atomically so failure, retry, or restart cannot silently lose or duplicate an incident. |
| IR-RPY-001 | A recorded incident contains a simulator-neutral replay position with session number and session time. |
| IR-RPY-002 | Reviewing an incident seeks to the configured lead-in before its recorded time, clamped to zero. |
| IR-RPY-003 | Replay actions unavailable in the current iRacing state are disabled or return a clear structured failure. |
| IR-STR-001 | Sessions and incidents survive application restart. |
| IR-STR-002 | Every application data mutation executes inside a transaction. |
| IR-STR-003 | A failed multi-step write leaves no partial application state. |
| IR-STR-004 | Released schema migrations are applied once and are never edited in place. |
| IR-STR-005 | Re-executing an identical command with the same operation identifier can produce at most one committed effect; a transient pre-commit failure may later succeed. |
| IR-STR-006 | An indeterminate commit can be reconciled by operation identifier before retry. |
| IR-HIS-001 | The user can list past sessions and open the incidents belonging to a selected session after restart. |
| IR-SET-001 | Mutable durable user preferences, including replay lead-in and playback choices, are stored through `IStore`. |
| IR-UI-001 | The UI displays connection state, active session, and incidents in chronological order. |
| IR-UI-002 | The UI can show safe, actionable messages derived from structured errors. |

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

Every item below is a separate SDK-style project with its own `.csproj`.

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

  # Deferred until needed
  IncidentReview.Export.Contracts/
  IncidentReview.Export.Json/
  IncidentReview.Store.Remote/
  IncidentReview.Store.Sync/

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

requirements/
  requirements.md
  release-checklist.md

test-assets/
  telemetry/
  databases/
  replay-commands/

eng/
  ArchitecturePolicy.props
  build.ps1
  test.ps1
  verify.ps1
```

### 6.1 Project responsibilities

| Project | Responsibility | Public surface |
|---|---|---|
| `Results` | Shared success/failure primitives | `Result`, `Result<T>`, `Error`, `ErrorKind` |
| `Domain` | Simulator-neutral identities, values, incident rules, and invariants | Domain values and pure services |
| `Store.Contracts` | Store capabilities expressed as typed queries, atomic commands, and host-only initialization | `IStore`, `IStoreInitializer`, store queries, store commands |
| `Telemetry.Contracts` | Stream of simulator-neutral telemetry observations | `ITelemetrySource`, telemetry records |
| `Replay.Contracts` | Replay capabilities in application vocabulary | `IReplayController`, replay records |
| `Application.Contracts` | Use cases, runtime lifecycle, and UI-facing state/events | Application service/lifecycle interfaces and immutable models |
| `Application` | Use-case orchestration and incident detection | Registration module; internal implementations |
| `Store.Sqlite` | Dapper queries, SQLite mappings, transactions, DbUp migrations | Registration/options plus a narrowly named testing registration surface |
| `Iracing` | SDK shared-memory reader, session-info translation, replay broadcasts | Registration/options plus a narrowly named testing registration surface |
| `Desktop.Wpf` | Views, view models, UI mapping, dispatcher interaction | Registration module and WPF application surface |
| `Host.Wpf` | Executable, composition root, startup and shutdown | Process entry point |
| `Analyzers` | Compile-time enforcement that needs semantic source analysis | Roslyn diagnostics only; no runtime API |
| `Verification` | Traceability, dependency, migration-manifest, artifact, and release-evidence checks | Repository CLI only; never shipped |
| `Iracing.ProtocolSimulator` | External shared-memory/event/window-message simulator for packaged tests | Test executable only; never shipped |

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
6. Repository analyzers reject known escape hatches: service location, nested `BuildServiceProvider`, interpolated/concatenated SQL, and Dapper mutations without an explicit transaction.
7. `IncidentReview.Verification` checks requirement/test links, migration manifests, the resolved dependency graph, release hashes, and retained evidence.
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
    public IncidentCounter IncidentCounter { get; }
    public LapNumber? Lap { get; }
    public LapDistance? LapDistance { get; }
    public OnTrackState OnTrackState { get; }
    public UtcInstant ObservedAt { get; }

    public static Result<TelemetrySample> TryCreate(/* untrusted values */);
}
```

The event types have controlled constructors/factories and carry validated values/reasons omitted from the conceptual sketch. `TelemetrySample` is intentionally simulator-neutral. Raw variable names such as `PlayerCarMyIncidentCount`, memory offsets, YAML nodes, and SDK buffers remain internal to `IncidentReview.Iracing`.

Connection, disconnection, transient unavailability, and samples are observable without using exceptions as routine stream messages. The adapter owns reconnection and emits state transitions in order. Cancellation ends enumeration with `OperationCanceledException`; an unexpected defect faults the stream and reaches the application runtime's top-level owner.

### 7.2 Replay

```csharp
public interface IReplayController
{
    Task<Result> SeekAsync(
        ReplayPosition position,
        CancellationToken cancellationToken);

    Task<Result> SetPlaybackAsync(
        ReplayPlayback playback,
        CancellationToken cancellationToken);
}

// Owned by IncidentReview.Domain and created with ReplayPosition.TryCreate(...).
public sealed class ReplayPosition
{
    public SessionNumber SessionNumber { get; }
    public SessionTime SessionTime { get; }
}
```

`ReplayPosition` has one authoritative definition in `IncidentReview.Domain`, because both telemetry and replay contracts consume it. The replay contract speaks in intent and does not expose iRacing broadcast enums or Windows message packing.

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

Queries and commands are immutable, capability-focused records declared in `Store.Contracts`. Examples include `ListSessions`, `GetSession`, `GetSessionBySimulatorKey`, `GetIncidents`, `GetPreferences`, `GetOperationOutcome`, `EnsureSession`, `EstablishIncidentCheckpoint`, `RecordDetectedIncident`, `AnnotateIncident`, and `UpdatePreferences`. They contain application/domain values only—never functions or provider objects.

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

public interface IApplicationRuntime
{
    Task Completion { get; }
    Task<Result> StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`ReviewUpdate` is an application-owned notification that tells the UI which immutable state to refresh; it does not expose telemetry frames, store commands, threads, dispatchers, or infrastructure events. `IApplicationRuntime` gives the composition root an explicit lifecycle for the telemetry/processing loop without exposing its implementation. `Completion` remains incomplete while the runtime is healthy, completes normally only after requested stop, and faults on an unexpected worker defect; an unrequested normal completion is also treated as a runtime failure. `StopAsync` is idempotent, cancels and awaits all owned workers, always observes their completion, and rethrows a worker fault that occurred before expected stop rather than converting it into successful shutdown. View models do not coordinate stores and replay controllers themselves. They invoke application use cases and marshal updates through a presentation-owned dispatcher abstraction.

## 8. Domain model

Initial concepts:

- `SessionIdentity`: application-owned stable identifier, generated before the session-creation command.
- `SimulatorSessionDescriptor`: validated simulator-owned identity evidence, session number/mode, and whether that evidence is durable or connection-scoped.
- `IncidentId`: client-generated stable identity, preferably UUIDv7.
- `Incident`: immutable core record plus controlled annotation/status transitions.
- `ReplayPosition`: session number and session-relative time.
- `IncidentPoints`: non-negative total and positive delta.
- `IncidentClassification`: optional user classification.
- `IncidentReviewStatus`: pending, reviewed, or dismissed.
- `IncidentAnnotation`: user notes and classification changes.
- `UserPreferences`: validated durable review behavior such as lead-in, playback, and optional camera preference.

Domain values validate themselves at creation through private constructors and `TryCreate`/factory methods returning `Result<T>`. This applies at every untrusted boundary: SDK decoding, database mapping, command-line/configuration input, and future wire decoding. Invalid session times, negative counters, NaN/out-of-range percentages, non-UTC instants, invalid lap numbers, and empty identities do not circulate through the system. Optional SDK data is represented explicitly—such as `LapNumber?` and `LapDistance?`—rather than by magic sentinel values.

The domain does not know how telemetry arrived, how replay commands are transmitted, how records are stored, or how views render them.

### 8.1 Session identity resolution

The iRacing adapter cannot manufacture an application `SessionIdentity` because it has no store dependency. It instead emits a `SimulatorSessionDescriptor`. For the pinned SDK, the adapter builds a durable opaque key only from the officially documented session-instance evidence plus replay session number after all fields validate. The exact raw field mapping is recorded with the SDK fixture; raw field names never become a public contract.

The application resolves the descriptor before incident detection: query `GetSessionBySimulatorKey`; if found, reuse its `SessionIdentity`; otherwise generate one UUIDv7 and execute `EnsureSession` with the proposed identity and descriptor. A concurrent unique-key conflict is resolved by querying the winner, never by creating a second logical session. SQLite enforces a partial unique key over `(simulator, simulator_session_key)` when the durable key is non-null.

| Observation | Session identity action | Detector action |
|---|---|---|
| Repeated sample with the same validated durable simulator key and session number | Reuse current identity | Continue from current/persisted checkpoint |
| Disconnect/reconnect in the same app run with the same durable key | Reuse identity | Reload/confirm checkpoint; do not synthesize an incident |
| App restart while iRacing reports the same durable key | Resolve existing identity from SQLite | Resume from persisted checkpoint |
| Replay session number changes | Resolve/create a new identity even when the enclosing subsession is unchanged | Finalize prior context and establish a new baseline |
| Durable subsession/session-instance evidence changes | Resolve/create a new identity | Establish a new baseline |
| Required identity evidence is missing, malformed, or contradictory | Create a clearly marked connection-scoped provisional identity; never guess a cross-restart match | Establish a baseline and surface degraded identity state |
| Reconnect with only provisional evidence | Create a new provisional identity unless future verified evidence proves continuity | Establish a baseline; do not merge records heuristically |
| Replay mode matches an existing durable key | Reuse identity for review only | Pause incident detection; replay seeking remains available |
| Standalone/unmatched replay playback | Use an ephemeral replay context | Do not persist inferred incidents or create a durable session automatically |

Changing track/car display metadata does not change identity. Once a provisional session later gains trustworthy durable evidence, merging or re-keying records is not automatic in the first release; that workflow requires a separately tested command so uniqueness and user history cannot be corrupted silently.

## 9. Incident detection

After Section 8.1 resolves the simulator descriptor, `Application` maps the telemetry sample to a domain `IncidentObservation` carrying the application `SessionIdentity`. The MVP detector observes that ordered stream and the incident counter relevant to the local player/team.

```text
first sample in session
    → establish baseline; do not emit incident

same session and current count == prior count
    → no change

same session and current count > prior count
    → emit one marker with delta = current - prior

current count < prior count
    → treat as reset/discontinuity; establish new baseline

session identity changes
    → finalize old detector state; establish baseline for new session
```

The marker time is initially the first observed sample containing the increased counter. A small in-memory telemetry ring buffer MAY later refine the event context, but that refinement must not change the persistence or replay interfaces.

The detector MUST be deterministic and pure with respect to an ordered input sequence. Connection management, persistence, and UI notification surround it but are not part of its decision logic. Its durable progress follows this protocol:

1. On first observation or restart, load the session's persisted `IncidentCheckpoint` before evaluating new samples.
2. For a new baseline or counter reset, create one `EstablishIncidentCheckpoint` command. A reset advances a monotonically increasing counter epoch.
3. For an increase, generate the `IncidentId` and `OperationId` once, then create one `RecordDetectedIncident` command containing the expected checkpoint, the complete incident, and the next checkpoint.
4. The store compares the expected checkpoint, inserts the incident, updates the checkpoint, and records the completed operation in one transaction.
5. The in-memory detector state advances only after success or after reconciliation proves an indeterminate commit succeeded. Until then, the same command remains pending; later telemetry cannot silently skip it.
6. On restart or retry, the operation identifier and unique incident key make the transition idempotent. A genuine checkpoint mismatch returns a conflict for explicit reconciliation rather than guessing.

The deterministic incident uniqueness key is `(session_id, counter_epoch, incident_points_total)`. A cumulative jump creates one marker with the complete positive delta; the app does not invent event timing for unobserved intermediate counter values.

Known limitation: the local SDK signals available and their semantics must be validated against actual iRacing session types. The first release must not claim reliable detection of every other driver's incident or assign causality.

## 10. iRacing integration

`IncidentReview.Iracing` is the only project that understands the local SDK protocol.

The implementation source of truth is the SDK/header distribution obtained from iRacing's official member/support channel. The exact upstream artifact version, cryptographic hash, source location, and applicable license/redistribution terms are recorded with any vendored definitions or generated bindings. Public GitHub mirrors may help discovery, but they are not authoritative and cannot silently update the protocol baseline.

The initial adapter is repository-owned and uses .NET/Windows interop directly; it does not add an unreviewed iRacing wrapper package. Named memory mappings/events use BCL primitives where they match the protocol. Required User32 calls use source-generated `LibraryImport` declarations with fixed-width validated packing. `unsafe` code, if unavoidable for copied frame decoding, is enabled only in `IncidentReview.Iracing`, kept in small reviewed methods, and covered by malformed-buffer and bounds tests.

Responsibilities:

- Open and monitor the iRacing shared-memory mapping and synchronization event.
- Copy telemetry frames promptly before processing to avoid holding SDK-owned buffers.
- Parse only the session metadata needed by the application.
- Translate raw telemetry into immutable `TelemetrySample` values.
- Detect connection, disconnection, replay, and on-track state.
- Encode replay commands, including search by session number and session time in milliseconds.
- Translate expected integration failures into stable `Result` errors.

Frame reads follow the official buffer-generation/tick protocol: copy the selected frame into app-owned memory, prove the header did not change during the copy, and retry a bounded number of times on a torn read. Every count, offset, element size, index, and string length is range-checked before slicing. Unknown variables, SDK-version drift, and malformed session information yield typed unavailability/errors rather than unchecked memory access.

iRacing session information is YAML-like text, not JSON. The adapter exposes only the few validated fields required by the application. A narrowly scoped decoder may be repository-owned and fixture/fuzz tested against the recorded official samples; it must not pretend to be a general YAML parser or use substring/line-splitting that ignores escaping and structure. If official fixtures demonstrate that a conforming YAML library is necessary, that library requires its own dependency ADR and supply-chain review before use.

The telemetry reader and downstream processing are decoupled by a bounded channel or equivalent controlled buffer. The policy must be explicitly tested. Dropping arbitrary counter-transition observations is forbidden; the cumulative counter permits coalescing only when the resulting positive delta is preserved.

Replay review behavior:

1. Load the incident through an application use case.
2. Subtract the configured lead-in and clamp to zero.
3. Confirm telemetry indicates a state in which iRacing can accept replay control.
4. Ask `IReplayController` to seek to the target session/time.
5. Optionally select the local car/camera and pause or start slow playback according to user settings.
6. Return a structured failure if iRacing is disconnected or does not expose a usable replay state.

`ReviewIncidentAsync` does not automatically mutate `review_status`; handing a replay command to Windows and committing SQLite cannot form one atomic transaction. Marking reviewed/dismissed or changing notes/classification is a separate explicit store command initiated by the UI, so the user is never told a cross-system action was atomic when it was not.

Replay command registration, command identifiers, field widths, signedness, and parameter packing are derived from the pinned official header and protected by golden-vector tests. SDK broadcast commands are fire-and-forget at the operating-system boundary. A successful `SeekAsync` means the command was valid and handed to the Windows broadcast mechanism; it does not falsely claim that iRacing applied it. Where telemetry provides corresponding state, the adapter SHOULD observe it to offer a separately defined confirmation state.

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

- SQLite permits one writer at a time. The first implementation uses one bounded, single-consumer executor for all queries and commands, keeping transactions short and behavior deterministic.
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

### 11.3 Initial logical schema

The physical schema will be introduced through migrations, but its initial logical records are:

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
  session_id                 text primary key, foreign key
  counter_epoch              integer
  last_incident_points_total integer
  last_replay_session_number integer
  last_replay_session_time_ms integer
  updated_at_utc_ms          integer

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
  updated_at_utc_ms          integer
```

Identifiers use canonical lowercase UUID text initially; timestamps use UTC Unix milliseconds in `INTEGER` columns; booleans use constrained `0`/`1`; enums use explicitly assigned stable integer codes. Each command has a stable `command_kind` and positive schema version. The operation fingerprint is SHA-256 over that kind/version plus a handler-owned canonical, length-delimited binary encoding of the persisted fields—it does not introduce JSON into the store path. Encoding, field order, normalization, and version are release contracts protected by golden vectors. Existing command versions remain readable/reconcilable for every supported database upgrade; changing their encoding in place is forbidden, and a new shape receives a new version. `Incident` has a unique constraint on `(session_id, counter_epoch, incident_points_total)` in addition to its primary key. Check constraints enforce non-negative counters/times, positive deltas, valid percentages, and known status/preference values. Foreign keys specify deliberate delete behavior rather than relying on provider defaults.

Client-generated identifiers, operation identifiers, and explicit timestamps preserve a credible path to remote synchronization. Application records remain separate from SQLite row models and future wire DTOs. Mutable user preferences are durable application state and go through `IStore`; host/deployment settings such as database path, logging level, and diagnostic switches remain validated startup configuration and are not mixed with user preferences.

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

WPF requires an STA entry thread. The host executable owns that constraint. Container validation, store initialization, migration, and durable-state loading finish before entering the WPF dispatcher. If asynchronous initialization must be synchronously joined to preserve STA startup, that join is confined to the composition root before a UI synchronization context exists.

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
    try
    {
        return RunOnStaThread(args);
    }
    catch (OperationCanceledException) when (ShutdownWasRequested())
    {
        return ExitCodes.Success;
    }
    catch (Exception exception)
    {
        return ReportFatalStartupOrRuntimeFailure(exception);
    }
}

private static int RunOnStaThread(string[] args)
{
    using var singleInstance = SingleInstanceGuard.TryAcquire();
    if (!singleInstance.IsAcquired) return ExitCodes.AlreadyRunning;

    IHost host = BuildHost(args); // ValidateOnBuild + ValidateScopes
    IApplicationRuntime? runtime = null;
    var hostStarted = false;
    var shutdownCompleted = false;
    ExceptionDispatchInfo? primaryFailure = null;
    try
    {
        host.StartAsync(ProcessShutdown.Token).GetAwaiter().GetResult();
        // ValidateOnStart has run; no application worker has started.
        hostStarted = true;

        using (var startup = CreateLinkedStartupTimeout(ProcessShutdown.Token))
        {
            var bootstrap = host.Services.GetRequiredService<BootstrapCoordinator>();
            RequireSuccess(bootstrap.InitializeAsync(startup.Token).GetAwaiter().GetResult());
            // create/validate directories → initialize SQLitePCL once → migrate →
            // validate schema/capabilities and open the store execution gate

            runtime = host.Services.GetRequiredService<IApplicationRuntime>();
            RequireSuccess(runtime.StartAsync(startup.Token).GetAwaiter().GetResult());
            // load preferences/checkpoints/session state, then start telemetry processing
        }

        var app = host.Services.GetRequiredService<App>();
        using var supervisor = RuntimeSupervisor.Attach(runtime, app.Dispatcher);
        var uiExitCode = app.Run(host.Services.GetRequiredService<MainWindow>());

        supervisor.BeginExpectedStop();
        ShutdownCoordinator.StopObserveAndDispose(runtime, host, hostStarted);
        // StopAsync and Completion are both awaited; any pre-stop worker fault is rethrown.
        shutdownCompleted = true;
        return uiExitCode;
    }
    catch (Exception exception)
    {
        primaryFailure = ExceptionDispatchInfo.Capture(exception);
        throw;
    }
    finally
    {
        if (!shutdownCompleted)
        {
            var cleanupFailure = ShutdownCoordinator.TryStopObserveAndDispose(
                runtime, host, hostStarted);

            if (primaryFailure is null)
                cleanupFailure?.Throw();
            else
                LogSecondaryCleanupFailure(primaryFailure, cleanupFailure);
        }
    }
}
```

The code is normative pseudocode: helper details may change, but the ordering and ownership may not. `ProcessShutdown.Token` is owned by the process lifetime; the separate linked startup token adds a validated finite startup timeout and is disposed immediately after startup. The single-instance guard is acquired before any database open or migration. `Host.StartAsync` may run framework lifetime/validation components only; no application service may observe telemetry, open the database, or mutate state during that call. `BootstrapCoordinator` owns all ordered initialization and is the only gate to `IApplicationRuntime.StartAsync`.

`RuntimeSupervisor` observes `IApplicationRuntime.Completion`. An unexpected fault or unrequested completion posts shutdown to the WPF dispatcher, causing `App.Run` to return. On every normal return from `App.Run`, the coordinator first marks shutdown as expected, then calls and awaits `StopAsync`, awaits/inspects `Completion`, stops the host, and disposes it before returning the UI exit code. Thus a worker fault in the race before or during stop is still surfaced through the outer fatal boundary.

On an exceptional path, `ExceptionDispatchInfo` preserves the primary stack. Cleanup still attempts runtime stop/completion observation, host stop, and disposal. Cleanup diagnostics are attached/logged without replacing an existing primary exception; when there is no primary exception, cleanup/worker failure becomes the thrown failure. No background `Task` is fire-and-forgotten or left unobserved.

Each expected startup step returns `Result`; `RequireSuccess` converts a failed startup result into one host-owned fatal-startup path without discarding its stable code. SQLitePCL initialization occurs before any `SqliteConnection` or DbUp use and is verified in the packaged-artifact smoke test. Failure stops later phases, logs diagnostics, presents one safe fatal-startup message when possible, and shuts the host down cleanly. Unexpected exceptions are caught only at this process boundary.

Shutdown reverses ownership:

```text
disable/close UI
    → cancel telemetry processing
    → await application runtime stop
    → finish or roll back active store operations
    → stop Generic Host
    → dispose host
```

JSON configuration is not used in the first release. Deployment settings use code defaults, prefixed environment variables, and command-line arguments. Durable user preferences belong to SQLite and are read through application use cases. Adding a JSON configuration provider later requires an explicit use case and dependency/configuration review; `System.Text.Json` remains reserved initially for the deferred export feature.

## 14. UI design boundary

The first UI should contain:

- global iRacing connection indicator;
- active session/track/car summary;
- chronological incident list showing time, lap, delta, total, and review status;
- Review action for the selected incident;
- optional lead-in control with a safe default;
- notes, classification, reviewed, and dismissed controls;
- nonblocking status/error surface;
- explicit unavailable state when replay control cannot be used.

View models depend only on `Application.Contracts`, domain values intended for presentation, `Results`, and presentation-owned abstractions such as a dispatcher or dialog service. They do not query the store, read telemetry, or encode replay commands.

UI event handlers contain presentation mechanics only. Use-case decisions live in `Application` and pure rules live in `Domain`.

The initial presentation layer uses WPF/BCL `INotifyPropertyChanged`, `ICommand`, data binding, collection views, and accessibility automation peers directly. No MVVM framework, mediator, event-bus, or reactive package is added before a concrete requirement justifies it. Long-running actions expose busy/cancellation state, disable duplicate commands, and never block the dispatcher; UI collections are updated only on the dispatcher through the presentation abstraction.

User-visible state and interactive elements have stable, documented Windows UI Automation names/`AutomationId` values. Keyboard navigation, focus order, screen-reader names, scaling, high contrast, and selection/review behavior are acceptance-tested; automation identifiers are contracts and do not depend on localized display text.

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

### 15.3 Requirements traceability

Every externally observable requirement and critical invariant receives a stable ID. Its record includes observable acceptance criteria, failure behavior, boundaries, criticality, owning contract, and implementation/verification status. MSTest cases attach IDs with `TestProperty` or an equivalent repository-owned attribute. CI produces or verifies a matrix showing:

- requirements with no tests;
- tests with no requirement/invariant rationale;
- requirements whose tests did not execute;
- release-checklist items and evidence.

Traceability does not replace assertions. A test must fail for a meaningful violation of its requirement.

### 15.4 Public-interface and contract testing

Production behavior is tested through the same contracts used by consumers.

- Every `IStore` implementation runs the same `Store.ContractTests` suite.
- The SQLite suite additionally validates observable on-disk behavior against a real temporary database.
- Telemetry implementations run common connect/disconnect/cancellation/ordering contracts where applicable.
- Replay implementations run command and state contracts.
- Application identity tests exercise every row in the Section 8.1 table, including restart against a real store and replay-only suppression.
- Tests do not use reflection to invoke private business logic; reflection is reserved for architecture inspection.
- Implementation-specific tests may reference their implementation project but must not teach production consumers to bypass its contract.

### 15.5 Deterministic test implementations

The testing toolkit includes first-class implementations rather than ad hoc mocks:

- `ScriptedTelemetrySource`;
- `RecordingReplayController`;
- `InMemoryStore`;
- `ManualTimeProvider` built on the BCL `TimeProvider` abstraction;
- `DeterministicIdGenerator`;
- deterministic scheduler/channel controls where concurrency matters;
- `DeterministicFaultInjector`.

These reusable fakes and recorders live in `IncidentReview.TestKit`. The test kit depends on public contracts and does not gain privileged access to production internals.

Randomized, property, and fuzz-style tests record the seed and minimized input required to reproduce a failure. Wall-clock time and nondeterministic delays are not used as assertions.

Lifecycle tests use deterministic barriers to fault or complete a runtime worker before `App.Run` returns, between UI return and expected-stop marking, and during `StopAsync`. They prove the dispatcher is asked to shut down, `Completion` is observed, primary exceptions retain their stack, secondary cleanup failures are preserved diagnostically, and no successful process exit hides a worker fault.

### 15.6 Fault injection

Infrastructure is designed with narrow fault probes at meaningful state transitions, for example:

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

Each adapter defines a deliberately public controller/facade and registration overload in an implementation-specific `.Testing` namespace within the same Release assembly. Internal runtime services know only an internal fault-probe contract outside that namespace. The one registration bridge in the owning assembly adapts the public controller to that internal contract. Normal production registration unconditionally supplies an internal no-op implementation and exposes no configuration, environment variable, command-line, reflection, or ordinary DI override that can replace it. Only implementation/reliability test projects may call the explicit `Add...ForTesting(controller)` overload. `eng/ArchitecturePolicy.props`, source analyzers, and architecture tests allow the single owning bridge but reject `.Testing` references from every other production source/assembly. Concrete adapters and probe invocation sites remain internal. This narrow facade is a documented exception to the registration/options-only public-surface rule, not `InternalsVisibleTo`.

Tests choose the exact probe and occurrence that fails. For every critical SQLite/I/O fault class, the reliability suite MUST sweep occurrences: fail occurrence 1 and verify, then occurrence 2 and verify, continuing until one complete operation reaches no later eligible fault point. Test-control code exercises the same compiled production paths and must not replace the behavior being tested.

These coordination probes verify our state machine around provider calls; they do not claim to reproduce a native filesystem or SQLite VFS failure inside `fsync`/commit. Real-provider tests separately cause supported failures through locks, read-only files, `max_page_count`/full conditions, invalid paths, corruption fixtures, and abrupt child-process termination.

The internal commit-boundary wrapper has two test-controller outcomes for the otherwise hard-to-reproduce ambiguous branch: report indeterminate without invoking native commit, and invoke the real native commit then withhold its outcome from the transaction executor. Both return `store.commit.indeterminate` to the caller and force reconciliation on a fresh connection, covering respectively absent and present operation records. This is a state-machine simulation of an unknown outcome, not evidence of physical power-loss behavior. By contrast, a fault at `AfterConfirmedCommit` cannot change a known success into failure; it is recorded as diagnostic/test evidence and the command still returns success.

For each injected store failure, tests close and reopen the database. Failure before commit must leave the pre-transaction state; success after a confirmed commit must leave the complete post-transaction state. An indeterminate commit is covered by present/absent reconciliation and idempotent-retry tests. Partial state is never acceptable.

### 15.7 Coverage and independent conditions

Critical hand-written logic requires complete decision/branch coverage:

- domain invariants and incident detection;
- `Result` state and composition;
- application orchestration and error propagation;
- transaction commit/rollback selection;
- SQLite error translation;
- iRacing frame decoding and replay command encoding;
- startup phase success/failure decisions.
- runtime worker completion/fault supervision and WPF shutdown routing.

Compound conditions receive decision-table tests showing that each condition can independently affect the outcome. A line percentage alone is insufficient. Generated WPF code and trivial generated boilerplate are excluded transparently; exclusions cannot hide application decisions.

`Microsoft.Testing.Extensions.CodeCoverage` is the initial collector. After the locked build, CI creates an empty run-ID-specific output directory and manifest, enumerates every expected test application, and invokes Microsoft Testing Platform with `dotnet test --no-restore --no-build --coverage --coverage-output-format cobertura`. `IncidentReview.Verification` accepts only reports named in the current successful run manifest, reads every resulting Cobertura file, applies an explicit reliability-kernel assembly include list, and fails the build below complete reachable branch coverage. It unions branch identities across reports only when invocation ID, module identity, PDB identity, and the recorded binary hash match; it never scans an ambient directory or averages percentages. A missing test application/required assembly, stale or unmanifested report, mismatched binary, unsuccessful producer process, unreadable report, or branch with no executed outcome fails closed. Every source/document exclusion is reviewed. Source/IL branch coverage plus decision tables is evidence of independent-condition testing; it is not proof of machine-code MC/DC under C# async lowering, JIT compilation, or other code generation. Coverage scenarios are rerun against the uninstrumented Release artifact because instrumented code is not the deliverable.

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
- deliberately failing migration;
- repeated migration execution;
- rollback failure while preserving both primary and cleanup diagnostics;
- rejection of in-flight or committed `OperationId` reuse with a different command/payload and factory prevention of reuse after a rolled-back attempt;
- duplicate execution of an already committed identical command producing no second effect;
- a transient pre-commit failure followed by successful retry of the same immutable command;
- golden command fingerprints and same-`OperationId` reconciliation across every supported command/database version upgrade;
- snapshot-consistent aggregate queries during concurrent reads/writes;
- checkpoint conflict, persistence failure, restart, and retry without a lost or duplicate incident;
- hostile-looking values containing quotes, semicolons, comments, SQL keywords, and Unicode, stored verbatim through parameters;
- large note text and boundary numeric values.

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

Initially, curated mutations verify that important tests detect:

- reversed comparisons in incident detection;
- removed rollback or commit calls;
- ignored failed results;
- altered replay lead-in arithmetic;
- swapped session/time fields;
- removed SQL transaction arguments;
- unsafe acceptance of reset counters;
- suppressed cancellation.

A third-party mutation tool such as Stryker.NET is not added without an explicit dependency decision. Mutation results begin as diagnostic evidence. Every surviving behavior-changing mutant in the reliability kernel is a test defect; equivalent and performance-only mutants require recorded classification, with benchmark evidence for performance-only behavior.

### 15.10 Test the deliverable

CI builds the Release deliverable once, records its hashes, and tests those same bytes without recompiling between stages. The final package is installed or expanded into a clean test location and smoke-tested as shipped. Checks include process startup, migration, database creation, dependency loading, telemetry ingestion, incident display, replay invocation, and clean shutdown.

`IncidentReview.Iracing.ProtocolSimulator` is a separate test process, not an alternate application composition. It independently implements the pinned header's variable table, frame/ring-buffer tick protocol, named shared-memory mapping, synchronization event, torn-frame cases, and replay-message unpacking. It also implements the session-information memory region, declared length/offset, update counter/lifecycle, valid independently authored YAML-like fixtures for session/local-driver/team/car/track identity, and malformed/truncated/update-transition cases. It owns a hidden receiver window that records the same Windows broadcast messages iRacing would receive. The simulator MUST NOT reference `IncidentReview.Iracing`, copy its encoder/decoder helpers, or consume production-generated expected values; its oracle comes from a separately reviewed transcription plus golden vectors tied directly to the official artifact hash. Deliberate disagreement tests prove the oracle can catch swapped fields, wrong widths/signedness, stale ticks, bad session-info bounds/update handling, and bad message packing.

The packaged application therefore runs its real `Iracing` adapter, real application services, real SQLite store, and production registration against an OS-level protocol peer. A Windows UI Automation driver uses stable `AutomationId` values to wait for the incident row, select it, and invoke Review, then the simulator independently validates the received replay command. This job runs on an interactive Windows agent in the same user session and integrity level as both processes. Setup proves real iRacing is stopped and the required named objects are free; otherwise the test fails without killing or interfering with a user process. Tests isolate the app's per-user data directory through a documented deployment-path option and a temporary Windows user/profile or test directory; there is no fake adapter, injectable test registration, or user-activatable “test mode” in the shipped executable.

Identical observable scenarios run first under coverage and then against the uninstrumented packaged application; their results are compared. The hash manifest proves the package under test is the package approved for release. The simulator itself is test tooling, is dependency-inventoried, and is never included in the user package.

Real iRacing acceptance testing follows a versioned checklist with captured simulator/app versions and expected observations. It supplements automated protocol tests; it does not replace them.

### 15.11 Test tiers

| Tier | Trigger | Contents | Expected role |
|---|---|---|---|
| Fast | Every local build/change | Results, domain, application, architecture, deterministic contracts | Immediate feedback |
| Integration | Every CI change | Real SQLite, migrations, protocol fixtures, concurrency/fault cases | Merge gate |
| Reliability | Scheduled and release | Fault sweeps, fixed-budget fuzzing, randomized seeds, stress, child-process termination, repeated runs | Deep defect discovery |
| Deliverable | Release candidate | Packaged artifact, clean-machine smoke, real-iRacing checklist | Release gate |

Flaky tests are defects. A failing test is not retried until green without preserving and investigating the first failure evidence.

Every reported defect begins with a failing regression test. The repository preserves the smallest useful reproducer and records why the existing suite failed to expose it.

Nightly fuzzing targets raw SDK buffers, session metadata, telemetry event sequences, notes/Unicode, and store query boundaries for a fixed recorded time budget. Failures retain the seed and input, are minimized, and enter the permanent regression corpus.

### 15.12 Assertions and explanatory comments

Assertions are executable statements of programmer invariants, not substitutes for validating external input. External invalid data returns a defined failure; broken internal assumptions assert and, where data safety requires it, remain checked in Release. Diagnostic/invariant builds and ordinary Release builds are both tested. Concise comments explain why a non-obvious invariant or branch exists rather than merely restating the code.

## 16. Technology stack and dependency policy

### 16.1 Selected baseline

| Area | Selection | Rationale |
|---|---|---|
| Runtime/language | .NET 10 LTS / C# 14 | Current Microsoft LTS baseline installed on the development machine |
| Desktop UI | WPF | Windows-native, mature, Microsoft-supported |
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

The default is Microsoft platform/framework dependencies plus the official iRacing protocol. Dapper and DbUp are explicitly approved third-party exceptions. SQLite itself and SQLitePCLRaw are also third-party code and must be acknowledged, pinned, licensed, and audited rather than described as Microsoft-only. This stack therefore cannot truthfully be called “Microsoft-only.” The control objective is a minimal, explicit, locked, reviewed graph—not reliance on publisher identity alone.

The Roslyn analysis packages and code-coverage extension are Microsoft-published and prefix-reserved. The coverage extension is closed-source under Microsoft's free-to-use .NET library license; it satisfies an official-Microsoft-publisher policy but not an all-source-auditable policy. That distinction is recorded in the dependency inventory rather than hidden.

The proposed provider stack is `Microsoft.Data.Sqlite.Core` plus `SQLitePCLRaw.bundle_winsqlite3`. It uses Windows' `winsqlite3.dll` instead of shipping the convenience `Microsoft.Data.Sqlite` package's bundled `e_sqlite3` engine. SQLitePCLRaw remains a reviewed third-party shim. This reduces shipped native code but ties available SQLite features to the supported Windows version, so startup and acceptance tests must probe the engine version and required features.

Before the first restore is committed, the resolved graph for Dapper, DbUp SQLite support, `Microsoft.Data.Sqlite.Core`, SQLitePCLRaw, and the Windows native engine must be recorded and reviewed. `dbup-sqlite` currently trails `dbup-core` in versioning; their pinned combination requires real integration tests. Any provider change requires an ADR and must not leak beyond `Store.Sqlite`.

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

### 16.3 Candidate package ledger

These are candidate stable pins verified as current on 2026-09-05. A pin becomes approved only after locked restore, publisher/license/transitive-graph review, and the applicable compatibility tests. Versions are recorded centrally, not copied into individual projects.

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
| `SQLitePCLRaw.bundle_winsqlite3` | 2.1.11 | Proposed reviewed third-party binding exception |
| `MSTest.Sdk` | 4.4.0 | Microsoft project/test SDK |
| `Microsoft.Testing.Extensions.CodeCoverage` | 18.11.0 | Microsoft test-only extension; closed-source license reviewed separately |
| `Microsoft.CodeAnalysis.CSharp` | 5.9.0 | Microsoft build-only dependency; private assets |
| `Microsoft.CodeAnalysis.Analyzers` | 5.9.0 | Microsoft build-only dependency; private assets |

Exact pins are a reproducible starting point, not permission to update automatically or a claim that the proposed provider graph already passed integration. Every version change follows the dependency review and lock-file process.

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

`IncidentReview.Verification` is a normal repository console tool invoked by `eng/verify.ps1`. It consumes explicit machine-readable inputs and exits nonzero on policy failure; it does not scrape human prose when a schema can be used. Both tools are restored under the same source, lock, audit, and license rules as production dependencies.

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
- Use scripted telemetry, recording replay, deterministic IDs/clocks, and in-memory store.
- Verify the complete detect → store → list → review workflow without iRacing.

### Milestone 3 — iRacing adapter

- Implement and fixture-test shared-memory decoding.
- Implement replay search-by-session-time command encoding.
- Build the external protocol simulator used by packaged-deliverable tests.
- Validate connection/reconnect and simulator-state behavior.
- Run the real-iRacing acceptance checklist.

### Milestone 4 — WPF experience

- Implement status, incident list, review action, annotations, and errors.
- Add accessibility and dispatcher/lifetime tests.
- Package and smoke-test the Release deliverable.

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
- Real-iRacing acceptance checklist with Windows, iRacing, SDK, and app versions.

## 22. Initial architecture decisions

| Decision | Status | Summary |
|---|---|---|
| ADR-0001 | Accepted | Windows desktop application using .NET 10 LTS, C# 14, and WPF. |
| ADR-0002 | Proposed, pending integration verification | SQLite is the initial `IStore`; use `Microsoft.Data.Sqlite.Core` with `SQLitePCLRaw.bundle_winsqlite3`, audit/pin the full graph, and test Windows engine capabilities. |
| ADR-0003 | Accepted | Dapper performs parameterized SQL mapping; raw SQL remains private to `Store.Sqlite`. |
| ADR-0004 | Accepted | DbUp performs ordered, journaled, embedded migrations before application startup. |
| ADR-0005 | Accepted | Every application mutation is one typed command with at-most-once committed effects, executed atomically through transaction-owning `IStore.ExecuteAsync`. |
| ADR-0006 | Accepted | `Result` and `Result<T>` are shared dependency-free failure primitives. |
| ADR-0007 | Accepted | Microsoft Generic Host is the composition/lifetime mechanism; `Host.Wpf` is the sole composition root. |
| ADR-0008 | Accepted | Assemblies, root policy, analyzers, and architecture tests enforce contracts; runtime implementation classes remain internal, with only approved `.Testing` registration seams public. |
| ADR-0009 | Accepted | Testing follows Hipp's reliability philosophy, including designed-in fault injection and deliverable testing. |
| ADR-0010 | Deferred | JSON is an optional export format, not an internal storage or communication requirement. |
| ADR-0011 | Accepted | Codex creates small, green Conventional Commits for completed work; push/merge/tag/history rewriting remain separately authorized actions. |

## 23. Open questions requiring evidence or product decisions

1. Which exact iRacing incident counters are reliable in solo, team, hosted, and replay sessions?
2. How much delay exists between the physical event and incident-counter update, and should the marker time be refined from a telemetry ring buffer?
3. Which replay camera, playback speed, and default lead-in provide the best review experience?
4. What telemetry state most reliably confirms that replay commands are currently accepted?
5. Does the pinned `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_winsqlite3` + `dbup-sqlite` graph pass migration, transaction, engine-capability, and deliverable tests on the minimum supported Windows build?
6. Which SQLite pragmas provide the desired durability/concurrency balance on supported Windows filesystems?
7. What retention, deletion, backup, and database-corruption recovery experience should the UI provide?
8. Should classification categories be fixed, user-configurable, or versioned?
9. What exact packaged format and update mechanism will be used?
10. What real-iRacing scenarios and versions form the first release acceptance matrix?
11. What is the minimum supported Windows build, and which `winsqlite3` version/features does that build guarantee?
12. Do the official session-info fixtures permit a small correct bounded extractor, or is a separately reviewed YAML dependency required?
13. Which exact official SDK fields form the durable simulator-session key in every supported live/team/offline scenario, and which fixtures prove the identity table in Section 8.1?
14. Which persistent Release diagnostic sink and rotation policy meet support needs without adding an unjustified logging package?

Open questions are resolved with experiments, fixtures, or ADRs—not assumptions embedded silently in implementation code.

## 24. References

- Richard Hipp, [*Reliability Lessons From SQLite* — SSW 2026](https://www.youtube.com/watch?v=V_qzqY1bb7I)
- iRacing Support, [distinction between the local simulator SDK and remote Data API](https://support.iracing.com/support/solutions/articles/31000177790-oauth-client-credentials)
- Non-authoritative discovery mirror, [`irsdk_defines.h`](https://github.com/vipoo/irsdk/blob/master/irsdk_defines.h); implementation must use a recorded official iRacing SDK artifact
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
