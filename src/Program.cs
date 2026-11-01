using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.IO.Compression;

namespace FolderScope {
 static class Program {
  [STAThread] public static int Main(string[] args){
   try{
    if(args.Length>0&&args[0]=="--self-test")return SelfTest.Run(args.Length>1?args[1]:Path.Combine(Application.StartupPath,"test-output"));
    L.Chinese=AppSettings.Load().Language!="en-US";
    if(args.Length>1&&args[0]=="--benchmark")return Benchmark.Run(args[1]);
    if(args.Length>1&&args[0]=="--job"){Jobs.Run(AtomicXml.Read<ScheduledJob>(args[1]));return 0;}
    if(args.Contains("--scan")){
     Func<string,string> get=key=>{int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;};string paths=get("--scan");if(string.IsNullOrWhiteSpace(paths))throw new ArgumentException("--scan requires one or more paths separated by |");
     var settings=AppSettings.Load();string config=get("--options");var options=config==null?settings.Scan:AtomicXml.Read<ScanOptions>(config);var snap=new Scanner().Run(paths.Split('|'),options,CancellationToken.None);string save=get("--snapshot"),export=get("--export"),report=get("--report")??"Folder details";if(save==null&&export==null)throw new ArgumentException("Use --snapshot or --export to save results.");if(save!=null)Snapshot.Save(snap,save);if(export!=null){var rows=report=="Folder details"?snap.Entries.Select(Reports.Row).ToList():Reports.Build(snap,report,0,get("--duplicate-mode")??"Name + size",CancellationToken.None);Exporter.Save(export,report,rows);}if(snap.Events.Count>0)File.WriteAllLines((save??export)+".events.txt",snap.Events.Select(e=>e.Path+"\t"+e.Message));return snap.Cancelled?3:0;
    }
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.ThreadException+=(s,e)=>UI.Error(e.Exception);Application.Run(new MainForm(args.Length>0?args[0]:null));return 0;
   }catch(Exception ex){string error=ex.ToString();try{File.AppendAllText(Path.Combine(Util.DataDir,"errors.log"),DateTime.Now.ToString("o")+"\n"+error+"\n");}catch{}if(args.Length==0)MessageBox.Show(ex.Message,"FolderScope",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
 public static class SelfTest {
  public static int Run(string folder){Directory.CreateDirectory(folder);string root=Path.Combine(folder,"fixture-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(root);var results=new List<string>();int fails=0;
   Action<string,Action> test=(name,a)=>{try{a();results.Add("PASS  "+name);}catch(Exception ex){fails++;results.Add("FAIL  "+name+" :: "+ex);}};
   Action<bool,string> check=(ok,msg)=>{if(!ok)throw new Exception(msg);};
   Directory.CreateDirectory(Path.Combine(root,"A"));Directory.CreateDirectory(Path.Combine(root,"B"));Directory.CreateDirectory(Path.Combine(root,"空文件夹"));byte[] data=Enumerable.Range(0,8192).Select(i=>(byte)(i%251)).ToArray();File.WriteAllBytes(Path.Combine(root,"A","same.bin"),data);File.WriteAllBytes(Path.Combine(root,"B","same.bin"),data);File.WriteAllBytes(Path.Combine(root,"不同名称.bin"),data);File.WriteAllText(Path.Combine(root,"中文文档.txt"),"磁盘空间分析 / bilingual interface",new System.Text.UTF8Encoding(false));File.WriteAllBytes(Path.Combine(root,"temporary.tmp"),new byte[17]);File.WriteAllBytes(Path.Combine(root,"zero.dat"),new byte[0]);
   Snapshot first=null;long expected=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);
   test("language resource initialization",()=>{L.Chinese=true;check(L.T("File Reports")=="文件报告","Chinese resource missing");L.Chinese=false;check(L.T("File Reports")=="File Reports","English fallback failed");L.Chinese=true;});
   test("native scan totals + parallel aggregation",()=>{first=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Threads=4},CancellationToken.None);check(first.Index[root].Size==expected,"Unexpected byte count");check(first.Index[root].Files==6,"Unexpected file count");check(first.Index[root].Folders==3,"Unexpected folder count");check(first.Events.Count==0,"Unexpected events: "+string.Join(";",first.Events.Select(e=>e.Message)));var single=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Threads=1},CancellationToken.None);check(single.Index[root].Size==first.Index[root].Size&&single.Entries.Count==first.Entries.Count,"Thread counts changed result");});
   test("overlapping roots not double counted",()=>{var s=new Scanner().Run(new[]{Path.Combine(root,"A"),root},new ScanOptions{UseMft=false},CancellationToken.None);check(s.Roots.Count==1&&s.Index[root].Size==expected,"Overlapping paths counted twice");});
   test("allocated sizes handle resident NTFS and regular files",()=>{foreach(var e in first.Entries.Where(e=>!e.Folder)){check(e.Allocated>=e.Size||(e.Size<1024&&e.Allocated==0),"Unexpected allocation for uncompressed fixture");check(e.Size<4096||e.Allocated%4096==0,"Unexpected allocation for non-resident fixture");}});
   test("scan include/exclude filters",()=>{var s=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Include="*.bin;*.txt",ExcludeFolders="B"},CancellationToken.None);check(s.Entries.Count(e=>!e.Folder)==3,"Filter count mismatch");check(!s.Entries.Any(e=>e.Path.Contains("\\B\\")),"Excluded branch scanned");});
   test("empty folder detection excludes skipped items",()=>{var rr=Reports.Build(first,"Empty Folders",0,"Name + size",CancellationToken.None);check(rr.Count==1&&rr[0].Name=="空文件夹","Incorrect empty folders");var s=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,MaximumDepth=1},CancellationToken.None);check(s.Entries.Any(e=>e.Skipped)&&s.Index[root].Incomplete,"Depth truncation not reported");check(Reports.Build(s,"Empty Folders",0,"Name",CancellationToken.None).Count==0,"Skipped directory reported empty");});
   test("metadata duplicate comparison",()=>{check(Reports.Build(first,"Duplicate Files",0,"Name + size",CancellationToken.None).Count==2,"Metadata duplicates mismatch");});
   test("filtered directories are not reported as empty",()=>{foreach(var opt in new[]{new ScanOptions{UseMft=false,Include="*.never-match"},new ScanOptions{UseMft=false,MinimumSize=999999},new ScanOptions{UseMft=false,Rules=new List<Rule>{new Rule{Value="*.never-match"}}}}){var s=new Scanner().Run(new[]{root},opt,CancellationToken.None);var rr=Reports.Build(s,"Empty Folders",0,"Name",CancellationToken.None);check(rr.Count==1&&rr[0].Name=="空文件夹","Filtered directories misreported as empty");}string xml=Path.Combine(folder,"filtered.fsscan");var filtered=new Scanner().Run(new[]{Path.Combine(root,"A")},new ScanOptions{UseMft=false,Include="*.never-match"},CancellationToken.None);Snapshot.Save(filtered,xml);check(Reports.Build(Snapshot.Load(xml),"Empty Folders",0,"Name",CancellationToken.None).Count==0,"Filtered state lost in snapshot");});
   test("folder scan rules prune branches without rejecting unrelated files",()=>{var opt=new ScanOptions{UseMft=false,Rules=new List<Rule>{new Rule{Action="Exclude",Target="Folders",Value="B"}}};var s=new Scanner().Run(new[]{root},opt,CancellationToken.None);check(s.Entries.Count(e=>!e.Folder)==5&&!s.Entries.Any(e=>Util.Under(e.Path,Path.Combine(root,"B"))),"Folder rule ignored");opt.Rules=new List<Rule>{new Rule{Action="Include",Target="Folders",Value="A"}};s=new Scanner().Run(new[]{root},opt,CancellationToken.None);check(s.Entries.Count(e=>!e.Folder)==5&&s.Entries.Count(e=>e.Folder)==2,"Folder inclusion incorrectly rejects file entries");bool rejected=false;try{new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Rules=new List<Rule>{new Rule{Target="Folders",Field="Size",Operator=">",Value="0"}}},CancellationToken.None);}catch(ArgumentException){rejected=true;}check(rejected,"Unavailable folder totals silently used during traversal");});
   test("extensionless files do not enter the image category",()=>{check(Reports.Category(new Entry{Path=@"C:\LICENSE"})=="Other","No-extension file classified as image");check(Reports.Category(new Entry{Path=@"C:\photo.JPG"})=="Images","Image category regression");});
   test("SHA-256 detects content with different names",()=>{var rr=Reports.Build(first,"Duplicate Files",0,"Content (SHA-256)",CancellationToken.None);check(rr.Count==3,"Content duplicate group mismatch");});
   test("all report families execute",()=>{foreach(string name in Reports.Names){var rr=Reports.Build(first,name,0,"Name + size",CancellationToken.None);check(rr!=null,"Null report "+name);}check(Reports.Build(first,"File Types",0,"Name",CancellationToken.None).Sum(r=>r.Size)==expected,"Classification lost bytes");});
   test("rule priority and typed inclusion",()=>{var rs=new[]{new Rule{Value="*.bin"},new Rule{Action="Exclude",Value="same.bin"}};check(first.Entries.Count(e=>Rules.Accept(e,rs))==1,"Exclusion or folder targeting wrong");check(new Rule{Field="Path length",Operator=">=",Value="2"}.Match(first.Entries.First(e=>!e.Folder)),"Numeric rule failed");});
   test("cancelled scan preserves partial-state marker",()=>{var c=new CancellationTokenSource();c.Cancel();var s=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false},c.Token);check(s.Cancelled&&s.Index[root].Incomplete,"Cancellation not visible");});
   string snapshot=Path.Combine(folder,"roundtrip.fsscan");test("snapshot save/load + comparison",()=>{Snapshot.Save(first,snapshot);var loaded=Snapshot.Load(snapshot);check(loaded.Entries.Count==first.Entries.Count&&loaded.Index[root].Size==expected,"Snapshot roundtrip lost data");File.AppendAllText(Path.Combine(root,"中文文档.txt"),"growth");var next=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false},CancellationToken.None);check(Reports.Compare(loaded,next).Any(r=>r.Path.EndsWith("中文文档.txt")&&r.Size>0),"Growth not detected");});
   test("untrusted XML DTD rejected",()=>{string path=Path.Combine(folder,"bad.xml");File.WriteAllText(path,"<!DOCTYPE Snapshot [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Snapshot><Format>&x;</Format></Snapshot>");bool rejected=false;try{Snapshot.Load(path);}catch{rejected=true;}check(rejected,"DTD accepted");});
   var rows=first==null?new List<ReportRow>():first.Entries.Select(Reports.Row).ToList();
   test("CSV formula neutralization + Unicode",()=>{var rr=new List<ReportRow>{new ReportRow{Name="=1+1",Path="中文路径"}};string csv=Exporter.ToCsv(rr,false);check(csv.Contains("'=1+1")&&csv.Contains("中文路径"),"Unsafe or corrupt CSV");});
   test("bilingual exports translate labels and preserve actual names",()=>{var sample=new Snapshot{Entries=new List<Entry>{new Entry{Path=@"C:\Other",Attributes=32,Size=1,Owner="Images"}}};L.Chinese=true;var named=Reports.Build(sample,"File Names",0,"Name",CancellationToken.None);var category=Reports.Build(sample,"File Categories",0,"Name",CancellationToken.None);var attrs=Reports.Build(sample,"File Attributes",0,"Name",CancellationToken.None);var owners=Reports.Build(sample,"File Owners",0,"Name",CancellationToken.None);check(named[0].DisplayName=="Other"&&owners[0].DisplayName=="Images","Actual name or owner was translated");check(category[0].DisplayName=="其他"&&attrs[0].DisplayName=="归档","Report label localization failed");check(Exporter.ToCsv(named,false).StartsWith("名称,大小（字节）")&&Exporter.ToCsv(named,false).Contains("Other"),"Chinese export headers wrong");L.Chinese=false;check(Exporter.ToCsv(named,false).StartsWith("Name,Size (bytes)")&&category[0].DisplayName=="Other","English export not restored");L.Chinese=true;});
   test("XLSX actual OpenXML archive",()=>{string file=Path.Combine(folder,"report.xlsx");Exporter.Save(file,"Report",rows);using(var z=ZipFile.OpenRead(file)){check(z.GetEntry("xl/worksheets/sheet1.xml")!=null,"Missing worksheet");using(var r=new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml").Open())){var doc=new XmlDocument();doc.LoadXml(r.ReadToEnd());check(doc.DocumentElement.LocalName=="worksheet","Invalid worksheet XML");}}});
      test("Excel preserves supplementary Unicode characters",()=>{string file=Path.Combine(folder,"unicode.xlsx");Exporter.Save(file,"Unicode",new List<ReportRow>{new ReportRow{Name="文件🗂️",Path="路径😀"}});using(var z=ZipFile.OpenRead(file))using(var r=new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml").Open())){string xml=r.ReadToEnd();check(xml.Contains("🗂️")&&xml.Contains("😀"),"Supplementary Unicode lost");}});
   test("HTML/XML/CSV/TXT reports",()=>{foreach(string ext in new[]{"html","xml","csv","txt"}){string file=Path.Combine(folder,"report."+ext);Exporter.Save(file,"中文报告",rows);check(new FileInfo(file).Length>100,"Empty export "+ext);}var doc=new XmlDocument();doc.Load(Path.Combine(folder,"report.xml"));check(doc.DocumentElement.Name=="FolderScopeReport","XML root wrong");});
   test("PDF header and trailer",()=>{string p=Path.Combine(folder,"report.pdf");Exporter.Save(p,"中文报告",rows);byte[] bytes=File.ReadAllBytes(p);check(System.Text.Encoding.ASCII.GetString(bytes.Take(8).ToArray()).StartsWith("%PDF-1.4"),"PDF header wrong");check(System.Text.Encoding.ASCII.GetString(bytes.Skip(bytes.Length-5).ToArray())=="%%EOF","PDF incomplete");});
      test("multipage PDF keeps all report pages",()=>{var list=Enumerable.Range(1,30).Select(i=>new ReportRow{Name="记录 / Row "+i,Size=i*1024,Count=i,Path="Test row "+i}).ToList();string p=Path.Combine(folder,"report-multipage.pdf");Exporter.Save(p,"多页报告 / Multipage",list);string raw=System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(p));check(raw.Contains("/Count 3 "),"Incorrect PDF page count");});
   test("ZIP content and empty directories",()=>{string z=Path.Combine(folder,"archive-"+Guid.NewGuid().ToString("N")+".zip");FileJobs.Archive(new[]{root},z);using(var zip=ZipFile.OpenRead(z)){check(zip.Entries.Count(e=>e.FullName.EndsWith("same.bin"))==2,"ZIP missing files");check(zip.Entries.Any(e=>e.FullName.EndsWith("空文件夹/")),"ZIP missing empty directory");}});
   test("copy and nested-destination protection",()=>{string dest=Path.Combine(folder,"copy-"+Guid.NewGuid().ToString("N"));FileJobs.Copy(Path.Combine(root,"A"),dest);check(File.ReadAllBytes(Path.Combine(dest,"same.bin")).SequenceEqual(data),"Copied content mismatch");bool denied=false;try{FileJobs.Copy(root,Path.Combine(root,"nested"));}catch(IOException){denied=true;}check(denied,"Nested copy accepted");});
      test("subfolder reports stay within selected scope",()=>{var scoped=first.Scope(new[]{Path.Combine(root,"A")});check(scoped.Entries.Count(e=>!e.Folder)==1,"Scope included sibling files");check(Reports.Build(scoped,"Largest Files",0,"Name",CancellationToken.None).Count==1,"Report ignored scope");});
   test("allocated size scan rules run after metadata collection",()=>{var opt=new ScanOptions{UseMft=false,Rules=new List<Rule>{new Rule{Field="Allocated",Operator=">=",Value="8192"}}};var ss=new Scanner().Run(new[]{root},opt,CancellationToken.None);check(ss.Entries.Count(e=>!e.Folder)==3,"Allocated filter applied before allocation collection");});
   test("long Unicode path scanning",()=>{string deep=Path.Combine(folder,"long-"+Guid.NewGuid().ToString("N"));while(deep.Length<310)deep=Path.Combine(deep,"目录abcdefghij0123456789");Directory.CreateDirectory(deep);string file=Path.Combine(deep,"测试.bin");File.WriteAllBytes(file,new byte[12000]);var ss=new Scanner().Run(new[]{deep},new ScanOptions{UseMft=false},CancellationToken.None);check(ss.Index[deep].Size==12000&&ss.Events.Count==0,"Long path scan failed");});
   test("scheduled job XML without registering a task",()=>{string xml=Jobs.TaskXml(new ScheduledJob{Paths=root,Output=folder,Frequency="Weekly",Start=DateTime.Now.AddDays(1)},Path.Combine(folder,"job & test.xml"));var d=new XmlDocument();d.LoadXml(xml);check(xml.Contains("InteractiveToken")&&xml.Contains("&amp;"),"Scheduler escaping/principal wrong");});
   ScannerTests.Run(test,check,folder);
   ReportTests.Run(folder,test,check);
   ForecastTests.Run(test,check);
   ExportTests.Run(folder,test,check);
   test("classification drill-down preserves displayed scope",()=>{var scoped=first.Scope(new[]{Path.Combine(root,"A")});foreach(string kind in new[]{"File Types","File Categories","File Names","File Years"}){var group=Reports.Build(scoped,kind,0,"Name",CancellationToken.None).Single();var drill=FileReportForm.DrillRows(scoped,kind,group.Name);check(drill.Count==1&&Util.Under(drill[0].Path,Path.Combine(root,"A")),"Drill-down leaked outside scope: "+kind);}});
   File.WriteAllLines(Path.Combine(folder,"test-results.txt"),results.Concat(new[]{"RESULT: "+(fails==0?"PASS":"FAIL")+" | "+(results.Count-fails)+"/"+results.Count,"FIXTURE: "+root}),new System.Text.UTF8Encoding(false));return fails==0?0:1;
  }
 }

 /// <summary>FolderScope.exe --benchmark &lt;path&gt;: times both scan engines and compares their results.</summary>
 public static class Benchmark {
  public static int Run(string path){
   var sb=new System.Text.StringBuilder();
   sb.AppendLine("FolderScope scan benchmark  "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
   sb.AppendLine("Path: "+path+"    Administrator: "+Scanner.IsElevated+"    CPU threads: "+Environment.ProcessorCount);sb.AppendLine();
   Snapshot mft=null,dir=null;
   foreach(bool useMft in new[]{true,false}){
    var o=new ScanOptions{UseMft=useMft};var sc=new Scanner();var sw=System.Diagnostics.Stopwatch.StartNew();
    Snapshot s;try{s=sc.Run(new[]{path},o,CancellationToken.None);}catch(Exception ex){sb.AppendLine((useMft?"MFT":"Directory")+": "+ex.Message);continue;}
    sw.Stop();var root=s.Index[s.Roots[0]];
    sb.AppendLine(string.Format("{0,-10} engine={1,-9} time={2,7:0.00}s  files={3,10:N0}  folders={4,9:N0}  size={5,14:N0}  allocated={6,14:N0}  events={7}",useMft?"UseMft":"NoMft",sc.Method,sw.Elapsed.TotalSeconds,root.Files,root.Folders,root.Size,root.Allocated,s.Events.Count));
    sb.AppendLine("           phases: "+sc.Timing);if(useMft&&sc.MftNeedsAdmin)sb.AppendLine("           (MFT mode needs administrator rights)");
    if(useMft&&sc.Method=="MFT")mft=s;else if(!useMft)dir=s;
   }
   if(mft!=null&&dir!=null){
    sb.AppendLine();sb.AppendLine("Largest folder differences (MFT minus directory enumeration):");
    var diffs=mft.Entries.Where(e=>e.Folder).Select(e=>{Entry d;dir.Index.TryGetValue(e.Path,out d);return new{e.Path,Delta=e.Size-(d==null?0:d.Size),Files=e.Files-(d==null?0:d.Files),Missing=d==null};}).Where(x=>x.Delta!=0||x.Files!=0).OrderByDescending(x=>Math.Abs(x.Delta)).Take(25);
    foreach(var x in diffs)sb.AppendLine(string.Format("  {0,16:N0} bytes  {1,8:N0} files  {2}{3}",x.Delta,x.Files,x.Path,x.Missing?"  [only in MFT]":""));
    int onlyDir=dir.Entries.Count(e=>!mft.Index.ContainsKey(e.Path));sb.AppendLine("Entries only found by directory enumeration: "+onlyDir.ToString("N0"));
    foreach(var e in dir.Entries.Where(e=>!mft.Index.ContainsKey(e.Path)).Take(15))sb.AppendLine("  "+e.Path);
   }
   string file=Path.Combine(Util.DataDir,"scan-benchmark.txt");File.WriteAllText(file,sb.ToString(),new System.Text.UTF8Encoding(true));
   try{System.Diagnostics.Process.Start("notepad.exe","\""+file+"\"");}catch{}
   return 0;
  }
 }
}
