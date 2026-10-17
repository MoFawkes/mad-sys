# Windows 10 taskbar toolbar

Optional x64 Windows 10 (1809+) deskband, enabled from **Settings → Display → Windows 10 taskbar display → Save**. It shows the same current period and countdown as the desktop clock, with next-period details on hover. Click, Enter, or Space opens AQI Clock through its existing activation event. If the app is stopped, clicking launches its stable executable with `--show`.

The toggle is disabled on Windows 11, ARM64, and 32-bit Windows. Automatic showing depends on the shell: if needed, right-click the Windows 10 taskbar and select **Toolbars → AQI Clock**. Unlock the taskbar to position or resize the band. Auto-hide hides it along with the taskbar. Sharing an entire desktop can include the toolbar.

## Build and verification

From PowerShell 7 at the repository root:

```powershell
./scripts/build-taskbar.ps1 -Test
dotnet build AqiClock.sln --configuration Release
```

The script downloads the pinned Zig 0.14.1 Windows toolchain, verifies its SHA-256, and keeps it under ignored `artifacts/tooling`. No Visual Studio C++ installation is required. Output is `artifacts/taskbar/AqiClock.Taskbar.dll`; the WPF project copies it into `taskbar/` when present. CI builds and tests it before the .NET build. The release workflow builds it before publishing. A development build without the DLL reports the missing component and cannot enable it.

The native test executable creates its own parent window rather than registering anything in Explorer. It checks COM interfaces, lifecycle, horizontal/vertical sizes, rendering, focus preservation, snapshot expiry, bounded text, and partial-write rejection. A display-binding case uses the real mapping name only if no app already owns it. Real Windows 10 shell acceptance remains required; see `docs/MANUAL-TESTS.md`.

## Runtime boundary

- Native C++ COM DLL implementing `IDeskBand2`, `IObjectWithSite`, `IPersistStream`, and `IInputObject`. Explorer loads no .NET runtime or app services.
- All registration lives under the current user's 64-bit `Software\Classes\CLSID`. No elevation or Explorer restart is requested. Registration/visibility failures appear in Settings and the app log.
- The app publishes a fixed 2,064-byte UTF-16 snapshot to `Local\AqiClock.Taskbar.v1.<current-user-SID>`. The session-local kernel mapping uses the creating user's default Windows security descriptor. It contains display labels only, never credentials. The toolbar opens it read-only and never writes to the app.
- Sequence counters reject partial updates. Version, string bounds, and a five-second heartbeat are checked before displaying a snapshot. Exit, crash, suspend, and sign-out cannot leave an indefinitely frozen countdown. No disk reads, network, or app calls occur in the refresh path.
- The toolbar follows the system taskbar light/dark setting and Windows high-contrast colours, scales with DPI, and keeps the countdown visible ahead of a long period name. Vertical taskbars use two lines; hover gives the full details.
- Registration points to a content-hashed immutable DLL under `%LOCALAPPDATA%\AqiClock\taskbar`, outside Velopack version directories. Updating the app does not overwrite a DLL loaded by Explorer. An existing toolbar may keep using its old native binary until removed/re-added or until the next Windows sign-in; protocol v1 stays compatible. Old DLL copies are deliberately retained while Explorer might have them loaded.
- Disabling hides/unregisters the toolbar and closes its snapshot publisher. Uninstall requests removal and unregisters this user's component. Loaded, immutable DLL copies may remain on disk until removed after Windows sign-out.

Microsoft references: [band objects](https://learn.microsoft.com/en-us/windows/win32/shell/band-objects), [IDeskBand2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nn-shobjidl-ideskband2), [ITrayDeskBand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl/nn-shobjidl-itraydeskband).
