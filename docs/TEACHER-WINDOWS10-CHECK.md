# Short Windows 10 hardware check

For one consenting tester before the proposed v0.15.4 stable release. The owner supplies the approved **0.15.4-beta.2** test installer manually; this is an unpublished test build, not the stable release. Confirm the version displayed by Setup before proceeding. The installer filename may contain `stable` because that is the package channel name.

## Record first

- Windows version/build, display scale, taskbar position and AQI version.
- Whether the clock currently shows a real active lesson or an idle/closed-day label.

## Checks

1. Open AQI, allow it to sync and enable **Settings → Display → Windows 10 taskbar display**. If needed, right-click the taskbar and choose **Toolbars → AQI Clock**.
2. Minimize AQI. Click its taskbar toolbar. Confirm the existing clock returns without a duplicate window. Repeat once; also try Enter and Space when the toolbar has keyboard focus.
3. With AQI minimized, open a private Teams/Zoom test session. Type a harmless test sentence in a local draft/input field while the toolbar updates. Confirm typing stays in that field and AQI does not jump in front. Do not send the sentence to a real class or colleague.
4. Share the whole test desktop, then share only the conferencing/application window. Record which source was selected and whether the viewer saw the taskbar. A second device/viewer gives stronger evidence than the sender's preview alone. Stop both shares afterward.
5. Put Windows into **Sleep** for at least two minutes using its normal power menu, then wake it. Confirm the main AQI clock shows the correct current time and the toolbar matches the current lesson/idle state. Confirm toolbar click still opens the app. Record any frozen or stale display and how long recovery took.
6. Restart Windows once. Confirm there is a single toolbar and click-to-open still works. Record if the Toolbars fallback was needed.

During a real active lesson, also record whether the countdown advances correctly while minimized and moves to the next lesson at the boundary without taking focus from Teams/Zoom. An idle-day test does **not** establish this active-lesson case. Do not change a normal work PC's date or institute timetable to manufacture a lesson.

## Reply format

```text
Windows/build:
Scale/taskbar position:
AQI version:
Active lesson or idle:
Click / Enter / Space:
Typing focus:
Desktop sharing / window sharing / receiver checked:
Sleep duration / clock after wake / toolbar after wake:
Restart / fallback needed:
Any failure, delay, or screenshot:
```

Return only test observations; no passwords or account tokens are needed. A failure or an untested item should stay explicit.
