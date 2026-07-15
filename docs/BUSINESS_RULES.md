# AQI Clock — Business Rules

Status: Draft 1.0 · Last updated: 2026-07-15

Plain-language, normative answers to "what happens when…" questions. Where a rule is implemented by the schedule engine, the authoritative technical statement is in ARCHITECTURE.md §4 (calculation), §6 (sync), §7 (notifications); this document must never contradict those sections.

---

## 1. Which timetable wins if two rules apply?

Strict precedence, evaluated per calendar date:

1. **Date override** for that exact date (including "Closed") — always wins.
2. Otherwise the **week schedule** entry for that weekday.
3. Otherwise **no school** (weekday has no assignment).

There can never be two overrides for the same date (unique constraint), so no tie is possible. An override referencing a timetable that no longer exists in the local cache is treated as "Closed" — the app never guesses.

## 2. What happens before the first lesson?

- The clock runs normally; the current-lesson card shows **"No lesson right now"**.
- The next-lesson line shows the first period of today ("Next: Registration at 08:25").
- No countdown/progress bar is shown (there is nothing to count down).
- No notifications fire before the first period's start toast.
- The same rules apply between periods and after the last period (after the last period, "next" comes from the following school day).

## 3. What if a lesson overlaps another?

- Overlaps are **allowed** in the data (an admin is warned at save time but not blocked — some schools genuinely run parallel blocks).
- At any instant, the **current** lesson is the overlapping period that **started latest**; if two started at the same minute, the one with the lower sort order wins.
- Both periods still appear in today's list; admins see a small warning marker on overlapping rows, staff see no warning.
- Both periods still get their own start and end-warning notifications.

## 4. What if today has an override?

- The override's timetable completely **replaces** the weekday's normal timetable for that date — the two are never merged.
- An override of **"Closed"** means: no lessons today, no lesson notifications today, and the next-lesson line looks ahead to the next school day.
- Overrides affect exactly one date; tomorrow reverts automatically to the week schedule.
- Creating or deleting an override for **today** takes effect on every client within seconds (same propagation as any edit — see rule 9).

## 5. What if the Ramadan timetable is active?

"Ramadan Day" is an ordinary timetable — there is no special mode. Two supported ways to activate it:

- **Recommended for a whole period (e.g. Ramadan):** the admin edits the **week schedule** to point the school weekdays at "Ramadan Day" on the first day, and points them back afterwards. Two minutes of admin work per switch.
- **For scattered single dates:** date overrides.

Either way every client follows automatically. Bulk-creating overrides for a date range is a post-MVP convenience (SPECIFICATION.md §4).

## 6. What if the computer starts halfway through a lesson?

- The app recomputes everything from the local cache at startup — it keeps no runtime state, so it immediately shows the correct current lesson, remaining time, and progress.
- The missed start notification: if the lesson started **within the last 120 seconds**, the toast still fires once (late but useful); if it started earlier than that, it is skipped silently — notifying about old events is noise.
- A notification that already fired before a restart never fires again (the fired-log is persisted on disk).

## 7. What if the PC wakes from sleep?

Same rules as a restart (rule 6): the app detects the time jump within a second, recomputes the display, and rebuilds its pending notifications. Boundaries that passed during sleep fire only if they are within the 120-second grace window; everything older is skipped silently. The end-warning for a lesson still in progress after waking is preserved and fires at its normal time.

## 8. What if the internet disappears?

- **Nothing visible breaks.** Clock, current/next lesson, today's list, and lesson notifications all keep working from the local cache — for hours, days, or weeks.
- A status line shows "Offline — last synced <time>"; after 7 days it escalates to a stronger "timetable may be out of date" warning.
- **Editing is unavailable offline** (by design — ADR-007), as are announcements posting and the audit view. Sign-in also requires internet, but an already-signed-in user keeps full read access even if their session expires while offline.
- When the connection returns, the app resynchronises automatically (no user action); any announcements posted while it was offline appear then, and still-unexpired ones raise a toast.

## 9. What if the admin changes the timetable during a lesson?

- All online clients receive the change within a few seconds and recompute immediately: the lesson card, countdown, and today's list update on the next tick.
- If the **current lesson was deleted** or its times moved so that nothing is active now, the display flips to "No lesson right now".
- Pending notifications are rebuilt against the new timetable:
  - A boundary that no longer exists never fires.
  - A boundary already notified does not re-notify just because the row was touched.
  - A boundary **moved to a later time** will notify again at the new time — intentional, since that's the informative behaviour.
- Offline clients keep the old timetable until they reconnect, then adopt the change (rule 8). Two admins editing the same row simultaneously: last save wins; the losing editor was warned beforehand if the change arrived while they had unsaved edits.

## 10. Related hard rules (for completeness)

| Rule | Statement |
|---|---|
| Wall-clock times | "08:30" always means 08:30 on the local clock, including across daylight-saving changes (ADR-006) |
| No midnight crossing | A period must start and end within one calendar date |
| Invalid periods | A period whose end ≤ start is rejected at edit time; if ever present in cached data it is treated as never active |
| Closed vs empty | "Closed" (override) and "no timetable assigned" display identically to staff ("No lessons today") |
| Staff cannot write | Read-only for staff is enforced by the server (RLS), not just hidden buttons |
| Every edit is audited | Who/what/when/before/after, recorded server-side, not forgeable from the app |
