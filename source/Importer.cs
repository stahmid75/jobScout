using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Diagnostics;

partial class JobScout {
 static object importLock = new object();
 static bool importRunning = false;
 static object importStatusLock = new object();
 static Dictionary<string,object> importStatus = new Dictionary<string,object>{{"running",false},{"phase","Idle"},{"current",0},{"total",0},{"added",0},{"skipped",0},{"failed",0},{"message","Ready"}};
 const string ConfigFile = "import-settings.json";
 class ImportConfig {
  public string AlertUrl = "https://www.higheredjobs.com/myHigherEdJobs/Agent/";
  public string ChromeDebugUrl = "http://127.0.0.1:9222";
  public string ChromeUserDataDir = ".chrome-import-profile";
  public string OllamaUrl = "http://127.0.0.1:11434";
  public string OllamaModel = "llama3.1";
  public int MaxJobs = 50;
 }
 class CandidateJob { public string Title, Url, JobCode, AlertName; }
 class JobInfo {
  public string job_title="", university="", department="", fields_for_hiring="", position_type="", location="", job_posted_date="", application_deadline="", job_id="", link="", job_alert_name="";
 }
 static string GetJobCode(string text) {
  if(string.IsNullOrEmpty(text)) return "";
  var m=Regex.Match(text,@"(?:JobCode=|job_id=|JobID=)(\d{6,})",RegexOptions.IgnoreCase);
  if(m.Success) return m.Groups[1].Value;
  m=Regex.Match(text,@"\b(17\d{7})\b");
  return m.Success?m.Groups[1].Value:"";
 }
 static string MdCell(string value) {
  string v=String.IsNullOrWhiteSpace(value)?"—":value.Trim();
  return v.Replace("\r"," ").Replace("\n"," ").Replace("\\","\\\\").Replace("|","\\|");
 }
 static ImportConfig LoadImportConfig() {
  string path=Path.Combine(root,ConfigFile);
  if(!File.Exists(path)) {
   var cfg=new ImportConfig();
   File.WriteAllText(path,json.Serialize(cfg),Encoding.UTF8);
   return cfg;
  }
  var loaded=json.Deserialize<ImportConfig>(File.ReadAllText(path));
  return loaded ?? new ImportConfig();
 }
 static HashSet<string> ExistingJobKeys(string jobsText) {
  var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  if(String.IsNullOrEmpty(jobsText)) return keys;
  foreach(Match m in Regex.Matches(jobsText,@"(?:JobCode=|job_id=|JobID=)(\d{6,})|\|\s*(\d{6,})\s*\|",RegexOptions.IgnoreCase)) {
   string id=m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value;
   if(!String.IsNullOrWhiteSpace(id)) keys.Add("id:"+id);
  }
  foreach(Match m in Regex.Matches(jobsText,@"https?://[^\s|]+",RegexOptions.IgnoreCase)) keys.Add("link:"+WebUtility.HtmlDecode(m.Value).Trim());
  return keys;
 }
 static string HttpGetImport(string url) {
  var req=(HttpWebRequest)WebRequest.Create(url);
  req.Timeout=8000;
  using(var res=(HttpWebResponse)req.GetResponse())
  using(var sr=new StreamReader(res.GetResponseStream())) return sr.ReadToEnd();
 }
 static string HttpNoBodyImport(string url, string method) {
  var req=(HttpWebRequest)WebRequest.Create(url);
  req.Method=method; req.Timeout=8000;
  using(var res=(HttpWebResponse)req.GetResponse())
  using(var sr=new StreamReader(res.GetResponseStream())) return sr.ReadToEnd();
 } static string HttpPostJsonImport(string url, string body, int timeoutMs) {
  var req=(HttpWebRequest)WebRequest.Create(url);
  req.Method="POST"; req.ContentType="application/json"; req.Timeout=timeoutMs;
  byte[] bytes=Encoding.UTF8.GetBytes(body);
  using(var rs=req.GetRequestStream()) rs.Write(bytes,0,bytes.Length);
  using(var res=(HttpWebResponse)req.GetResponse())
  using(var sr=new StreamReader(res.GetResponseStream())) return sr.ReadToEnd();
 }
 static bool CanReachOllama(ImportConfig cfg) {
  try { HttpGetImport(cfg.OllamaUrl.TrimEnd('/')+"/api/tags"); return true; } catch { return false; }
 }
 static void EnsureOllamaImport(ImportConfig cfg) {
  if(CanReachOllama(cfg)) return;
  SetImportStatus("Starting Ollama",0,0,0,0,0,"Ollama is not running. Starting ollama serve...");
  try {
   var psi=new ProcessStartInfo("ollama","serve");
   psi.CreateNoWindow=true;
   psi.UseShellExecute=false;
   psi.RedirectStandardOutput=true;
   psi.RedirectStandardError=true;
   Process.Start(psi);
  } catch(Exception e) {
   throw new Exception("Ollama is not reachable at "+cfg.OllamaUrl+" and JobScout could not start it automatically. Install Ollama or start it manually. "+e.Message);
  }
  DateTime until=DateTime.Now.AddSeconds(15);
  while(DateTime.Now<until) {
   if(CanReachOllama(cfg)) return;
   Thread.Sleep(500);
  }
  throw new Exception("Ollama was started but did not become reachable at "+cfg.OllamaUrl+". Check that Ollama installed correctly and that no firewall or port conflict blocks it.");
 } static string ChooseOllamaModel(ImportConfig cfg) {
  string tags=HttpGetImport(cfg.OllamaUrl.TrimEnd('/')+"/api/tags");
  var obj=json.Deserialize<Dictionary<string,object>>(tags);
  var models=new List<string>();
  if(obj.ContainsKey("models")) {
   var list=obj["models"] as object[];
   if(list!=null) foreach(var item in list) {
    var m=item as Dictionary<string,object>;
    if(m!=null && m.ContainsKey("name")) models.Add(Convert.ToString(m["name"]));
   }
   var arrayList=obj["models"] as System.Collections.ArrayList;
   if(arrayList!=null) foreach(var item in arrayList) {
    var m=item as Dictionary<string,object>;
    if(m!=null && m.ContainsKey("name")) models.Add(Convert.ToString(m["name"]));
   }
  }
  models=models.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  if(models.Count==0) throw new Exception("Ollama is running, but no installed models were found. Run: ollama pull llama3.2:3b");
  if(!String.IsNullOrWhiteSpace(cfg.OllamaModel) && models.Any(x=>String.Equals(x,cfg.OllamaModel,StringComparison.OrdinalIgnoreCase))) return cfg.OllamaModel;
  string[] preferred={"llama3.2:3b","llama3.2","llama3.1:8b","llama3.1","llama3","mistral","qwen2.5","gemma2"};
  foreach(string pref in preferred) {
   string found=models.FirstOrDefault(x=>String.Equals(x,pref,StringComparison.OrdinalIgnoreCase) || x.StartsWith(pref+":",StringComparison.OrdinalIgnoreCase));
   if(found!=null) return found;
  }
  return models[0];
 } static string ChromePathImport() {
  string[] candidates = {
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google\\Chrome\\Application\\chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google\\Chrome\\Application\\chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google\\Chrome\\Application\\chrome.exe")
  };
  return candidates.FirstOrDefault(File.Exists);
 }
 static void EnsureChromeImport(ImportConfig cfg) {
  try { HttpGetImport(cfg.ChromeDebugUrl.TrimEnd('/')+"/json/version"); return; } catch {}
  string chrome=ChromePathImport();
  if(chrome==null) throw new Exception("Chrome was not found. Install Chrome or update import-settings.json.");
  var uri=new Uri(cfg.ChromeDebugUrl); int port=uri.Port>0?uri.Port:9222;
  string userData=cfg.ChromeUserDataDir;
  if(String.IsNullOrWhiteSpace(userData)) userData=".chrome-import-profile";
  if(!Path.IsPathRooted(userData)) userData=Path.Combine(root,userData);
  Directory.CreateDirectory(userData);
  Process.Start(chrome,"--remote-debugging-port="+port+" --remote-debugging-address=127.0.0.1 --user-data-dir=\""+userData+"\" \""+cfg.AlertUrl+"\"");
  DateTime until=DateTime.Now.AddSeconds(8);
  while(DateTime.Now<until) { try { HttpGetImport(cfg.ChromeDebugUrl.TrimEnd('/')+"/json/version"); return; } catch { Thread.Sleep(400); } }
  throw new Exception("Import Chrome did not expose its local debugging interface. Close Chrome windows opened by this app and retry. No HigherEdJobs username or password is requested or stored by the dashboard.");
 }
 static string OpenChromeTabImport(ImportConfig cfg, string url) {
  string baseUrl=cfg.ChromeDebugUrl.TrimEnd('/');
  try {
   string listJson=HttpGetImport(baseUrl+"/json/list");
   var tabs=json.Deserialize<List<Dictionary<string,object>>>(listJson) ?? new List<Dictionary<string,object>>();
   foreach(var tab in tabs) {
    string type=tab.ContainsKey("type")?Convert.ToString(tab["type"]):"";
    string ws=tab.ContainsKey("webSocketDebuggerUrl")?Convert.ToString(tab["webSocketDebuggerUrl"]):"";
    if(type=="page" && !String.IsNullOrWhiteSpace(ws)) {
     NavigateImport(ws,url);
     return ws;
    }
   }
  } catch {}
  string endpoint=baseUrl+"/json/new?"+Uri.EscapeDataString(url);
  string data;
  try { data=HttpNoBodyImport(endpoint,"PUT"); } catch { data=HttpGetImport(endpoint); }
  var obj=json.Deserialize<Dictionary<string,object>>(data);
  if(!obj.ContainsKey("webSocketDebuggerUrl")) throw new Exception("Chrome opened the page but did not return a debuggable tab.");
  return (string)obj["webSocketDebuggerUrl"];
 } static Dictionary<string,object> CdpImport(string wsUrl, string method, Dictionary<string,object> parameters) {
  using(var ws=new ClientWebSocket()) {
   ws.ConnectAsync(new Uri(wsUrl),CancellationToken.None).GetAwaiter().GetResult();
   var payload=json.Serialize(new Dictionary<string,object>{{"id",1},{"method",method},{"params",parameters ?? new Dictionary<string,object>()}});
   byte[] send=Encoding.UTF8.GetBytes(payload);
   ws.SendAsync(new ArraySegment<byte>(send),WebSocketMessageType.Text,true,CancellationToken.None).GetAwaiter().GetResult();
   var buf=new byte[1024*1024]; var ms=new MemoryStream(); WebSocketReceiveResult rr;
   do { rr=ws.ReceiveAsync(new ArraySegment<byte>(buf),CancellationToken.None).GetAwaiter().GetResult(); ms.Write(buf,0,rr.Count); } while(!rr.EndOfMessage);
   try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure,"done",CancellationToken.None).GetAwaiter().GetResult(); } catch {}
   return json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(ms.ToArray()));
  }
 }
 static void SetImportStatus(string phase, int current, int total, int added, int skipped, int failed, string message) {
  lock(importStatusLock) {
   importStatus = new Dictionary<string,object>{{"running",importRunning},{"phase",phase},{"current",current},{"total",total},{"added",added},{"skipped",skipped},{"failed",failed},{"message",message ?? ""},{"updated",DateTime.Now.ToString("HH:mm:ss")}};
  }
 }
 static object ImportStatusSnapshot() { lock(importStatusLock) return new Dictionary<string,object>(importStatus); }
 static void NavigateImport(string wsUrl, string url) {
  CdpImport(wsUrl,"Page.navigate",new Dictionary<string,object>{{"url",url}});
  WaitReadyImport(wsUrl);
 } static string EvalImport(string wsUrl, string expression) {
  var result=CdpImport(wsUrl,"Runtime.evaluate",new Dictionary<string,object>{{"expression",expression},{"returnByValue",true}});
  if(result.ContainsKey("error")) throw new Exception("Chrome evaluation failed: "+json.Serialize(result["error"]));
  var outer=(Dictionary<string,object>)result["result"];
  var inner=(Dictionary<string,object>)outer["result"];
  return inner.ContainsKey("value") && inner["value"]!=null ? Convert.ToString(inner["value"]) : "";
 }
 static void WaitReadyImport(string wsUrl) {
  DateTime until=DateTime.Now.AddSeconds(20);
  while(DateTime.Now<until) { Thread.Sleep(500); string state=EvalImport(wsUrl,"document.readyState"); if(state=="complete" || state=="interactive") return; }
 }
 static void AddJobLinksImport(string ws, string alertName, List<CandidateJob> jobs, HashSet<string> seen, int cap) {
  string script="JSON.stringify(Array.from(document.querySelectorAll('a')).map(a=>({text:a.innerText||a.textContent||'',href:a.href||a.getAttribute('href')||''})).filter(x=>/details\\.cfm/i.test(x.href) && /JobCode=/i.test(x.href) && !/(delete|remove|edit|update|save|manage|unsubscribe|deactivate|activate|alertid=)/i.test(x.href+x.text)).slice(0,300))";
  string raw=EvalImport(ws,script);
  var links=json.Deserialize<List<Dictionary<string,object>>>(raw) ?? new List<Dictionary<string,object>>();
  foreach(var link in links) {
   string url=Convert.ToString(link.ContainsKey("href")?link["href"]:"").Replace("&amp;","&");
   string code=GetJobCode(url); string key=String.IsNullOrEmpty(code)?url:code;
   if(String.IsNullOrEmpty(key) || seen.Contains(key)) continue;
   seen.Add(key);
   jobs.Add(new CandidateJob{Title=WebUtility.HtmlDecode(Convert.ToString(link.ContainsKey("text")?link["text"]:"")).Trim(),Url=url,JobCode=code,AlertName=String.IsNullOrWhiteSpace(alertName)?"HigherEdJobs":alertName});
   if(jobs.Count>=cap) return;
  }
 }
 static List<Dictionary<string,object>> AlertLinksImport(string ws, string baseUrl) {
  string script="JSON.stringify(Array.from(document.querySelectorAll('a')).map(a=>({text:(a.innerText||a.textContent||'').trim(),href:a.href||a.getAttribute('href')||''})).filter(x=>/^results$/i.test(x.text) && /higheredjobs\\.com/i.test(x.href) && !/(delete|remove|edit|copy|update|save|manage|unsubscribe|deactivate|activate|settings|preferences)/i.test(x.href)).slice(0,50))";
  return json.Deserialize<List<Dictionary<string,object>>>(EvalImport(ws,script)) ?? new List<Dictionary<string,object>>();
 }
 static List<CandidateJob> ReadCandidatesImport(ImportConfig cfg, string ws) {
  NavigateImport(ws,cfg.AlertUrl);
  string body=EvalImport(ws,"document.body ? document.body.innerText : ''");
  if(Regex.IsMatch(body,@"\b(log\s*in|sign\s*in|password)\b",RegexOptions.IgnoreCase) && !Regex.IsMatch(body,@"Job Alert|Agent|JobCode",RegexOptions.IgnoreCase))
   throw new Exception("HigherEdJobs appears to require login in the import Chrome profile. Log into HigherEdJobs in the Chrome window opened by Import New Jobs once, then retry.");
  var jobs=new List<CandidateJob>(); var seen=new HashSet<string>(); int cap=Math.Max(1,cfg.MaxJobs)*3;
  AddJobLinksImport(ws,"HigherEdJobs",jobs,seen,cap);
  foreach(var alert in AlertLinksImport(ws,cfg.AlertUrl)) {
   if(jobs.Count>=cap) break;
   string url=Convert.ToString(alert.ContainsKey("href")?alert["href"]:"").Replace("&amp;","&");
   string name=WebUtility.HtmlDecode(Convert.ToString(alert.ContainsKey("text")?alert["text"]:"")).Trim();
   if(String.IsNullOrWhiteSpace(url) || !Regex.IsMatch(url,@"/myHigherEdJobs/Agent/viewagent\.cfm\?AgentID=\d+",RegexOptions.IgnoreCase)) continue;
   SetImportStatus("Scanning alerts",0,cap,0,0,0,"Opening alert results: "+(String.IsNullOrWhiteSpace(name)?url:name));
   NavigateImport(ws,url);
   AddJobLinksImport(ws,name,jobs,seen,cap);
  }
  if(jobs.Count==0) throw new Exception("No HigherEdJobs postings were found on the configured alerts page. Check import-settings.json AlertUrl and confirm the page shows alert results in Chrome.");
  return jobs;
 }
 static string ReadPostingTextImport(string ws, CandidateJob job) {
  NavigateImport(ws,job.Url);
  string text=EvalImport(ws,"document.body ? document.body.innerText.replace(/\\s+\\n/g,'\\n').replace(/\\n{3,}/g,'\\n\\n') : ''");
  if(String.IsNullOrWhiteSpace(text) || text.Length<200) throw new Exception("Posting text was empty or too short for "+job.Url);
  return text.Length>30000?text.Substring(0,30000):text;
 }
 static string JsonValue(Dictionary<string,object> obj, string key) {
  if(obj==null || !obj.ContainsKey(key) || obj[key]==null) return "";
  object value=obj[key];
  var arr=value as System.Collections.ArrayList;
  if(arr!=null) return String.Join("; ", arr.Cast<object>().Select(x=>Convert.ToString(x)).Where(x=>!String.IsNullOrWhiteSpace(x)).ToArray());
  var objArr=value as object[];
  if(objArr!=null) return String.Join("; ", objArr.Select(x=>Convert.ToString(x)).Where(x=>!String.IsNullOrWhiteSpace(x)).ToArray());
  return Convert.ToString(value);
 } static JobInfo ExtractWithOllamaImport(ImportConfig cfg, CandidateJob candidate, string postingText) {
  string schema="Return only compact JSON with keys: job_title, university, department, fields_for_hiring, position_type, location, job_posted_date, application_deadline, job_id. Use an em dash for unknown values. Do not include markdown.";
  string prompt=schema+"\n\nJob URL: "+candidate.Url+"\nJobCode: "+candidate.JobCode+"\nVisible posting text:\n"+postingText;
  string model=ChooseOllamaModel(cfg);
  var body=json.Serialize(new Dictionary<string,object>{{"model",model},{"stream",false},{"prompt",prompt},{"options",new Dictionary<string,object>{{"temperature",0}}}});
  string response=HttpPostJsonImport(cfg.OllamaUrl.TrimEnd('/')+"/api/generate",body,120000);
  var obj=json.Deserialize<Dictionary<string,object>>(response);
  if(!obj.ContainsKey("response")) throw new Exception("Ollama returned an unexpected response.");
  string content=Convert.ToString(obj["response"]); var match=Regex.Match(content,@"\{[\s\S]*\}");
  if(!match.Success) throw new Exception("Ollama did not return JSON for "+candidate.Url);
  var parsed=json.Deserialize<Dictionary<string,object>>(match.Value);
  var info=new JobInfo();
  info.job_title=JsonValue(parsed,"job_title");
  info.university=JsonValue(parsed,"university");
  info.department=JsonValue(parsed,"department");
  info.fields_for_hiring=JsonValue(parsed,"fields_for_hiring");
  info.position_type=JsonValue(parsed,"position_type");
  info.location=JsonValue(parsed,"location");
  info.job_posted_date=JsonValue(parsed,"job_posted_date");
  info.application_deadline=JsonValue(parsed,"application_deadline");
  info.job_id=JsonValue(parsed,"job_id");
  info.job_title=String.IsNullOrWhiteSpace(info.job_title)||info.job_title=="—"?candidate.Title:info.job_title;
  info.job_id=String.IsNullOrWhiteSpace(info.job_id)||info.job_id=="—"?candidate.JobCode:info.job_id;
  info.link=candidate.Url; info.job_alert_name=String.IsNullOrWhiteSpace(candidate.AlertName)?"HigherEdJobs":candidate.AlertName;
  return info;
 }
 static void AppendJobsImport(List<JobInfo> jobs) {
  if(jobs.Count==0) return;
  string path=Path.Combine(root,"jobs_raw.md"); string text=File.ReadAllText(path); int maxNo=0;
  foreach(Match m in Regex.Matches(text,@"^\|\s*(\d+)\s*\|",RegexOptions.Multiline)) maxNo=Math.Max(maxNo,Int32.Parse(m.Groups[1].Value));
  var sb=new StringBuilder();
  foreach(var j in jobs) {
   maxNo++;
   sb.Append("| ").Append(maxNo).Append(" | ").Append(MdCell(j.job_title)).Append(" | ").Append(MdCell(j.university)).Append(" | ").Append(MdCell(j.department)).Append(" | ").Append(MdCell(j.fields_for_hiring)).Append(" | ").Append(MdCell(j.position_type)).Append(" | ").Append(MdCell(j.location)).Append(" | ").Append(MdCell(j.job_posted_date)).Append(" | ").Append(MdCell(j.application_deadline)).Append(" | ").Append(MdCell(j.job_id)).Append(" | ").Append(MdCell(j.link)).Append(" | ").Append(MdCell(j.job_alert_name)).Append(" |").AppendLine();
  }
  File.AppendAllText(path,(text.EndsWith("\n")?"":"\n")+sb.ToString(),Encoding.UTF8);
 }
 static object ImportJobs() {
  lock(importLock) { if(importRunning) throw new Exception("An import is already running."); importRunning=true; }
  SetImportStatus("Starting",0,0,0,0,0,"Starting HigherEdJobs import...");
  try {
   var cfg=LoadImportConfig(); if(cfg.MaxJobs<=0) cfg.MaxJobs=50;
   EnsureChromeImport(cfg);
   EnsureOllamaImport(cfg);
   var existing=ExistingJobKeys(Read("jobs_raw.md") ?? "");
   SetImportStatus("Scanning alerts",0,cfg.MaxJobs,0,0,0,"Scanning HigherEdJobs alerts...");
   string workTab=OpenChromeTabImport(cfg,cfg.AlertUrl);
   var candidates=ReadCandidatesImport(cfg,workTab);
   var appended=new List<JobInfo>(); var skipped=new List<string>(); var failures=new List<string>();
   int targetTotal=Math.Min(cfg.MaxJobs,candidates.Count);
   int processed=0;
   SetImportStatus("Importing",0,targetTotal,0,0,0,"Found "+candidates.Count+" candidate postings. Importing up to "+targetTotal+" new jobs...");
   foreach(var c in candidates) {
    if(appended.Count>=cfg.MaxJobs) break;
    processed++;
    SetImportStatus("Importing",processed,targetTotal,appended.Count,skipped.Count,failures.Count,"Importing "+processed+" of "+targetTotal+": "+(String.IsNullOrWhiteSpace(c.Title)?c.JobCode:c.Title));
    string id=String.IsNullOrEmpty(c.JobCode)?"":"id:"+c.JobCode;
    if((id!="" && existing.Contains(id)) || existing.Contains("link:"+c.Url)) { skipped.Add(c.JobCode!=""?c.JobCode:c.Url); continue; }
    try {
     var info=ExtractWithOllamaImport(cfg,c,ReadPostingTextImport(workTab,c));
     string key=String.IsNullOrEmpty(info.job_id)||info.job_id=="—"?"link:"+info.link:"id:"+info.job_id;
     if(existing.Contains(key)) { skipped.Add(info.job_id); continue; }
     existing.Add(key); if(!String.IsNullOrWhiteSpace(info.link)) existing.Add("link:"+info.link); appended.Add(info);
    } catch(Exception e) { failures.Add((c.JobCode!=""?c.JobCode:c.Url)+": "+e.Message); }
   }
   AppendJobsImport(appended);
   return new {added=appended.Count, skipped=skipped.Count, failed=failures.Count, failures=failures.Take(8).ToArray(), config=ConfigFile};
  } catch(Exception e) { SetImportStatus("Failed",0,0,0,0,1,e.Message); throw; } finally { lock(importLock) importRunning=false; }
 }
}
