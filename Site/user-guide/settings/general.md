# General

Three tabs: **System**, **Search Bar Layout**, and **Preview**.

## System

- **Start SwiftList with Windows** — checkbox, launches SwiftList at sign-in.
- **Run as Administrator** — checkbox, only enabled if the current user account is an
  administrator. Elevating SwiftList avoids the global hotkey being blocked by other elevated
  (as-admin) foreground applications.
- **Auto check for updates on startup** — checkbox.
- **Auto silent update when a new version is detected** — checkbox, only enabled while the check
  above is on; downloads and installs updates in the background without prompting.
- **Log level** — dropdown: Error / Warn / Info (default) / Debug. Controls verbosity across the
  App, Service, and Hook logs (see [Service Status](./service-status)).
- **Interface language** — dropdown, populated from every installed translation provider (built-in
  languages plus any a plugin adds).
- **Interface theme** — dropdown: Light, Dark, Nord, Sakura, Cyberpunk (more can be added by theme
  plugins).

## Search Bar Layout

Customizes the size, corner rounding, and on-screen position of the quick search bar:

- **Search Bar Width (px)**
- **Search Bar Height (px)** — default 70px. This one number also drives the overall UI scale
  factor (`height / 70`) for result-row icons and fonts, so a taller search bar scales the whole
  result list up with it.
- **Corner Radius (px)**
- **Result Icon Size (px)** — range 16–64px, default 42px. Result name/path font size scales with
  this (with a legibility floor), independent of the search bar height above.
- **Reset Layout Settings** button — restores all four values to their defaults.

## Preview

- **Preview Width (px)** — range 250–900px.
- **Preview Height (px)** — range 250–1200px, default sized so the pane isn't overly tall on a
  typical display.
- **Reset Preview Window Settings** button.

The preview window ignores the current result count — it's a fixed size, not a size that grows
with content. See [Actions Menu & Preview](../actions-and-preview) for how the pane is positioned.
