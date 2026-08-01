# Mobile UI reference

Stitch-generated mockups for the seven v0.11.0 screens, in flow order. Screens 4–7
are the corrected second round; 1–3 were right first time and were not regenerated.

| File | Screen | Route |
|---|---|---|
| `1-role-choice.png` | Teacher / Student | `app/role-choice.tsx` |
| `2-teacher-sign-in.png` | Email + password | `app/sign-in.tsx` |
| `3-student-join-code.png` | 16-character join code | `app/student-setup.tsx` |
| `4-class-picker.png` | Classes + independent Naseehah AM/PM | `app/student-setup.tsx` |
| `5-clock.png` | Clock, NOW card, today's periods | `app/(tabs)/clock.tsx` |
| `6-announcements.png` | Announcement list | `app/(tabs)/announcements.tsx` |
| `7-settings.png` | Settings (student variant shown) | `app/(tabs)/settings.tsx` |

## How to use these

**Layout and hierarchy only.** `mobile/src/ui/theme.ts` is the source of truth for
colour. The mockups happen to be close now — they lead with `#F4F0E6` and `#112549`
— but where they disagree with the theme, the theme wins.

## Known-wrong chrome — do not replicate

Stitch has no model of the navigation graph, so the per-screen chrome is unreliable
even where the content is right:

1. **Hamburger icons** on Announcements and the class picker — there is no drawer.
   Tab roots take no leading icon.
2. **The class picker shows a tab bar with "Announcements" active.** It is a setup
   flow reached before the tabs exist and must have no tab bar at all.
3. **Settings is missing the tab bar** that Clock and Announcements both have.
4. **The clock's status strip overlaps the last row of the Today list.** The list
   needs bottom padding equal to the strip height so it scrolls clear.

Smaller: the settings slider knob sits far-left while the value reads "5 min", and
"My settings" groups "Change my classes" (a preference) with "Sync now" (a data
action) — split those.

## States not covered

One render per screen, so these specified states have no mockup and must still be
built: the clock's no-lesson / closed-day / offline / stale variants, the class
picker's "Select at least one class", the sign-in error, the announcements empty
state, and the teacher variant of Settings.
