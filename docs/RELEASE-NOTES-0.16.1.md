# AQI Clock v0.16.1

Windows patch for announcement visibility and notifications when the device and institute use different timezones.

## Fixed

- Check announcement publication and expiry against the current UTC instant. Previously, institute wall time was interpreted in the device timezone, which could hide a valid announcement as expired or not yet published.
- Use the same absolute time for announcement notifications, read timestamps and relative ages.

## Install and update

Existing Windows installations receive v0.16.1 through the stable updater. The Windows Setup executable and portable ZIP are attached to this release. Android remains on v0.16.0; no database migration is required.

## Verification

The focused Windows test run passed all 130 cases, including six regression cases covering institute time ahead of and behind device time and the clock's UTC instant. The complete local Release application run passed 267 cases, with one opt-in interactive case skipped.

All four [tag-bound hosted CI gates and Windows packaging](https://github.com/MoFawkes/mad-sys/actions/runs/42519670014) passed. Release source is `20a4966d3abd16569cfe1b94b910db23577b7dd9`. Verified the downloaded asset sizes and SHA256 digests, updater SHA1/SHA256 hashes, packaged version/source, public configuration and matching portable contents. SHA256SUMS.txt accompanies the six public assets. Installed-device reproduction and update installation remain manual acceptance checks.
