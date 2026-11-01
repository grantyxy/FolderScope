using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FolderScope {
 internal static unsafe class ScannerTests {
  public static void Run(Action<string,Action> test,Action<bool,string> check,string folder){
   string root=Path.Combine(folder,"scanner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   Directory.CreateDirectory(Path.Combine(root,"child"));File.WriteAllText(Path.Combine(root,"one.txt"),"one");File.WriteAllText(Path.Combine(root,"child","two.txt"),"two");
   test("wildcard excludes all files or all subdirectories",()=>{
    foreach(string wildcard in new[]{"*","*.*"," ; * ; "}){
     var files=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Exclude=wildcard},CancellationToken.None);
     check(files.Entries.All(e=>e.Folder)&&files.Index[root].Filtered,"Wildcard file exclusion was ignored");
     var dirs=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,ExcludeFolders=wildcard},CancellationToken.None);
     check(dirs.Entries.Count==2&&dirs.Index[root].Filtered,"Wildcard folder exclusion was ignored");
    }
    var blank=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false,Exclude=" ; ; ",ExcludeFolders=";"},CancellationToken.None);check(blank.Entries.Count==4,"Empty wildcard list excluded entries");
   });
   test("cloud placeholder filtering preserves normal files and skips roots",()=>{
    string offline=Path.Combine(root,"offline.bin");File.WriteAllText(offline,"offline metadata fixture");File.SetAttributes(offline,File.GetAttributes(offline)|FileAttributes.Offline);
    var included=new Scanner().Run(new[]{root},new ScanOptions{UseMft=false},CancellationToken.None);
    var excluded=new Scanner().Run(new[]{root},new ScanOptions{SkipCloudPlaceholders=true},CancellationToken.None);
    check(included.Index.ContainsKey(offline)&&!excluded.Index.ContainsKey(offline),"Offline file filtering mismatch");
    check(excluded.Index[root].Filtered&&excluded.Events.Any(e=>e.Path==offline)&&excluded.Method=="Directory","Filtered placeholder not reported");
    string offlineRoot=Path.Combine(root,"offline-root");Directory.CreateDirectory(offlineRoot);File.WriteAllText(Path.Combine(offlineRoot,"inside.txt"),"untouched");File.SetAttributes(offlineRoot,File.GetAttributes(offlineRoot)|FileAttributes.Offline);
    var skipped=new Scanner().Run(new[]{offlineRoot},new ScanOptions{SkipCloudPlaceholders=true},CancellationToken.None);
    check(skipped.Entries.Count==1&&skipped.Index[offlineRoot].Skipped&&skipped.Index[offlineRoot].Incomplete,"Offline root was traversed");
    File.SetAttributes(offline,FileAttributes.Normal);File.SetAttributes(offlineRoot,FileAttributes.Directory);
   });
   test("MFT update sequence is complete and failure is atomic",()=>{
    byte[] good=Record(Resident(0x80,new byte[]{1,2,3}));fixed(byte* p=good)check(MftScan.Fixup(p,good.Length),"Valid update sequence rejected");
    byte[] torn=Record(Resident(0x80,new byte[]{1}));Put16(torn,1022,0x9999);fixed(byte* p=torn)check(!MftScan.Fixup(p,torn.Length),"Torn record accepted");
    check(BitConverter.ToUInt16(torn,510)==0xABCD,"Failed fixup partially modified the record");
    byte[] shortUsa=Record(Resident(0x80,new byte[]{1}));Put16(shortUsa,6,2);fixed(byte* p=shortUsa)check(!MftScan.Fixup(p,shortUsa.Length),"Incomplete update sequence accepted");
   });
   test("MFT resident, regular, sparse and compressed allocation",()=>{
    foreach(ushort flags in new ushort[]{0,1,0x8000}){
     byte[] attr=new byte[80];Put32(attr,0,0x80);Put32(attr,4,(uint)attr.Length);attr[8]=1;Put16(attr,12,flags);Put16(attr,32,72);Put64(attr,40,16384);Put64(attr,48,12000);Put64(attr,56,12000);Put64(attr,64,8192);
     var table=ParseRecord(Record(attr));check(table.Size[6]==12000&&table.Alloc[6]==(flags==0?16384:8192),"Nonresident allocation mismatch");
    }
    var resident=ParseRecord(Record(Resident(0x80,new byte[19])));check(resident.Size[6]==19&&resident.Alloc[6]==0,"Resident allocation mismatch");
   });
   test("MFT malformed attribute boundaries are rejected",()=>{
    byte[] invalidOffset=Record(Resident(0x80,new byte[12]));Put16(invalidOffset,56+20,1000);check(Rejected(()=>ParseRecord(invalidOffset)),"Out-of-range resident value accepted");
    byte[] shortName=Record(Resident(0x30,new byte[4]));check(Rejected(()=>ParseRecord(shortName)),"Truncated filename accepted");
    byte[] hugeLength=Record(Resident(0x80,new byte[12]));Put32(hugeLength,60,uint.MaxValue);check(Rejected(()=>ParseRecord(hugeLength)),"Overflowing attribute length accepted");
   });
   test("MFT extent parser checks lengths, termination and volume bounds",()=>{
    byte[] valid=new byte[72];Put16(valid,32,64);new byte[]{0x11,2,100,0x11,1,206,0}.CopyTo(valid,64);
    fixed(byte* p=valid){var runs=MftScan.ReadRuns(p,valid.Length,1000);check(runs.Count==2&&runs[0].Key==100&&runs[1].Key==50,"Signed MFT extent delta decoded incorrectly");}
    byte[] truncated=new byte[68];Put16(truncated,32,64);truncated[64]=0x88;check(Rejected(()=>ReadRuns(truncated,1000)),"Truncated mapping pairs accepted");
    byte[] outside=(byte[])valid.Clone();outside[65]=255;check(Rejected(()=>ReadRuns(outside,200)),"Out-of-volume extent accepted");
    byte[] noEnd=new byte[67];Put16(noEnd,32,64);noEnd[64]=0x11;noEnd[65]=1;noEnd[66]=100;check(Rejected(()=>ReadRuns(noEnd,1000)),"Unterminated mapping pairs accepted");
   });
   test("MFT hard-link names retain matching parent references under parallel parsing",()=>{
    var table=new MftScan.Table(100);
    Parallel.For(0,256,i=>{string name="link-"+i;long parent=Reference(10+i%10,1);fixed(char* p=name)MftScan.AddName(table,6,p,name.Length,1,parent);});
    check(table.LinkFrn.Count==255,"Parallel hard-link name lost");
    var names=new HashSet<string>(table.LinkName);names.Add(table.Name[6]);check(names.Count==256,"Duplicate or missing hard-link name");
    int primary=int.Parse(table.Name[6].Substring(5));check(table.Parent[6]==Reference(10+primary%10,1),"Primary name and parent published separately");
    for(int k=0;k<table.LinkName.Count;k++){int n=int.Parse(table.LinkName[k].Substring(5));check(table.LinkParent[k]==Reference(10+n%10,1),"Hard-link parent changed");}
   });
   test("MFT rejects obsolete base and parent sequence numbers",()=>{
    var table=new MftScan.Table(10);table.Flags[5]=3;table.Sequence[5]=1;table.Flags[6]=1;table.Sequence[6]=2;table.Name[6]="file";table.Parent[6]=Reference(5,1);table.BaseReference[7]=Reference(6,2);MftScan.ValidateReferences(table);
    table.BaseReference[7]=Reference(6,1);check(Rejected(()=>MftScan.ValidateReferences(table)),"Obsolete extension accepted");table.BaseReference[7]=0;table.Parent[6]=Reference(5,2);check(Rejected(()=>MftScan.ValidateReferences(table)),"Obsolete parent accepted");
   });
  }
  static bool Rejected(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
  static MftScan.Table ParseRecord(byte[] record){var t=new MftScan.Table(10);fixed(byte* p=record)MftScan.Parse(t,p,6,record.Length);return t;}
  static void ReadRuns(byte[] attribute,long clusters){fixed(byte* p=attribute)MftScan.ReadRuns(p,attribute.Length,clusters);}
  static long Reference(int id,ushort sequence){return (long)(uint)id|((long)sequence<<48);}
  static byte[] Record(params byte[][] attributes){
   var record=new byte[1024];Put32(record,0,0x454C4946);Put16(record,4,48);Put16(record,6,3);Put16(record,16,1);Put16(record,20,56);Put16(record,22,1);Put32(record,28,1024);
   int offset=56;foreach(byte[] attribute in attributes){attribute.CopyTo(record,offset);offset+=attribute.Length;}Put32(record,offset,uint.MaxValue);Put32(record,24,(uint)(offset+8));Put16(record,48,0xABCD);Put16(record,50,BitConverter.ToUInt16(record,510));Put16(record,52,BitConverter.ToUInt16(record,1022));Put16(record,510,0xABCD);Put16(record,1022,0xABCD);return record;
  }
  static byte[] Resident(uint type,byte[] value){var a=new byte[(24+value.Length+7)&~7];Put32(a,0,type);Put32(a,4,(uint)a.Length);Put32(a,16,(uint)value.Length);Put16(a,20,24);value.CopyTo(a,24);return a;}
  static void Put16(byte[] b,int offset,ushort value){BitConverter.GetBytes(value).CopyTo(b,offset);}
  static void Put32(byte[] b,int offset,uint value){BitConverter.GetBytes(value).CopyTo(b,offset);}
  static void Put64(byte[] b,int offset,long value){BitConverter.GetBytes(value).CopyTo(b,offset);}
 }
}
