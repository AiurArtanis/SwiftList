# Instant Answers & Keyword Shortcuts

Some results appear instantly as you type, without waiting for a file search — either always on,
or gated behind a short keyword you type before your actual query (so a large feature like browser
history search doesn't compete for attention on every unrelated keystroke). Most keyword-gated
ones have their keyword configured in **Settings → Plugins → Configure**.

## Always on

These need no keyword — they activate as soon as what you type matches their pattern.

### Calculator

Type a math expression directly and the result appears live, with **Enter** copying it to the
clipboard:

```
12 * (4 + 3)
```

Explicit base conversion is also supported:

```
255 to hex
0xFF to dec
```

### Environment variables

- `%NAME%` expands a specific variable (including multi-path variables like `%PATH%`, split into
  one entry per path).
- `%partial` fuzzy-lists every variable whose name matches `partial`.

### Run a command

- `#<command>` opens a Command Prompt window and runs `<command>` **as Administrator**.
- `$<command>` opens a Command Prompt window and runs `<command>` normally.

### Bare URLs

Type or paste a `http://`/`https://` address and SwiftList offers to open it directly.

## Keyword-triggered (built-in, configurable prefix)

Type the keyword, a space, then your query. Each keyword defaults to a short prefix but can be
changed independently in that plugin's own **Configure** dialog if it collides with something you
type often.

| Keyword (default) | Plugin | What it searches |
|---|---|---|
| `ps` | Process Manager | Running processes by name — select one to kill it. |
| `tr` | Translation | Translates the typed text, auto-detecting source language and translating to/from your interface language. |
| `bm` | Browser Data | Bookmarks and history from Chrome/Chromium-family and Firefox-family browser profiles you've added under that plugin's own settings. |

## Web search

Web Search ships with default keywords for several engines/sites — `bd` (Baidu), `g` (Google),
`bing` (Bing), `gh` (GitHub), `wiki` (Wikipedia), `yt` (YouTube) — and lets you add, edit, or
remove entries entirely, each with its own name, keyword, icon, and URL template, from
**Settings → Plugins → Web Search → Configure**.

```
g swiftlist github
```

opens a Google search for "swiftlist github" in your browser.

## Fully custom (you define the keyword)

### Custom Commands

Define your own `<keyword> <args>` commands that launch an external program, configured entirely
under **Settings → Plugins → Custom Commands → Configure**. The command's parameter template
supports positional placeholders (`%s1`, `%s2`, ... for each space-separated argument) and a
whole-remainder placeholder (`%s` for everything typed after the keyword).

---

None of the plugins on this page are required — each can be disabled independently under
[Settings → Plugins](./settings/plugins) if you don't want it competing for keyboard space, and
[Search Syntax](./search-syntax) covers the separate, always-active fuzzy query language used for
everything else (files, folders, applications).
