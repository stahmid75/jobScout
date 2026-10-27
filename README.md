# JobScout

JobScout is an AI integrated Windows desktop dashboard for reviewing academic job postings. It can import new jobs from HigherEdJobs alerts, use local LLM to extract structured job details, and keep your notes/status in local metadata.

## Prerequisites

- Windows with Google Chrome installed.
- Ollama installed and running.
- An Ollama model, for example `llama3.2:3b`.

## Ollama set up (if not present already)
--Install Ollama from [ollama.com](https://ollama.com/download)
-- open powershell and run 'ollama run llama3.2:3b'

## Use

2. Double-click `JobScout.exe`.
3. Click **Import New Jobs** to open a dedicated Chrome import profile. This will take you to higheredjobs.com and ask you to log in. Once logged in, run **Import New Jobs** again.

JobScout appends new non-duplicate jobs to `jobs_raw.md`. It stores dashboard notes/status in `meta.md`. The import Chrome profile, settings, and metadata are local runtime files and are ignored by Git.
## Import limits and coverage

`MaxJobs` in `import-settings.json` is the maximum number of **new jobs saved per run**. Existing jobs do not consume it. The importer scans every discovered alert and follows linked result pages before applying this limit. With `MaxJobs: 200`, further new jobs are reported as deferred; run another import to collect them.

`last-import-scan.json` lists discovered alert IDs, pages scanned, and unique additions to the candidate list. Compare its alert IDs with your HigherEdJobs alert list to verify account coverage. `last-import-result.json` records added, duplicate, deferred, and failed jobs, including all failure messages. Zero deferred jobs and zero failures mean every discovered candidate was handled; they do not prove that an unrecognized website layout exposed every alert.

Run `source/test-importer.ps1` for isolated pagination and batch-limit regression tests. To build while JobScout is running, use `source/build.ps1 -OutputPath ..\JobScout.updated.exe`; exit the old app from its tray icon before opening the updated executable.
