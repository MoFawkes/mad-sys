DROP TABLE week_schedule;
CREATE TABLE week_schedule (
    id TEXT PRIMARY KEY,
    weekday INTEGER NOT NULL,
    audience_class_id TEXT NULL,
    timetable_id TEXT NULL
);
CREATE INDEX ix_week_schedule_weekday ON week_schedule(weekday);
