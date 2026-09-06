ALTER TABLE Incident
ADD COLUMN participant_identity TEXT NOT NULL DEFAULT 'local-player'
    CONSTRAINT ck_incident_participant_identity CHECK (
        length(participant_identity) BETWEEN 1 AND 128
        AND length(trim(participant_identity)) > 0);

ALTER TABLE Incident
ADD COLUMN driver_name TEXT NULL
    CONSTRAINT ck_incident_driver_name CHECK (
        driver_name IS NULL
        OR (length(driver_name) BETWEEN 1 AND 128
            AND length(trim(driver_name)) > 0));

ALTER TABLE Incident
ADD COLUMN team_name TEXT NULL
    CONSTRAINT ck_incident_team_name CHECK (
        team_name IS NULL
        OR (length(team_name) BETWEEN 1 AND 128
            AND length(trim(team_name)) > 0));

ALTER TABLE Incident
ADD COLUMN car_number TEXT NULL
    CONSTRAINT ck_incident_car_number CHECK (
        car_number IS NULL
        OR (length(car_number) BETWEEN 1 AND 128
            AND length(trim(car_number)) > 0));

DROP INDEX ux_incident_session_epoch_total;

CREATE UNIQUE INDEX ux_incident_session_participant_epoch_total
    ON Incident (
        session_id,
        participant_identity,
        counter_epoch,
        incident_points_total);

CREATE TABLE IncidentCheckpointV3 (
    session_id TEXT NOT NULL,
    participant_identity TEXT NOT NULL,
    counter_epoch INTEGER NOT NULL,
    last_incident_points_total INTEGER NOT NULL,
    last_replay_session_number INTEGER NOT NULL,
    last_replay_session_time_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    CONSTRAINT pk_incident_checkpoint
        PRIMARY KEY (session_id, participant_identity),
    CONSTRAINT fk_checkpoint_session FOREIGN KEY (session_id)
        REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_checkpoint_participant_identity CHECK (
        length(participant_identity) BETWEEN 1 AND 128
        AND length(trim(participant_identity)) > 0),
    CONSTRAINT ck_checkpoint_values CHECK (
        counter_epoch BETWEEN 0 AND 2147483647
        AND last_incident_points_total BETWEEN 0 AND 2147483647
        AND last_replay_session_number BETWEEN 0 AND 2147483647
        AND last_replay_session_time_ms BETWEEN 0 AND 922337203685477
        AND updated_at_utc_ms BETWEEN -62135596800000 AND 253402300799999)
);

INSERT INTO IncidentCheckpointV3 (
    session_id,
    participant_identity,
    counter_epoch,
    last_incident_points_total,
    last_replay_session_number,
    last_replay_session_time_ms,
    updated_at_utc_ms)
SELECT
    session_id,
    'local-player',
    counter_epoch,
    last_incident_points_total,
    last_replay_session_number,
    last_replay_session_time_ms,
    updated_at_utc_ms
FROM IncidentCheckpoint;

DROP TABLE IncidentCheckpoint;

ALTER TABLE IncidentCheckpointV3 RENAME TO IncidentCheckpoint;

PRAGMA user_version = 3;
