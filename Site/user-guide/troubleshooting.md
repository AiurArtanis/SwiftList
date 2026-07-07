# Troubleshooting

## The global hotkey doesn't respond

1. Check **Settings → Service Status** — if the background service isn't running, reinstalling or
   restarting it from that page usually fixes indexing-related issues, but the toggle hotkey itself
   is handled by the App process, not the service, so this is a secondary check.
2. If the foreground app is running elevated (as administrator) and SwiftList isn't, Windows blocks
   lower-privilege processes from sending it input. Enable **Run as Administrator** under
   **Settings → General → System**, or run SwiftList elevated once to confirm this is the cause.
3. Check the **[Process Blacklist](./settings/hotkeys-page#process-blacklist)** — if the
   foreground app's executable name was added there (intentionally or by accident), SwiftList's
   global hotkeys are deliberately let through untouched while it's focused.

## Search results seem out of date

Local drives update from the USN Journal in near real time. If something still looks stale (a file
you just created isn't showing up, or a deleted file still appears), use **Rebuild Index** on the
affected drive under **Settings → Index → Local Drives** (or **Network Drives**).

## A network drive never seems to refresh

Network shares don't have a USN Journal SwiftList can watch, so they're re-scanned on a schedule
instead. Check the drive's **Refresh Mode** under **Settings → Index → Network Drives** — if it's
set to **Manual**, nothing updates automatically; switch it to a timed interval, or use
**Rebuild Index** to refresh on demand.

## The preview window looks wrong / cut off

This shouldn't happen — SwiftList clamps the QuickLook preview window's position and size to your
monitor's usable area automatically. If you still see clipping, try
**Settings → General → Preview → Reset Preview Window Settings** to rule out an unusual manual
width/height value, and make sure you're on the latest release.

## A file/folder doesn't show up at all

- Check it isn't excluded — **Settings → Index → Exclusion Rules** supports exact paths, glob
  patterns, and regexes, and any of the three could be catching it unintentionally.
- Check the drive it lives on is enabled under **Settings → Index → Local/Network Drives**.

## Still stuck?

Check the **Service**, **App**, and **Hook** log tabs under
**[Settings → Service Status](./settings/service-status)** — the search box there filters by
keyword, and the level dropdown filters by severity — before filing an issue on GitHub.
