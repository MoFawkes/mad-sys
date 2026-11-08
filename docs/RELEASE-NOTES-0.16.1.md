# AQI Clock v0.16.1

Windows patch for announcement visibility and notifications when the device and institute use different timezones.

## Fixed

- Check announcement publication and expiry against the current UTC instant. Previously, institute wall time was interpreted in the device timezone, which could hide a valid announcement as expired or not yet published.
- Use the same absolute time for announcement notifications, read timestamps and relative ages.

## Install and update

Existing Windows installations receive v0.16.1 through the stable updater. The Windows Setup executable and portable ZIP are attached to this release. Android remains on v0.16.0; no database migration is required.

## Verification

The focused Windows test run passed all 130 cases, including six regression cases covering institute time ahead of and behind device time and the clock's UTC instant. Installed-device reproduction and update installation remain manual acceptance checks.
