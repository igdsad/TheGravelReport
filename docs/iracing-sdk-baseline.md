# Official iRacing SDK 1.20 baseline

- **Status:** pinned and implemented; current-build live acceptance pending
- **Reviewed:** 2026-09-08
- **Owner:** `IncidentReview.Iracing`
- **Official member-forum page:** <https://forums.iracing.com/discussion/62/iracing-sdk/p1>
- **Downloaded artifact URL:** <https://us.v-cdn.net/6034148/uploads/NS68R31LI7T6/irsdk-1-20.zip>
- **Archive filename:** `irsdk-1-20.zip`
- **Archive SHA-256:** `af4948cc8efe03fa7c99332a63da1ab9d7b34e5b6107540c176ed682e48a2d79`
- **Archive size:** 102,658 bytes
- **Filesystem timestamp (UTC):** 2026-09-06 12:04:32

The archive was acquired by the user through the authenticated official iRacing member-forum context above; the forum linked the iRacing-hosted CDN artifact URL recorded above. It contains 50 safe relative entries (40 files and 10 directories). It was inspected by streaming entries directly from the ZIP and was never extracted into the repository. The recorded filename, URLs, size, timestamp, and digest are the pinned evidence. A future archive replacement must record the same evidence and undergo the same review.

## Dependency and licensing decision

The adapter has no unofficial iRacing package dependency. It uses .NET memory-mapped-file and safe-handle APIs plus source-generated `LibraryImport` boundaries for one Kernel32 function and the two User32 functions verified below. The upstream archive is protocol evidence, not a runtime binary dependency.

The archive has no standalone `LICENSE`, `COPYING`, `NOTICE`, or `README`. The six core files reviewed below embed the same BSD-3-Clause notice, copyright 2013 iRacing.com Motorsport Simulations, LLC. The complete notice and provenance are retained in `vendor/third_party/iracing-sdk-1.20/NOTICE.md`. We make no blanket licensing claim about the 19 archive files that contain no embedded notice.

| Reviewed archive entry | SHA-256 |
|---|---|
| `irsdk_1_20/irsdk_defines.h` | `f2b90e43cd05fff35cea9962cdf8d9c9d2058253f163ae69a8e652b88f6270dd` |
| `irsdk_1_20/irsdk_client.h` | `07bc9347959aad5b2b9e428f130b07329cc9f45a3dfe9fce10a6843fffafd64a` |
| `irsdk_1_20/irsdk_client.cpp` | `e947cd59852f4f40fb75743ddb88ecb085e9aca25de60e9b5d9012ac1284c26f` |
| `irsdk_1_20/irsdk_utils.cpp` | `04c92d61cbeda74a929b2d49090df5008217978b319337e1e9bb42a2e4eabb71` |
| `irsdk_1_20/yaml_parser.cpp` | `86908b310a74d040c27824b02920bc2d69c501ac072602599a2ed0a90614796a` |
| `irsdk_1_20/yaml_parser.h` | `212f0fd93805094cbbb0011b832deefe88463861e13b08980306cec789534d50` |

## Verified shared-memory ABI

The following values are transcribed from `irsdk_defines.h` and checked against the access sequence in `irsdk_client.cpp` and `irsdk_utils.cpp`:

| Item | Verified value |
|---|---|
| Mapping name | `Local\IRSDKMemMapFileName` |
| Data-valid event | `Local\IRSDKDataValidEvent` |
| Registered broadcast name | `IRSDK_BROADCASTMSG` |
| ABI `IRSDK_VER` | 2 |
| Connected status bit | 1 |
| Maximum variable buffers | 4 |
| Fixed variable name/description widths | 32 / 64 bytes |

The ABI is little-endian with these MSVC layouts:

- `irsdk_varHeader`, 144 bytes: type at 0, frame offset at 4, count at 8, one-byte `countAsTime` at 12, name at 16, description at 48, unit at 112.
- `irsdk_varBuf`, 16 bytes: tick count at 0, frame offset at 4, beginning tick at 8, padding at 12.
- `irsdk_header`, 112 bytes: version/status/tick rate at 0/4/8; session-information update/length/offset at 12/16/20; variable count/header offset at 24/28; buffer count/length/current tick at 32/36/40; one-byte current-buffer index at 44; four buffer descriptors from 48.
- Variable type ordinals and sizes: `char` 0/1 byte, `bool` 1/1 byte, `int` 2/4 bytes, bitfield 3/4 bytes, `float` 4/4 bytes, `double` 5/8 bytes.

