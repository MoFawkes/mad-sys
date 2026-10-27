# AQI Clock v0.15.5

Stable Windows users can now opt into beta updates directly from the app.

- Open **Settings → About → Include beta updates**, enable the switch, then **Save and restart AQI Clock** to receive newer beta releases.
- Beta updates are off by default. Turning the switch off prevents pending beta updates from being applied and returns to stable updates when a newer stable version becomes available; it does not downgrade the installed version.

This release backports the update preference onto v0.15.4. The v0.16.0 beta features continue in the beta channel. Android is unchanged.

## Verification

Release build passed with zero warnings or errors. All 311 local domain, application and integration tests passed, including beta preference persistence and disabling. One interactive desktop test and 64 database tests were skipped locally; the release workflow runs the database checks in CI. Installed stable-to-beta update behaviour has not been manually verified for this patch.
