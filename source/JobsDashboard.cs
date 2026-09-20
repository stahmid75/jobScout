using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;

class JobsDashboard {
 static string root, token, revision, page;
 static JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
 static TcpListener listener;
 static string Hash(string s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", ""); }
 static string Read(string name) { string p=Path.Combine(root,name); return File.Exists(p)?File.ReadAllText(p):null; }
 static string Meta() { return Read("meta.md") ?? ""; }
 static void Reply(NetworkStream s,int code,string type,string data) {
  byte[] b=Encoding.UTF8.GetBytes(data);
  byte[] h=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: "+type+"; charset=utf-8\r\nContent-Length: "+b.Length+"\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
  s.Write(h,0,h.Length);s.Write(b,0,b.Length);
 }
 static void Serve() {
  while(true) { TcpClient c; try{c=listener.AcceptTcpClient();}catch{return;}
   using(c) { c.ReceiveTimeout=5000;c.SendTimeout=5000;
    try { var s=c.GetStream(); var bytes=new List<byte>(); int v;
     while((v=s.ReadByte())>=0) { bytes.Add((byte)v); int n=bytes.Count;if(n>32768)throw new Exception("Header too large");if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break; }
     string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
     string[] first=lines[0].Split(' '); var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
     for(int i=1;i<lines.Length;i++){int k=lines[i].IndexOf(':');if(k>0)headers[lines[i].Substring(0,k)]=lines[i].Substring(k+1).Trim();}
     string path=first[1], method=first[0];
     if(path=="/"+token+"/"&&method=="GET") { Reply(s,200,"text/html",page);continue; }
     if(!path.StartsWith("/"+token+"/api/")) {Reply(s,404,"text/plain","Not found");continue;}
     if(method=="GET"&&path.EndsWith("/api/load")) {
      string meta=Meta(); revision=Hash(meta);
      Reply(s,200,"application/json",json.Serialize(new { jobs=Read("jobs_raw.md"), meta=meta, revision=revision, directory=root }));continue;
     }
     if(method=="POST"&&path.EndsWith("/api/save")) {
      if(!headers.ContainsKey("X-Jobs-Token")||headers["X-Jobs-Token"]!=token){Reply(s,403,"text/plain","Invalid token");continue;}
      int len; if(!headers.ContainsKey("Content-Length")||!int.TryParse(headers["Content-Length"],out len)||len<0||len>12000000){Reply(s,400,"text/plain","Invalid size");continue;}
      byte[] body=new byte[len];int pos=0;while(pos<len){int n=s.Read(body,pos,len-pos);if(n==0)throw new Exception("Incomplete request");pos+=n;}
      var obj=json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));
      string current=Meta(); if((string)obj["revision"]!=Hash(current)){Reply(s,409,"text/plain","meta.md changed outside this window. Reload before editing further.");continue;}
      string next=(string)obj["meta"]; if(!next.StartsWith("# Job dashboard metadata")||!next.Contains("<!-- jobs-dashboard-state-v1 -->")){Reply(s,400,"text/plain","Invalid metadata");continue;}
      string dest=Path.Combine(root,"meta.md"),temp=Path.Combine(root,".meta-"+Guid.NewGuid().ToString("N")+".tmp");
      try { using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] b=Encoding.UTF8.GetBytes(next);f.Write(b,0,b.Length);f.Flush(true);}
       if(File.Exists(dest))File.Replace(temp,dest,Path.Combine(root,"meta.md.bak"));else File.Move(temp,dest);
      } finally {if(File.Exists(temp))File.Delete(temp);}
      revision=Hash(next);Reply(s,200,"application/json",json.Serialize(new {revision=revision}));continue;
     }
     Reply(s,404,"text/plain","Not found");
    } catch(Exception e) {try{Reply(c.GetStream(),500,"text/plain",e.Message);}catch{}}
   }
  }
 }
 static void OpenDashboard(string url) {
  string[] candidates = {
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google\\Chrome\\Application\\chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google\\Chrome\\Application\\chrome.exe"),
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google\\Chrome\\Application\\chrome.exe")
  };
  foreach(string chrome in candidates) {
   if(File.Exists(chrome)) { Process.Start(chrome,"--app="+url); return; }
  }
  Process.Start(url);
 }
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles();
  root=Path.GetFullPath(args.Length>0?args[0]:AppDomain.CurrentDomain.BaseDirectory);
  bool owner;using(var mutex=new Mutex(true,"Local\\JobsDashboard-"+Hash(root.ToLowerInvariant()),out owner)) {
   if(!owner){MessageBox.Show("Job Dashboard is already running for this folder. Use its tray icon to reopen it.");return;}
   if(!File.Exists(Path.Combine(root,"jobs_raw.md"))){MessageBox.Show("jobs_raw.md was not found in:\n"+root);return;}
   token=Guid.NewGuid().ToString("N");
   using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("dashboard.html")))page=r.ReadToEnd().Replace("__APP_TOKEN__",token);
   listener=new TcpListener(IPAddress.Loopback,0);listener.Start();string url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/"+token+"/";
   var thread=new Thread(Serve);thread.IsBackground=true;thread.Start();
   if(args.Length>1 && args[1]=="--headless") {Console.WriteLine(url);thread.Join();return;}
   var tray=new NotifyIcon {Icon=SystemIcons.Application,Text="Job Dashboard — local autosave",Visible=true};
   var menu=new ContextMenuStrip();menu.Items.Add("Open dashboard",null,delegate{OpenDashboard(url);});menu.Items.Add("Exit",null,delegate{Application.Exit();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{OpenDashboard(url);};
   OpenDashboard(url);Application.Run();tray.Dispose();listener.Stop();
  }
 }
}
