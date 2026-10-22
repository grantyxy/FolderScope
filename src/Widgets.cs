using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace FolderScope {
 public static class UI {
  public static void Error(Exception ex){MessageBox.Show(L.ErrorText(ex.GetBaseException().Message),L.T("Error"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
  public static void Info(string s){MessageBox.Show(s,L.T("Information"),MessageBoxButtons.OK,MessageBoxIcon.Information);}
  public static bool Confirm(string s){return MessageBox.Show(s,L.T("Confirm"),MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
  private static readonly HashSet<string> PrimaryCaptions=new HashSet<string>{"OK","Start","Generate","Analyze all local drives","Create scheduled task","Run now"};
  public static Button Button(string caption,Action action){var b=new FlatButton{Text=L.T(caption),AutoSize=true,Margin=new Padding(4),Primary=PrimaryCaptions.Contains(caption)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};return b;}
  /// <summary>DPI scale factor (logical 96-dpi pixels to device pixels).</summary>
  public static float Scale;
  public static int D(float v){if(Scale<=0){try{using(var g=Graphics.FromHwnd(IntPtr.Zero))Scale=g.DpiX/96f;}catch{Scale=1;}}return (int)Math.Round(v*Scale);}
  public static void StyleGrid(DataGridView g){
   g.BorderStyle=BorderStyle.None;g.BackgroundColor=Palette.Background;g.GridColor=Palette.LightLine;g.EnableHeadersVisualStyles=false;
   g.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;g.RowHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
   var h=g.ColumnHeadersDefaultCellStyle;h.BackColor=Palette.Background;h.ForeColor=Palette.SubText;h.SelectionBackColor=Palette.Background;h.SelectionForeColor=Palette.SubText;h.Padding=new Padding(D(6),0,D(4),0);h.WrapMode=DataGridViewTriState.False;
   var c=g.DefaultCellStyle;c.BackColor=Palette.Background;c.ForeColor=Palette.Text;c.SelectionBackColor=Palette.Selection;c.SelectionForeColor=Palette.Text;
   g.AlternatingRowsDefaultCellStyle.BackColor=Palette.Background;g.RowsDefaultCellStyle.BackColor=Palette.Background;
   if(g is ReportGrid){g.CellBorderStyle=DataGridViewCellBorderStyle.None;c.Padding=new Padding(D(6),0,D(6),0);}
   else g.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
   if(g.ColumnHeadersHeightSizeMode!=DataGridViewColumnHeadersHeightSizeMode.AutoSize)g.ColumnHeadersHeight=Math.Max(g.ColumnHeadersHeight,D(30));
  }
  public static void StyleTree(TreeView t){
   t.BorderStyle=BorderStyle.None;t.ShowLines=false;t.HotTracking=true;t.FullRowSelect=true;t.BackColor=Palette.Background;t.ForeColor=Palette.Text;
   Action theme=()=>{try{Win.SetWindowTheme(t.Handle,Palette.Dark?"DarkMode_Explorer":"Explorer",null);}catch{}};
   if(t.IsHandleCreated)theme();else t.HandleCreated+=(s,e)=>theme();
  }
  public static ToolStripButton Tool(string caption,string icon,Action action){var b=new ToolStripButton(L.T(caption),Art.Icon(icon)){DisplayStyle=ToolStripItemDisplayStyle.ImageAndText,ToolTipText=L.T(caption)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex);}};return b;}
  public static Form Dialog(string title,int width,int height){return new Form{Text=L.T(title),Size=new Size(width,height),StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",9),MinimumSize=new Size(Math.Min(width,640),Math.Min(height,450)),ShowIcon=false,AutoScaleDimensions=new SizeF(96,96),AutoScaleMode=AutoScaleMode.Dpi};}
  public static string Folder(string initial){using(var d=new FolderBrowserDialog{Description=L.T("Choose a folder..."),SelectedPath=Directory.Exists(initial)?initial:"",ShowNewFolderButton=true})return d.ShowDialog()==DialogResult.OK?d.SelectedPath:null;}
  public static string Input(string title,string label,string value){using(var f=Dialog(title,540,185)){f.MinimumSize=new Size(400,160);var l=new Label{Text=L.T(label),Dock=DockStyle.Top,Height=32,Padding=new Padding(12,8,0,0)};var t=new TextBox{Text=value,Dock=DockStyle.Top,Margin=new Padding(12)};var pane=new Panel{Dock=DockStyle.Top,Height=38,Padding=new Padding(12,0,12,0)};pane.Controls.Add(t);var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft};var ok=Button("OK",()=>{f.DialogResult=DialogResult.OK;f.Close();});var cancel=Button("Cancel",()=>f.Close());bar.Controls.Add(cancel);bar.Controls.Add(ok);f.Controls.Add(pane);f.Controls.Add(l);f.Controls.Add(bar);f.AcceptButton=ok;f.CancelButton=cancel;Palette.Apply(f);return f.ShowDialog()==DialogResult.OK?t.Text:null;}}
  public static void Export(IWin32Window owner,string title,List<ReportRow> rows){using(var d=new SaveFileDialog{Title=L.T("Export"),FileName="FolderScope-report",Filter="Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|HTML (*.html)|*.html|PDF (*.pdf)|*.pdf|XML (*.xml)|*.xml|Text (*.txt)|*.txt",FilterIndex=2,OverwritePrompt=true})if(d.ShowDialog(owner)==DialogResult.OK){Exporter.Save(d.FileName,title,rows);Info(L.F("Saved: {0}",d.FileName));}}
  public static void Print(IWin32Window owner,string title,List<ReportRow> rows){using(var doc=Exporter.Document(title,rows))using(var p=new PrintPreviewDialog{Document=doc,Width=1150,Height=800})p.ShowDialog(owner);}
  public static T Clone<T>(T obj){using(var m=new MemoryStream()){var ser=new System.Xml.Serialization.XmlSerializer(typeof(T));ser.Serialize(m,obj);m.Position=0;return (T)ser.Deserialize(m);}}
  public static void Shell(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ShellInfo {public int cbSize;public uint mask;public IntPtr hwnd;public string verb;public string file;public string parameters;public string directory;public int show;public IntPtr instance;public IntPtr idList;public string cls;public IntPtr key;public uint hotkey;public IntPtr icon;public IntPtr process;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern bool ShellExecuteEx(ref ShellInfo info);
  public static void Properties(string path){var s=new ShellInfo{cbSize=Marshal.SizeOf(typeof(ShellInfo)),mask=12,verb="properties",file=path,show=1};ShellExecuteEx(ref s);}
 }
 public class ReportGrid:DataGridView {
  public List<ReportRow> Data=new List<ReportRow>();private int sorted=-1;private bool ascending;
  public bool Detail;
  private int hoverRow=-1;
  public static int BaseWidth(string n){return n=="Name"?260:n=="Full Path"?380:n=="Modified"||n=="Created"||n=="Accessed"?150:n=="Information"?260:n=="Owner"?180:n=="Percent"?130:n=="Size on Disk"?105:n=="Attributes"?120:90;}
  public ReportGrid(bool detail){Detail=detail;Dock=DockStyle.Fill;ReadOnly=true;AllowUserToAddRows=false;AllowUserToDeleteRows=false;AllowUserToOrderColumns=true;AllowUserToResizeRows=false;MultiSelect=true;SelectionMode=DataGridViewSelectionMode.FullRowSelect;RowHeadersVisible=false;BorderStyle=BorderStyle.None;BackgroundColor=Color.White;AutoGenerateColumns=false;VirtualMode=true;ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;DoubleBuffered=true;
   string[] names=detail?new[]{"Name","Size","Size on Disk","Files","Folders","Modified","Percent","Attributes","Owner","Created","Accessed","Full Path","Information"}:new[]{"Name","Size","Size on Disk","Count","Percent","Modified","Owner","Full Path","Information"};
   foreach(string n in names){var col=new DataGridViewTextBoxColumn{Name=n,HeaderText=L.T(n),Width=UI.D(BaseWidth(n)),SortMode=DataGridViewColumnSortMode.Programmatic,Visible=n!="Full Path"&&n!="Created"&&n!="Accessed"&&n!="Information"};
    if(n=="Size"||n=="Size on Disk"||n=="Files"||n=="Folders"||n=="Count"){col.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;col.HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;}
    if(n=="Name")col.DefaultCellStyle.Padding=new Padding(UI.D(30),0,UI.D(6),0);
    Columns.Add(col);}
   UI.StyleGrid(this);ApplyMetrics();
   CellValueNeeded+=(s,e)=>{if(e.RowIndex>=0&&e.RowIndex<Data.Count)e.Value=Value(Data[e.RowIndex],Columns[e.ColumnIndex].Name,false);};
   ColumnHeaderMouseClick+=(s,e)=>SortColumn(e.ColumnIndex);
   CellMouseEnter+=(s,e)=>SetHover(e.RowIndex);MouseLeave+=(s,e)=>SetHover(-1);
   CellPainting+=PaintCell;
  }
  void SetHover(int row){if(row==hoverRow)return;int old=hoverRow;hoverRow=row;if(old>=0&&old<RowCount)InvalidateRow(old);if(row>=0&&row<RowCount)InvalidateRow(row);}
  void PaintCell(object sender,DataGridViewCellPaintingEventArgs e){
   var g=e.Graphics;var b=e.CellBounds;
   if(e.RowIndex<0){
    if(e.ColumnIndex<0)return;
    using(var br=new SolidBrush(Palette.Background))g.FillRectangle(br,b);
    e.PaintContent(b);
    using(var p=new Pen(Palette.Line))g.DrawLine(p,b.Left,b.Bottom-1,b.Right,b.Bottom-1);
    using(var p=new Pen(Palette.LightLine))g.DrawLine(p,b.Right-1,b.Top+UI.D(7),b.Right-1,b.Bottom-UI.D(7));
    e.Handled=true;return;
   }
   if(e.ColumnIndex<0||e.RowIndex>=Data.Count)return;
   bool selected=(e.State&DataGridViewElementStates.Selected)!=0;
   using(var br=new SolidBrush(selected?Palette.Selection:e.RowIndex==hoverRow?Palette.RowHover:Palette.Background))g.FillRectangle(br,b);
   string col=Columns[e.ColumnIndex].Name;var row=Data[e.RowIndex];
   if(col=="Percent"){
    var r=new Rectangle(b.X+UI.D(6),b.Y+UI.D(5),b.Width-UI.D(12),b.Height-UI.D(10));
    using(var br=new SolidBrush(Palette.BarTrack))g.FillRectangle(br,r);
    int w=(int)(r.Width*Math.Min(100,Math.Max(0,row.Percent))/100);
    if(w>0)using(var br=new LinearGradientBrush(new Rectangle(r.X,r.Y-1,w,r.Height+2),Palette.Blend(Palette.BarFill,Color.White,0.3f),Palette.BarFill,90))g.FillRectangle(br,r.X,r.Y,w,r.Height);
    using(var p=new Pen(Palette.Line))g.DrawRectangle(p,r.X,r.Y,r.Width-1,r.Height-1);
    Draw.Text(g,Convert.ToString(e.FormattedValue),e.CellStyle.Font??Font,r,Palette.Text,Draw.Center);
   }else{
    if(col=="Name"){var icon=row.Source!=null?Art.For(row.Source):Art.Icon(Detail?"folder":"file");int s=UI.D(16);if(icon!=null)g.DrawImage(icon,b.X+UI.D(9),b.Y+(b.Height-s)/2,s,s);}
    e.PaintContent(b);
   }
   e.Handled=true;
  }
  object Value(ReportRow r,string column,bool raw){Entry e=r.Source;switch(column){case "Name":return r.DisplayName;case "Size":return raw?(object)r.Size:Util.Size(r.Size);case "Size on Disk":return raw?(object)r.Allocated:Util.Size(r.Allocated);case "Files":case "Count":return raw?(object)r.Count:r.Count.ToString("N0");case "Folders":return e!=null?(raw?(object)e.Folders:e.Folders.ToString("N0")):"";case "Modified":return raw?(object)r.Modified:Util.Date(r.Modified);case "Created":return e==null?"":Util.Date(e.Created);case "Accessed":return e==null?"":Util.Date(e.Accessed);case "Percent":return raw?(object)r.Percent:r.Percent.ToString("0.00")+"%";case "Attributes":return e==null?"":L.Attributes(e.AttrText);case "Owner":return r.Owner;case "Full Path":return r.Path;default:return L.Value(r.Extra);}}
  public void ApplyMetrics(){RowTemplate.Height=UI.D(26);ColumnHeadersHeight=UI.D(30);}
  public void SetData(List<ReportRow> rows){Data=rows??new List<ReportRow>();ApplyMetrics();RowCount=0;RowCount=Data.Count;ClearSelection();Invalidate();}
  public List<ReportRow> Selected(){return SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).Where(r=>r.Index<Data.Count).Select(r=>Data[r.Index]).ToList();}
  public void SortColumn(int column){ascending=sorted==column?!ascending:true;sorted=column;string name=Columns[column].Name;Data=ascending?Data.OrderBy(r=>Value(r,name,true),Comparer<object>.Default).ToList():Data.OrderByDescending(r=>Value(r,name,true),Comparer<object>.Default).ToList();foreach(DataGridViewColumn c in Columns)c.HeaderCell.SortGlyphDirection=SortOrder.None;Columns[column].HeaderCell.SortGlyphDirection=ascending?SortOrder.Ascending:SortOrder.Descending;ClearSelection();Invalidate();}
  public void ColumnMenu(Control owner,Point p){var m=new ContextMenuStrip();foreach(DataGridViewColumn c in Columns){var item=new ToolStripMenuItem(c.HeaderText){Checked=c.Visible,CheckOnClick=true};var col=c;item.CheckedChanged+=(s,e)=>col.Visible=item.Checked;m.Items.Add(item);}m.Show(owner,p);}
 }
 public static class Operations {
  public static void Attach(ReportGrid grid,Func<bool> offline,Action refresh){
   var menu=new ContextMenuStrip();Action<string,string,Action> add=(name,icon,a)=>{var i=new ToolStripMenuItem(L.T(name),Art.Icon(icon));i.Click+=(s,e)=>{try{a();}catch(Exception ex){UI.Error(ex);}};menu.Items.Add(i);};
   Func<string> first=()=>{var r=grid.Selected().FirstOrDefault();if(r==null||string.IsNullOrEmpty(r.Path))throw new InvalidOperationException(L.T("Select files or folders first."));return r.Path;};
   add("Open","open",()=>UI.Shell(first()));add("Open location","search",()=>Process.Start("explorer.exe","/select,\""+first()+"\""));add("Copy path","clipboard",()=>Clipboard.SetText(string.Join(Environment.NewLine,grid.Selected().Select(r=>r.Path))));add("Properties","properties",()=>UI.Properties(first()));menu.Items.Add(new ToolStripSeparator());
   foreach(string action in new[]{"Copy","Move","Delete","Archive","Rename"}){string a=action;add(a,a=="Archive"?"zip":a=="Delete"?"delete":a=="Copy"?"copy":a=="Move"?"move":a=="Rename"?"rename":"file",()=>Execute(a,grid,offline(),refresh));}
   menu.Items.Add(new ToolStripSeparator());add("Export","export",()=>UI.Export(grid.FindForm(),grid.FindForm().Text,grid.Selected().Count>0?grid.Selected():grid.Data));add("Columns","columns",()=>grid.ColumnMenu(grid,new Point(20,20)));grid.ContextMenuStrip=menu;
   grid.CellMouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right&&e.RowIndex>=0&&!grid.Rows[e.RowIndex].Selected){grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;}};
   grid.KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.C){Clipboard.SetText(Exporter.ToCsv(grid.Selected(),true));e.Handled=true;}if(e.KeyCode==Keys.Delete){try{Execute("Delete",grid,offline(),refresh);}catch(Exception ex){UI.Error(ex);}e.Handled=true;}};
  }
  public static void Execute(string action,ReportGrid grid,bool offline,Action refresh){
   var selected=grid.Selected().Where(r=>!string.IsNullOrEmpty(r.Path)&&r.Source!=null).ToList();if(selected.Count==0)throw new InvalidOperationException(L.T("Select files or folders first."));if(offline)throw new InvalidOperationException(L.T("Offline snapshots cannot be used to change live files."));
   var paths=FileJobs.Normalize(selected.Select(r=>r.Path));foreach(ReportRow r in selected){if(r.Source!=null&&!r.Source.Folder){var f=new FileInfo(r.Path);if(!f.Exists||f.Length!=r.Source.Size||f.LastWriteTimeUtc!=r.Source.Modified)throw new IOException(L.F("Source has changed since the scan. Refresh before modifying:\n{0}",r.Path));}}
   string destination=null;
   if(action=="Copy"||action=="Move"){destination=UI.Folder("");if(destination==null)return;if(action=="Move"&&!UI.Confirm(L.F("Move {0} selected items to:\n{1}",paths.Count,destination)))return;}
   if(action=="Delete"&&!UI.Confirm(L.F("Move {0} selected items to the Recycle Bin?\n\n{1}",paths.Count,string.Join("\n",paths.Take(8)))))return;
   if(action=="Archive"){using(var d=new SaveFileDialog{Filter="ZIP (*.zip)|*.zip",FileName="Archive.zip",OverwritePrompt=true}){if(d.ShowDialog()!=DialogResult.OK)return;if(File.Exists(d.FileName))throw new IOException("Choose a new ZIP filename; existing archives are not overwritten.");FileJobs.Archive(paths,d.FileName);FileJobs.Audit(action,d.FileName,"OK");UI.Info(L.F("Saved: {0}",d.FileName));return;}}
   if(action=="Rename"){if(paths.Count!=1)throw new InvalidOperationException("Select one item to rename.");string name=UI.Input("Rename","New name",Path.GetFileName(paths[0]));if(name==null)return;if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name=="."||name=="..")throw new ArgumentException("Invalid file name.");string target=Path.Combine(Path.GetDirectoryName(paths[0]),name);if(Directory.Exists(paths[0]))Directory.Move(paths[0],target);else File.Move(paths[0],target);FileJobs.Audit(action,paths[0],target);refresh();return;}
   var failed=new List<string>();Cursor old=Cursor.Current;Cursor.Current=Cursors.WaitCursor;try{foreach(string p in paths)try{
    if(action=="Delete"){if(Directory.Exists(p))FileSystem.DeleteDirectory(p,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);else FileSystem.DeleteFile(p,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.ThrowException);}
    else {string dest=Path.Combine(destination,Path.GetFileName(p.TrimEnd('\\')));if(Util.Under(dest,p))throw new IOException("Destination cannot be inside source.");if(action=="Copy")FileJobs.Copy(p,dest);else {if(Directory.Exists(p))FileSystem.MoveDirectory(p,dest,UIOption.AllDialogs,UICancelOption.ThrowException);else FileSystem.MoveFile(p,dest,UIOption.AllDialogs,UICancelOption.ThrowException);}}
    FileJobs.Audit(action,p,"OK");
   }catch(Exception ex){failed.Add(p+": "+ex.Message);FileJobs.Audit(action,p,ex.Message);}}finally{Cursor.Current=old;}
   if(failed.Count>0)UI.Info(L.F("Some operations failed:\n{0}",string.Join("\n",failed)));else UI.Info(L.T("Operation completed."));if(action!="Copy")refresh();
  }
 }
}
