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