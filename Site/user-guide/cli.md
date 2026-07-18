# Command-Line Search (slf)

SwiftList also ships a small command-line companion, **`slf`** — an fzf-style fuzzy finder that
searches through the same index the App itself already maintains, instead of duplicating any of
that setup. It's for anyone who lives in a terminal and wants SwiftList's search (fuzzy matching,
pinyin aliasing, network drives, everything) available there too, not just in the search windows.

`slf` needs the SwiftList App to already be running — it talks to the App over a local, per-user
pipe rather than re-scanning anything itself. If the App isn't running, `slf` fails fast with an
error on stderr instead of hanging.

## Setting it up

`slf.exe` is installed alongside the App. Check **Add the slf command-line search tool to PATH**
on the installer's task selection page to make it runnable as `slf` from any terminal — this adds
SwiftList's install folder to your system PATH. Open a new terminal window afterward; one already
open when you installed won't pick up the change.

If you skipped that option, you can still run it directly from wherever SwiftList is installed.

## Basic usage

```
slf
```

opens an interactive picker: type to fuzzy-filter, exactly like the App's own search — including
pinyin-alias matching for Chinese filenames (see [Search Syntax](./search-syntax)).

| Key | Action |
|---|---|
| Type | Filter results |
| ↑ / ↓ | Move the highlight |
| Page Up / Page Down | Jump a page at a time |
| ← / → | Move the text cursor within the query |
| Tab | Toggle-select the highlighted result (marked rows show `*`) |
| Enter | Print the selected path(s) — or just the highlighted one, if nothing's marked — and exit |
| Esc / Ctrl+C | Exit without printing anything |

## Pre-filling the query

```
slf report
```

and

```
echo report | slf
```

both open already filtered to `report`. Either way this only pre-fills the query box and starts
the same search typing it would — it never auto-selects or auto-prints a result on its own, so you
still navigate and press Enter/Tab yourself.

## Selecting multiple results

Tab marks or unmarks the highlighted row. Marked rows persist even after you change the query — so
you can search for one file, mark it, search for something else, mark that too, and so on. The
status line shows how many are currently marked. Pressing Enter while anything is marked prints
every marked path, one per line, regardless of what's currently highlighted.

## Using the result in another command

`slf`'s interactive picker is drawn directly to the console, never through the normal
input/output streams — the only thing that ever goes to stdout is the final selected path(s), one
per line, printed on Enter. That's what lets its output compose with the usual shell techniques
for capturing another command's result.

PowerShell:

```powershell
code (slf)
$path = slf; code $path
```

cmd.exe (no built-in command substitution — use `for /f`):

```cmd
for /f "delims=" %i in ('slf') do code "%i"
```

## Limitations

- No preview pane — deliberately out of scope, since the App's own [Actions Menu &
  Preview](./actions-and-preview) already covers that.
- Requires the SwiftList App to be running; `slf` doesn't index anything on its own.
