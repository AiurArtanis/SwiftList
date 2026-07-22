# Search Syntax

SwiftList's query box supports more than plain typing. Every operator below can be combined with
plain fuzzy terms in the same query.

## Fuzzy matching (default)

Type any part of a name and SwiftList finds it as long as the characters occur in order, anywhere
in the file/folder name — you don't need to type a contiguous substring:

| You type | Matches |
|---|---|
| `swlst` | `SwiftList.exe` |
| `report` | `Q3-report-final.docx` |

## Multiple words

Separate words with a space. Each word narrows the result set further — it does **not** require
the words to appear in the same order you typed them:

```
report final
```

matches `final-Q3-report.docx` just as well as `Q3-report-final.docx`.

## Case sensitivity

- An **all-lowercase** query is case-insensitive: `myfile` matches `MyFile`, `MYFILE`, etc.
- A query with **any uppercase letter** becomes case-sensitive for that term: `MyFile` only
  matches `MyFile`, not `myfile`.

## Operators

| Prefix/Suffix | Example | Effect |
|---|---|---|
| *(none)* | `report` | Fuzzy match anywhere in the name (default). |
| `!` | `!temp` | **Exclude** results that match `temp`. |
| `'` | `'report` | **Exact** substring match instead of fuzzy. |
| `'...'` | `'final report'` | Exact match anchored to word boundaries (won't match inside a larger word). |
| `^` | `^IMG` | **Prefix** match — the name must start with `IMG`. |
| `$` | `.pdf$` | **Suffix** match — the name must end with `.pdf`. |
| `\|` | `report \| summary` | **OR** — match either side of the pipe. |

You can mix these freely, e.g. `^IMG !.png$ 2024` finds files starting with `IMG`, from 2024,
that are *not* PNGs.

## Targeting a drive

Start the query with a drive letter followed by a colon to restrict results to that drive, then
continue typing your search as normal:

```
d: report
```

searches only on the `D:` drive.

## Path mode

If your query contains a path separator (`\` or `/`), SwiftList switches to path mode and matches
against full paths instead of just names — useful for jumping straight to a known folder:

```
D:\Projects\SwiftList
```

A trailing separator (`D:\Projects\`) searches the *contents* of that exact folder.

## Filtering by folder name

Add a trailing `::<text>` to a query to additionally require that the result's own name or one of
its ancestor folders matches `<text>` (fuzzy, same matching — including pinyin — as everywhere
else):

```
1080 ::wallpapers
```

finds files with `1080` in the name that live somewhere under a folder matching `wallpapers`,
without needing to know or type the exact path. Combine multiple filters with a comma:
`report ::2024,:final`.

## Bypassing exclusion rules for one search

Start a query with `*` to search past your own [exclusion rules](./settings/index-drives#exclusion-rules) —
`ExcludedPaths`, ignored globs, and ignored regexes — just for that search, without changing your
settings:

```
*node_modules
```

The `*` itself is stripped before matching, so it's never treated as part of the search text. This
only reveals results that are already indexed; a folder that was *never* indexed in the first place
(an excluded folder on a network or WSL drive) still won't appear. Hidden/system files stay filtered
either way — this only affects your own exclusion-rule configuration.

## Result type trigger

Optional, and off by default — you assign the character yourself. If you've assigned a trigger
character to a result type under **Settings → General → Quick Search Window → Result Type
Priority**, typing that character as the very first thing in the quick window shows only that
type's results — Applications, Settings, one specific File Filter, a plugin's own items, or plain
Files — hiding every other type:

```
;vs
```

finds "Visual Studio" among Applications only, if `;` is that type's configured trigger, regardless
of which other type's results would otherwise have matched the text better. Typing just the trigger
character with nothing after it yet shows a prompt naming the type instead of "No Search Results".
History and Favorites are unaffected either way — they always come first, trigger or not. No trigger
is configured by default; see [General settings](./settings/general#quick-search-window) to set one up.

## Chinese filenames: pinyin aliasing

Filenames containing Chinese characters are automatically searchable by pinyin, with no setup
required:

- **Full pinyin**: typing `chongqing` matches a file named `重庆`.
- **Initials**: typing `cq` also matches `重庆` (first letter of each syllable).
- **Polyphonic characters** (characters with more than one valid pronunciation) generate aliases
  for each common reading, so whichever pronunciation you think of is likely to match.

This is handled by a bundled alias plugin — see **Settings → Plugins** if you ever want to check
it's enabled.

## Favorites, not custom aliases

SwiftList does not have a general-purpose "define your own alias/macro" system. The closest
equivalent is [Favorites](./settings/favorites): pin a folder, file, or URL under a custom display
name, and it becomes searchable by that name (shown with a ★ marker in results). If what you
actually want is a custom keyword that launches a program, see
[Custom Commands](./instant-answers#custom-commands) instead.
