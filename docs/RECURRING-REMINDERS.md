# Recurring teacher reminders

Admins open **Administration → Announcements → Recurring reminders**, enter a title and message, select one or more weekdays, and leave **After every lesson** checked or uncheck it to enter a fixed 24-hour institute time. **Save reminder** creates the rule. Saved rules provide **Edit**, **Pause/Resume** and **Delete**; **New reminder** clears the form. **Enabled** also lets an admin save a paused rule. Managing rules requires a connection.

For example, select Friday and After every lesson with “Fill in the weekly chart.” The text is fully configurable; this example is not a built-in reminder.

## Delivery rules

- All active teachers and admins in the organisation receive the reminders. There is no per-teacher timetable assignment in this feature. Student devices have neither read nor write access; teachers cannot change rules.
- Each selected weekday repeats indefinitely until paused or deleted. Weekday bits use Monday=1 through Sunday=64 and permit any combination from 1 to 127.
- Lesson-end triggers use the actual day's assigned timetables, including class-specific schedules and date replacements. Only valid periods marked as lessons count. Breaks and closed dates produce no lesson-end reminder. Simultaneous lesson ends produce one notification per rule.
- Fixed-time reminders are calendar-based and still fire on selected weekdays without lessons, including closed dates. Times use the organisation's timezone.
- Announcement notifications control both kinds. Lesson-start and end-warning settings are independent. Recurring reminders do not create items in the announcements feed.
- Windows requires AQI Clock to remain running, including in the notification area. Its durable notification log prevents repeated delivery across ticks/restarts; occurrences over two minutes late are silently skipped.
- Mobile schedules the next seven days through the existing reconciliation process, capped with other notifications at the earliest 60 requests. Opening the app, syncing, or background reconciliation refreshes the window. Rule edits, pauses, deletion and timetable changes cancel stale pending requests after syncing. Offline devices keep their last synced plan until they reconnect. Notification permission and OS delivery restrictions still apply.
- Different rules at the same time remain separate messages. A moved trigger has a new time-based occurrence identity; touching unchanged data does not repeat an already delivered boundary.

## Storage and rollout

Apply `supabase/migrations/20261108160431_recurring_reminders.sql` before either updated client ships. `recurring_reminders` stores `id`, `org_id`, `title`, `body`, `weekdays`, `timing`, nullable `fixed_time`, and `is_enabled`. Database constraints reject empty/oversized messages, invalid day masks and mismatched trigger/time combinations. Fixed times must be whole minutes below 24:00.

The organisation index supports RLS filtering. Staff read within their organisation, admins write within their organisation, and cross-organisation reassignment is forbidden. Existing audit triggers record changes and the table participates in `supabase_realtime`. Students omit this table from sync. Windows cache migration 4 and mobile cache migration 7 add local copies without modifying earlier cache migrations. Older clients do not know this table and retain their existing notification behaviour.

Automated coverage includes recurrence and calendar mapping, closures/replacements, concurrent lesson boundaries, independent settings, restarts and grace rules, editor validation/actions, light/dark/small-window WPF renders, cache round trips, mobile timezone planning and cancellation, seven server personas, cross-organisation writes, database constraints and audit history. Installed-device delivery is covered by the release checklist in `MANUAL-TESTS.md`.
