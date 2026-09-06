ALTER TABLE ApplicationPreferences
ADD COLUMN theme_preference INTEGER NOT NULL DEFAULT 0
    CONSTRAINT ck_preferences_theme CHECK (theme_preference IN (0, 1, 2));

PRAGMA user_version = 2;
