# Job Dashboard

Double-click **JobsDashboard.exe** in this folder. It opens the dashboard in a Chrome app window (or your default browser if Chrome is unavailable). All dashboard loading and saving happens on this computer; no internet or account is needed. Opening job links uses the internet.

Keep the executable beside `jobs_raw.md`. It reads data from its own folder by default. You can also launch `JobsDashboard.exe "D:\another folder"` to choose a data folder explicitly. The HTML interface is embedded inside the executable; no Python, Node, installation, or separate HTML file is needed to run it on this Windows computer.

- `jobs_raw.md` is read only to the app.
- `meta.md` is created on your first change, and automatically saves preferred/applied markings, notes, edited cells, removals, and deleted rows. A readable Markdown table is accompanied by a JSON section that preserves complete state. Existing table-only metadata is imported when first opened.
- `meta.md.bak` keeps the previous successful save. Save operations replace metadata atomically. Source files are never save targets.

Use search, position/field/state filters, column sorting, editable cells, Preferred, Applied, Note, Remove, Unmark all removals, Delete Marked, and Export as before. Deletion hides jobs persistently; Restore deleted jobs brings them back. Cell edits are overlays in metadata and never alter the source list. Job Alert Name remains the final column; scroll horizontally to reach all columns.

Changes save after a short typing pause. Wait for the green Saved indicator before closing. If saving fails, the dashboard shows NOT SAVED and warns before closing; use Save now / Retry after resolving the problem. If another window or program changed metadata, the app rejects a stale save instead of overwriting it. Keep one editing window open. Reload source files reads new jobs while retaining saved markings.

Export Updated Jobs .md downloads a separate `jobs_raw_updated.md` through your browser. The executable only writes metadata and its backup. As with any browser download, do not manually choose a protected source filename as the download destination.

Closing the dashboard window leaves the local helper running. Use the Job Dashboard icon in the Windows notification area to reopen it or choose Exit. It only listens on this computer's loopback address and uses a random session URL and save token.

## Source and build

The root folder contains the runnable program and live data:

- `JobsDashboard.exe` — application
- `jobs_raw.md` — read-only job source
- `meta.md` and `meta.md.bak` — saved dashboard state and backup
- `udatedPositions.md` — unrelated data retained for its separate workflow

Developer files are grouped under `source`. `JobsDashboard.cs` contains the local server and launcher, `dashboard-template.html` is the original interface, and `persistence.js` adds autosave. Run `source/build.ps1` in PowerShell to regenerate the executable using the Windows .NET Framework compiler. The temporary generated HTML is removed automatically after compilation. Exit the running dashboard before rebuilding.
