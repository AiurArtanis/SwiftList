# Appearance

Pinned above [About](./about) at the bottom of the sidebar. Two groups: **Theme Mode** and the
theme picker itself.

## Theme Mode

- **Follow system light/dark setting** — checkbox, off by default. When on, SwiftList switches
  between two themes you pick — one for when Windows is in light mode, one for dark — instead of
  using a single fixed theme, and updates immediately whenever you toggle Windows' own setting (no
  restart needed).

## Interface Theme

With "Follow system" off, a single card grid lists every installed theme. With it on, the grid
splits into two: **Light Theme** (only non-dark-flavored themes) and **Dark Theme** (only
dark-flavored ones) — each defaults to whichever matching-flavor theme happens to be installed
first, since which themes exist at all depends entirely on which theme plugins are installed.

Each theme is shown as a card, not just a name: a small mock-up of the quick search window (search
box plus a couple of result rows, one shown selected) rendered in that theme's own colors, so you
can see what a theme actually looks like before switching to it. The active theme's card shows a
checkmark badge.

Built-in and bundled theme plugins:

- **CoreExtensions** (built-in) — Light, Dark, Nord, Sakura, Cyberpunk.
- **AnimeThemes** (bundled, if installed and enabled) — Neon Genesis, Sakura Bloom, Weathering Blue.
- **Curated Themes** (bundled, if installed and enabled) — nine light/dark pairs: Glacier,
  Terracotta, Forest, Amethyst, Crimson, Graphite, Indigo, Mint, and Champagne.

Any other theme plugin can add more cards to the grid the same way.
