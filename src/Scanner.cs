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
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace FolderScope {
 /// <summary>
 /// File-system scanner with two engines:
 ///  1. NTFS MFT direct read (like Everything / WizTree): reads the whole Master File Table of a local NTFS volume
 ///     sequentially and rebuilds the tree in memory. Requires administrator rights.
 ///  2. Parallel directory enumeration via GetFileInformationByHandleEx(FileFullDirectoryInfo): one system call returns
 ///     names, sizes, allocation sizes, attributes and timestamps for batches of entries.
 /// Engine 1 is used when possible and falls back to engine 2 when raw metadata cannot be validated.
 /// </summary>
 public sealed class Scanner {
  public readonly ManualResetEventSlim Pause=new ManualResetEventSlim(true);
  public long FileCount,FolderCount,ByteCount;
  public string Current="";
  /// <summary>"MFT", "Directory" or "Mixed" after Run().</summary>
  public string Method="";
  /// <summary>True when an NTFS volume could not use MFT mode only because the process is not elevated.</summary>
  public bool MftNeedsAdmin;
  /// <summary>Per-phase timings of the last run (for --benchmark).</summary>
  public string Timing="";
  internal System.Diagnostics.Stopwatch Clock=new System.Diagnostics.Stopwatch();
  internal void Phase(string name){Timing+=(Timing.Length>0?"  ":"")+name+"="+Clock.Elapsed.TotalSeconds.ToString("0.00")+"s";Clock.Restart();}
  private ConcurrentBag<ScanEvent> events;
  private ScanOptions options;private Filters filters;private CancellationToken token;
  internal void Log(string path,string msg){events.Add(new ScanEvent{Path=path,Message=msg});}
  public static string OwnerOf(string path,bool folder) {try {FileSystemSecurity acl=folder?(FileSystemSecurity)Directory.GetAccessControl(path,AccessControlSections.Owner):File.GetAccessControl(path,AccessControlSections.Owner);return acl.GetOwner(typeof(NTAccount)).Value;}catch(Exception ex){return "["+ex.GetType().Name+"]";}}
  public static bool IsElevated {get{try{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}catch{return false;}}}

  /// <summary>Pre-compiled filters so the per-entry cost is a few comparisons.</summary>
  internal sealed class Filters {
   public Func<string,bool> Include,Exclude,ExcludeFolders;public List<Rule> FileRules,FolderRules;
   public Filters(ScanOptions o){
    Include=Util.WildMatcher(o.Include);Exclude=ExcludeMatcher(o.Exclude);ExcludeFolders=ExcludeMatcher(o.ExcludeFolders);
    var enabled=o.Rules.Where(r=>r.Enabled).ToList();
    FileRules=enabled.Any(r=>r.Target!="Folders")?o.Rules.Where(r=>r.Target!="Folders").ToList():null;
    FolderRules=enabled.Any(r=>r.Target!="Files")?o.Rules.Where(r=>r.Target!="Files").ToList():null;
   }
   static Func<string,bool> ExcludeMatcher(string patterns){return string.IsNullOrWhiteSpace(patterns)||patterns.Split(';').All(string.IsNullOrWhiteSpace)?null:(Util.WildMatcher(patterns)??(n=>true));}
  }

  /// <summary>Applies every scan option to a freshly created entry. Returns false when it must be dropped.</summary>
  internal bool Admit(Entry dir,Entry e,string name,out bool recurse){
   recurse=false;var o=options;int attr=e.Attributes;if(name==null&&NeedName(e.Folder))name=e.Name;
   if(o.SkipCloudPlaceholders&&Native.IsCloudPlaceholder(attr,true)){dir.Filtered=true;Log(e.Path,"Cloud/offline placeholder excluded by scan option.");return false;}
   if(o.SkipHidden&&(attr&2)!=0||o.SkipSystem&&(attr&4)!=0){dir.Filtered=true;return false;}
   bool reparse=(attr&1024)!=0;
   if(e.Folder){
    if(filters.ExcludeFolders!=null&&(filters.ExcludeFolders(name)||filters.ExcludeFolders(e.Path))){dir.Filtered=true;return false;}
    if(o.ReadOwners)e.Owner=OwnerOf(e.Path,true);
    if(filters.FolderRules!=null&&!Rules.Accept(e,filters.FolderRules)){dir.Filtered=true;return false;}
    if(reparse){e.Skipped=true;Log(e.Path,"Reparse-point directory was not followed (loop/duplicate traversal prevention).");}
    else if(o.MaximumDepth>0&&e.Depth>=o.MaximumDepth){e.Skipped=true;Log(e.Path,"Maximum scan depth reached.");}
    else recurse=true;
    return true;
   }
   if(filters.Include!=null&&!filters.Include(name)||filters.Exclude!=null&&filters.Exclude(name)||e.Size<o.MinimumSize||o.MaximumSize>0&&e.Size>o.MaximumSize){dir.Filtered=true;return false;}
   if(o.ReadOwners)e.Owner=OwnerOf(e.Path,false);
   if(reparse){e.Allocated=0;e.Skipped=true;Log(e.Path,"Reparse-point file: content not read; allocation unavailable.");}
   if(filters.FileRules!=null&&!Rules.Accept(e,filters.FileRules)){dir.Filtered=true;return false;}
   return true;
  }
  internal static string Join(string parent,string name){return parent[parent.Length-1]=='\\'?parent+name:parent+"\\"+name;}
  /// <summary>Builds parent+"\\"+name with a single string allocation.</summary>
  internal static unsafe string Join(string parent,char* name,int length){
   bool slash=parent[parent.Length-1]!='\\';string s=new string('\0',parent.Length+(slash?1:0)+length);
   fixed(char* d=s){char* w=d;for(int i=0;i<parent.Length;i++)*w++=parent[i];if(slash)*w++='\\';for(int i=0;i<length;i++)*w++=name[i];}
   return s;
  }
  /// <summary>The bare name is only materialised when a name filter actually needs it.</summary>
  internal bool NeedName(bool folder){return folder?filters.ExcludeFolders!=null:filters.Include!=null||filters.Exclude!=null;}
  internal void Wait(){if(!Pause.IsSet)Pause.Wait(token);token.ThrowIfCancellationRequested();}

  public Snapshot Run(IEnumerable<string> inputs,ScanOptions options,CancellationToken token) {
   Rules.Validate(options.Rules);
   if(options.Rules.Any(r=>r.Enabled&&r.Target!="Files"&&(r.Field=="Size"||r.Field=="Allocated"||r.Field=="File count")))throw new ArgumentException(L.Chinese?"扫描前无法按文件夹的总大小、占用空间或文件数剪枝；请先扫描，再在搜索中使用这些条件。":"Folder size, allocation and file counts are unavailable before traversal. Scan first, then apply these rules in Search.");
   events=new ConcurrentBag<ScanEvent>();FileCount=0;FolderCount=0;ByteCount=0;Pause.Set();this.options=options;this.token=token;filters=new Filters(options);MftNeedsAdmin=false;
var watch=System.Diagnostics.Stopwatch.StartNew();Timing="";Clock.Restart();var roots=new List<string>();
   foreach(string raw in inputs.Where(x=>!string.IsNullOrWhiteSpace(x))){string p=Util.Norm(raw);if(!Directory.Exists(p))throw new DirectoryNotFoundException(p);if(!roots.Any(r=>Util.Under(p,r))){roots.RemoveAll(r=>Util.Under(r,p));roots.Add(p);}}
   if(roots.Count==0)throw new ArgumentException("Choose at least one folder.");
   var records=new List<Entry>();var methods=new HashSet<string>();
   foreach(var group in roots.GroupBy(r=>Path.GetPathRoot(r),StringComparer.OrdinalIgnoreCase)){
    var list=group.ToList();
    if(options.UseMft&&!options.SkipCloudPlaceholders&&!token.IsCancellationRequested){
     long beforeFiles=FileCount,beforeFolders=FolderCount,beforeBytes=ByteCount;
     string why;List<Entry> got=null;try{got=MftScan.TryScan(this,group.Key,list,token,out why);}catch(OperationCanceledException){why="cancel";}
     if(got!=null){records.AddRange(got);methods.Add("MFT");continue;}
     FileCount=beforeFiles;FolderCount=beforeFolders;ByteCount=beforeBytes;
     if(why=="admin")MftNeedsAdmin=true;
    }
    records.AddRange(DirectoryScan(list));methods.Add("Directory");Phase("enumerate");
   }
   watch.Stop();
   Method=methods.Count==1?methods.First():methods.Count==0?"Directory":"Mixed";Current="";
   var result=new Snapshot{Roots=roots,Options=options,Entries=records,Events=events.OrderBy(e=>e.Time).ToList(),Cancelled=token.IsCancellationRequested,ElapsedSeconds=watch.Elapsed.TotalSeconds,Method=Method};
   if(result.Cancelled){foreach(Entry e in result.Entries)if(e.Folder)e.Incomplete=true;result.Events.Add(new ScanEvent{Path=string.Join(";",roots),Message="Scan cancelled: results are partial."});}
   Phase("collect");result.Aggregate();Phase("aggregate");return result;
  }

  internal Entry RootEntry(string p){
   var d=new DirectoryInfo(p);int attributes=(int)d.Attributes;
   if(options.SkipCloudPlaceholders){
    int found;if(Native.TryGetDirectoryAttributes(p,out found))attributes=found;
    else attributes&=~0x40000; // RECALL_ON_OPEN shares its bit with EA outside directory enumeration.
   }
   var e=new Entry{Path=p,Folder=true,Depth=0,Created=d.CreationTimeUtc,Modified=d.LastWriteTimeUtc,Accessed=d.LastAccessTimeUtc,Attributes=attributes,Owner=""};
   if(options.SkipCloudPlaceholders&&Native.IsCloudPlaceholder(attributes,true)){e.Skipped=true;e.Incomplete=true;Log(p,"Cloud/offline root was not traversed by scan option.");}
   else if(options.ReadOwners)e.Owner=OwnerOf(p,true);
   return e;
  }

  // ---------------------------------------------------------------- directory enumeration engine
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string file,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle h,int infoClass,IntPtr buffer,uint size);

  private List<Entry> DirectoryScan(List<string> roots){
   var work=new BlockingCollection<Entry>(new ConcurrentStack<Entry>());int pending=0;
   var output=new List<Entry>();
   foreach(string p in roots){var e=RootEntry(p);output.Add(e);if(!e.Skipped){Interlocked.Increment(ref pending);work.Add(e);}}
   if(pending==0){work.Dispose();return output;}
   int workers=Math.Max(1,Math.Min(32,options.Threads));
   var lists=new List<Entry>[workers];var threads=new Thread[workers];
   for(int k=0;k<workers;k++){
    int slot=k;lists[k]=new List<Entry>(4096);
    threads[k]=new Thread(()=>{
     const int size=256*1024;IntPtr buffer=Marshal.AllocHGlobal(size);var mine=lists[slot];
     try{
      foreach(Entry dir in work.GetConsumingEnumerable(token)){
       try{Wait();Current=dir.Path;Interlocked.Increment(ref FolderCount);EnumerateDirectory(dir,buffer,size,mine,work,ref pending);}
       catch(OperationCanceledException){dir.Incomplete=true;}
       catch(Exception ex){dir.Incomplete=true;Log(dir.Path,ex.Message);}
       finally{if(Interlocked.Decrement(ref pending)==0)work.CompleteAdding();}
      }
     }catch(OperationCanceledException){}
     finally{Marshal.FreeHGlobal(buffer);}
    }){IsBackground=true,Name="FolderScope scan "+k};
    threads[k].Start();
   }
   foreach(var t in threads)t.Join();
   foreach(var l in lists)output.AddRange(l);
   work.Dispose();
   return output;
  }
  private unsafe void EnumerateDirectory(Entry dir,IntPtr buffer,int size,List<Entry> output,BlockingCollection<Entry> work,ref int pending){
   uint flags=0x02000000;if(options.SkipCloudPlaceholders)flags|=0x00100000|0x00200000;
   using(var h=CreateFileW(Util.LongPath(dir.Path),0x100001,7,IntPtr.Zero,3,flags,IntPtr.Zero)){
    if(h.IsInvalid){int err=Marshal.GetLastWin32Error();if(err!=2&&err!=3){dir.Incomplete=true;Log(dir.Path,new Win32Exception(err).Message);}return;}
    bool first=true;long files=0,bytes=0;
    while(true){
     try{Wait();}catch(OperationCanceledException){dir.Incomplete=true;break;}
     if(!GetFileInformationByHandleEx(h,first?15:14,buffer,(uint)size)){int err=Marshal.GetLastWin32Error();if(err!=18){dir.Incomplete=true;Log(dir.Path,"Enumeration incomplete: "+new Win32Exception(err).Message);}break;}
     first=false;byte* p=(byte*)buffer;
     while(true){
      uint next=*(uint*)p;int nameBytes=*(int*)(p+60);char* n=(char*)(p+68);
      if(!(nameBytes==2&&n[0]=='.'||nameBytes==4&&n[0]=='.'&&n[1]=='.')){
       uint attr=*(uint*)(p+56);bool folder=(attr&16)!=0;
       var e=new Entry{Path=Join(dir.Path,n,nameBytes/2),Parent=dir.Path,Up=dir,Folder=folder,Depth=dir.Depth+1,Attributes=(int)attr,Created=Time(*(long*)(p+8)),Accessed=Time(*(long*)(p+16)),Modified=Time(*(long*)(p+24)),Owner=""};
       string name=NeedName(folder)?new string(n,0,nameBytes/2):null;
       if(!folder){e.Size=*(long*)(p+40);e.Allocated=*(long*)(p+48);}
       bool recurse;
       if(Admit(dir,e,name,out recurse)){
        output.Add(e);(dir.Kids??(dir.Kids=new List<Entry>())).Add(e);
        if(folder){if(recurse){Interlocked.Increment(ref pending);work.Add(e);}}
        else{files++;bytes+=e.Size;}
       }
      }
      if(next==0)break;p+=next;
     }
    }
    if(files>0){Interlocked.Add(ref FileCount,files);Interlocked.Add(ref ByteCount,bytes);}
   }
  }
  internal static DateTime Time(long fileTime){if(fileTime<=0)return DateTime.MinValue;try{return DateTime.FromFileTimeUtc(fileTime);}catch{return DateTime.MinValue;}}
 }

 /// <summary>NTFS Master File Table reader.</summary>
 internal static unsafe class MftScan {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string file,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr inBuffer,int inSize,out VolumeData outBuffer,int outSize,out int returned,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadFile(SafeFileHandle h,byte* buffer,int count,out int read,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetFilePointerEx(SafeFileHandle h,long distance,out long position,int method);
  [StructLayout(LayoutKind.Sequential)] struct VolumeData {public long Serial,Sectors,TotalClusters,FreeClusters,TotalReserved;public int BytesPerSector,BytesPerCluster,BytesPerRecord,ClustersPerRecord;public long MftValidDataLength,MftStartLcn,Mft2StartLcn,MftZoneStart,MftZoneEnd;}

  internal sealed class Table {
   public int Count;public long[] Parent,Size,Alloc,Created,Modified,Accessed;public int[] Attr,First,Next;public string[] Name;public byte[] Ns,Flags;
   public long[] BaseReference;public ushort[] Sequence;
   public readonly object[] NameLocks=Enumerable.Range(0,64).Select(i=>new object()).ToArray();
   /// <summary>Extra hard-link names: node id = Count + k.</summary>
   public List<int> LinkFrn=new List<int>();public List<long> LinkParent=new List<long>();public List<string> LinkName=new List<string>();
   public Dictionary<int,List<int>> LinksByFile=new Dictionary<int,List<int>>();
   public int Frn(int node){return node<Count?node:LinkFrn[node-Count];}
   public string NodeName(int node){return node<Count?Name[node]:LinkName[node-Count];}
   public Table(int n){Count=n;Parent=new long[n];Size=new long[n];Alloc=new long[n];Created=new long[n];Modified=new long[n];Accessed=new long[n];Attr=new int[n];First=new int[n];Next=new int[n];Name=new string[n];Ns=new byte[n];Flags=new byte[n];BaseReference=new long[n];Sequence=new ushort[n];}
  }
  struct Chunk {public long Disk;public int Length;public long Stream;}

  /// <summary>Returns entries for the given roots, or null (with a reason) when MFT mode is not possible.</summary>
  public static List<Entry> TryScan(Scanner s,string volumeRoot,List<string> roots,CancellationToken token,out string why){
   why="";
   try{
    var drive=new DriveInfo(volumeRoot);
    if(!drive.IsReady||drive.DriveFormat!="NTFS"||(drive.DriveType!=DriveType.Fixed&&drive.DriveType!=DriveType.Removable)){why="fs";return null;}
    string device=@"\\.\"+volumeRoot.TrimEnd('\\');
    using(var volume=CreateFileW(device,0x80000000,3,IntPtr.Zero,3,0x20000000,IntPtr.Zero)){
     if(volume.IsInvalid){int err=Marshal.GetLastWin32Error();why=err==5?"admin":"open";return null;}
     VolumeData vd;int got;
     if(!DeviceIoControl(volume,0x00090064,IntPtr.Zero,0,out vd,Marshal.SizeOf(typeof(VolumeData)),out got,IntPtr.Zero)){why="ioctl";return null;}
     if(vd.BytesPerRecord<1024||vd.BytesPerRecord>65536||vd.BytesPerRecord%512!=0||vd.BytesPerCluster<512||vd.BytesPerCluster>2*1024*1024||vd.BytesPerCluster%512!=0||vd.MftValidDataLength<6L*vd.BytesPerRecord||vd.MftValidDataLength%vd.BytesPerRecord!=0||vd.MftValidDataLength/vd.BytesPerRecord>int.MaxValue-1){why="geometry";s.Log(volumeRoot,"Unsupported MFT geometry; directory enumeration was used instead.");return null;}
     var table=ReadTable(s,volume,vd,volumeRoot,token);s.Phase("mft-read");
     if(table==null){why="mft";s.Log(volumeRoot,"MFT records or extents could not be validated; directory enumeration was used instead.");return null;}
     if(!Plausible(table,volumeRoot,roots)){why="verify";s.Log(volumeRoot,"MFT data did not match the directory listing; directory enumeration was used instead.");return null;}
     var result=Build(s,table,volumeRoot,roots,token);s.Phase("mft-build");
     if(result==null){why="paths";return null;}
     return result;
    }
   }catch(OperationCanceledException){throw;}
   catch(Exception ex){why="error";try{s.Log(volumeRoot,"MFT read failed, directory enumeration was used instead: "+ex.Message);}catch{}return null;}
  }

  static IntPtr Aligned(int size,out IntPtr raw){raw=Marshal.AllocHGlobal(size+4096);return new IntPtr((raw.ToInt64()+4095)&~4095L);}
  static void ReadAt(SafeFileHandle h,long offset,byte* buffer,int count){
   long pos;if(!SetFilePointerEx(h,offset,out pos,0))throw new Win32Exception(Marshal.GetLastWin32Error());
   int total=0;while(total<count){int n;if(!ReadFile(h,buffer+total,count-total,out n,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());if(n==0)throw new EndOfStreamException();total+=n;}
  }
  /// <summary>Applies the NTFS update-sequence fix-ups. Returns false for a torn / invalid record.</summary>
  internal static bool Fixup(byte* p,int recordSize){
   if(recordSize<512||recordSize%512!=0||*(uint*)p!=0x454C4946)return false;
   int usa=*(ushort*)(p+4),count=*(ushort*)(p+6);
   if(usa<0x28||(usa&1)!=0||count!=recordSize/512+1||usa+count*2>510)return false;
   ushort usn=*(ushort*)(p+usa);
   for(int k=1;k<count;k++)if(*(ushort*)(p+k*512-2)!=usn)return false;
   for(int k=1;k<count;k++)*(ushort*)(p+k*512-2)=*(ushort*)(p+usa+k*2);
   return true;
  }

  internal static List<KeyValuePair<long,long>> ReadRuns(byte* a,int length,long totalClusters){
   if(length<0x40)throw new InvalidDataException("Truncated MFT data attribute.");
   int offset=*(ushort*)(a+0x20);if(offset<0x40||offset>=length)throw new InvalidDataException("Invalid MFT mapping-pairs offset.");
   var runs=new List<KeyValuePair<long,long>>();byte* r=a+offset;long lcn=0;
   while(r<a+length&&*r!=0){
    int ls=*r&15,os=*r>>4;r++;
    if(ls==0||ls>8||os==0||os>8||a+length-r<ls+os)throw new InvalidDataException("Truncated or sparse MFT extent.");
    long clusters=0;for(int k=0;k<ls;k++)clusters|=(long)r[k]<<(8*k);r+=ls;
    long delta=0;for(int k=0;k<os;k++)delta|=(long)r[k]<<(8*k);if(os<8&&(r[os-1]&128)!=0)delta|=-1L<<(8*os);r+=os;
    lcn=checked(lcn+delta);
    if(clusters<=0||lcn<0||lcn>=totalClusters||clusters>totalClusters-lcn)throw new InvalidDataException("MFT extent is outside the volume.");
    runs.Add(new KeyValuePair<long,long>(lcn,clusters));
   }
   if(r>=a+length)throw new InvalidDataException("MFT mapping pairs have no terminator.");
   return runs;
  }

  static Table ReadTable(Scanner s,SafeFileHandle volume,VolumeData vd,string volumeRoot,CancellationToken token){
   int rec=vd.BytesPerRecord;long cluster=vd.BytesPerCluster;
   // 1. Record 0 ($MFT) describes where the MFT itself lives.
   int firstRead=(int)Math.Max(cluster,rec);firstRead=(firstRead+4095)&~4095;
   IntPtr raw0;byte* b0=(byte*)Aligned(firstRead,out raw0);
   var runs=new List<KeyValuePair<long,long>>();
   try{
    ReadAt(volume,vd.MftStartLcn*cluster,b0,firstRead);
    if(!Fixup(b0,rec))return null;
    int ao=*(ushort*)(b0+0x14);
    if(ao<0x28||ao>rec-4)return null;
    while(ao<=rec-4){
     byte* a=b0+ao;uint type=*(uint*)a;if(type==0xFFFFFFFF)break;if(ao>rec-16)return null;int len=*(int*)(a+4);if(len<24||len>rec-ao||(len&7)!=0)return null;
     if(type==0x80&&a[9]==0&&a[8]!=0&&*(long*)(a+0x10)==0){
      runs=ReadRuns(a,len,vd.TotalClusters);
      break;
     }
     ao+=len;
    }
   }finally{Marshal.FreeHGlobal(raw0);}
   long mftBytes=vd.MftValidDataLength;
   if(runs.Count==0||checked(runs.Sum(r=>r.Value)*cluster)<mftBytes)return null; // fragmented beyond record 0 (attribute list): fall back
   if(cluster<rec&&runs.Any(r=>r.Value*cluster%rec!=0))return null;
   int total=(int)(mftBytes/rec);
   var t=new Table(total);
   // 2. Plan large sequential reads.
   const int chunkSize=8*1024*1024;var chunks=new List<Chunk>();long stream=0;
   foreach(var run in runs){
    long ext=run.Value*cluster,done=0;
    while(done<ext&&stream<mftBytes){
     long want=Math.Min(chunkSize,ext-done);long left=mftBytes-stream;if(want>left)want=(left+cluster-1)/cluster*cluster;
     chunks.Add(new Chunk{Disk=run.Key*cluster+done,Length=(int)want,Stream=stream});done+=want;stream+=want;
    }
   }
   // 3. Read and parse with double buffering (disk read of chunk n+1 overlaps parsing of chunk n).
   IntPtr rawA,rawB;byte* bufA=(byte*)Aligned(chunkSize,out rawA);byte* bufB=(byte*)Aligned(chunkSize,out rawB);
   Task nextRead=null;
   try{
    string label=volumeRoot.TrimEnd('\\');
    ReadAt(volume,chunks[0].Disk,bufA,chunks[0].Length);
    for(int c=0;c<chunks.Count;c++){
     s.Wait();
     if(c+1<chunks.Count){var ch=chunks[c+1];long target=(long)bufB;nextRead=Task.Run(()=>ReadAt(volume,ch.Disk,(byte*)target,ch.Length));}
     var cur=chunks[c];
      int first=(int)(cur.Stream/rec),count=(int)Math.Min(cur.Length/rec,total-first);long basePtr=(long)bufA;
      if(count>0){int parts=Math.Min(Environment.ProcessorCount,Math.Max(1,count/2048));int per=(count+parts-1)/parts;
       Parallel.For(0,parts,part=>{int a0=part*per,a1=Math.Min(count,a0+per);for(int r=a0;r<a1;r++)Parse(t,(byte*)basePtr+(long)r*rec,first+r,rec);});}
     s.Current=(L.Chinese?"正在读取 MFT（":"Reading MFT (")+label+(L.Chinese?"）  ":")  ")+((cur.Stream+cur.Length)*100/Math.Max(1,mftBytes))+"%";
     if(nextRead!=null){nextRead.GetAwaiter().GetResult();nextRead=null;}
     byte* swap=bufA;bufA=bufB;bufB=swap;
    }
   }catch(AggregateException ex){throw ex.GetBaseException();}
   finally{
    // A failed parse must not release a buffer while the prefetched read still owns it.
    if(nextRead!=null)try{nextRead.GetAwaiter().GetResult();}catch{}
    Marshal.FreeHGlobal(rawA);Marshal.FreeHGlobal(rawB);
   }
   ValidateReferences(t);
   // 4. Link children to parents.
   t.Next=new int[total+t.LinkFrn.Count];for(int i=0;i<t.Next.Length;i++)t.Next[i]=-1;for(int i=0;i<total;i++)t.First[i]=-1;
   for(int i=0;i<total;i++){
    if((t.Flags[i]&1)==0||t.Name[i]==null||i==5)continue;
    long parent=t.Parent[i]&0xFFFFFFFFFFFFL;if(parent==i)continue;int pi=(int)parent;
    if((t.Flags[pi]&2)==0)continue;
    t.Next[i]=t.First[pi];t.First[pi]=i;
   }
   for(int k=0;k<t.LinkFrn.Count;k++){
    int frn=t.LinkFrn[k];if((t.Flags[frn]&1)==0||(t.Flags[frn]&2)!=0)continue;
    long parent=t.LinkParent[k]&0xFFFFFFFFFFFFL;int pi=(int)parent;if((t.Flags[pi]&2)==0)continue;
    int node=total+k;t.Next[node]=t.First[pi];t.First[pi]=node;
   }
   return t;
  }

  /// <summary>Publishes a name, namespace and parent together, including parallel extension records.</summary>
  internal static void AddName(Table t,int target,char* chars,int length,byte ns,long parent){
   lock(t.NameLocks[target%t.NameLocks.Length]){
    if(t.Name[target]!=null&&ns==2)return;
    string n=new string(chars,0,length);
    if(t.Name[target]==null||t.Ns[target]==2){t.Name[target]=n;t.Ns[target]=ns;t.Parent[target]=parent;return;}
    if(parent==t.Parent[target]&&string.Equals(n,t.Name[target],StringComparison.OrdinalIgnoreCase))return;
    lock(t){
     List<int> links;if(!t.LinksByFile.TryGetValue(target,out links)){links=new List<int>();t.LinksByFile.Add(target,links);}
     foreach(int k in links)if(t.LinkParent[k]==parent&&string.Equals(t.LinkName[k],n,StringComparison.OrdinalIgnoreCase))return;
     links.Add(t.LinkFrn.Count);
     t.LinkFrn.Add(target);t.LinkParent.Add(parent);t.LinkName.Add(n);
    }
   }
  }
  internal static void Parse(Table t,byte* p,int index,int rec){
   if(rec<0x30)throw new InvalidDataException("Truncated MFT record.");
   if(*(uint*)p==0)return; // Unused, never initialized record.
   if(*(uint*)p!=0x454C4946)throw new InvalidDataException("Invalid MFT record signature.");
   if(!Fixup(p,rec))throw new InvalidDataException("MFT update-sequence check failed.");
   ushort flags=*(ushort*)(p+0x16);if((flags&1)==0)return;
   t.Sequence[index]=*(ushort*)(p+0x10);long reference=*(long*)(p+0x20);t.BaseReference[index]=reference;
   long baseRef=reference&0xFFFFFFFFFFFFL;int target=index;
   if(reference!=0){if(baseRef>=t.Count||baseRef==index)throw new InvalidDataException("Invalid MFT base reference.");target=(int)baseRef;}
   else t.Flags[index]=(byte)(1|((flags&2)!=0?2:0));
   int used=*(int*)(p+0x18),ao=*(ushort*)(p+0x14);
   if(used<0x30||used>rec||ao<0x28||ao>used-4||(ao&7)!=0)throw new InvalidDataException("Invalid MFT attribute boundary.");
   while(ao<=used-4){
    byte* a=p+ao;uint type=*(uint*)a;if(type==0xFFFFFFFF)return;
    if(ao>used-24)throw new InvalidDataException("Truncated MFT attribute header.");
    int len=*(int*)(a+4);if(len<24||len>used-ao||(len&7)!=0)throw new InvalidDataException("Invalid MFT attribute length.");
    byte nonResident=a[8],nameLength=a[9];
    int valueOffset=0;uint valueLength=0;
    if(nonResident==0){valueOffset=*(ushort*)(a+0x14);valueLength=*(uint*)(a+0x10);if(valueOffset<24||valueOffset>len||valueLength>len-valueOffset)throw new InvalidDataException("Resident value exceeds its MFT attribute.");}
    else if(nonResident!=1||len<0x40)throw new InvalidDataException("Invalid nonresident MFT attribute.");
    if(nameLength>0){int no=*(ushort*)(a+10);if(no<(nonResident==0?24:0x40)||no>len-nameLength*2)throw new InvalidDataException("Invalid MFT attribute name.");}
    if(type==0x10&&nonResident==0){
     byte* v=a+valueOffset;if(valueLength<36)throw new InvalidDataException("Truncated MFT standard information.");t.Created[target]=*(long*)v;t.Modified[target]=*(long*)(v+8);t.Accessed[target]=*(long*)(v+24);t.Attr[target]=*(int*)(v+32);
    }else if(type==0x30&&nonResident==0){
     if(valueLength<66)throw new InvalidDataException("Truncated MFT filename.");byte* v=a+valueOffset;int nl=v[64];byte ns=v[65];
     if(nl==0||ns>3||66+nl*2>valueLength)throw new InvalidDataException("Invalid MFT filename length.");
     AddName(t,target,(char*)(v+66),nl,ns,*(long*)v);
    }else if(type==0x80&&nameLength==0){
     if(nonResident==0){t.Size[target]=valueLength;t.Alloc[target]=0;}
     else if(*(long*)(a+0x10)==0){
      ushort af=*(ushort*)(a+0x0C);long alloc=*(long*)(a+0x28);
      if((af&0x80FF)!=0){if(len<0x48||*(ushort*)(a+0x20)<0x48)throw new InvalidDataException("Missing compressed/sparse MFT allocation.");alloc=*(long*)(a+0x40);}
      if(alloc<0||*(long*)(a+0x30)<0)throw new InvalidDataException("Negative MFT size.");
      t.Size[target]=*(long*)(a+0x30);t.Alloc[target]=alloc;
     }
    }
    ao+=len;
   }
   throw new InvalidDataException("MFT attribute list has no terminator.");
  }

  internal static void ValidateReferences(Table t){
   for(int i=0;i<t.Count;i++){
    if(t.BaseReference[i]!=0&&!ValidReference(t,t.BaseReference[i],false))throw new InvalidDataException("Stale MFT extension reference.");
    if((t.Flags[i]&1)!=0&&t.Name[i]!=null&&i!=5&&!ValidReference(t,t.Parent[i],true))throw new InvalidDataException("Stale MFT parent reference.");
   }
   foreach(long parent in t.LinkParent)if(!ValidReference(t,parent,true))throw new InvalidDataException("Stale MFT hard-link parent reference.");
  }
  static bool ValidReference(Table t,long reference,bool directory){
   long id=reference&0xFFFFFFFFFFFFL;
   return id<t.Count&&(t.Flags[id]&1)!=0&&(!directory||(t.Flags[id]&2)!=0)&&t.Sequence[id]==(ushort)((ulong)reference>>48);
  }

  /// <summary>Resolves a path to its MFT record by walking names from the root directory (record 5).</summary>
  static int Resolve(Table t,string volumeRoot,string path){
   int cur=5;string rest=path.Substring(Math.Min(path.Length,volumeRoot.Length)).Trim('\\');
   if(rest.Length==0)return cur;
   foreach(string part in rest.Split('\\')){
    int found=-1;for(int c=t.First[cur];c>=0;c=t.Next[c])if(c<t.Count&&string.Equals(t.Name[c],part,StringComparison.OrdinalIgnoreCase)){found=c;break;}
    if(found<0||(t.Flags[found]&2)==0)return -1;cur=found;
   }
   return cur;
  }

  static List<Entry> Build(Scanner s,Table t,string volumeRoot,List<string> roots,CancellationToken token){
   var output=new List<Entry>();var work=new BlockingCollection<KeyValuePair<int,Entry>>(new ConcurrentStack<KeyValuePair<int,Entry>>());int pending=0;
   foreach(string root in roots){
    int frn=Resolve(t,volumeRoot,root);if(frn<0)return null;
    var re=s.RootEntry(root);output.Add(re);Interlocked.Increment(ref pending);work.Add(new KeyValuePair<int,Entry>(frn,re));
   }
   int workers=Math.Max(1,Math.Min(16,Environment.ProcessorCount));var lists=new List<Entry>[workers];var threads=new Thread[workers];Exception failure=null;
   for(int w=0;w<workers;w++){
    int slot=w;lists[w]=new List<Entry>(t.Count/workers+1024);
    threads[w]=new Thread(()=>{
     var mine=lists[slot];int tick=0;
     try{
      foreach(var item in work.GetConsumingEnumerable(token)){
       try{
        Entry dir=item.Value;long files=0,bytes=0;
        s.Wait();if((++tick&255)==0)s.Current=dir.Path;
        for(int c=t.First[item.Key];c>=0;c=t.Next[c]){
         if((++tick&255)==0)s.Wait();
         int f=t.Frn(c);if(item.Key==5&&f<24)continue; // NTFS metadata files ($MFT, $LogFile, $Extend ...)
         string name=t.NodeName(c);bool folder=(t.Flags[f]&2)!=0;int attr=t.Attr[f]|(folder?16:0);
         var e=new Entry{Path=Scanner.Join(dir.Path,name),Parent=dir.Path,Up=dir,Folder=folder,Depth=dir.Depth+1,Attributes=attr,Created=Scanner.Time(t.Created[f]),Modified=Scanner.Time(t.Modified[f]),Accessed=Scanner.Time(t.Accessed[f]),Owner=""};
         if(!folder){e.Size=t.Size[f];e.Allocated=t.Alloc[f];}
         bool recurse;
         if(s.Admit(dir,e,name,out recurse)){
          mine.Add(e);(dir.Kids??(dir.Kids=new List<Entry>())).Add(e);
          if(folder){if(recurse){Interlocked.Increment(ref pending);work.Add(new KeyValuePair<int,Entry>(f,e));}}
          else{files++;bytes+=e.Size;}
         }
        }
        Interlocked.Increment(ref s.FolderCount);if(files>0){Interlocked.Add(ref s.FileCount,files);Interlocked.Add(ref s.ByteCount,bytes);}
       }finally{if(Interlocked.Decrement(ref pending)==0)work.CompleteAdding();}
      }
     }catch(OperationCanceledException){}
     catch(Exception ex){failure=ex;try{work.CompleteAdding();}catch{}}
    }){IsBackground=true,Name="FolderScope MFT build "+w};
    threads[w].Start();
   }
   foreach(var th in threads)th.Join();
   work.Dispose();
   if(failure!=null)throw failure;
   token.ThrowIfCancellationRequested();
   int total=output.Count;foreach(var l in lists)total+=l.Count;output.Capacity=total;foreach(var l in lists)output.AddRange(l);
   return output;
  }

  /// <summary>Cheap safety net: each root's direct children in the MFT must match a real directory listing.</summary>
  static bool Plausible(Table t,string volumeRoot,List<string> roots){
   foreach(string root in roots){
    int frn=Resolve(t,volumeRoot,root);if(frn<0)return false;
    string[] actual;try{actual=Directory.GetFileSystemEntries(root);}catch{continue;}
    if(actual.Length==0)continue;
    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(int c=t.First[frn];c>=0;c=t.Next[c]){string n=t.NodeName(c);if(n!=null)names.Add(n);}
    int hits=actual.Count(a=>names.Contains(Path.GetFileName(a)));
    if(hits!=actual.Length)return false;
   }
   return true;
  }
 }
}
