using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace FolderScope {
 internal static class ReportTests {
  public static void Run(string folder,Action<string,Action> test,Action<bool,string> check){
   string root=Path.Combine(folder,"report-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   Func<string,byte[],Entry> write=(name,bytes)=>{string path=Path.Combine(root,name);File.WriteAllBytes(path,bytes);var f=new FileInfo(path);return new Entry{Path=path,Size=f.Length,Modified=f.LastWriteTimeUtc,Attributes=(int)f.Attributes};};
   test("sampled duplicates require a full-file content match",()=>{
    byte[] a=new byte[256*1024];a[128*1024]=1;var first=write("large-a.bin",a);var copy=write("large-copy.bin",a);
    a[128*1024]=2;var middle=write("large-middle.bin",a);a[0]=3;var head=write("large-head.bin",a);
    var rows=Reports.Duplicates(new List<Entry>{first,copy,middle,head},"Content (SHA-256)",CancellationToken.None);
    check(rows.Count==2&&rows.All(r=>r.Path==first.Path||r.Path==copy.Path),"Matching samples were mistaken for matching full content");
   });
   test("content duplicate boundaries include empty and small files",()=>{
    foreach(int size in new[]{0,65536,131072,131073}){
     byte[] bytes=new byte[size];if(size>0)bytes[size-1]=7;
     var a=write("boundary-a-"+size+".bin",bytes);var b=write("boundary-b-"+size+".bin",bytes);
     check(Reports.Duplicates(new List<Entry>{a,b},"Content (SHA-256)",CancellationToken.None).Count==2,"Duplicate lost at size "+size);
    }
   });
   test("changed duplicate candidates require a rescan",()=>{
    var a=write("changed-a.bin",new byte[200]);var b=write("changed-b.bin",new byte[200]);
    File.SetLastWriteTimeUtc(b.Path,b.Modified.AddSeconds(-10));bool changed=false;
    try{Reports.Duplicates(new List<Entry>{a,b},"Content (SHA-256)",CancellationToken.None);}catch(IOException){changed=true;}
    check(changed,"Changed file was silently omitted");
   });
   test("unreadable duplicate candidates fail explicitly",()=>{
    var a=write("locked-a.bin",new byte[201]);var b=write("locked-b.bin",new byte[201]);bool failed=false;
    using(var locked=new FileStream(b.Path,FileMode.Open,FileAccess.Read,FileShare.None))try{Reports.Duplicates(new List<Entry>{a,b},"Content (SHA-256)",CancellationToken.None);}catch(IOException){failed=true;}
    check(failed,"Unreadable duplicate content was treated as verified");
   });
   test("content comparison skips unavailable entries with deduplicated events",()=>{
    var a=write("local-a.bin",new byte[202]);var b=write("local-b.bin",new byte[202]);
    var snapshot=new Snapshot{Entries=new List<Entry>{a,b,
     new Entry{Path=Path.Combine(root,"offline-missing.bin"),Size=202,Attributes=0x1000},
     new Entry{Path=Path.Combine(root,"recall-missing.bin"),Size=202,Attributes=0x400000},
     new Entry{Path=Path.Combine(root,"reparse-missing.bin"),Size=202,Attributes=0x400},
     new Entry{Path=Path.Combine(root,"skipped-missing.bin"),Size=202,Skipped=true},
     new Entry{Path=Path.Combine(root,"partial-missing.bin"),Size=202,Incomplete=true}}};
    snapshot.Events.Add(new ScanEvent{Path=root,Message="scan event retained"});snapshot.Events.Add(new ScanEvent{Path=Path.Combine(root,"old-content-warning.bin"),Message=Reports.ContentSkippedMessage});
    for(int i=0;i<2;i++)check(Reports.Build(snapshot,"Duplicate Files",0,"Content (SHA-256)",CancellationToken.None).Count==2,"Unavailable entry was read or local duplicate lost");
    check(snapshot.Events.Count(e=>e.Message==Reports.ContentSkippedMessage)==5,"Skipped content missing from events or logged twice");
    check(snapshot.Events.Any(e=>e.Message=="scan event retained"),"Unrelated scan event was removed");
   });
   test("raw MFT extended-attribute flag is not mistaken for cloud recall",()=>{
    var a=write("ea-a.bin",new byte[203]);var b=write("ea-b.bin",new byte[203]);a.Attributes|=0x40000;b.Attributes|=0x40000;
    check(Reports.Duplicates(new List<Entry>{a,b},"Content (SHA-256)",CancellationToken.None).Count==2,"EA flag incorrectly excluded a local file");
    check(Native.IsCloudPlaceholder(0x40000,true)&&!Native.IsCloudPlaceholder(0x40000,false),"RecallOnOpen was not limited to directory enumeration");
   });
   test("all duplicate modes and reports honor pre-cancellation",()=>{
    var c=new CancellationTokenSource();c.Cancel();
    foreach(string mode in new[]{"Name","Name + size","Name + size + modified","Content (SHA-256)"}){
     bool cancelled=false;try{Reports.Duplicates(new List<Entry>(),mode,c.Token);}catch(OperationCanceledException){cancelled=true;}check(cancelled,"Cancellation ignored for "+mode);
    }
    bool reportCancelled=false;try{Reports.Build(new Snapshot(),"Largest Files",0,"Name",c.Token);}catch(OperationCanceledException){reportCancelled=true;}check(reportCancelled,"Report ignored cancellation");
   });
   test("content duplicate search does not read unique-size files",()=>{
    var a=write("unique-locked.bin",new byte[204]);var b=write("pair-a.bin",new byte[205]);var c=write("pair-b.bin",new byte[205]);
    using(var locked=new FileStream(a.Path,FileMode.Open,FileAccess.Read,FileShare.None))check(Reports.Duplicates(new List<Entry>{a,b,c},"Content (SHA-256)",CancellationToken.None).Count==2,"Unique-size file was opened unnecessarily");
   });
   test("case-colliding paths mark every scan root incomplete",()=>{
    string other=Path.Combine(root,"other");var snapshot=new Snapshot{Roots=new List<string>{root,other},Entries=new List<Entry>{
     new Entry{Path=root,Folder=true},new Entry{Path=other,Folder=true},
     new Entry{Path=Path.Combine(root,"A.bin"),Parent=root,Size=1,Depth=1},
     new Entry{Path=Path.Combine(root,"a.bin"),Parent=root,Size=2,Depth=1}}};
    snapshot.Aggregate();check(snapshot.Index[root].Incomplete&&snapshot.Index[other].Incomplete,"Collision silently reported complete");
    check(snapshot.Entries.Count==3&&snapshot.Events.Count==1&&snapshot.Events[0].Path==Path.Combine(root,"a.bin"),"Omitted path not reported exactly once");
    snapshot.Reindex(true);check(snapshot.Events.Count==1,"Collision event repeated during reindex");
   });
  }
 }
}
