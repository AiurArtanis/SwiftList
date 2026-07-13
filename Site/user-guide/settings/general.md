# General

Four tabs: **System**, **Search Bar Layout**, **Preview**, and **Search Window**.

## System

- **Start SwiftList with Windows** — checkbox, launches SwiftList at sign-in.
- **Run as Administrator** — checkbox, only enabled if the current user account is an
  administrator. Elevates just the background hotkey-listener process (not the whole app), so the
  global hotkey isn't blocked by other elevated (as-admin) foreground applications.
- **Auto check for updates on startup** — checkbox.
- **Auto silent update when a new version is detected** — checkbox, only enabled while the check
  above is on; downloads and installs updates in the background without prompting.
- **Enable hardware acceleration** — checkbox, on by default. Turning it off forces the quick
  search window to render in software instead of using Direct3D — this works around NVIDIA
  Advanced Optimus refusing to hot-switch GPUs while SwiftList is running (only the quick window is
  affected, not the whole app). Requires restarting SwiftList to take effect.
- **Log level** — dropdown: Error / Warn / Info (default) / Debug. Controls verbosity across the
  App, Service, and Hook logs (see [Service Status](./service-status)).
- **Interface language** — dropdown, populated from every installed translation provider (built-in
  languages plus any a plugin adds).
- **Interface theme** — dropdown: Light, Dark, Nord, Sakura, Cyberpunk from the built-in
  CoreExtensions plugin, plus Neon Genesis, Sakura Bloom, and Weathering Blue from the bundled
  AnimeThemes plugin if it's installed and enabled (any other theme plugin can add more).

## Search Bar Layout

Customizes the size, corner rounding, and on-screen position of the quick search bar:

- **Search Bar Width (px)** — range 300–1200px, default 570px.
- **Search Bar Height (px)** — range 45–120px, default 55px. This one number also drives the
  overall UI scale factor (`height / 70`) for result-row icons and fonts, so a taller search bar
  scales the whole result list up with it.
- **Corner Radius (px)** — range 0–50px, default 8px.
- **Result Icon Size (px)** — range 16–64px, default 55px. Result name/path font size scales with
  this (with a legibility floor), independent of the search bar height above.
- **Show clock in search box** — checkbox, off by default. While the search box is empty, replaces
  the usual "Type to search..." placeholder text with the current date, day of week, and time
  instead. Disappears the moment you start typing, same as the placeholder it replaces.
- **Reset Layout Settings** button — restores all five values above to their defaults.

Right-clicking the status icon inside the quick window's search box resets just its on-screen
position (not size), re-centering it the same way it centers on first launch.

## Preview

- **Preview Width (px)** — range 250–900px.
- **Preview Height (px)** — range 250–1200px, default sized so the pane isn't overly tall on a
  typical display.
- **Reset Preview Window Settings** button.

The preview window ignores the current result count — it's a fixed size, not a size that grows
with content. See [Actions Menu & Preview](../actions-and-preview) for how the pane is positioned.

## Search Window

Sets the default size of the full/main search window (the larger window you get from the taskbar
or Start Menu shortcut, as opposed to the quick popup — see
[Getting Started](../getting-started#the-three-windows)):

- **Window Width (px)** — range 640–2000px, default 854px.
- **Window Height (px)** — range 400–1400px, default 480px. The minimums match the window's own
  resize floor, so a configured value is never silently overridden by the window itself.
- **Reset Search Window Settings** button.

Dragging the window's edge to resize it manually is remembered automatically — the next time you
open the window (or open a new one), it comes back at whatever size you last left it at, and this
page's fields update to match. Resizing while maximized doesn't overwrite the remembered size; only
resizing in the normal (non-maximized) state does.
