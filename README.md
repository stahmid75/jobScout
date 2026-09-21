# JobScout

JobScout is a Windows desktop dashboard for reviewing academic job postings. It can import new jobs from HigherEdJobs alerts, use local Ollama to extract structured job details, and keep your notes/status in local metadata.

## Prerequisites

- Windows with Google Chrome installed.
- Ollama installed and running.
- At least one Ollama model. JobScout will use `llama3.2:3b` when available, or choose another installed model automatically.

Install Ollama from [ollama.com](https://ollama.com/download), then open PowerShell and run:

```powershell
ollama pull llama3.2:3b
ollama serve
```

If Ollama is already running, `ollama serve` may say the port is in use. That is okay.

## Use

2. Double-click `JobScout.exe`.
3. Click **Import New Jobs** to open a dedicated Chrome import profile. This will take you to higheredjobs.com and ask you to log in. Once logged in, run **Import New Jobs** again.

JobScout appends new non-duplicate jobs to `jobs_raw.md`. It stores dashboard notes/status in `meta.md`. The import Chrome profile, settings, and metadata are local runtime files and are ignored by Git.