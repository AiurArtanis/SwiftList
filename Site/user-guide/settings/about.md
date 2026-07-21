# About

Shows version numbers for the App, Core, Service, and CLI components (colored to reflect whether
the service is currently healthy), a short description of SwiftList, and a link to the project
homepage.

## Checking for updates

- **Check Update** button — queries for a newer release; the button's own label reflects progress
  ("Checking...", then either "up to date" or the new version number found).
- If a non-administrator account can't stop the background service to install an update in place,
  a warning banner explains this and points you to the manual download page instead.
- Once a new version is found:
  - **Silent Auto-Update** — downloads and installs in the background, showing a progress bar,
    then restarts SwiftList automatically.
  - **Go to Download Page** — opens the GitHub release page in your browser for a manual install.

This mirrors the **Auto check for updates** / **Auto silent update** checkboxes under
[General → System](./general#system) — those control whether this check happens automatically on
startup; this page lets you trigger it manually at any time.
