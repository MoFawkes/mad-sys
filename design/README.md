# Desktop UI reference

Mockups for the Windows desktop client, mirroring the `mobile/design/` convention.

| File | Screen | Implemented by |
|---|---|---|
| `role-choice.html` | Teacher / Student audience chooser | `src/AqiClock.App/Views/RoleChoiceWindow.xaml` |

## `role-choice.html`

Supplied by the owner on 2026-08-01. Open it in a browser to view — it pulls Tailwind and
Google Fonts from CDNs, so it needs an internet connection to render correctly.

**It is a composition reference, not a colour reference.** Owner decision on the same date:
keep the layout, spacing, proportions and interactions; take colours from the app's existing
`DynamicResource` brushes rather than the mockup's palette. The mockup uses `#031134` navy
with `#cce0fb` pale blue, whereas the app ships Navy `#24457A` with Cream from v0.10.0, and a
one-window exception would look inconsistent beside the main window, Settings and Admin.

Deliberate departures — WPF has no per-element `backdrop-filter`, the ambient blur blobs are
too expensive for low-end classroom PCs, the icons come from WPF-UI rather than Material
Symbols, and `Version 4.2.0 | Secured Environment` is mockup filler.

Not yet implemented. Deferred until after the v0.11.0 release; the implementation brief
covers sizing constraints for 1366×768 hardware, Light-theme handling, and keyboard access.