The managed reader bounds-checks the mapping, every count, multiplication, offset, value extent, buffer index, scalar type, scalar count, name, session text, and one-byte Boolean. It copies every selected telemetry frame into application-owned memory. Variable headers are decoded once per stable structural and session-information version signature (the SDK exposes no dedicated variable-table counter), and session information is reduced only to session-identity evidence, eligible participant scored-counter/display/focus metadata, local-driver display context, and camera metadata when its version changes; ordinary telemetry ticks reuse those immutable caches. A copy is accepted only when the structural header fields and current buffer remain unchanged, the tick observed before the copy equals both the tick and `tickCountBegin` observed after the memory barrier, and the session-information update counter is unchanged. A wakeup is only a hint because the official producer uses pulse-style notification, so the adapter checks memory before every bounded wait. The data-valid event is opened through `OpenEventW` with only the official client's `SYNCHRONIZE` (`0x00100000`) access; the resulting `SafeWaitHandle` is consumer-owned, cannot signal/reset the producer event, and is deterministically released. Like `irsdk_utils.cpp`, the shared-memory reader declares the connection stale after 30 seconds without a structurally stable new frame. The telemetry source separately retains a monotonic timestamp for the last successfully decoded sample across internal reader reopen attempts and ends the logical connection after 30 seconds without one. Continuous structurally valid but semantically invalid frames can refresh the reader clock, but never the source clock.

SDK 1.20's `irsdk_client.h` defines the session-information encoding negotiation explicitly: `WeekendInfo:Encoding: UTF8` selects strict UTF-8, while a missing or different value means the legacy ISO-8859-1 encoding. The adapter performs that negotiation on each changed session-information version. Invalid bytes under an explicit UTF-8 signal fail closed; every byte is valid under the legacy encoding.

When a previously usable connection produces malformed or inconsistent input, the source clears the transient replay-confirmation frame and emits one coalesced `TelemetryUnavailable`; it does not reinterpret rejected data as `TelemetryDisconnected`. Internal reader reopen attempts retain the same logical connection. If another frame decodes successfully before the 30-second source deadline, availability returns under the same connection-scoped key so the active application session, participant checkpoints, and incidents remain intact. Every successfully decoded frame refreshes this deadline even when its application projection is redundant and therefore emits no sample event. A frame first accepted at or beyond the deadline cannot revive expired identity: the old logical connection disconnects and the frame is reread under a new provisional identity. A real SDK disconnected status or expiry of the source deadline likewise ends that logical connection.

The source owns a producer loop that continues copying frames while downstream application or store work is busy. Its connection/recovery policy is one closed immutable lifecycle reduced by a pure `(state, input) → (state, ordered effects, loop directive)` function. The loop alone interprets shared-memory, clock, wait, channel, replay-publication, and reader-lifetime effects. SDK read outcomes and replay availability are closed variants rather than status/Boolean values paired with nullable frames; replay frame plus availability changes atomically. It publishes only connection-state events and samples whose session, mode, on-track state, or participant identity/counter set differs from the preceding sample into a bounded single-reader queue (256 events by default, configurable from 2 through 4,096). Participant counters are canonicalized by identity for this comparison, so a mere roster reorder is coalesced while an opponent-only increase or actual omission/reappearance is preserved. The minimum admits the atomic logical startup pair of `TelemetryConnected` and its first sample. Position-only frames are redundant for the current incident detector and are coalesced. A distinct transition is never silently dropped: if it cannot enter the queue immediately, the adapter stops that observation and reports `iracing.telemetry.buffer-overflow` after draining already accepted events. Consumer cancellation, early enumeration disposal, and adapter disposal all cancel and join the registered producer; disposal cannot return before reader, telemetry, and replay publication have stopped.

## Telemetry mapping decisions

The current vertical slice reads these official variables:

