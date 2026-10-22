using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;
using System.ComponentModel;

namespace FolderScope {
 public static class Util {
  public static string Size(long n) { string[] u={"B","KB","MB","GB","TB","PB"}; double v=n; int k=0; while(Math.Abs(v)>=1024 && k<u.Length-1){v/=1024;k++;} return v.ToString(k==0?"N0":"0.##")+" "+u[k]; }
  public static string Norm(string p) { p=Path.GetFullPath(p.Trim().Trim('"')); return p.Length>3?p.TrimEnd('\\'):p; }
  public static bool Under(string p,string root) {return p.Equals(root,StringComparison.OrdinalIgnoreCase)||p.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
  public static string LongPath(string p) {if(p.StartsWith(@"\\?\"))return p;return p.StartsWith(@"\\")?@"\\?\UNC\"+p.Substring(2):@"\\?\"+p;}
  public static string Date(DateTime d){return d==DateTime.MinValue?"":d.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");}
  private static readonly ConcurrentDictionary<string,Func<string,bool>> wildCache=new ConcurrentDictionary<string,Func<string,bool>>(StringComparer.Ordinal);
  public static bool Wild(string name,string patterns) {var m=WildMatcher(patterns);return m==null||m(name??"");}
  /// <summary>Compiles a ';'-separated wildcard list once. Returns null when everything matches.</summary>
  public static Func<string,bool> WildMatcher(string patterns){
   if(string.IsNullOrWhiteSpace(patterns))return null;
   return wildCache.GetOrAdd(patterns,key=>{
    var parts=key.Split(';').Select(p=>p.Trim()).Where(p=>p.Length>0).ToList();
    if(parts.Count==0||parts.Any(p=>p=="*"||p=="*.*"))return null;
    if(parts.All(p=>p.StartsWith("*.")&&p.IndexOfAny(new[]{'*','?'},2)<0)){var ext=parts.Select(p=>p.Substring(1)).ToArray();return n=>{foreach(var x in ext)if(n.EndsWith(x,StringComparison.OrdinalIgnoreCase))return true;return false;};}
    var rx=new Regex("^(?:"+string.Join("|",parts.Select(p=>Regex.Escape(p).Replace("\\*",".*").Replace("\\?",".")))+")$",RegexOptions.IgnoreCase|RegexOptions.Compiled|RegexOptions.CultureInvariant);
    return n=>rx.IsMatch(n);
   });
  }
  public static string Safe(string s) {return s??"";}
  public static string DataDir {get {string d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FolderScope");Directory.CreateDirectory(d);return d;}}
 }
 public class Entry {
  [XmlAttribute] public string Path;
  [XmlAttribute] public string Parent;
  [XmlAttribute] public bool Folder;
  [XmlAttribute] public long Size;
  [XmlAttribute] public long Allocated;
  [XmlAttribute] public long Files;
  [XmlAttribute] public long Folders;
  [XmlAttribute] public int Depth;
  [XmlAttribute] public int Attributes;
  [XmlAttribute] public DateTime Created;
  [XmlAttribute] public DateTime Modified;
  [XmlAttribute] public DateTime Accessed;
  [XmlAttribute] public string Owner;
  [XmlAttribute] public bool Incomplete;
  [XmlAttribute] public bool Skipped;
  [XmlAttribute] public bool Filtered;
  /// <summary>Parent entry object set by the scanner (avoids path lookups while aggregating).</summary>
  [XmlIgnore] public Entry Up;
  /// <summary>Child entries collected by the scanner (same list object as Snapshot.Children[Path]).</summary>
  [XmlIgnore] public List<Entry> Kids;
  [XmlIgnore] public string Name {get {string s=System.IO.Path.GetFileName((Path??"").TrimEnd('\\'));return string.IsNullOrEmpty(s)?Path:s;}}
  [XmlIgnore] public string Extension {get {return Folder?"":System.IO.Path.GetExtension(Path).ToLowerInvariant();}}
  [XmlIgnore] public string AttrText {get{return ((FileAttributes)Attributes).ToString();}}
 }
 public class ScanEvent { public DateTime Time=DateTime.UtcNow; public string Path; public string Message; }
 public class ScanOptions {
  public int Threads=4;
  public bool ReadOwners=false;
  public bool SkipHidden=false;
  public bool SkipSystem=false;
  public string Include="*";
  public string Exclude="";
  public string ExcludeFolders="";
  public long MinimumSize=0;
  public long MaximumSize=0;
  public int MaximumDepth=0;
  /// <summary>Read the NTFS Master File Table directly when running as administrator (much faster).</summary>
  public bool UseMft=true;
  public List<Rule> Rules=new List<Rule>();
 }
 public class Snapshot {
  public string Format="FolderScope/1";
  public DateTime Captured=DateTime.UtcNow;
  public bool Cancelled;
  public double ElapsedSeconds;
  public string Method="";
  public List<string> Roots=new List<string>();
  public ScanOptions Options=new ScanOptions();
  public List<Entry> Entries=new List<Entry>();
  public List<ScanEvent> Events=new List<ScanEvent>();
  [XmlIgnore] public PathIndex Index;
  [XmlIgnore] public Dictionary<string,List<Entry>> Children;
  public void Reindex() {Reindex(false);}
  /// <summary>Builds the path index. When tolerant, duplicate paths (e.g. case-sensitive folders) are dropped instead of failing.</summary>
  public void Reindex(bool tolerant) {
   var entries=Entries;bool linked=entries.Count>0;
   foreach(Entry e in entries)if(e.Up==null&&!string.IsNullOrEmpty(e.Parent)){linked=false;break;}
   HashSet<Entry> dup;
   var build=Task.Run(()=>{HashSet<Entry> d;var ix=PathIndex.Build(entries,tolerant,out d);return new KeyValuePair<PathIndex,HashSet<Entry>>(ix,d);});
   Dictionary<string,List<Entry>> children;
   if(linked){
    // Fresh scans: children come from the parent links; only folders need a path lookup.
    children=new Dictionary<string,List<Entry>>(StringComparer.OrdinalIgnoreCase);
    foreach(Entry e in entries)if(e.Kids!=null&&e.Kids.Count>0&&!children.ContainsKey(e.Path))children.Add(e.Path,e.Kids);
   }else{
    children=new Dictionary<string,List<Entry>>(StringComparer.OrdinalIgnoreCase);
    foreach(Entry e in entries)if(!string.IsNullOrEmpty(e.Parent)){List<Entry> a;if(!children.TryGetValue(e.Parent,out a)){a=new List<Entry>();children[e.Parent]=a;}a.Add(e);}
   }
   KeyValuePair<PathIndex,HashSet<Entry>> r;try{r=build.Result;}catch(AggregateException ex){throw ex.GetBaseException();}
   dup=r.Value;
   if(dup!=null){foreach(var a in children.Values)a.RemoveAll(dup.Contains);entries.RemoveAll(dup.Contains);}
   Index=r.Key;Children=children;
  }
  public void Aggregate() {
   Reindex(true);int max=0;
   foreach(Entry e in Entries){if(e.Folder){e.Size=0;e.Allocated=0;e.Files=0;e.Folders=0;}if(e.Depth>max)max=e.Depth;}
   var levels=new List<Entry>[max+1];for(int d=0;d<=max;d++)levels[d]=new List<Entry>();
   foreach(Entry e in Entries)levels[e.Depth].Add(e);
   for(int d=max;d>0;d--)foreach(Entry e in levels[d]){Entry p=e.Up;if(p==null&&!string.IsNullOrEmpty(e.Parent))Index.TryGetValue(e.Parent,out p);if(p!=null){p.Size+=e.Size;p.Allocated+=e.Allocated;p.Files+=e.Folder?e.Files:1;p.Folders+=e.Folder?e.Folders+1:0;if(e.Incomplete||e.Skipped)p.Incomplete=true;}}
  }
  public IEnumerable<Entry> Branch(string path) {return Entries.Where(e=>Util.Under(e.Path,path));}
  public List<Entry> Direct(string path) {List<Entry> a;return Children.TryGetValue(path,out a)?a:new List<Entry>();}
  public Snapshot Scope(IEnumerable<string> input){var roots=new List<string>();foreach(string raw in input){string p=Util.Norm(raw);if(!Index.ContainsKey(p))throw new InvalidOperationException(L.Chinese?"当前扫描结果不包含此路径，请先扫描："+p:"This path is outside the current scan; scan it first: "+p);if(!roots.Any(r=>Util.Under(p,r))){roots.RemoveAll(r=>Util.Under(r,p));roots.Add(p);}}if(roots.Count==Roots.Count&&roots.All(r=>Roots.Contains(r,StringComparer.OrdinalIgnoreCase)))return this;var picked=new List<Entry>();var stack=new Stack<Entry>();foreach(string r in roots)stack.Push(Index[r]);while(stack.Count>0){Entry e=stack.Pop();picked.Add(e);List<Entry> kids;if(e.Folder&&Children.TryGetValue(e.Path,out kids))foreach(Entry k in kids)stack.Push(k);}var s=new Snapshot{Captured=Captured,Cancelled=Cancelled,ElapsedSeconds=ElapsedSeconds,Method=Method,Options=Options,Roots=roots,Entries=picked,Events=Events.Where(e=>roots.Any(r=>Util.Under(e.Path,r))).ToList()};s.Reindex(true);return s;}
  public static void Save(Snapshot s,string file) {AtomicXml.Write(s,file);}
  public static Snapshot Load(string file) {Snapshot s=AtomicXml.Read<Snapshot>(file);if(s.Format!="FolderScope/1")throw new InvalidDataException("This file is not a FolderScope snapshot.");s.Reindex();return s;}
 }
 /// <summary>Case-insensitive path -> entry map split into shards so millions of paths can be indexed in parallel.</summary>
 public sealed class PathIndex {
  const int Shards=32;static readonly StringComparer Cmp=StringComparer.OrdinalIgnoreCase;
  private readonly Dictionary<string,Entry>[] parts=new Dictionary<string,Entry>[Shards];
  public PathIndex(int capacity){for(int i=0;i<Shards;i++)parts[i]=new Dictionary<string,Entry>(capacity/Shards+16,Cmp);}
  static int Shard(string key){return (int)((uint)Cmp.GetHashCode(key)%Shards);}
  public bool ContainsKey(string key){return parts[Shard(key)].ContainsKey(key);}
  public bool TryGetValue(string key,out Entry value){return parts[Shard(key)].TryGetValue(key,out value);}
  public Entry this[string key]{get{return parts[Shard(key)][key];}}
  public void Add(string key,Entry value){parts[Shard(key)].Add(key,value);}
  public int Count{get{int n=0;foreach(var p in parts)n+=p.Count;return n;}}
  public IEnumerable<string> Keys{get{foreach(var p in parts)foreach(string k in p.Keys)yield return k;}}
  public static PathIndex Build(List<Entry> entries,bool tolerant,out HashSet<Entry> duplicates){
   int n=entries.Count;var index=new PathIndex(n);var shard=new byte[n];
   Parallel.For(0,Math.Max(1,Math.Min(Environment.ProcessorCount,n/8192+1)),part=>{int count=Math.Max(1,Math.Min(Environment.ProcessorCount,n/8192+1));int a=(int)((long)n*part/count),b=(int)((long)n*(part+1)/count);for(int i=a;i<b;i++)shard[i]=(byte)Shard(entries[i].Path);});
   var dups=new HashSet<Entry>[Shards];string bad=null;
   Parallel.For(0,Shards,k=>{var d=index.parts[k];byte key=(byte)k;for(int i=0;i<n;i++){if(shard[i]!=key)continue;Entry e=entries[i];if(d.ContainsKey(e.Path)){if(!tolerant){bad=e.Path;return;}if(dups[k]==null)dups[k]=new HashSet<Entry>();dups[k].Add(e);continue;}d.Add(e.Path,e);}});
   if(bad!=null)throw new InvalidDataException("Duplicate path in snapshot: "+bad);
   duplicates=null;foreach(var d in dups)if(d!=null){if(duplicates==null)duplicates=new HashSet<Entry>();duplicates.UnionWith(d);}
   return index;
  }
 }
 public static class AtomicXml {
  public static void Write<T>(T obj,string file) {file=Path.GetFullPath(file);Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var w=XmlWriter.Create(temp,new XmlWriterSettings{Indent=true,Encoding=new UTF8Encoding(false)})){new XmlSerializer(typeof(T)).Serialize(w,obj);}if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}}
  public static T Read<T>(string file) {using(var r=XmlReader.Create(file,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024L*1024*1024})){return (T)new XmlSerializer(typeof(T)).Deserialize(r);}}
 }
 internal static class Native {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct FindData {
   public uint Attr;public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;public uint SizeHigh,SizeLow,Reserved0,Reserved1;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=14)] public string Alt;
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr FindFirstFileExW(string path,int info,out FindData data,int search,IntPtr filter,int flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool FindNextFileW(IntPtr h,out FindData data);
  [DllImport("kernel32.dll")] public static extern bool FindClose(IntPtr h);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern uint GetCompressedFileSizeW(string path,out uint high);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool GetDiskFreeSpaceW(string root,out uint sectors,out uint bytes,out uint free,out uint total);
  [StructLayout(LayoutKind.Sequential)] public struct StandardInfo {public long AllocationSize,EndOfFile;public uint NumberOfLinks;public byte DeletePending,Directory;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFileW(string file,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandleEx(IntPtr h,int info,out StandardInfo data,uint size);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static bool Allocation(string path,out long bytes){bytes=0;IntPtr h=CreateFileW(Util.LongPath(path),0,7,IntPtr.Zero,3,0x00200000,IntPtr.Zero);if(h==new IntPtr(-1))return false;try{StandardInfo info;if(!GetFileInformationByHandleEx(h,1,out info,(uint)Marshal.SizeOf(typeof(StandardInfo))))return false;bytes=info.AllocationSize;return true;}finally{CloseHandle(h);}}
  public static DateTime Time(System.Runtime.InteropServices.ComTypes.FILETIME t){try{return DateTime.FromFileTimeUtc(((long)t.dwHighDateTime<<32)|(uint)t.dwLowDateTime);}catch{return DateTime.MinValue;}}
 }
 public sealed class ScanProgress {public long Files;public long Folders;public long Bytes;public string Current;public int Events;}
 public class Rule {
  public bool Enabled=true;
  public string Action="Include";
  public string Target="Files";
  public string Field="Name";
  public string Operator="Wildcard";
  public string Value="*";
  public string Description="";
  public bool Match(Entry e) {
   if(!Enabled||Target=="Files"&&e.Folder||Target=="Folders"&&!e.Folder)return false;
   string text=Field=="Path"?e.Path:Field=="Extension"?e.Extension:Field=="Owner"?e.Owner:Field=="Attributes"?e.AttrText:e.Name;
   if(Field=="Size"||Field=="Allocated"||Field=="Depth"||Field=="Path length"||Field=="Name length"||Field=="File count"||Field=="Age days") {
    double value;if(!double.TryParse(Value,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out value))throw new FormatException("Rule numeric value is invalid: "+Value);
    double actual=Field=="Size"?e.Size:Field=="Allocated"?e.Allocated:Field=="Depth"?e.Depth:Field=="Path length"?e.Path.Length:Field=="Name length"?e.Name.Length:Field=="File count"?e.Files:(DateTime.UtcNow-e.Modified).TotalDays;
    return Operator==">="?actual>=value:Operator=="<="?actual<=value:Operator==">"?actual>value:Operator=="<"?actual<value:Operator=="!="?actual!=value:actual==value;
   }
   text=text??"";
   if(Operator=="Regex")return Regex.IsMatch(text,Value,RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));
   if(Operator=="Contains")return text.IndexOf(Value,StringComparison.OrdinalIgnoreCase)>=0;
   if(Operator=="!=")return !text.Equals(Value,StringComparison.OrdinalIgnoreCase);
   if(Operator=="=")return text.Equals(Value,StringComparison.OrdinalIgnoreCase);
   return Util.Wild(text,Value);
  }
 }
 public static class Rules {
  public static bool Accept(Entry e,IEnumerable<Rule> list){var all=list.Where(r=>r.Enabled).ToList();var rules=all.Where(r=>r.Target=="Both"||r.Target=="Files"&&!e.Folder||r.Target=="Folders"&&e.Folder).ToList();if(rules.Any(r=>r.Action=="Exclude"&&r.Match(e)))return false;var inc=rules.Where(r=>r.Action=="Include").ToList();return inc.Count==0?!all.Any(r=>r.Action=="Include"):inc.Any(r=>r.Match(e));}
  public static void Validate(IEnumerable<Rule> rules){foreach(var r in rules.Where(r=>r.Enabled)){if(r.Operator=="Regex")new Regex(r.Value,RegexOptions.IgnoreCase,TimeSpan.FromSeconds(1));r.Match(new Entry{Path=@"C:\sample.txt",Folder=r.Target=="Folders",Modified=DateTime.UtcNow});}}
 }
 public class ReportRow {
  public string Name;public long Size;public long Allocated;public long Count;public double Percent;public string Path;public string Owner;public DateTime Modified;public string Extra;public Entry Source;
  public bool LocalizeName;public bool AttributeName;
  public string DisplayName {get{return AttributeName?L.Attributes(Name??""):LocalizeName?L.Value(Name):Name??"";}}
 }
 public static class Reports {
  public static readonly string[] Names={"Largest Files","Oldest Files","Newest Files","Temporary Files","Duplicate Files","File Types","File Categories","File Attributes","File Owners","File Sizes","File Names","File Ages","File Depths","File Years","Non-Business Files","Empty Folders","Long Paths"};
  public static string Category(Entry e){string x=e.Extension;if(x.Length>1&&".jpg.jpeg.png.gif.webp.heic.bmp.tif.tiff.svg.raw".Split('.').Contains(x.TrimStart('.')))return "Images";if(new[]{".mp4",".mkv",".avi",".mov",".wmv",".ts",".m2ts",".webm"}.Contains(x))return "Video";if(new[]{".mp3",".flac",".wav",".aac",".ogg",".m4a"}.Contains(x))return "Audio";if(new[]{".zip",".rar",".7z",".gz",".tar",".iso"}.Contains(x))return "Archives";if(new[]{".doc",".docx",".pdf",".xls",".xlsx",".ppt",".pptx",".txt",".md",".csv",".rtf"}.Contains(x))return "Documents";if(new[]{".exe",".dll",".sys",".msi",".com"}.Contains(x))return "Applications";if(new[]{".cs",".py",".js",".ts",".cpp",".h",".json",".html",".css",".xml",".sql"}.Contains(x))return "Source & data";return "Other";}
  public static string SizeBucket(long n){return n==0?"0 B":n<1024?"1 B - 1 KB":n<1024*1024?"1 KB - 1 MB":n<100L*1024*1024?"1 MB - 100 MB":n<1024L*1024*1024?"100 MB - 1 GB":n<10L*1024*1024*1024?"1 GB - 10 GB":">= 10 GB";}
  public static string AgeBucket(DateTime d){double days=(DateTime.UtcNow-d).TotalDays;return days<1?"Today":days<7?"1 - 7 days":days<30?"7 - 30 days":days<90?"1 - 3 months":days<365?"3 - 12 months":days<1095?"1 - 3 years":">= 3 years";}
  public static ReportRow Row(Entry e){return new ReportRow{Name=e.Name,Size=e.Size,Allocated=e.Allocated,Count=e.Folder?e.Files:1,Path=e.Path,Owner=e.Owner,Modified=e.Modified,Extra=e.AttrText+(e.Skipped?" [skipped]":e.Incomplete?" [partial]":""),Source=e};}
  public static List<ReportRow> Build(Snapshot s,string kind,int limit,string duplicateMode,CancellationToken token) {
   var files=s.Entries.Where(e=>!e.Folder).ToList();IEnumerable<Entry> seq=files;Func<Entry,string> group=null;
   switch(kind){
    case "Largest Files":seq=files.OrderByDescending(e=>e.Size);break;
    case "Oldest Files":seq=files.OrderBy(e=>e.Modified);break;
    case "Newest Files":seq=files.OrderByDescending(e=>e.Modified);break;
    case "Temporary Files":seq=files.Where(e=>Util.Wild(e.Name,"*.tmp;*.temp;*.bak;~*;*.old;*.dmp")||e.Path.IndexOf("\\Temp\\",StringComparison.OrdinalIgnoreCase)>=0);break;
    case "Empty Folders":seq=s.Entries.Where(e=>e.Folder&&e.Files==0&&e.Folders==0&&!e.Incomplete&&!e.Skipped&&!e.Filtered);break;
    case "Long Paths":seq=s.Entries.Where(e=>e.Path.Length>=260).OrderByDescending(e=>e.Path.Length);break;
    case "Non-Business Files":seq=files.Where(e=>new[]{"Images","Audio","Video"}.Contains(Category(e)));break;
    case "File Types":group=e=>string.IsNullOrEmpty(e.Extension)?"(no extension)":e.Extension;break;
    case "File Categories":group=Category;break;
    case "File Attributes":group=e=>e.AttrText;break;
    case "File Owners":group=e=>string.IsNullOrEmpty(e.Owner)?"(owner not collected)":e.Owner;break;
    case "File Sizes":group=e=>SizeBucket(e.Size);break;
    case "File Names":group=e=>e.Name;break;
    case "File Ages":group=e=>AgeBucket(e.Modified);break;
    case "File Depths":group=e=>"Depth "+e.Depth;break;
    case "File Years":group=e=>e.Modified.Year.ToString();break;
    case "Duplicate Files":var duplicates=Duplicates(files,duplicateMode,token);double allBytes=files.Sum(e=>e.Size);foreach(var duplicate in duplicates)duplicate.Percent=allBytes==0?0:duplicate.Size*100.0/allBytes;return duplicates;
   }
   List<ReportRow> rows;
   if(group!=null)rows=files.GroupBy(group,StringComparer.OrdinalIgnoreCase).Select(g=>new ReportRow{Name=g.Key,LocalizeName=kind!="File Names"&&(kind!="File Owners"||g.Key=="(owner not collected)"),AttributeName=kind=="File Attributes",Size=g.Sum(e=>e.Size),Allocated=g.Sum(e=>e.Allocated),Count=g.LongCount(),Extra="",Path=""}).OrderByDescending(r=>r.Size).ToList();
   else rows=(limit>0?seq.Take(limit):seq).Select(Row).ToList();
   double total=files.Sum(e=>e.Size);foreach(var r in rows)r.Percent=total==0?0:r.Size*100.0/total;return rows;
  }
  public static List<ReportRow> Duplicates(List<Entry> files,string mode,CancellationToken token) {
   var rows=new List<ReportRow>();int id=0;IEnumerable<IGrouping<string,Entry>> groups;
   if(mode=="Content (SHA-256)"){
    var hashes=new List<KeyValuePair<string,Entry>>();
    foreach(var candidates in files.Where(e=>!e.Skipped).GroupBy(e=>e.Size).Where(g=>g.Count()>1))foreach(Entry e in candidates){token.ThrowIfCancellationRequested();try{var before=new FileInfo(e.Path);if(before.Length!=e.Size||before.LastWriteTimeUtc!=e.Modified)continue;using(var stream=new FileStream(e.Path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan))using(var hash=SHA256.Create()){byte[] buffer=new byte[1024*1024];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();hash.TransformBlock(buffer,0,n,buffer,0);}hash.TransformFinalBlock(buffer,0,0);hashes.Add(new KeyValuePair<string,Entry>(Convert.ToBase64String(hash.Hash),e));}}catch(OperationCanceledException){throw;}catch(Exception ex){throw new IOException("Could not verify duplicate content: "+e.Path+" - "+ex.Message,ex);}}
    groups=hashes.GroupBy(p=>p.Key,p=>p.Value);
   }else groups=files.GroupBy(e=>e.Name.ToLowerInvariant()+(mode=="Name"?"":"|"+e.Size)+(mode=="Name + size + modified"?"|"+e.Modified.Ticks:""));
   foreach(var g in groups.Where(g=>g.Count()>1)){id++;foreach(Entry e in g){var r=Row(e);r.Extra="Group "+id+" | "+mode;rows.Add(r);}}
   return rows;
  }
  public static List<ReportRow> Compare(Snapshot old,Snapshot current){var result=new List<ReportRow>();foreach(string path in old.Index.Keys.Union(current.Index.Keys,StringComparer.OrdinalIgnoreCase)){Entry a,b;old.Index.TryGetValue(path,out a);current.Index.TryGetValue(path,out b);long diff=(b==null?0:b.Size)-(a==null?0:a.Size);if(a==null||b==null||diff!=0||(a.Modified!=b.Modified)){result.Add(new ReportRow{Name=(b??a).Name,Size=diff,Allocated=(b==null?0:b.Allocated)-(a==null?0:a.Allocated),Count=(b==null?0:b.Files)-(a==null?0:a.Files),Path=path,Modified=(b??a).Modified,Extra=a==null?"Added":b==null?"Removed":"Changed",Source=b});}}return result.OrderByDescending(r=>Math.Abs(r.Size)).ToList();}
 }
 public class AppSettings {
  public string Language="zh-CN";
  public string Theme="Light (Blue)";
  public string LastPath="";
  public bool Welcome=true;
  public ScanOptions Scan=new ScanOptions();
  public string SmtpHost="";public int SmtpPort=587;public bool SmtpSsl=true;public string SmtpUser="";public string SmtpPassword="";public string MailFrom="";public string MailTo="";
  public static AppSettings Load(){try{return AtomicXml.Read<AppSettings>(Path.Combine(Util.DataDir,"settings.xml"));}catch{return new AppSettings();}}
  public void Save(){AtomicXml.Write(this,Path.Combine(Util.DataDir,"settings.xml"));}
  public string Password {get{try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(SmtpPassword),null,DataProtectionScope.CurrentUser));}catch{return "";}}set{SmtpPassword=string.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));}}
 }
}
