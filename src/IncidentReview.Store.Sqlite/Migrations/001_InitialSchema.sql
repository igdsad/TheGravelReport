CREATE TABLE "Session" (
    session_id TEXT NOT NULL CONSTRAINT pk_session PRIMARY KEY,
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
    CONSTRAINT ck_session_id_uuid CHECK (
        length(session_id) = 36
        AND substr(session_id, 9, 1) = '-'
        AND substr(session_id, 14, 1) = '-'
        AND substr(session_id, 19, 1) = '-'
        AND substr(session_id, 24, 1) = '-'
        AND session_id = lower(session_id)
        AND length(replace(session_id, '-', '')) = 32
        AND replace(session_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_session_simulator CHECK (
        length(simulator) BETWEEN 1 AND 32
        AND substr(simulator, 1, 1) GLOB '[a-z]'
        AND simulator NOT GLOB '*[^a-z0-9-]*'
        AND simulator NOT GLOB '*--*'
        AND substr(simulator, -1, 1) <> '-'),
    CONSTRAINT ck_session_key CHECK (
        simulator_session_key IS NULL
        OR (length(simulator_session_key) BETWEEN 1 AND 256
            AND length(trim(simulator_session_key)) > 0)),
    CONSTRAINT ck_session_identity_kind CHECK (identity_kind IN (1, 2)),
    CONSTRAINT ck_session_number CHECK (simulator_session_number BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_session_mode CHECK (session_mode IN (1, 2)),
    CONSTRAINT ck_session_times CHECK (
        started_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND (ended_at_utc_ms IS NULL
            OR ended_at_utc_ms BETWEEN started_at_utc_ms AND 253402300799999)),
    CONSTRAINT ck_session_audit_times CHECK (
        created_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND updated_at_utc_ms BETWEEN created_at_utc_ms AND 253402300799999)
);

CREATE UNIQUE INDEX ux_session_simulator_key
    ON "Session" (simulator, simulator_session_key)
    WHERE simulator_session_key IS NOT NULL;

CREATE TABLE Incident (
    incident_id TEXT NOT NULL CONSTRAINT pk_incident PRIMARY KEY,
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
    CONSTRAINT fk_incident_session FOREIGN KEY (session_id)
        REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_incident_id_uuid CHECK (
        length(incident_id) = 36
        AND substr(incident_id, 9, 1) = '-'
        AND substr(incident_id, 14, 1) = '-'
        AND substr(incident_id, 19, 1) = '-'
        AND substr(incident_id, 24, 1) = '-'
        AND incident_id = lower(incident_id)
        AND length(replace(incident_id, '-', '')) = 32
        AND replace(incident_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_incident_replay_position CHECK (
        replay_session_number BETWEEN 0 AND 2147483647
        AND replay_session_time_ms BETWEEN 0 AND 922337203685477),
    CONSTRAINT ck_incident_observed_time CHECK (
        observed_at_utc_ms BETWEEN -62135596800000 AND 253402300799999),
    CONSTRAINT ck_incident_points CHECK (
        incident_points_delta BETWEEN 1 AND 2147483647
        AND incident_points_total BETWEEN incident_points_delta AND 2147483647
        AND counter_epoch BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_incident_lap CHECK (
        lap IS NULL OR lap BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_incident_lap_distance CHECK (
        lap_distance_percent IS NULL
        OR (lap_distance_percent >= 0.0 AND lap_distance_percent <= 1.0)),
    CONSTRAINT ck_incident_review_status CHECK (review_status IN (1, 2, 3)),
    CONSTRAINT ck_incident_classification CHECK (
        classification IS NULL OR classification IN (1, 2, 3, 4, 5)),
    CONSTRAINT ck_incident_notes CHECK (notes IS NULL OR length(notes) <= 2000),
    CONSTRAINT ck_incident_audit_times CHECK (
        created_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND updated_at_utc_ms BETWEEN created_at_utc_ms AND 253402300799999)
);

CREATE UNIQUE INDEX ux_incident_session_epoch_total
    ON Incident (session_id, counter_epoch, incident_points_total);

CREATE TABLE IncidentCheckpoint (
    session_id TEXT NOT NULL CONSTRAINT pk_incident_checkpoint PRIMARY KEY,
    counter_epoch INTEGER NOT NULL,
    last_incident_points_total INTEGER NOT NULL,
    last_replay_session_number INTEGER NOT NULL,
    last_replay_session_time_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT fk_checkpoint_session FOREIGN KEY (session_id)
        REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_checkpoint_values CHECK (
        counter_epoch BETWEEN 0 AND 2147483647
        AND last_incident_points_total BETWEEN 0 AND 2147483647
        AND last_replay_session_number BETWEEN 0 AND 2147483647
        AND last_replay_session_time_ms BETWEEN 0 AND 922337203685477
        AND updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
);

CREATE TABLE StoreOperation (
    operation_id TEXT NOT NULL CONSTRAINT pk_store_operation PRIMARY KEY,
    command_kind TEXT NOT NULL,
    command_version INTEGER NOT NULL,
    payload_fingerprint_sha256 BLOB NOT NULL,
    committed_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT ck_operation_id_uuid CHECK (
        length(operation_id) = 36
        AND substr(operation_id, 9, 1) = '-'
        AND substr(operation_id, 14, 1) = '-'
        AND substr(operation_id, 19, 1) = '-'
        AND substr(operation_id, 24, 1) = '-'
        AND operation_id = lower(operation_id)
        AND length(replace(operation_id, '-', '')) = 32
        AND replace(operation_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_operation_kind CHECK (length(trim(command_kind)) > 0),
    CONSTRAINT ck_operation_version CHECK (command_version > 0),
    CONSTRAINT ck_operation_fingerprint CHECK (length(payload_fingerprint_sha256) = 32),
    CONSTRAINT ck_operation_committed_time CHECK (
        committed_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
);

CREATE TABLE ApplicationPreferences (
    preferences_id INTEGER NOT NULL CONSTRAINT pk_application_preferences PRIMARY KEY,
    replay_lead_in_ms INTEGER NOT NULL,
    auto_pause INTEGER NOT NULL,
    playback_speed REAL NOT NULL,
    preferred_camera TEXT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT ck_preferences_singleton CHECK (preferences_id = 1),
    CONSTRAINT ck_preferences_lead_in CHECK (replay_lead_in_ms BETWEEN 0 AND 60000),
    CONSTRAINT ck_preferences_auto_pause CHECK (auto_pause IN (0, 1)),
    CONSTRAINT ck_preferences_playback_speed CHECK (
        playback_speed = playback_speed
        AND playback_speed BETWEEN 0.1 AND 1.0),
    CONSTRAINT ck_preferences_camera CHECK (
        preferred_camera IS NULL
        OR (length(preferred_camera) BETWEEN 1 AND 128
            AND length(trim(preferred_camera)) > 0)),
    CONSTRAINT ck_preferences_updated_time CHECK (
        updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
);

INSERT INTO ApplicationPreferences (
    preferences_id,
    replay_lead_in_ms,
    auto_pause,
    playback_speed,
    preferred_camera,
    updated_at_utc_ms)
VALUES (1, 5000, 1, 1.0, NULL, 0);

PRAGMA user_version = 1;
