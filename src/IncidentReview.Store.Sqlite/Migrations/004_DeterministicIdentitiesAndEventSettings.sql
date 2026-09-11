CREATE TABLE Session_v4 (
    session_id TEXT NOT NULL CONSTRAINT pk_session_v4 PRIMARY KEY,
    simulator TEXT NOT NULL,
    simulator_session_key TEXT NULL,
    identity_kind INTEGER NOT NULL,
    simulator_session_number INTEGER NOT NULL,
    session_mode INTEGER NOT NULL,
    started_at_utc_ms INTEGER NOT NULL,
    ended_at_utc_ms INTEGER NULL,
    track_id TEXT NULL,
    track_name TEXT NULL,
    car_id TEXT NULL,
    car_name TEXT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT ck_session_v4_id_uuid CHECK (
        length(session_id) = 36
        AND substr(session_id, 9, 1) = '-'
        AND substr(session_id, 14, 1) = '-'
        AND substr(session_id, 19, 1) = '-'
        AND substr(session_id, 24, 1) = '-'
        AND substr(session_id, 15, 1) IN ('5', '7')
        AND substr(session_id, 20, 1) IN ('8', '9', 'a', 'b')
        AND session_id = lower(session_id)
        AND length(replace(session_id, '-', '')) = 32
        AND replace(session_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_session_v4_simulator CHECK (
        length(simulator) BETWEEN 1 AND 32
        AND substr(simulator, 1, 1) GLOB '[a-z]'
        AND simulator NOT GLOB '*[^a-z0-9-]*'
        AND simulator NOT GLOB '*--*'
        AND substr(simulator, -1, 1) <> '-'),
    CONSTRAINT ck_session_v4_key CHECK (
        simulator_session_key IS NULL
        OR (length(simulator_session_key) BETWEEN 1 AND 256
            AND length(trim(simulator_session_key)) > 0)),
    CONSTRAINT ck_session_v4_identity_kind CHECK (identity_kind IN (1, 2)),
    CONSTRAINT ck_session_v4_number CHECK (
        simulator_session_number BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_session_v4_mode CHECK (session_mode IN (1, 2)),
    CONSTRAINT ck_session_v4_times CHECK (
        started_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND (ended_at_utc_ms IS NULL
            OR ended_at_utc_ms BETWEEN started_at_utc_ms AND 253402300799999)),
    CONSTRAINT ck_session_v4_audit_times CHECK (
        created_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND updated_at_utc_ms BETWEEN created_at_utc_ms AND 253402300799999)
);

INSERT INTO Session_v4 (
    session_id,
    simulator,
    simulator_session_key,
    identity_kind,
    simulator_session_number,
    session_mode,
    started_at_utc_ms,
    ended_at_utc_ms,
    track_id,
    track_name,
    car_id,
    car_name,
    created_at_utc_ms,
    updated_at_utc_ms)
SELECT
    session_id,
    simulator,
    simulator_session_key,
    identity_kind,
    simulator_session_number,
    session_mode,
    started_at_utc_ms,
    ended_at_utc_ms,
    track_id,
    track_name,
    car_id,
    car_name,
    created_at_utc_ms,
    updated_at_utc_ms
FROM "Session";

CREATE TABLE Incident_v4 (
    incident_id TEXT NOT NULL CONSTRAINT pk_incident_v4 PRIMARY KEY,
    session_id TEXT NOT NULL,
    replay_session_number INTEGER NOT NULL,
    replay_session_time_ms INTEGER NOT NULL,
    observed_at_utc_ms INTEGER NOT NULL,
    incident_points_delta INTEGER NOT NULL,
    incident_points_total INTEGER NOT NULL,
    counter_epoch INTEGER NOT NULL,
    lap INTEGER NULL,
    lap_distance_percent REAL NULL,
    review_status INTEGER NOT NULL,
    classification INTEGER NULL,
    notes TEXT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    participant_identity TEXT NOT NULL DEFAULT 'local-player',
    driver_name TEXT NULL,
    team_name TEXT NULL,
    car_number TEXT NULL,
    CONSTRAINT fk_incident_v4_session FOREIGN KEY (session_id)
        REFERENCES Session_v4 (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_incident_v4_id_uuid CHECK (
        length(incident_id) = 36
        AND substr(incident_id, 9, 1) = '-'
        AND substr(incident_id, 14, 1) = '-'
        AND substr(incident_id, 19, 1) = '-'
        AND substr(incident_id, 24, 1) = '-'
        AND substr(incident_id, 15, 1) IN ('5', '7')
        AND substr(incident_id, 20, 1) IN ('8', '9', 'a', 'b')
        AND incident_id = lower(incident_id)
        AND length(replace(incident_id, '-', '')) = 32
        AND replace(incident_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_incident_v4_replay_position CHECK (
        replay_session_number BETWEEN 0 AND 2147483647
        AND replay_session_time_ms BETWEEN 0 AND 922337203685477),
    CONSTRAINT ck_incident_v4_observed_time CHECK (
        observed_at_utc_ms BETWEEN -62135596800000 AND 253402300799999),
    CONSTRAINT ck_incident_v4_points CHECK (
        incident_points_delta BETWEEN 1 AND 2147483647
        AND incident_points_total BETWEEN incident_points_delta AND 2147483647
        AND counter_epoch BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_incident_v4_lap CHECK (
        lap IS NULL OR lap BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_incident_v4_lap_distance CHECK (
        lap_distance_percent IS NULL
        OR (lap_distance_percent >= 0.0 AND lap_distance_percent <= 1.0)),
    CONSTRAINT ck_incident_v4_review_status CHECK (review_status IN (1, 2, 3)),
    CONSTRAINT ck_incident_v4_classification CHECK (
        classification IS NULL OR classification IN (1, 2, 3, 4, 5)),
    CONSTRAINT ck_incident_v4_notes CHECK (notes IS NULL OR length(notes) <= 2000),
    CONSTRAINT ck_incident_v4_audit_times CHECK (
        created_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND updated_at_utc_ms BETWEEN created_at_utc_ms AND 253402300799999),
    CONSTRAINT ck_incident_v4_participant_identity CHECK (
        length(participant_identity) BETWEEN 1 AND 128
        AND length(trim(participant_identity)) > 0),
    CONSTRAINT ck_incident_v4_driver_name CHECK (
        driver_name IS NULL
        OR (length(driver_name) BETWEEN 1 AND 128
            AND length(trim(driver_name)) > 0)),
    CONSTRAINT ck_incident_v4_team_name CHECK (
        team_name IS NULL
        OR (length(team_name) BETWEEN 1 AND 128
            AND length(trim(team_name)) > 0)),
    CONSTRAINT ck_incident_v4_car_number CHECK (
        car_number IS NULL
        OR (length(car_number) BETWEEN 1 AND 128
            AND length(trim(car_number)) > 0))
);

INSERT INTO Incident_v4 (
    incident_id,
    session_id,
    replay_session_number,
    replay_session_time_ms,
    observed_at_utc_ms,
    incident_points_delta,
    incident_points_total,
    counter_epoch,
    lap,
    lap_distance_percent,
    review_status,
    classification,
    notes,
    created_at_utc_ms,
    updated_at_utc_ms,
    participant_identity,
    driver_name,
    team_name,
    car_number)
SELECT
    incident_id,
    session_id,
    replay_session_number,
    replay_session_time_ms,
    observed_at_utc_ms,
    incident_points_delta,
    incident_points_total,
    counter_epoch,
    lap,
    lap_distance_percent,
    review_status,
    classification,
    notes,
    created_at_utc_ms,
    updated_at_utc_ms,
    participant_identity,
    driver_name,
    team_name,
    car_number
FROM Incident;

CREATE TABLE IncidentCheckpoint_v4 (
    session_id TEXT NOT NULL,
    participant_identity TEXT NOT NULL,
    counter_epoch INTEGER NOT NULL,
    last_incident_points_total INTEGER NOT NULL,
    last_replay_session_number INTEGER NOT NULL,
    last_replay_session_time_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT pk_incident_checkpoint_v4
        PRIMARY KEY (session_id, participant_identity),
    CONSTRAINT fk_checkpoint_v4_session FOREIGN KEY (session_id)
        REFERENCES Session_v4 (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_checkpoint_v4_participant_identity CHECK (
        length(participant_identity) BETWEEN 1 AND 128
        AND length(trim(participant_identity)) > 0),
    CONSTRAINT ck_checkpoint_v4_values CHECK (
        counter_epoch BETWEEN 0 AND 2147483647
        AND last_incident_points_total BETWEEN 0 AND 2147483647
        AND last_replay_session_number BETWEEN 0 AND 2147483647
        AND last_replay_session_time_ms BETWEEN 0 AND 922337203685477
        AND updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
);

INSERT INTO IncidentCheckpoint_v4 (
    session_id,
    participant_identity,
    counter_epoch,
    last_incident_points_total,
    last_replay_session_number,
    last_replay_session_time_ms,
    updated_at_utc_ms)
SELECT
    session_id,
    participant_identity,
    counter_epoch,
    last_incident_points_total,
    last_replay_session_number,
    last_replay_session_time_ms,
    updated_at_utc_ms
FROM IncidentCheckpoint;

CREATE TEMP TABLE Migration004CopyGuard (
    copy_is_complete INTEGER NOT NULL CHECK (copy_is_complete = 1)
);

INSERT INTO Migration004CopyGuard (copy_is_complete)
SELECT CASE
    WHEN (SELECT COUNT(*) FROM Session_v4) = (SELECT COUNT(*) FROM "Session")
     AND (SELECT COUNT(*) FROM Incident_v4) = (SELECT COUNT(*) FROM Incident)
     AND (SELECT COUNT(*) FROM IncidentCheckpoint_v4) =
         (SELECT COUNT(*) FROM IncidentCheckpoint)
    THEN 1
    ELSE 0
END;

DROP TABLE Migration004CopyGuard;
DROP TABLE IncidentCheckpoint;
DROP TABLE Incident;
DROP TABLE "Session";

ALTER TABLE Session_v4 RENAME TO "Session";
ALTER TABLE Incident_v4 RENAME TO Incident;
ALTER TABLE IncidentCheckpoint_v4 RENAME TO IncidentCheckpoint;

CREATE UNIQUE INDEX ux_session_simulator_key
    ON "Session" (simulator, simulator_session_key)
    WHERE simulator_session_key IS NOT NULL AND identity_kind = 1;

CREATE UNIQUE INDEX ux_incident_session_participant_epoch_total
    ON Incident (
        session_id,
        participant_identity,
        counter_epoch,
        incident_points_total);

ALTER TABLE ApplicationPreferences
ADD COLUMN submitter_name TEXT NULL
    CONSTRAINT ck_preferences_submitter_name CHECK (
        submitter_name IS NULL
        OR (length(submitter_name) BETWEEN 1 AND 128
            AND length(trim(submitter_name)) > 0));

ALTER TABLE ApplicationPreferences
ADD COLUMN custom_event_key TEXT NOT NULL DEFAULT 'F9'
    CONSTRAINT ck_preferences_custom_event_key CHECK (
        length(custom_event_key) BETWEEN 1 AND 64
        AND length(trim(custom_event_key)) > 0);

ALTER TABLE ApplicationPreferences
ADD COLUMN event_join_code TEXT NULL
    CONSTRAINT ck_preferences_event_join_code CHECK (
        event_join_code IS NULL
        OR (length(event_join_code) BETWEEN 1 AND 4096
            AND length(trim(event_join_code)) > 0));

PRAGMA user_version = 4;
