# Plugins

Lists every installed plugin, with the currently-loaded Plugin SDK version shown as a badge in the
page header (relevant if you're pairing a plugin against the
[Developer Manual](../../dev-guide/)).

## Per-plugin card

Each installed plugin gets a card showing its icon, name, version, DLL filename, and an **overall function description**.

Click the card to expand it and see its registered components, grouped by type (search providers, dynamic menu providers, etc.) — each toggleable component has its own **enable/disable checkbox**; a component marked as required shows a lock icon instead and can't be turned off. Hovering over a component reveals its **detailed function tooltip**.

When a group (or the plugin as a whole) has more than one toggleable component, a **Select All / Deselect All** link appears next to its header, letting you flip every checkbox in that scope at once instead of one at a time.

If a plugin exposes its own configuration (custom settings beyond simple enable/disable), a
**Configure** button appears in the card header, opening that plugin's own settings dialog.

A banner at the bottom of the page reminds you that some component toggles only take effect after
restarting SwiftList.

If no plugins are installed, the page shows an empty-state message instead.

For a concrete example of what a plugin's own **Configure** dialog looks like in practice (e.g.
changing a trigger keyword), see [Instant Answers & Keyword Shortcuts](../instant-answers).
