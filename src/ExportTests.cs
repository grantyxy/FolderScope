using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using System.Xml;

namespace FolderScope {
 internal static class ExportTests {
  public static void Run(string folder,Action<string,Action> test,Action<bool,string> check){
   test("XLSX splits worksheets without losing or repeating rows",()=>{
    check(Exporter.ExcelDataRowsPerSheet==1048575,"Excel row limit must reserve one header row");
    foreach(int count in new[]{0,1,3,4,6,7}){
     var rows=Enumerable.Range(0,count).Select(i=>new ReportRow{Name="=记录🗂️ & "+i,Size=i+100,Allocated=i+200,Count=i+1,Percent=i+0.25,Path="路径\\"+i}).ToList();
     string file=Path.Combine(folder,"split-"+count+".xlsx");Exporter.Excel(file,"Report",rows,3);
     using(var zip=ZipFile.OpenRead(file)){
      var workbook=Read(zip,"xl/workbook.xml");var ns=Namespaces(workbook);
      var sheets=workbook.SelectNodes("/s:workbook/s:sheets/s:sheet",ns);
      int expected=count==0?1:(count+2)/3;
      check(sheets.Count==expected,"Wrong sheet count for "+count+" rows");
      var relationships=Read(zip,"xl/_rels/workbook.xml.rels");var contentTypes=Read(zip,"[Content_Types].xml");
      check(relationships.DocumentElement.ChildNodes.Count==expected,"Worksheet relationship count mismatch");
      check(zip.Entries.Count(e=>e.FullName.StartsWith("xl/worksheets/"))==expected,"Extra or missing worksheet parts");
      var names=new HashSet<string>();int consumed=0;
      foreach(XmlElement sheet in sheets){
       check(names.Add(sheet.GetAttribute("name")),"Duplicate worksheet name");
       string id=sheet.GetAttribute("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships");
       var rel=relationships.DocumentElement.ChildNodes.Cast<XmlElement>().Single(e=>e.GetAttribute("Id")==id);
       string part="xl/"+rel.GetAttribute("Target");
       check(contentTypes.DocumentElement.ChildNodes.Cast<XmlElement>().Any(e=>e.GetAttribute("PartName")=="/"+part&&e.GetAttribute("ContentType")=="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),"Missing worksheet content type");
       var document=Read(zip,part);var sheetNs=Namespaces(document);var exported=document.SelectNodes("/s:worksheet/s:sheetData/s:row",sheetNs);
       int take=Math.Min(3,count-consumed);check(exported.Count==take+1,"Wrong row count in "+part);
       var header=exported[0].SelectNodes("s:c/s:is/s:t",sheetNs).Cast<XmlNode>().Select(n=>n.InnerText);
       check(header.SequenceEqual(Exporter.Headers),"Missing repeated header");
       check(document.SelectSingleNode("/s:worksheet/s:sheetViews/s:sheetView/s:pane",sheetNs).Attributes["state"].Value=="frozen","Header is not frozen");
       check(document.SelectSingleNode("/s:worksheet/s:autoFilter",sheetNs).Attributes["ref"].Value=="A1:I"+(take+1),"Filter range mismatch");
       for(int i=1;i<exported.Count;i++){
        var row=(XmlElement)exported[i];var cells=row.SelectNodes("s:c",sheetNs);var original=rows[consumed++];
        check(row.GetAttribute("r")== (i+1).ToString()&&cells[0].Attributes["r"].Value=="A"+(i+1),"Sheet row numbers did not restart");
        check(cells[0].InnerText==original.Name&&cells[7].InnerText==original.Path,"Rows lost, reordered or repeated");
        check(cells[0].Attributes["t"].Value=="inlineStr"&&document.SelectNodes("//s:f",sheetNs).Count==0,"Text was exported as a formula");
        check(cells[1].InnerText==original.Size.ToString()&&cells[3].InnerText==original.Count.ToString()&&cells[4].InnerText==original.Percent.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),"Numeric values changed");
       }
      }
      check(consumed==count,"Not all input rows were exported");
     }
    }
   });
   test("XLSX single worksheet export remains compatible",()=>{
    string file=Path.Combine(folder,"single-sheet.xlsx");Exporter.Save(file,"Report",new List<ReportRow>{new ReportRow{Name="one"}});
    using(var zip=ZipFile.OpenRead(file)){var workbook=Read(zip,"xl/workbook.xml");var ns=Namespaces(workbook);var sheets=workbook.SelectNodes("/s:workbook/s:sheets/s:sheet",ns);check(sheets.Count==1&&sheets[0].Attributes["name"].Value=="Report","Single-sheet workbook changed");}
   });
   test("capacity exports label used free samples and observation time",()=>{
    var rows=new List<ReportRow>{new ReportRow{Name=@"C:\",Size=700,Allocated=300,Count=3,Percent=70,Modified=new DateTime(2026,1,3,0,0,0,DateTimeKind.Utc),Extra="Linear projection | 3 samples / 2.0 days | Growth per day: 100 B | R²=1.000 | Projected full date: 2026-01-06"}};
    bool chinese=L.Chinese;
    try{foreach(bool language in new[]{false,true}){
     L.Chinese=language;string stem=Path.Combine(folder,language?"capacity-zh":"capacity-en");string[] headers=Exporter.HeadersFor(true);
     check(headers[1]==L.T("Used (bytes)")&&headers[2]==L.T("Free (bytes)")&&headers[3]==L.T("Samples")&&headers[5]==L.T("As of (local)"),"Capacity headers have file-size semantics");
     foreach(string ext in new[]{"csv","txt","html","xml","xlsx","pdf"})Exporter.Save(stem+"."+ext,"Capacity forecast",rows,true);
     check(File.ReadAllLines(stem+".csv")[0]==string.Join(",",headers)&&File.ReadAllLines(stem+".txt")[0]==string.Join("\t",headers),"Capacity delimited headers incorrect");
     check(File.ReadAllText(stem+".csv").Contains("\"700\",\"300\",\"3\""),"Capacity values changed");
     string html=File.ReadAllText(stem+".html");foreach(string header in headers)check(html.Contains("<th>"+System.Security.SecurityElement.Escape(header)+"</th>"),"Capacity HTML header missing");
     var xml=new XmlDocument();xml.Load(stem+".xml");var row=xml.SelectSingleNode("/FolderScopeReport/Row");
     check(row["Used"].InnerText=="700"&&row["Free"].InnerText=="300"&&row["Samples"].InnerText=="3"&&row["AsOf"].InnerText==Util.Date(rows[0].Modified),"Capacity XML values mislabeled");
     check(row["Size"]==null&&row["Allocated"]==null&&row["Modified"]==null,"Capacity XML exposes misleading file fields");
     using(var zip=ZipFile.OpenRead(stem+".xlsx")){var sheet=Read(zip,"xl/worksheets/sheet1.xml");var ns=Namespaces(sheet);check(sheet.SelectNodes("//s:row[@r='1']/s:c/s:is/s:t",ns).Cast<XmlNode>().Select(n=>n.InnerText).SequenceEqual(headers),"Capacity XLSX headers incorrect");check(sheet.SelectSingleNode("//s:c[@r='B2']/s:v",ns).InnerText=="700"&&sheet.SelectSingleNode("//s:c[@r='C2']/s:v",ns).InnerText=="300","Capacity XLSX values incorrect");}
     Exporter.Save(stem+"-ordinary.xml","Ordinary",rows);xml.Load(stem+"-ordinary.xml");check(xml.SelectSingleNode("/FolderScopeReport/Row/Allocated").InnerText=="300"&&xml.SelectSingleNode("/FolderScopeReport/Row/Free")==null,"Ordinary XML compatibility changed");
    }}finally{L.Chinese=chinese;}
    string pages=Path.Combine(folder,"capacity-multipage.pdf");Exporter.Save(pages,"Capacity forecast",Enumerable.Repeat(rows[0],6).ToList(),true);check(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pages)).Contains("/Count 2 "),"Capacity PDF row spacing lost pages");
   });
  }
  private static XmlDocument Read(ZipArchive zip,string path){var doc=new XmlDocument();using(var stream=zip.GetEntry(path).Open())doc.Load(stream);return doc;}
  private static XmlNamespaceManager Namespaces(XmlDocument doc){var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("s","http://schemas.openxmlformats.org/spreadsheetml/2006/main");return ns;}
 }
}
