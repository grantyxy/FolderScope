using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FolderScope {
 public sealed class CapacityEstimate {
  public DiskPoint Latest;
  public int Samples;
  public double SpanDays,BytesPerDay,Fit;
  public DateTime? FullAt;
  public string Status="Insufficient history";
 }
 public static class CapacityForecast {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  private static extern bool GetVolumeNameForVolumeMountPointW(string root,StringBuilder name,int length);
  public static string VolumeId(string root){var name=new StringBuilder(50);return GetVolumeNameForVolumeMountPointW(root,name,name.Capacity)?name.ToString():"";}

  public static CapacityEstimate Estimate(IEnumerable<DiskPoint> history){
   var points=history.OrderBy(p=>p.Time).GroupBy(p=>p.Time).Select(g=>g.Last()).ToList();
   var result=new CapacityEstimate();if(points.Count==0)return result;
   DiskPoint last=points[points.Count-1];result.Latest=last;
   if(string.IsNullOrEmpty(last.VolumeId)||last.Total<=0||last.Free<0||last.Free>last.Total)return result;
   // Never fit across a replaced/resized volume or an unidentified legacy sample.
   var segment=points.AsEnumerable().Reverse().TakeWhile(p=>p.VolumeId==last.VolumeId&&p.Total==last.Total&&p.Free>=0&&p.Free<=p.Total&&(last.Time-p.Time).TotalDays<=30).Reverse().ToList();
   result.Samples=segment.Count;result.SpanDays=(last.Time-segment[0].Time).TotalDays;
   if(last.Free==0){result.Status="No available space";result.FullAt=last.Time;return result;}
   if(segment.Count<3||result.SpanDays<1)return result;
   DateTime origin=segment[0].Time;long initialFree=segment[0].Free;
   double meanX=segment.Average(p=>(p.Time-origin).TotalDays),meanY=segment.Average(p=>(double)(initialFree-p.Free));
   double xx=0,xy=0,yy=0;
   foreach(DiskPoint p in segment){double x=(p.Time-origin).TotalDays-meanX,y=(double)(initialFree-p.Free)-meanY;xx+=x*x;xy+=x*y;yy+=y*y;}
   result.BytesPerDay=xy/xx;result.Fit=yy==0?1:Math.Max(0,Math.Min(1,xy*xy/(xx*yy)));
   if(result.BytesPerDay<=0){result.Status="No net growth";return result;}
   double days=last.Free/result.BytesPerDay;
   if(days>=(DateTime.MaxValue-last.Time).TotalDays){result.Status="Beyond date range";return result;}
   result.FullAt=last.Time.AddDays(days);result.Status="Linear projection";return result;
  }

  public static List<ReportRow> Rows(DiskHistory history){
   return history.Points.Where(p=>!string.IsNullOrEmpty(p.Root)).GroupBy(p=>p.Root,StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.Key).Select(g=>{
    CapacityEstimate e=Estimate(g);DiskPoint p=e.Latest;
    string detail=L.T(e.Status)+" | "+L.F("{0} samples / {1:0.0} days",e.Samples,e.SpanDays);
    if(e.Status!="Insufficient history"&&e.Status!="No available space"&&e.Samples>=3&&e.SpanDays>=1)detail+=" | "+L.T("Growth per day")+": "+Util.Size((long)e.BytesPerDay)+" | R²="+e.Fit.ToString("0.000");
    if(e.FullAt.HasValue)detail+=" | "+L.T("Projected full date")+": "+Util.Date(e.FullAt.Value);
    return new ReportRow{Name=g.Key,Size=p.Total-p.Free,Allocated=p.Free,Count=e.Samples,Modified=p.Time,Percent=p.Total<=0?0:(p.Total-p.Free)*100.0/p.Total,Extra=detail};
   }).ToList();
  }
 }

 internal static class ForecastTests {
  internal static void Run(Action<string,Action> test,Action<bool,string> check){
   DateTime start=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
   Func<int,long,DiskPoint> point=(day,free)=>new DiskPoint{Time=start.AddDays(day),Root=@"C:\",VolumeId="volume-a",Total=1000,Free=free};
   test("capacity forecast uses elapsed time and latest free space",()=>{var e=CapacityForecast.Estimate(new[]{point(0,900),point(1,800),point(4,500)});check(e.Samples==3&&Math.Abs(e.BytesPerDay-100)<0.001&&Math.Abs(e.Fit-1)<0.001,"Incorrect regression");check(e.FullAt==start.AddDays(9),"Incorrect projected full date");});
   test("capacity forecast requires distinct samples spanning a day",()=>{var same=point(0,700);var e=CapacityForecast.Estimate(new[]{point(0,900),same,point(1,800)});check(e.Status=="Insufficient history"&&e.Samples==2,"Duplicate timestamps accepted");var shortSample=point(0,600);shortSample.Time=start.AddHours(3);check(CapacityForecast.Estimate(new[]{point(0,900),new DiskPoint{Time=start.AddHours(2),Root=@"C:\",VolumeId="volume-a",Total=1000,Free=800},shortSample}).FullAt==null,"Short burst extrapolated");});
   test("capacity forecast separates replaced resized and legacy volumes",()=>{var old=new[]{point(0,900),point(1,800),point(2,700)};old[2].VolumeId="volume-b";check(CapacityForecast.Estimate(old).Samples==1,"Different volumes mixed");old[2].VolumeId="volume-a";old[2].Total=2000;check(CapacityForecast.Estimate(old).Samples==1,"Resized volume mixed");old[2].VolumeId=null;check(CapacityForecast.Estimate(old).FullAt==null,"Unidentified history trusted");});
   test("capacity forecast handles flat shrinking full and old histories",()=>{check(CapacityForecast.Estimate(new[]{point(0,800),point(1,800),point(2,800)}).Status=="No net growth","Flat history forecast full");check(CapacityForecast.Estimate(new[]{point(0,600),point(1,700),point(2,800)}).FullAt==null,"Shrinking usage forecast full");check(CapacityForecast.Estimate(new[]{point(0,0)}).Status=="No available space","Full disk missed");check(!CapacityForecast.Rows(new DiskHistory{Points=new List<DiskPoint>{point(0,900),point(1,800),point(2,0)}})[0].Extra.Contains("R²="),"Uncomputed fit displayed for full disk");check(CapacityForecast.Estimate(new[]{point(0,900),point(1,800),point(40,700)}).Samples==1,"History window ignored");});
  }
 }
}
