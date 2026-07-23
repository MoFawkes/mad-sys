CREATE TABLE classes (id TEXT PRIMARY KEY, name TEXT NOT NULL, sort_order INTEGER NOT NULL);
CREATE TABLE period_classes (
  period_id TEXT NOT NULL,
  class_id TEXT NOT NULL,
  PRIMARY KEY (period_id, class_id)
);
CREATE INDEX ix_period_classes_class_id ON period_classes(class_id);

ALTER TABLE announcements ADD COLUMN audience_type TEXT NOT NULL DEFAULT 'everyone';
ALTER TABLE announcements ADD COLUMN audience_class_id TEXT NULL;
ALTER TABLE announcements ADD COLUMN update_type TEXT NOT NULL DEFAULT 'general';
ALTER TABLE announcements ADD COLUMN publish_at TEXT NULL;
ALTER TABLE announcements ADD COLUMN e_masjid_link TEXT NULL;
ALTER TABLE announcements ADD COLUMN status TEXT NOT NULL DEFAULT 'published';
ALTER TABLE announcements ADD COLUMN deleted_at TEXT NULL;
CREATE INDEX ix_announcements_publish_status ON announcements(status, publish_at, deleted_at);
