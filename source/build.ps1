$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
$generatedDashboard = Join-Path $PSScriptRoot 'dashboard.generated.html'
$h = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'dashboard-template.html'))
function Replace-Once([string]$old,[string]$new) {
 if(-not $script:h.Contains($old)){throw "Template fragment not found: $old"}
 $script:h=$script:h.Replace($old,$new)
}
Replace-Once '  enableControls(false);' '  enableControls(false);'
$addition = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'persistence.js'))
Replace-Once '  autoLoadLocalFiles();' ($addition + "`n  loadLocal();")
Replace-Once '    metaDirty = true;' "    metaDirty = true;`n    queueSave();"
Replace-Once 'row.removed = checkbox.checked;' 'row.removed = checkbox.checked; queueSave();'
Replace-Once 'row.edited = true;' 'row.edited = true; queueSave();'
Replace-Once 'row.removed = false;' 'row.removed = false; queueSave();'
Replace-Once '    rows = rows.filter(row => !row.removed);' "    savedEntries = snapshotEntries();`n    rows.filter(row=>row.removed).forEach(row=>deletedKeys.add(row.sourceKey));`n    rows = rows.filter(row => !row.removed);`n    queueSave();"
Replace-Once 'syncButton.addEventListener("click", exportMeta);' 'syncButton.addEventListener("click", saveLocal);'
Replace-Once 'Sync / Save meta.md' 'Save now / Retry'
Replace-Once 'Automatically loads <strong>jobs_raw.md</strong> and <strong>meta.md</strong> from the same folder. Manual file selection remains available as a fallback.' 'Loads jobs and markings from this folder. Changes automatically save to <strong>meta.md</strong>.'
Replace-Once 'are kept in memory until you click <strong>Save now / Retry</strong>' 'are saved automatically, along with cell edits and removals'
Replace-Once 'return -parsedDate;' 'return (Date.now() - parsedDate) / 86400000;'
Replace-Once 'link.href = raw;' 'if (/^https?:\/\//i.test(raw)) link.href = raw;'
# Commit editable cells on input so typing is autosaved without requiring focus loss.
Replace-Once 'td.addEventListener("blur", () => {' 'td.addEventListener("input", () => { row.data[header] = td.textContent; row.edited = true; queueSave(); }); td.addEventListener("blur", () => { buildFilters(); updateCounts();'
[IO.File]::WriteAllText($generatedDashboard,$h,[Text.UTF8Encoding]::new($false))
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /out:..\JobScout.exe /resource:dashboard.generated.html,dashboard.html /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll JobScout.cs Importer.cs
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}

} finally {
 if($generatedDashboard -and (Test-Path -LiteralPath $generatedDashboard)) {
  Remove-Item -LiteralPath $generatedDashboard
 }
 Pop-Location
}
