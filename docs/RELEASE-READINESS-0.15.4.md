# v0.15.4 stable release readiness

Target requested by owner: **Sunday 25 October 2026**, general stable Windows release. This replaces the earlier limited-prerelease rollout target; it does not mark unfinished acceptance complete.

Current decision: **not ready to publish**. PR [#34](https://github.com/MoFawkes/mad-sys/pull/34) and `v0.15.4-beta.2` remain drafts. Latest published stable is `v0.15.3`.

## Evidence and remaining checks

Detailed observations and the complete TB-01–TB-08 checklist are in [MANUAL-TESTS.md](MANUAL-TESTS.md). Partial/component evidence must not be substituted for an unchecked OS case.

| Area | Recorded evidence | Work still required |
| --- | --- | --- |
| Corrected snapshot reader | Regression failed before precision fix, passed after; corrected beta.2 DLL hashes verified in installed guest | Real active countdown and period transition on corrected build |
| Activation | Corrected beta.2 toolbar click and explicit minimize/restore cycle passed on 24 October | Full tooltip/lifecycle combination and repeated recovery cases |
| Focus and presenting | Earlier synthetic-publisher, sender-side Zoom checks only | Real beta.2 lesson transition with conferencing in front; typing focus; whole-screen and application-window sharing |
| Sleep/resume | VM offers no Windows Sleep option | Suitable Windows 10 hardware; emulator pause does not qualify |
| Sessions and timetable | Earlier installed sign-out and stale clearing, plus independent local-scope Auth regression | Full class-change/selection behavior on corrected build |
| Display matrix | Earlier idle 100%/125%, bottom/top/left, light/dark and auto-hide observations | 150%/200%, high contrast, crowded/resized layouts, full truncated tooltips |
| Lifecycle | Earlier per-user install/update/uninstall, corrected beta.2 successful install hook, boot restoration with occasional fallback | Remaining reliable boot/update/Explorer recovery combination |
| Windows 11 | Automated availability behavior covered | Installed UI explanation, compact mode and settings preservation |
| Automated release checks | Corrected beta.2 passed four gates and packaging | All required checks green for final release commit and package |

## Safe test procedure

- Use the preserved higher-resource guest child with four vCPUs, 4 GB guest RAM, four host logical processors, BelowNormal priority and software emulation. Keep it on DISPLAY2. Original recovery disk remains unchanged.
- Owner handles authentication and permission prompts. Do not run old mounted installers or use `-Install`.
- Sync on the real date. Before a cached teaching-day clock test, disconnect guest networking. Restore automatic time and verify the real date before reconnecting. Do not edit institute data to manufacture a lesson.
- Record countdown, transition and focus results separately from transmitted sharing results if networking is disconnected. Do not claim the combined conferencing case from separate partial tests.
- Shut the guest down normally after testing. No host service, security or startup changes.

## Publication sequence after acceptance

1. Record final manual results and resolve failures. Confirm remaining TB-01–TB-08 acceptance is adequate for a general stable release.
2. Review the final PR diff and green required checks; merge only when release readiness is established.
3. Prepare stable v0.15.4 notes and the exact release commit. Do not relabel the beta package as stable.
4. Create the stable tag only at the intended publication step: the current stable-tag workflow publishes automatically after its gates and packaging succeed.
5. Verify the published stable flag, Latest selection, version, Setup/portable/package/manifest assets and hashes. Verify stable update behavior and retain a rollback path to v0.15.3.

No stable tag or publication has been performed for this target.
