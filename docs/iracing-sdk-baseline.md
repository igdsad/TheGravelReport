# Official iRacing SDK acquisition baseline

- **Status:** Acquisition required before protocol implementation
- **Last verified:** 2026-09-06
- **Owner:** `IncidentReview.Iracing`

This file is the evidence gate for implementing the local iRacing adapter. It deliberately separates facts supported by public iRacing material from details that still require the current authenticated SDK archive. Production code MUST NOT take ABI layouts, enum ordinals, object names, or Windows-message packing from an unofficial wrapper or mirror.

## Dependency decision

The adapter will not depend on an unofficial iRacing NuGet package. We will retrieve the current SDK archive through the [official SDK forum discussion](https://forums.iracing.com/discussion/62/iracing-sdk/p1), review its included license, retain its provenance and hashes, and transcribe only the protocol surface the application uses.

The managed implementation SHOULD use .NET framework facilities for memory-mapped files and synchronization where they accurately implement the pinned protocol. Narrow User32 calls SHOULD use source-generated `LibraryImport`. The official archive is protocol evidence; it does not need to become a runtime library dependency.

iRacing distinguishes this simulator-local SDK from its `/data` web API. OAuth credentials are therefore not part of the local telemetry/replay adapter. See [iRacing Support's OAuth client-credentials guidance](https://support.iracing.com/support/solutions/articles/31000177790-oauth-client-credentials).

## Required acquisition record

Before adding layout or broadcast constants, record all of the following in a reviewed manifest:

- official download URL and retrieval timestamp;
- archive filename and upstream version/date, when supplied;
- SHA-256 of the original archive;
- SHA-256 of every retained header, source, fixture, license, and notice;
- the iRacing simulator build used to capture acceptance fixtures;
- the complete license and redistribution notice from that archive.

Inventory the current files that serve the historical roles of:

- `irsdk_defines.h`: ABI structures, enums, broadcast commands, and declarations;
- `irsdk_utils.cpp`: mapping, event, buffer-copy, and broadcast packing behavior;
- `irsdk_client.h` and `irsdk_client.cpp`: intended connection lifecycle;
- examples, release history, license text, and session-information fixtures.

Those filenames are expected from historical releases but MUST be confirmed against the downloaded archive. `IRSDK_VER` is an ABI/layout value and MUST NOT be presented as the archive's distribution version.

## Publicly verified behavior

The [official iRacing-hosted historical telemetry reference](https://us.v-cdn.net/6034148/uploads/8DD84H30FIC8/telemetry-11-23-15.pdf) documents a live telemetry feed sampled at 60 Hz and a YAML session-information string. The session information changes during an event as drivers register and results become available; the adapter must treat its update counter and bounds as live input rather than startup-only configuration.

The current runtime variable headers still need to be compared with the archive, but official historical documentation establishes these relevant names and types:

| Purpose | Historical variable | Historical type |
|---|---|---:|
| Active session | `SessionNum` | `int` |
| Live session time | `SessionTime` | `double` seconds |
| Ambiguously described session ID | `SessionUniqueID` | `int` |
| Lap | `Lap` | `int` |
| Normalized lap progress | `LapDistPct` | `float` |
| Local driver physically active | `IsOnTrack` | `bool` |
| Team car on track | `IsOnTrackCar` | `bool` |
| Replay playback flag | `IsReplayPlaying` | `bool` |
| Replay position/state | `ReplayFrameNum`, `ReplayFrameNumEnd`, `ReplayPlaySpeed`, `ReplaySessionNum` | `int` |
| Replay slow motion | `ReplayPlaySlowMotion` | `bool` |
| Replay session time | `ReplaySessionTime` | `double` seconds |
| Camera state | `CamCameraState` | SDK bitfield |

Official release notes define the incident counters as follows:

- `PlayerCarTeamIncidentCount` covers the entire team;
- `PlayerCarMyIncidentCount` covers the local member;
- `PlayerCarDriverIncidentCount` covers the current team driver;
- all three agree in non-team events;
- replay exposes incident counts subject to visibility rules.

The definitions are in the [2016 Season 3 release notes](https://www.iracing.com/2016-season-3-release-notes/). The [2016 Season 4 release notes](https://www.iracing.com/2016-season-4-release-notes/) subsequently fixed team-counter updates while the local member was not driving. Driver identity must also be resolved dynamically in team replay: the [2022 Season 1 release notes](https://support.iracing.com/support/solutions/articles/31000162773-2022-season-1-release-notes-2021-12-06-03-) describe a correction to current-driver reporting while viewing multi-driver replays.

The 2026 [Official Sporting Code](https://ir-core-sites.iracing.com/members/pdfs/20260310-official_sporting_code_dated_Mar_10_2026.pdf) defines a Subsession ID as the unique number assigned when an event session launches. Public official material does not establish equivalent durable-identity semantics for `SessionUniqueID`.

SDK-related corrections continue to ship; for example, [2026 Season 3 Patch 3](https://support.iracing.com/support/solutions/articles/31000179134-2026-season-3-patch-3-release-notes-2026-07-10-04-) includes telemetry corrections. Runtime headers and captured fixtures remain part of acceptance even after the archive is pinned.

## Proposed application mapping

The following is a design mapping to validate with the current archive and real fixtures:

| Application value | Proposed iRacing evidence |
|---|---|
| Durable `SimulatorSessionKey` | A canonical, versioned encoding of a positive `WeekendInfo:SubSessionID` and the applicable session number |
| Live `SessionNumber` | `SessionNum` |
| Replay `SessionNumber` | `ReplaySessionNum` |
| Live `SessionTime` | `SessionTime`, converted from seconds only when finite, non-negative, in range, and exactly representable in whole milliseconds |
| Replay `SessionTime` | `ReplaySessionTime` with the same validation |
| Non-team incident counter | `PlayerCarMyIncidentCount`, while checking that all documented counters agree |
| Default team incident counter | `PlayerCarTeamIncidentCount` |
| Optional personal-only team counter | `PlayerCarMyIncidentCount` |
| Lap context | `Lap` and `LapDistPct` |
| Personal on-track state | `IsOnTrack` |
| Team-car on-track state | `IsOnTrackCar` |

Missing, zero, malformed, or contradictory Subsession evidence produces `SimulatorIdentityScope.ConnectionScoped`; it is never heuristically merged across reconnects. `SessionUniqueID` is not accepted as durable evidence without current official clarification.

Detection MUST be disabled while reviewing replay. A paused replay makes `IsReplayPlaying` false, and replay seeking can move the incident counter backward or forward; neither condition is valid evidence of a live counter transition.

## ABI and reader checklist pending archive verification

Confirm every item below directly against the authenticated archive before implementation:

- mapping and synchronization-event names;
- header packing, field widths, signedness, ABI version, status bits, tick rate, variable counts and offsets, session-information update/length/offset, buffer length/count, and tick descriptors;
- maximum buffer count and alignment requirements;
- connection/freshness timeout and tick-regression behavior;
- event reset semantics and whether a wakeup is only a hint;
- newest-buffer selection and the exact torn-copy detection/retry algorithm.

Regardless of upstream helper behavior, managed decoding MUST range-check every count, multiplication, offset, length, and buffer index before accessing mapped memory. It copies a validated frame into application-owned memory before exposing contract values.

## Replay broadcast checklist pending archive verification

Confirm the declarations and implementation for:

- search/seek by replay session time;
- set replay playback speed and slow-motion mode;
- registered Windows message name;
- `RegisterWindowMessageW` and fire-and-forget broadcast delivery;
- exact `wParam`/`lParam` packing, including signed 16-bit and signed 32-bit slots;
- enum ordinals and all range restrictions.

Historical transcriptions commonly describe a replay-session-time command with a signed 16-bit session number and signed 32-bit milliseconds, and a playback command with integer speed plus a slow-motion flag. These are test hypotheses, not production constants.

The adapter must reject a domain `SessionNumber` or `SessionTime` that does not fit the verified wire slot. It must also map `ReplayPlayback` only when the requested positive finite rate is exactly representable by the verified iRacing speed/slow-motion protocol; it never rounds silently.

## Acceptance evidence

Independent golden vectors will transcribe the reviewed header values rather than reuse production encoders. The protocol simulator will validate shared-memory layout, changing session information, driver swaps, counter selection, torn frames, tick ordering, connection loss/recovery, and replay-message packing. Real-iRacing acceptance records the pinned SDK hash, simulator build, and the applicable checklist result.

Until the acquisition record and license review are complete, `IncidentReview.Iracing` may contain simulator-neutral orchestration seams and tests, but no claimed-current ABI constants or replay broadcast ordinals.