| Application value | SDK evidence |
|---|---|
| Live position | `SessionNum` and `SessionTime` |
| Active replay position | `ReplaySessionNum` and `ReplaySessionTime` when `IsReplayPlaying` is true or `CamCameraState` includes `IsSessionScreen` |
| Eligible participant scored counters | `DriverInfo:Drivers[]:CurDriverIncidentCount` when that row key is present; legacy `TeamIncidentCount` only when it is absent |
| Participant identity and incident display/focus context | `CarIdx`, `TeamID`, `UserID`, `UserName`, `TeamName`, `CarNumber`, and `CarNumberRaw` from the same `Drivers` entry |
| Safe local substitute | `PlayerCarDriverIncidentCount` for a selected current-driver row, or `PlayerCarTeamIncidentCount` for a selected legacy team row, only for the matching local entry |
| Optional local-participant lap context | `Lap` and `LapDistPct` |
| On-track state | `IsOnTrack` |
| Mode | `IsReplayPlaying` or the `CamCameraState.IsSessionScreen` bit |
| Replay playback confirmation | `ReplayPlaySpeed` and `ReplayPlaySlowMotion` |
| Replay camera confirmation | `CamCarIdx`, `CamGroupNumber`, and `CamCameraNumber` |

SDK session time is a finite, non-negative `double` in seconds. Normal 60 Hz samples are not whole milliseconds, so the adapter deterministically floors the converted value to the previous whole millisecond. It never rounds into the future.

An eligible field entry has a unique supported `CarIdx`, deterministic identity evidence from valid current `TeamID`/`UserID` values, one selected non-negative scored counter, is not marked `CarIsPaceCar` or `IsSpectator`, and is not `DriverInfo:PaceCarIdx`. Presence of `CurDriverIncidentCount` selects driver scope even when its value is negative or malformed; the adapter never switches that row to `TeamIncidentCount`. An unavailable opponent contributes no observation and is never converted to zero. A matching local row may instead use the same-scope scalar named above. `SessionInfo:CurrentSessionNum` must match the live frame's `SessionNum` before any row is authoritative. A valid frame may therefore carry no counters while still carrying connection, replay position, on-track state, and observation time.

The adapter derives identity independently on every frame. A legacy team-counter row uses `car-index:{CarIdx}:team:{TeamID}` when `TeamID` is positive, otherwise `car-index:{CarIdx}:user:{UserID}`. A current-driver row for a positive team adds `:driver-user:{UserID}` so a driver swap starts the new driver's independent baseline and a returning driver resumes that driver's checkpoint. For a no-team entry, the positive `UserID` form is already driver-scoped and is compatible with either counter shape. Missing, malformed, or insufficient identity evidence omits the row rather than inventing a weaker identity or consulting process memory. The source shape is represented by compatible identity scope, so team and per-driver counters never share an incompatible checkpoint. A transient schema-shape change may create or resume another checkpoint and remains a live-acceptance case; no mutable alias table guesses equivalence. Driver text is observation context, not proof that the named driver caused the points. Display `CarNumber` is not used for identity or command packing; a stored driver-scoped team identity is normalized to its stable team/car identity before exact replay-row lookup supplies validated `CarNumberRaw`.

The application persists one checkpoint per event and deterministic participant identity. Temporary participant or whole-roster omission leaves checkpoints untouched. The adapter never reads `PlayerCarMyIncidentCount`. When a matching local row lacks a usable selected value, it reads only the scalar with the same semantics: `PlayerCarDriverIncidentCount` for driver scope or `PlayerCarTeamIncidentCount` for legacy team scope. Otherwise it emits no local observation. On authoritative recovery, equality means no change, an increase records one marker with the complete positive delta, and a decrease advances only that participant's epoch and establishes a baseline without an incident. A replay session-number change, such as a new heat, also advances each returning participant's epoch and establishes a baseline before comparing that heat's points. Multiple participant increases in one sample produce separate markers; one multi-point jump produces one delta marker because no trustworthy intermediate times exist.

