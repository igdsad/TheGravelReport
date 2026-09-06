# Official iRacing SDK 1.20 baseline

- **Status:** pinned and implemented
- **Reviewed:** 2026-09-06
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

The managed reader bounds-checks the mapping, every count, multiplication, offset, value extent, buffer index, scalar type, scalar count, name, session text, and one-byte Boolean. It copies every selected telemetry frame into application-owned memory. Variable headers are decoded once per stable structural and session-information version signature (the SDK exposes no dedicated variable-table counter), and session information is reduced only to identity evidence plus the local-player/camera metadata required for replay focus when its version changes; ordinary telemetry ticks reuse those immutable caches. A copy is accepted only when the structural header fields and current buffer remain unchanged, the tick observed before the copy equals both the tick and `tickCountBegin` observed after the memory barrier, and the session-information update counter is unchanged. A wakeup is only a hint because the official producer uses pulse-style notification, so the adapter checks memory before every bounded wait. The data-valid event is opened through `OpenEventW` with only the official client's `SYNCHRONIZE` (`0x00100000`) access; the resulting `SafeWaitHandle` is consumer-owned, cannot signal/reset the producer event, and is deterministically released. Like `irsdk_utils.cpp`, the shared-memory reader declares the connection stale after 30 seconds without a structurally stable new frame. The telemetry source separately retains a monotonic timestamp for the last successfully decoded sample across internal reader reopen attempts and ends the logical connection after 30 seconds without one. Continuous structurally valid but semantically invalid frames can refresh the reader clock, but never the source clock.

SDK 1.20's `irsdk_client.h` defines the session-information encoding negotiation explicitly: `WeekendInfo:Encoding: UTF8` selects strict UTF-8, while a missing or different value means the legacy ISO-8859-1 encoding. The adapter performs that negotiation on each changed session-information version. Invalid bytes under an explicit UTF-8 signal fail closed; every byte is valid under the legacy encoding.

When a previously usable connection produces malformed or inconsistent input, the source clears the transient replay-confirmation frame and emits one coalesced `TelemetryUnavailable`; it does not reinterpret rejected data as `TelemetryDisconnected`. Internal reader reopen attempts retain the same logical connection. If another frame decodes successfully before the 30-second source deadline, availability returns under the same connection-scoped key so the active application session, checkpoint, and incidents remain intact. A real SDK disconnected status or expiry of the source deadline ends that logical connection and permits a new provisional key.

The source owns a producer loop that continues copying frames while downstream application or store work is busy. It publishes only connection-state events and samples whose session, mode, on-track state, or incident counter differs from the preceding sample into a bounded single-reader queue (256 events by default, configurable from 2 through 4,096). The minimum admits the atomic logical startup pair of `TelemetryConnected` and its first sample. Position-only frames are redundant for the current incident detector and are coalesced. A distinct transition is never silently dropped: if it cannot enter the queue immediately, the adapter stops that observation and reports `iracing.telemetry.buffer-overflow` after draining already accepted events. Consumer cancellation, early enumeration disposal, and adapter disposal all cancel and join the producer.

## Telemetry mapping decisions

The current vertical slice reads these official variables:

| Application value | SDK evidence |
|---|---|
| Live position | `SessionNum` and `SessionTime` |
| Active replay position | `ReplaySessionNum` and `ReplaySessionTime` when `IsReplayPlaying` is true or `CamCameraState` includes `IsSessionScreen` |
| Local member incident counter | `PlayerCarMyIncidentCount` |
| Optional lap context | `Lap` and `LapDistPct` |
| On-track state | `IsOnTrack` |
| Mode | `IsReplayPlaying` or the `CamCameraState.IsSessionScreen` bit |
| Replay playback confirmation | `ReplayPlaySpeed` and `ReplayPlaySlowMotion` |
| Replay camera confirmation | `CamCarIdx`, `CamGroupNumber`, and `CamCameraNumber` |

SDK session time is a finite, non-negative `double` in seconds. Normal 60 Hz samples are not whole milliseconds, so the adapter deterministically floors the converted value to the previous whole millisecond. It never rounds into the future.

