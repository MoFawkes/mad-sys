CREATE TABLE recurring_reminders (
    id TEXT PRIMARY KEY, title TEXT NOT NULL, body TEXT NOT NULL,
    weekdays INTEGER NOT NULL, timing TEXT NOT NULL, fixed_time TEXT NULL,
    is_enabled INTEGER NOT NULL
);