This source observes scored cumulative points, not physical contacts. A `0x` produces no counter change and is therefore not detectable. “Field-wide” means every eligible entry iRacing actually exposes and transmits to this client, not every entrant or event known to iRacing servers. The [official 2016 Season 3 release notes](https://www.iracing.com/2016-season-3-release-notes/) document that an ordinary client in a live race sees only its own team's incident count, while an admin can see everyone's; non-race sessions and completed races expose broader counts. That historical policy still requires validation against the current build and applicable admin/broadcaster modes. The user's **Max Cars** setting must be **63** for the intended coverage, but the official [Connection Type & Max Cars guidance](https://support.iracing.com/support/solutions/articles/31000149355-connection-type-max-cars) states that even 63 does not guarantee every car will be transmitted because connection and server factors also apply. These boundaries are not overridden by deterministic fixtures.

A positive `WeekendInfo:SubSessionID` creates durable event key `v2:event:subsession:{id}`. `SessionNum` remains part of every replay position but is not part of that event identity, so practice, qualifying, and heats in one SubSession share one incident list. The application combines that exact validated key with canonical simulator code `iracing` to derive the cross-client UUIDv5 `SessionIdentity`; the adapter itself remains unaware of application identity. A changed session number creates a new participant counter epoch and baseline without fake points. Missing, zero, malformed, misplaced, or duplicate evidence produces a UUIDv7 connection-scoped key and therefore only a provisional UUIDv7 application session, which cannot be encoded in an event-sync join code. Existing `v1` per-heat database rows remain valid and separate; the adapter does not heuristically merge or rewrite them. The narrow parser recognizes indentation, quoted scalar values, escapes while finding delimiters, and comments; it does not pretend to be a general YAML implementation.

`IsReplayPlaying` identifies a moving replay, but it becomes false when replay is paused. The adapter also uses the official `irsdk_CameraState_IsSessionScreen` (`0x0001`) bit from `CamCameraState`, so a paused session-screen frame remains `SessionMode.Replay` and cannot be fed to incident detection as live telemetry. The application additionally suppresses detection throughout its own replay-navigation workflow until telemetry reports the car back on track. Real-simulator acceptance must validate the session-screen interpretation across every supported session type; the signal is implemented rather than silently guessed from position or playback speed.

## Verified replay broadcast encoding

`RegisterWindowMessageW("IRSDK_BROADCASTMSG")` supplies the dynamic Windows message identifier. Delivery uses fire-and-forget `SendNotifyMessageW(HWND_BROADCAST, messageId, wParam, lParam)`; success means Windows accepted the notification, not that the simulator applied it.

- `ReplaySearchSessionTime` has ordinal 12. `wParam` is `MAKELONG(12, sessionNumber)` with a signed 16-bit non-negative session number, and `lParam` is the full signed 32-bit non-negative session milliseconds.
- `CamSwitchNum` has ordinal 1. `wParam` is `MAKELONG(1, driverNumber)`, where `driverNumber` is the recorded participant's validated current `CarNumberRaw`, and `lParam` is `MAKELONG(cameraGroupNumber, cameraNumber)`.
- `ReplaySetPlaySpeed` has ordinal 3. `wParam` is `MAKELONG(3, speed)` and `lParam` is `MAKELONG(slowMotion, 0)`.
- Pause is speed 0. Rates of at least 1 require an exact positive integer speed. Rates below 1 require an exact integer reciprocal and set slow motion. Unsupported values fail validation; none are rounded.
- Cancellation is checked immediately before the native delivery boundary. Registration failure and delivery rejection have distinct stable errors.
- Production telemetry and replay singletons share an internal availability and latest-frame state. Seek, camera, and playback delivery fails without broadcasting after disconnect, malformed input, or adapter disposal.
- `IReplayController.ValidatePlayback` runs the same exact encoder as delivery without sending. Application workflows preflight playback before a seek so an unsupported preference cannot leave replay half-configured.

The camera adapter parses only the required session-information shape: eligible `DriverInfo:Drivers` focus entries and their freshly derived canonical team/user identity, `CarIdx`, and `CarNumberRaw`; the local `DriverInfo:DriverCarIdx` compatibility context; and `CameraInfo:Groups`. Focus requires metadata `CurrentSessionNum` to match the frame's live `SessionNum`. The active `ReplaySessionNum` may name another heat in the same application event after seek, but it must remain unchanged through camera confirmation. A stored current-driver team identity is reduced only to its canonical team/car identity, then must exactly match one current row; there is no process-memory rewrite or car-index fallback. A non-null camera preference is matched case-insensitively and uses that group's first camera; a null preference reuses the current group/camera. Confirmation revalidates live metadata, replay heat, exact canonical participant/raw car number, `CamCarIdx`, and `CamGroupNumber`. A TV group may advance its sub-camera. Missing, changed, or ambiguous evidence fails closed.

The public replay operations have applied-result semantics. After Windows accepts each notification, the controller waits for a stable telemetry frame newer than the command's baseline:

- seek confirms `ReplaySessionNum` and `ReplaySessionTime` within 250 milliseconds of the requested time;
- camera focus revalidates live metadata and the selected replay heat, normalizes a stored driver-scoped team identity to its canonical team car, then confirms raw car number, car index, and camera group;
- playback confirms the encoded `ReplayPlaySpeed` and `ReplayPlaySlowMotion` values.

Each stage has its own default ten-second confirmation window. Failure to observe the requested state returns the exact `iracing.replay.seek-timeout`, `iracing.replay.camera-timeout`, or `iracing.replay.playback-timeout` error. Disconnection while waiting returns replay unavailable, and caller cancellation remains `OperationCanceledException`. Thus native delivery acceptance is diagnostic evidence only; it is never surfaced to the application or user as completed navigation.

## Deterministic acceptance evidence

`IncidentReview.Iracing.ProtocolSimulator` is an independent executable: it has no production-project reference and independently writes the pinned header, variable headers, participant-bearing session text, rotating buffers, tick transitions, and named event. Integration tests start it out of process with unique kernel-object names and cover encoding negotiation, cache/version races, torn-copy defenses, replay classification, participant filtering, current-driver and legacy team counter shapes, matching local driver/team scalar substitution, opponent increases, driver swaps, roster reorder/omission, event-wide durable identity across heat numbers, connection-scoped identity, recovery and timeout boundaries, cancellation/disposal, bounded-buffer behavior, and replay confirmation. A focused ACL test proves the adapter can open and release an event whose DACL grants the current consumer only `SYNCHRONIZE`.

Replay-controller tests compare seek, camera, and playback payloads with independently authored raw 32-bit golden vectors. They cover representability, delivery branches, strict participant/camera metadata parsing, migrated-local and opponent focus, driver-scoped team normalization, cross-heat replay focus, explicit canonical identity change, stable live/replay metadata, camera resolution, later-frame requirements, seek tolerance, TV-shot changes, cancellation, disconnect, and the three distinct confirmation timeouts. A Windows-only integration test sends the production seek command through `RegisterWindowMessageW`/`SendNotifyMessageW` and proves that a hidden native top-level receiver gets the exact `wParam` and `lParam`. The native receiver and independent shared-memory simulator remain separate test peers; no current automated test drives the packaged WPF application through all three confirmed commands, and none substitutes for live-iRacing acceptance.

Still required before calling this real-simulator accepted:

- record the iRacing simulator build and exercise the checklist against a live current build;
- capture an allowed, redacted session-information fixture for compatibility regression;
- set **Max Cars = 63** and validate which eligible `Drivers[].CurDriverIncidentCount` and legacy `TeamIncidentCount` values are exposed in solo, team, hosted, admin, and broadcaster contexts;
- validate source-shape stability, driver swaps and returns, temporary identifier loss/recovery, participant and whole-roster omission/reappearance, matching local driver/team scalar substitution, restart behavior, heat changes, counter resets, simultaneous/batched deltas, and the expected inability to observe `0x` contacts;
- validate `CamCameraState.IsSessionScreen`, camera focus, seek tolerance, and per-stage confirmation timing across the supported real session types;
- prove recorded-participant focus for local, opponent, and team-car incidents across heat numbers, including the expected structured failure when the recorded car/team is absent from current metadata;
- combine packaged WPF UI Automation, the independent shared-memory simulator, and an OS-level replay receiver into one end-to-end acceptance peer;
- expand the simulator matrix for variable-header mutation.