`PlayerCarMyIncidentCount` is deliberate: `PlayerCarTeamIncidentCount` includes team-mate incidents and could attribute another driver's incident increase to this user. There is no fallback to the team counter. This matches iRacing's definitions in the [2016 Season 3 release notes](https://www.iracing.com/2016-season-3-release-notes/); a future explicit “whole team” review mode should be a separate policy above this adapter.

A positive `WeekendInfo:SubSessionID`, combined with the selected session number, creates durable key `v1:subsession:{id}:session:{number}`. Missing, zero, malformed, misplaced, or duplicate evidence produces a UUIDv7 connection-scoped key. The narrow parser recognizes indentation, quoted scalar values, escapes while finding delimiters, and comments; it does not pretend to be a general YAML implementation.

`IsReplayPlaying` identifies a moving replay, but it becomes false when replay is paused. The adapter also uses the official `irsdk_CameraState_IsSessionScreen` (`0x0001`) bit from `CamCameraState`, so a paused session-screen frame remains `SessionMode.Replay` and cannot be fed to incident detection as live telemetry. The application additionally suppresses detection throughout its own replay-navigation workflow until telemetry reports the car back on track. Real-simulator acceptance must validate the session-screen interpretation across every supported session type; the signal is implemented rather than silently guessed from position or playback speed.

## Verified replay broadcast encoding

`RegisterWindowMessageW("IRSDK_BROADCASTMSG")` supplies the dynamic Windows message identifier. Delivery uses fire-and-forget `SendNotifyMessageW(HWND_BROADCAST, messageId, wParam, lParam)`; success means Windows accepted the notification, not that the simulator applied it.

- `ReplaySearchSessionTime` has ordinal 12. `wParam` is `MAKELONG(12, sessionNumber)` with a signed 16-bit non-negative session number, and `lParam` is the full signed 32-bit non-negative session milliseconds.
- `CamSwitchNum` has ordinal 1. `wParam` is `MAKELONG(1, driverNumber)`, where `driverNumber` is the local driver's validated `CarNumberRaw`, and `lParam` is `MAKELONG(cameraGroupNumber, cameraNumber)`.
- `ReplaySetPlaySpeed` has ordinal 3. `wParam` is `MAKELONG(3, speed)` and `lParam` is `MAKELONG(slowMotion, 0)`.
- Pause is speed 0. Rates of at least 1 require an exact positive integer speed. Rates below 1 require an exact integer reciprocal and set slow motion. Unsupported values fail validation; none are rounded.
- Cancellation is checked immediately before the native delivery boundary. Registration failure and delivery rejection have distinct stable errors.
- Production telemetry and replay singletons share an internal availability and latest-frame state. Seek, camera, and playback delivery fails without broadcasting after disconnect, malformed input, or adapter disposal.
- `IReplayController.ValidatePlayback` runs the same exact encoder as delivery without sending. Application workflows preflight playback before a seek so an unsupported preference cannot leave replay half-configured.

The camera adapter parses only the required official session-information shape: `DriverInfo:DriverCarIdx`, the matching `Drivers` entry's `CarNumberRaw`, and `CameraInfo:Groups` with each `GroupNum`, `GroupName`, and ordered `CameraNum` values. A non-null preference is matched case-insensitively to a group and uses its first camera for the command. A null preference reuses the currently reported group/camera. Confirmation requires the later frame's `CamCarIdx` to equal `DriverCarIdx` and `CamGroupNumber` to equal the resolved group; it intentionally does not require the exact `CamCameraNumber` because a TV group can advance shots automatically. Missing, malformed, duplicate, ambiguous, or unrepresentable player/camera metadata fails closed before broadcasting.

The public replay operations have applied-result semantics. After Windows accepts each notification, the controller waits for a stable telemetry frame newer than the command's baseline:

- seek confirms `ReplaySessionNum` and `ReplaySessionTime` within 250 milliseconds of the requested time;
- camera focus confirms the local-player car index and resolved camera group;
- playback confirms the encoded `ReplayPlaySpeed` and `ReplayPlaySlowMotion` values.

Each stage has its own default ten-second confirmation window. Failure to observe the requested state returns the exact `iracing.replay.seek-timeout`, `iracing.replay.camera-timeout`, or `iracing.replay.playback-timeout` error. Disconnection while waiting returns replay unavailable, and caller cancellation remains `OperationCanceledException`. Thus native delivery acceptance is diagnostic evidence only; it is never surfaced to the application or user as completed navigation.

## Deterministic acceptance evidence

`IncidentReview.Iracing.ProtocolSimulator` is an independent executable: it has no production-project reference and independently writes the pinned header, variable headers, UTF-8 and ISO-8859-1 session text, three rotating variable buffers, tick transitions, and named event. Integration tests start it out of process with unique kernel-object names and cover encoding negotiation, metadata/session-version caching, a session update racing a cache-miss copy, current-buffer rotation, tick regression/resynchronization, live/moving-replay/paused-session-screen decoding, fractional-millisecond flooring, local-vs-team counter selection, durable and connection-scoped identity, recovery from malformed Boolean, session text, metadata, and time without provisional-key rotation, persistent invalid input ending the logical connection only at the last-successfully-decoded-sample deadline, true disconnect/reconnect key rotation, caller cancellation, disposal, slow-consumer reset/rise preservation, deterministic bounded-buffer overflow, and confirmation of an outstanding seek by a position-only frame that the application event queue may coalesce. A focused ACL test proves the adapter can open and release an event whose DACL grants the current consumer only `SYNCHRONIZE`.

Replay-controller tests compare seek, camera, and playback payloads with independently authored raw 32-bit golden vectors. They cover representability, delivery branches, strict player/camera metadata parsing, preferred/current camera resolution, later-frame requirements, seek tolerance, TV-shot camera-number changes, cancellation, disconnect, and the three distinct confirmation timeouts. A Windows-only integration test sends the production seek command through `RegisterWindowMessageW`/`SendNotifyMessageW` and proves that a hidden native top-level receiver gets the exact `wParam` and `lParam`. The native receiver and independent shared-memory simulator remain separate test peers; no current automated test drives the packaged WPF application through all three confirmed commands, and none substitutes for live-iRacing acceptance.

Still required before calling this real-simulator accepted:

- record the iRacing simulator build and exercise the checklist against a live current build;
- capture an allowed, redacted session-information fixture for compatibility regression;
- validate `CamCameraState.IsSessionScreen`, camera focus, seek tolerance, and per-stage confirmation timing across the supported real session types;
- combine packaged WPF UI Automation, the independent shared-memory simulator, and an OS-level replay receiver into one end-to-end acceptance peer;
- expand the simulator matrix for variable-header mutation.
