CREATE TABLE CustomEvent (
    custom_event_id TEXT NOT NULL CONSTRAINT pk_custom_event PRIMARY KEY,
    session_id TEXT NOT NULL,
    replay_session_number INTEGER NOT NULL,
    replay_session_time_ms INTEGER NOT NULL,
    submitter_name TEXT NOT NULL,
    occurred_at_utc_ms INTEGER NOT NULL,
    synchronized_at_utc_ms INTEGER NULL,
    CONSTRAINT fk_custom_event_session FOREIGN KEY (session_id)
        REFERENCES "Session" (session_id) ON UPDATE RESTRICT ON DELETE CASCADE,
    CONSTRAINT ck_custom_event_id_uuid CHECK (
        length(custom_event_id) = 36
        AND substr(custom_event_id, 9, 1) = '-'
        AND substr(custom_event_id, 14, 1) = '-'
        AND substr(custom_event_id, 19, 1) = '-'
        AND substr(custom_event_id, 24, 1) = '-'
        AND substr(custom_event_id, 15, 1) = '5'
        AND substr(custom_event_id, 20, 1) IN ('8', '9', 'a', 'b')
        AND custom_event_id = lower(custom_event_id)
        AND length(replace(custom_event_id, '-', '')) = 32
        AND replace(custom_event_id, '-', '') NOT GLOB '*[^0-9a-f]*'),
    CONSTRAINT ck_custom_event_position CHECK (
        replay_session_number BETWEEN 0 AND 2147483647
        AND replay_session_time_ms BETWEEN 0 AND 922337203685477),
    CONSTRAINT ck_custom_event_submitter CHECK (
        length(submitter_name) BETWEEN 1 AND 128
        AND length(trim(submitter_name)) > 0),
    CONSTRAINT ck_custom_event_times CHECK (
        occurred_at_utc_ms BETWEEN -62135596800000 AND 253402300799999
        AND (synchronized_at_utc_ms IS NULL
            OR synchronized_at_utc_ms BETWEEN occurred_at_utc_ms AND 253402300799999))
);

CREATE INDEX ix_custom_event_session_time
    ON CustomEvent (session_id, replay_session_number, replay_session_time_ms);

CREATE INDEX ix_custom_event_pending
    ON CustomEvent (session_id, synchronized_at_utc_ms)
    WHERE synchronized_at_utc_ms IS NULL;

PRAGMA user_version = 5;
