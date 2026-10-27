$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Importer.cs'))
function Replace-Method([string]$begin,[string]$end,[string]$body) {
 $a=$script:source.IndexOf($begin); $b=$script:source.IndexOf($end,$a+$begin.Length)
 if($a -lt 0 -or $b -lt 0){throw "Test seam missing: $begin"}
 $script:source=$script:source.Substring(0,$a)+$body+[Environment]::NewLine+$script:source.Substring($b)
}
Replace-Method ' static void EnsureOllamaImport(' ' static string ChooseOllamaModel(' ' static void EnsureOllamaImport(ImportConfig cfg) {}'
Replace-Method ' static void EnsureChromeImport(' ' static string OpenChromeTabImport(' ' static void EnsureChromeImport(ImportConfig cfg) {}'
Replace-Method ' static string OpenChromeTabImport(' ' static Dictionary<string,object> CdpImport(' ' static string OpenChromeTabImport(ImportConfig cfg,string url) { return "fixture"; }'
Replace-Method ' static void NavigateImport(' ' static string EvalImport(' ' static void NavigateImport(string ws,string url) { fixtureUrl=url; }'
Replace-Method ' static string EvalImport(' ' static void WaitReadyImport(' ' static string EvalImport(string ws,string expression) { return FixtureEval(expression); }'
Replace-Method ' static string ReadPostingTextImport(' ' static string JsonValue(' ' static string ReadPostingTextImport(string ws,CandidateJob job) { return "fixture"; }'
Replace-Method ' static JobInfo ExtractWithOllamaImport(' ' static Dictionary<string,string> StateAbbreviationMapImport(' ' static JobInfo ExtractWithOllamaImport(ImportConfig cfg,CandidateJob c,string text) { return new JobInfo {job_id=c.JobCode,link=c.Url}; }'
Replace-Method ' static void AppendJobsImport(' ' static object ImportJobs(' ' static void AppendJobsImport(List<JobInfo> jobs) { foreach(var j in jobs) File.AppendAllText(Path.Combine(root,"jobs_raw.md"),"| "+j.job_id+" |\n"); }'
$harness=@"
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
partial class JobScout {
 static string root,fixtureUrl;
 static JavaScriptSerializer json=new JavaScriptSerializer();
 static string Read(string name) { string p=Path.Combine(root,name); return File.Exists(p)?File.ReadAllText(p):null; }
 const string index="https://www.higheredjobs.com/myHigherEdJobs/Agent/";
 static string Alert(int id) { return index+"viewagent.cfm?AgentID="+id; }
 static object Link(int n) { return new {text="Research update manager "+n,href="https://www.higheredjobs.com/faculty/details.cfm?JobCode="+(179000000+n)}; }
 static string FixtureEval(string script) {
  if(script==AlertPageStateScript) return "ready";
  if(script.Contains("document.body")) return "Job Alerts";
  if(script.Contains("a[href]")) {
   if(fixtureUrl.StartsWith(Alert(1))) return json.Serialize(new[]{Alert(1),Alert(1)+"&start=501"});
   return "[]";
  }
  if(script.Contains("details")) {
   if(fixtureUrl==index || fixtureUrl==Alert(3)) return "[]";
   if(fixtureUrl==Alert(1)) return json.Serialize(Enumerable.Range(1,500).Select(Link).ToArray());
   if(fixtureUrl==Alert(1)+"&start=501") return json.Serialize(Enumerable.Range(501,350).Select(Link).ToArray());
   return json.Serialize(new[]{Link(1),Link(851)});
  }
  return json.Serialize(new[]{new{text="First",href=Alert(1)},new{text="First duplicate",href=Alert(1)},new{text="Later",href=Alert(2)},new{text="Empty",href=Alert(3)}});
 }
 static void Check(bool ok,string message) { if(!ok) throw new Exception(message); Console.WriteLine("PASS "+message); }
 static void Main() {
  root=Path.Combine(Path.GetTempPath(),"jobscout-regression-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
  File.WriteAllText(Path.Combine(root,"import-settings.json"),"{\"MaxJobs\":200}");
  var cfg=new ImportConfig{MaxJobs=200};
  var jobs=ReadCandidatesImport(cfg,"fixture");
  Check(jobs.Count==851,"all 851 unique jobs across multiple pages and later alerts, beyond old 600 limit");
  Check(jobs[0].AlertName=="First; Later","overlapping alert attribution");
  var scan=json.Deserialize<Dictionary<string,object>>(Read("last-import-scan.json"));
  Check(((System.Collections.ArrayList)scan["alerts"]).Count==3,"duplicate alert links deduplicated and empty alert scanned");
  File.WriteAllText(Path.Combine(root,"jobs_raw.md"),String.Join("\n",Enumerable.Range(1,650).Select(n=>"| "+(179000000+n)+" |").ToArray()));
  var first=json.Deserialize<Dictionary<string,object>>(json.Serialize(ImportJobs()));
  Check(Convert.ToInt32(first["added"])==200 && Convert.ToInt32(first["skipped"])==650 && Convert.ToInt32(first["deferred"])==1,"MaxJobs applies to 200 NEW jobs; existing jobs do not consume limit; deferred job reported");
  var second=json.Deserialize<Dictionary<string,object>>(json.Serialize(ImportJobs()));
  Check(Convert.ToInt32(second["added"])==1 && Convert.ToInt32(second["deferred"])==0,"next run imports remaining later-alert job");
  Check(Convert.ToInt32(second["scanned"])==851,"progress denominator accounts for full scan");
  File.WriteAllText(Path.Combine(root,"jobs_raw.md"),"");
  var chosen=json.Deserialize<Dictionary<string,object>>(json.Serialize(ImportJobs(3)));
  Check(Convert.ToInt32(chosen["added"])==3 && Convert.ToInt32(chosen["deferred"])==848,"chosen per-run count overrides configured 200");
  Check(LoadImportConfig().MaxJobs==200,"per-run count leaves saved configuration unchanged");
  bool rejected=false; try { ImportJobs(0); } catch { rejected=true; }
  Check(rejected,"non-positive per-run count rejected");
  Console.WriteLine("Fixtures: "+root);
 }
}
"@
$testDir=Join-Path $env:TEMP ('jobscout-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
[IO.File]::WriteAllText((Join-Path $testDir 'Importer.cs'),$source)
[IO.File]::WriteAllText((Join-Path $testDir 'Harness.cs'),$harness)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$testDir\Tests.exe" /r:System.Web.Extensions.dll (Join-Path $testDir 'Importer.cs') (Join-Path $testDir 'Harness.cs')
if($LASTEXITCODE -ne 0){throw 'Regression harness compilation failed'}
& (Join-Path $testDir 'Tests.exe')
if($LASTEXITCODE -ne 0){throw 'Importer regressions failed'}