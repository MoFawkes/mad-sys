# v0.15.4 stable release readiness

Target requested by owner: **Sunday 25 October 2026**, general stable Windows release. This replaces the earlier limited-prerelease rollout target; it does not mark unfinished acceptance complete.

Current decision: **owner approved general stable publication with the remaining manual risks accepted**, on 25 October 2026. Publication is pending final green checks and packaging. The incomplete cases below remain untested; this decision does not turn them into passes. Latest published stable at the time of approval is `v0.15.3`.

After the outstanding cases and likely failure areas were explained, the owner asked: “can we release the taskbar update and ill lyk if we encounter errors tomorrow”. In the existing general-stable scope, this authorizes publishing v0.15.4 and following up on reported errors. It supersedes the earlier hold on merging/tagging, while retaining automated gates, honest release notes and a fallback to v0.15.3. The optional taskbar setting defaults to Off. No teacher consent or results are inferred.

## Evidence and remaining checks

Detailed observations and the complete TB-01–TB-08 checklist are in [MANUAL-TESTS.md](MANUAL-TESTS.md). Partial/component evidence must not be substituted for an unchecked OS case.

| Area | Recorded evidence | Work still required |
| --- | --- | --- |
| Corrected snapshot reader | Regression failed before precision fix, passed after; installed beta.2 DLL hashes verified; cached Lesson 1 countdown and transition to Lesson 2 passed on 24 October while minimized | Continuous every-second behavior with conferencing in front remains unverified |
| Activation | Corrected beta.2 click/minimize/restore passed; 25 October idle next-lesson tooltip and click after explicit Explorer restart passed; native component tests preserve full long tooltip across narrow/wide sizes and replace tooltip-only updates | Actual Explorer active/truncated tooltip details and remaining lifecycle/repeated recovery cases |
| Focus and presenting | Corrected beta.2 stayed minimized through a cached lesson transition with Settings foreground; earlier synthetic-publisher, sender-side Zoom checks only. On 25 October, a longer Teams retry reached `/v2/` then **Oops, app failed to load!**; Zoom remained blank/Untitled | Real beta.2 lesson transition with conferencing in front; typing focus; whole-screen and application-window sharing with receiver verification |
| Sleep/resume | VM offers no Windows Sleep option | Suitable Windows 10 hardware; emulator pause does not qualify |
| Sessions and timetable | Earlier installed sign-out/stale clearing and local-scope Auth regression; unchanged beta.2 local-fixture picker/cache/clock/formatter passed empty validation, Alpha→Beta replacement and remembered Beta selection | Full installed Windows 10 class-change/native-publication and authenticated acceptance |
| Display matrix | Earlier idle 100%/125%, bottom/top/left, light/dark and auto-hide; 25 October idle High Contrast Black and 1920×1080 at 150% label/tooltip/click passed; original theme and 1024×768/100% restored | 200% (menu offered up to 175% at 1920×1080), active/long labels and remaining high-contrast coverage, crowded/resized layouts, full truncated tooltips |
| Lifecycle | Earlier install/update/uninstall and beta.2 hook; initial-null-byte settings input explained default Off. Later valid saved preference survived boot without another Save; one idle Explorer restart restored a live band and click activation | Corrupted-input origin; reliable automatic startup timing, remaining update/active-recovery and repeated cases |
| Windows 11 | On build 26300, unchanged beta.2 packaged UI in an isolated harness disabled/explained the toolbar, saved another preference with stored taskbar=true, and passed compact/normal switching plus compact relaunch | Full installed-client/normal-startup acceptance; harness scope is recorded in manual tests |
| Automated release checks | Corrected beta.2 passed four gates and packaging | All required checks green for final release commit and package |

## Safe test procedure

- Use the preserved higher-resource guest child with four vCPUs, 4 GB guest RAM, four host logical processors, BelowNormal priority and software emulation. Keep it on DISPLAY2. Original recovery disk remains unchanged.
- Owner handles authentication and permission prompts. Do not run old mounted installers or use `-Install`.
- Sync on the real date. Before a cached teaching-day clock test, disconnect guest networking. Restore automatic time and verify the real date before reconnecting. Do not edit institute data to manufacture a lesson.
- Record countdown, transition and focus results separately from transmitted sharing results if networking is disconnected. Do not claim the combined conferencing case from separate partial tests.
- Shut the guest down normally after testing. No host service, security or startup changes.

## Publication sequence after owner risk acceptance

1. Preserve the manual results and unchecked TB-01–TB-08 cases above. Disclose startup uncertainty and incomplete manual coverage in the stable release notes.
2. Review the final PR diff and green required checks before merging PR [#34](https://github.com/MoFawkes/mad-sys/pull/34).
3. Prepare stable v0.15.4 notes and the exact release commit. Do not relabel the beta package as stable.
4. Create the stable tag only at the intended publication step: the current stable-tag workflow publishes automatically after its gates and packaging succeed.
5. Verify the published stable flag, Latest selection, version, Setup/portable/package/manifest assets and hashes. Verify stable update behavior and retain a rollback path to v0.15.3.

At this decision checkpoint, no stable tag or publication has yet been performed. Publication evidence will be recorded after verification.

## Evidence update — 25 October 2026

The 24 October follow-up used the corrected installed beta.2 on the four-vCPU/4 GB profile. After real-date sync, guest networking was disconnected and the guest clock set to 26 October at 09:37. Main clock Lesson 1 ended at 09:40 with about 2:23 remaining; the toolbar matched within the sampling interval. After explicit minimization, toolbar samples progressed through 2:13, 1:17 and 0:24, then Lesson 2 with 29:49 remaining. Settings stayed foreground and AQI did not restore itself. This passes sampled cached progression and transition, without completing TB-02 or TB-06.

Automatic time was restored and 24 October at 17:38 verified against the host before networking was reconnected; the idle label returned. No production data changed. Conferencing attempts completed no meeting, typing-focus, permissions, sharing or receiving-frame check. Teams may load with more time; its loading logo is not evidence that it cannot load. Hardware sleep/resume remains untested. The teacher checklist is prepared, but no teacher consent or result is recorded and nobody has been contacted.

The 25 October retries additionally passed idle High Contrast Black and 150% toolbar checks; original appearance, 1024×768 and 100% scale were restored. A one-hour guest time discrepancy was corrected by Windows time sync. The initial Off preference was traced to a logged invalid JSON input; later valid settings survived another boot without Save, although reliable automatic live startup timing remains unresolved. Longer conferencing attempts produced a Teams app-load error and a blank Zoom page; no new conferencing acceptance was gained. Detailed startup, recovery, local class-fixture and DPI scope limits are in the dated manual-test records. All four CI checks passed for head `ac6bbf2` (run `40217458736`); any later final release head still requires its own green checks and package.
