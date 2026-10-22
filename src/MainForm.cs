using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FolderScope {
 public class MainForm:Form {
  public AppSettings Settings;
  public Snapshot Data;
  public bool Offline;
  public string CurrentPath;
  private string launchPath;private bool constructed;
  private Scanner scanner;private CancellationTokenSource cancel;private bool busy;private System.Windows.Forms.Timer timer;private Stopwatch elapsed=new Stopwatch();
  private TitleBar chrome;private ToolStrip menu,navigation,links;private Panel navBar;private RibbonPanel ribbon;private Panel ribbonHost;private SearchBox command,address;private TextBox pathBox;
  private SplitContainer split,leftSplit;private TreeView folderTree,scanTree;private DockHeader browserHeader;private DockTabs browserTabs;private DataGridView drives;private Label driveTotals;
  private PivotView content;private ReportGrid details;private List<SizeChart> charts=new List<SizeChart>();private Panel computer;private FlowLayoutPanel tiles;private HintLine hint;private InfoPanel info;
  private ToolStripStatusLabel status,folderCount,fileCount,timeLabel,sizeLabel,filter;private ToolStripProgressBar progress;
  private Stack<string> back=new Stack<string>(),forward=new Stack<string>();private string activeRibbon="Home";
  private ImageList treeImages;
  static int D(float v){return UI.D(v);}

  [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] struct RECT {public int L,T,R,B;}
  [StructLayout(LayoutKind.Sequential)] struct NCCALCSIZE_PARAMS {public RECT r0,r1,r2;public IntPtr pos;}
  [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO {public Point reserved,maxSize,maxPosition,minTrack,maxTrack;}

  public MainForm(string initial){
   SuspendLayout();Settings=AppSettings.Load();L.Chinese=Settings.Language!="en-US";Palette.Set(Settings.Theme);launchPath=initial;
   ToolStripManager.Renderer=new ModernRenderer();
   Text="FolderScope";Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.None;UI.Scale=DeviceDpi/96f;
   MinimumSize=new Size(D(980),D(660));Size=new Size(Math.Min(D(1440),Screen.PrimaryScreen.WorkingArea.Width-40),Math.Min(D(940),Screen.PrimaryScreen.WorkingArea.Height-40));
   StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.Sizable;Icon=Art.AppIcon();
   Build();
   timer=new System.Windows.Forms.Timer{Interval=300};timer.Tick+=(s,e)=>UpdateProgress();timer.Start();KeyPreview=true;KeyDown+=Shortcut;
   FormClosing+=(s,e)=>{if(busy&&!UI.Confirm(L.T("Stop the current scan before closing?"))){e.Cancel=true;return;}if(cancel!=null){cancel.Cancel();if(scanner!=null)scanner.Pause.Set();}Settings.LastPath=CurrentPath??"";Settings.Save();};
   ResumeLayout(true);constructed=true;
   Shown+=(s,e)=>{LayoutPanels();if(!string.IsNullOrEmpty(launchPath))StartScan(new[]{launchPath});else if(Settings.Welcome)ShowWelcome();};
  }

  // Custom frame: keep the native (invisible) resize borders and shadow, remove the system caption.
  protected override void WndProc(ref Message m){
   if(m.Msg==0x83&&m.WParam!=IntPtr.Zero){
    var before=(NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam,typeof(NCCALCSIZE_PARAMS));
    base.WndProc(ref m);
    var after=(NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam,typeof(NCCALCSIZE_PARAMS));
    after.r0.T=before.r0.T+(IsZoomed(Handle)?after.r0.L-before.r0.L:0);
    Marshal.StructureToPtr(after,m.LParam,false);m.Result=IntPtr.Zero;return;
   }
   if(m.Msg==0x84){
    base.WndProc(ref m);
    if((int)m.Result==1&&!IsZoomed(Handle)){long lp=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(lp&0xFFFF),(short)((lp>>16)&0xFFFF)));if(p.Y<D(5))m.Result=(IntPtr)(p.X<D(10)?13:p.X>ClientSize.Width-D(10)?14:12);}
    return;
   }
   base.WndProc(ref m);
  }

  private void Build(){
   Rectangle oldBounds=Bounds;SuspendLayout();foreach(Control c in Controls.Cast<Control>().ToArray())c.Dispose();Controls.Clear();charts.Clear();
   UI.Scale=DeviceDpi/96f;BackColor=Palette.Panel;ForeColor=Palette.Text;

   // Title bar
   chrome=new TitleBar{Caption="FolderScope 1.0  -  "+L.T("PERSONAL EDITION"),BackColor=Palette.Accent,ForeColor=Color.White,Font=Font,ToggleMax=ToggleMax};
   chrome.Controls.Add(new CaptionButton("\uE897","Help",ShowHelp){GlyphSize=11});
   chrome.Controls.Add(new CaptionButton("\uE921","Minimize",()=>WindowState=FormWindowState.Minimized));
   chrome.Controls.Add(new CaptionButton(WindowState==FormWindowState.Maximized?"\uE923":"\uE922","Maximize",ToggleMax));
   chrome.Controls.Add(new CaptionButton("\uE8BB","Close",Close){IsClose=true});
   foreach(var q in new[]{new CaptionButton("\uE768","Start",()=>StartScan(new[]{pathBox.Text})),new CaptionButton("\uE74E","Save Results",SaveSnapshot),new CaptionButton("\uE8E5","Load Results",LoadSnapshot)}){q.Dock=DockStyle.Left;q.Width=D(30);q.GlyphSize=12;chrome.Controls.Add(q);}
   chrome.Controls.Add(new Panel{Dock=DockStyle.Left,Width=D(34),BackColor=Color.Transparent,Enabled=false});
   chrome.LeftReserve=D(34)+D(30)*3;

   // Tab row
   menu=new ToolStrip{Dock=DockStyle.Top,GripStyle=ToolStripGripStyle.Hidden,Tag="noline",BackColor=Palette.Panel,AutoSize=false,Height=D(30),Padding=new Padding(D(4),0,D(6),0),CanOverflow=false,ImageScalingSize=new Size(D(16),D(16))};
   var file=new ToolStripDropDownButton(L.T("File")){Tag="file",ShowDropDownArrow=false,AutoSize=false,Size=new Size(D(58),D(27)),Margin=new Padding(0,D(3),D(4),0)};
   AddMenu(file,"Load Results","load",LoadSnapshot);AddMenu(file,"Save Results","save",SaveSnapshot);file.DropDownItems.Add(new ToolStripSeparator());AddMenu(file,"Export","export",()=>ExportDetails());AddMenu(file,"Print","print",()=>Print());file.DropDownItems.Add(new ToolStripSeparator());AddMenu(file,"Options","settings",ShowOptions);AddMenu(file,"Close",null,Close);menu.Items.Add(file);
   foreach(string tab in new[]{"Home","Tools","Help","Detail View"}){string a=tab;string text=L.T(tab);var b=new ToolStripButton(text){Name=tab,Tag="tab",Checked=activeRibbon==tab,AutoSize=false,Size=new Size(TextRenderer.MeasureText(text,Font).Width+D(26),D(27)),Margin=new Padding(0,D(3),D(2),0)};b.Click+=(s,e)=>SelectRibbon(a);menu.Items.Add(b);}
   command=new SearchBox("Tell me what you want to do..."){Glyph="\uEA80",GlyphColor=Color.FromArgb(230,160,20),Font=Font};
   command.Box.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){string q=command.Text.Trim();if(q.Length==0)return;string match=Reports.Names.FirstOrDefault(n=>L.T(n).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0);if(match!=null)OpenReports(match);else ShowSearch();e.Handled=true;e.SuppressKeyPress=true;}};
   menu.Items.Add(new ToolStripControlHost(command){AutoSize=false,Size=new Size(D(270),D(24)),Margin=new Padding(D(18),D(3),0,0)});
   var compact=new ToolStripButton(L.T("Compact")){Alignment=ToolStripItemAlignment.Right,Margin=new Padding(D(2),D(3),0,0)};compact.Click+=(s,e)=>ToggleCompact();menu.Items.Add(compact);
   var language=new ToolStripDropDownButton("中文 / EN"){Alignment=ToolStripItemAlignment.Right,Margin=new Padding(D(2),D(3),0,0)};var zh=new ToolStripMenuItem("简体中文"){Checked=L.Chinese};zh.Click+=(s,e)=>ChangeLanguage(true);var en=new ToolStripMenuItem("English"){Checked=!L.Chinese};en.Click+=(s,e)=>ChangeLanguage(false);language.DropDownItems.Add(zh);language.DropDownItems.Add(en);menu.Items.Add(language);
   var theme=new ToolStripDropDownButton(L.T("Theme")){Alignment=ToolStripItemAlignment.Right,Margin=new Padding(D(2),D(3),0,0)};foreach(string name in new[]{"Light (Blue)","Dark (Blue)","Light (Red)","Light (Green)","Light (Orange)","Light (Purple)"}){string n=name;var i=new ToolStripMenuItem(L.T(n)){Checked=Settings.Theme==n};i.Click+=(s,e)=>{Settings.Theme=n;Settings.Save();Palette.Set(n);ToolStripManager.Renderer=new ModernRenderer();Build();};theme.DropDownItems.Add(i);}menu.Items.Add(theme);
   menu.LayoutCompleted+=(s,e)=>UpdateGap();

   // Ribbon
   ribbonHost=new Panel{Dock=DockStyle.Top,Height=D(102),Padding=new Padding(D(4),0,D(4),D(4)),BackColor=Palette.Panel,Tag="keep"};
   ribbon=new RibbonPanel{Dock=DockStyle.Fill};ribbon.CollapseClicked+=ToggleCompact;ribbonHost.Controls.Add(ribbon);BuildRibbon();

   // Navigation bar: [nav buttons][address (fill)][scan buttons]
   navBar=new Panel{Dock=DockStyle.Top,Height=D(34),Padding=new Padding(D(4),D(5),D(6),D(5)),BackColor=Palette.Panel,Tag="keep"};
   navigation=NavStrip(DockStyle.Left);var navRight=NavStrip(DockStyle.Right);
   navigation.Items.Add(IconTool("This PC",Art.Icon("computer"),ShowComputer));
   navigation.Items.Add(IconTool("Back",Art.GlyphImage("\uE72B",D(16),Palette.SubText),GoBack));
   navigation.Items.Add(IconTool("Forward",Art.GlyphImage("\uE72A",D(16),Palette.SubText),GoForward));
   navigation.Items.Add(IconTool("Up",Art.GlyphImage("\uE74A",D(16),Palette.SubText),GoUp));
   address=new SearchBox("Enter a path and press Enter to scan"){Icon=Art.Icon("computer"),Font=Font,Dock=DockStyle.Fill};pathBox=address.Box;pathBox.AutoCompleteMode=AutoCompleteMode.SuggestAppend;pathBox.AutoCompleteSource=AutoCompleteSource.FileSystemDirectories;
   pathBox.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){StartScan(new[]{pathBox.Text});e.Handled=true;e.SuppressKeyPress=true;}};
   navRight.Items.Add(IconTool("Refresh",Art.Icon("refresh"),RefreshScan));
   navRight.Items.Add(new ToolStripSeparator());
   navRight.Items.Add(UI.Tool("Start","play",()=>StartScan(new[]{pathBox.Text})));
   navRight.Items.Add(UI.Tool("Stop","stop",()=>{if(cancel!=null){cancel.Cancel();scanner.Pause.Set();}}));
   navRight.Items.Add(UI.Tool("Pause","pause",()=>{if(scanner!=null&&busy){if(scanner.Pause.IsSet)scanner.Pause.Reset();else scanner.Pause.Set();}}));
   navRight.Items.Add(new ToolStripSeparator());
   navRight.Items.Add(UI.Tool("Browse","browse",Browse));navRight.Items.Add(UI.Tool("Paths","folder",MultiplePaths));
   navBar.Controls.Add(address);navBar.Controls.Add(navigation);navBar.Controls.Add(navRight);

   // Status bar
   var stat=new StatusStrip{Dock=DockStyle.Bottom,SizingGrip=false,BackColor=Palette.Panel,Padding=new Padding(D(6),0,D(12),0),ImageScalingSize=new Size(D(16),D(16))};
   status=new ToolStripStatusLabel(L.T("Ready. Choose a folder or drive to start.")){Spring=true,TextAlign=ContentAlignment.MiddleLeft};
   progress=new ToolStripProgressBar{Width=D(120),Visible=false,Style=ProgressBarStyle.Marquee};
   folderCount=new ToolStripStatusLabel(L.F("Folders: {0}",0),Art.Icon("folder"));fileCount=new ToolStripStatusLabel(L.F("Files: {0}",0),Art.Icon("file"));timeLabel=new ToolStripStatusLabel("0:00:00",Art.Icon("clock"));filter=new ToolStripStatusLabel("",Art.Icon("filter"));sizeLabel=new ToolStripStatusLabel("0 B",Art.Icon("disk"));
   stat.Items.Add(status);stat.Items.Add(progress);foreach(var item in new ToolStripItem[]{folderCount,fileCount,timeLabel,filter,sizeLabel}){stat.Items.Add(new ToolStripSeparator());stat.Items.Add(item);}

   // Body
   var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(D(4),0,D(4),D(4)),BackColor=Palette.Panel,Tag="keep"};
   split=new SplitContainer{Dock=DockStyle.Fill,SplitterWidth=D(4),FixedPanel=FixedPanel.Panel1,Panel1MinSize=D(150),BackColor=Palette.Panel};split.Size=new Size(D(1200),D(600));split.SplitterDistance=D(260);
   leftSplit=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,FixedPanel=FixedPanel.Panel2,SplitterWidth=D(4),BackColor=Palette.Panel};leftSplit.Size=new Size(D(260),D(600));leftSplit.SplitterDistance=D(420);split.Panel1.Controls.Add(leftSplit);
   body.Controls.Add(split);

   // Left: folder browser / scan data
   var browserBox=Frame();
   treeImages=new ImageList{ImageSize=SystemInformation.SmallIconSize,ColorDepth=ColorDepth.Depth32Bit};
   folderTree=NewTree();scanTree=NewTree();scanTree.Visible=false;
   browserHeader=new DockHeader("Folder Browser"){CloseAction=()=>split.Panel1Collapsed=true};
   browserTabs=new DockTabs{Font=Font};browserTabs.Add("Folder Browser",Art.Icon("folder"));browserTabs.Add("Scan Data",Art.Icon("chart"));
   browserTabs.Changed+=i=>{folderTree.Visible=i==0;scanTree.Visible=i==1;browserHeader.Text=L.T(i==0?"Folder Browser":"Scan Data");};
   browserBox.Controls.Add(folderTree);browserBox.Controls.Add(scanTree);browserBox.Controls.Add(browserHeader);browserBox.Controls.Add(browserTabs);leftSplit.Panel1.Controls.Add(browserBox);
   PopulateBrowser();folderTree.BeforeExpand+=(s,e)=>ExpandLive(e.Node);folderTree.NodeMouseDoubleClick+=(s,e)=>{string p=e.Node.Tag as string;if(p!=null)OpenPath(p);else ShowComputer();};
   scanTree.BeforeExpand+=(s,e)=>ExpandScan(e.Node);scanTree.AfterSelect+=(s,e)=>{Entry entry=e.Node.Tag as Entry;if(entry!=null)Navigate(entry.Path,true);};

   // Left: drive space
   var driveBox=Frame();
   drives=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,MultiSelect=false,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
   foreach(string h in new[]{"Disk","Total","Free","% Used"})drives.Columns.Add(h,L.T(h));drives.Columns[0].FillWeight=90;drives.Columns[3].FillWeight=110;
   UI.StyleGrid(drives);drives.CellBorderStyle=DataGridViewCellBorderStyle.None;drives.RowTemplate.Height=D(24);drives.ColumnHeadersHeight=D(26);drives.DefaultCellStyle.Padding=new Padding(D(4),0,D(2),0);drives.ColumnHeadersDefaultCellStyle.Padding=new Padding(D(4),0,0,0);
   drives.CellPainting+=PaintDriveCell;drives.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)StartScan(new[]{Convert.ToString(drives.Rows[e.RowIndex].Tag)});};
   driveTotals=new Label{Dock=DockStyle.Bottom,Height=D(24),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(D(6),0,0,0),BackColor=Palette.Panel,ForeColor=Palette.Link,Tag="keep"};
   driveBox.Controls.Add(drives);driveBox.Controls.Add(driveTotals);driveBox.Controls.Add(new DockHeader("Drive Space"){CloseAction=()=>leftSplit.Panel2Collapsed=true});leftSplit.Panel2.Controls.Add(driveBox);FillDrives();

   // Right: content
   var contentBox=Frame();
   content=new PivotView{Dock=DockStyle.Fill,BackColor=Palette.Background,Font=Font};content.SelectedIndexChanged+=(s,e)=>UpdateHint();
   details=new ReportGrid(true);Operations.Attach(details,()=>Offline,RefreshScan);details.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.RowIndex<details.Data.Count){Entry ent=details.Data[e.RowIndex].Source;if(ent!=null){if(ent.Folder)Navigate(ent.Path,true);else if(!Offline)UI.Shell(ent.Path);}}};
   content.AddPage("Details",Art.Icon("details"),details);
   foreach(string mode in new[]{"Bar Chart","Pie Chart","Sunburst Chart","Folder Map"}){var chart=new SizeChart{Mode=mode};chart.Navigate+=ent=>Navigate(ent.Path,true);content.AddPage(mode,Art.Icon(mode=="Bar Chart"?"chart":mode=="Pie Chart"?"pie":mode=="Sunburst Chart"?"sunburst":"map"),chart);charts.Add(chart);}
   computer=new Panel{Dock=DockStyle.Fill,BackColor=Palette.Background,Tag="keep"};
   links=new ToolStrip{Dock=DockStyle.Top,GripStyle=ToolStripGripStyle.Hidden,Tag="noline",BackColor=Palette.Background,Padding=new Padding(D(8),D(4),D(8),D(2)),CanOverflow=false,ImageScalingSize=new Size(D(16),D(16))};
   links.Items.Add(Link("Analyze all local drives",()=>StartScan(DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed&&d.IsReady).Select(d=>d.Name))));links.Items.Add(Link("Export drives",ExportDrives));links.Items.Add(Link("Network Shares",ShowNetwork));links.Items.Add(Link("Paths",MultiplePaths));
   var refresh=Link("Refresh",()=>{FillDrives();ShowComputer();});refresh.Image=Art.Icon("refresh");refresh.Alignment=ToolStripItemAlignment.Right;links.Items.Add(refresh);
   tiles=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(D(8),D(4),D(8),D(8)),BackColor=Palette.Background,Tag="keep"};
   computer.Controls.Add(tiles);computer.Controls.Add(links);
   hint=new HintLine();info=new InfoPanel{Font=Font};info.TitleClicked+=()=>{if(CurrentPath==null){try{Process.Start("control.exe","system");}catch{}}else if(!Offline&&Directory.Exists(CurrentPath))UI.Shell(CurrentPath);};
   contentBox.Controls.Add(content);contentBox.Controls.Add(computer);contentBox.Controls.Add(hint);contentBox.Controls.Add(info);split.Panel2.Controls.Add(contentBox);

   Controls.Add(body);Controls.Add(navBar);Controls.Add(ribbonHost);Controls.Add(menu);Controls.Add(chrome);Controls.Add(stat);
   if(Data!=null){PopulateScan();if(CurrentPath!=null&&Data.Index.ContainsKey(CurrentPath))Navigate(CurrentPath,false);else ShowComputer();}else ShowComputer();
   UpdateCounts();UpdateFilter();
   if(constructed)Bounds=oldBounds;
   ResumeLayout(true);if(constructed)LayoutPanels();
  }
  private Panel Frame(){return new Panel{Dock=DockStyle.Fill,Padding=new Padding(1),BackColor=Palette.Line,Tag="keep"};}
  private TreeView NewTree(){var t=new TreeView{Dock=DockStyle.Fill,HideSelection=false,ItemHeight=D(24),Indent=D(14),ImageList=treeImages,Font=Font};UI.StyleTree(t);return t;}
  private ToolStripButton IconTool(string hint,Image image,Action a){var b=new ToolStripButton(image){ToolTipText=L.T(hint),DisplayStyle=ToolStripItemDisplayStyle.Image,AutoSize=false,Size=new Size(D(28),D(24))};b.Click+=(s,e)=>{try{a();}catch(Exception ex){UI.Error(ex);}};return b;}
  private ToolStripButton Link(string text,Action a){var b=new ToolStripButton(L.T(text)){Tag="link",Margin=new Padding(0,0,D(14),0)};b.Click+=(s,e)=>{try{a();}catch(Exception ex){UI.Error(ex);}};b.MouseEnter+=(s,e)=>links.Cursor=Cursors.Hand;b.MouseLeave+=(s,e)=>links.Cursor=Cursors.Default;return b;}
  private ToolStrip NavStrip(DockStyle dock){return new ToolStrip{Dock=dock,LayoutStyle=ToolStripLayoutStyle.HorizontalStackWithOverflow,GripStyle=ToolStripGripStyle.Hidden,Tag="noline",BackColor=Palette.Panel,AutoSize=true,Padding=dock==DockStyle.Left?new Padding(0,0,D(6),0):new Padding(D(6),0,0,0),CanOverflow=false,ImageScalingSize=new Size(D(16),D(16))};}
  private void UpdateGap(){if(menu==null||ribbon==null)return;foreach(ToolStripItem i in menu.Items){var b=i as ToolStripButton;if(b!=null&&b.Tag as string=="tab"&&b.Checked){int off=ribbon.Left+ribbonHost.Left-menu.Left;ribbon.GapLeft=b.Bounds.Left-off;ribbon.GapRight=b.Bounds.Right-off;ribbon.Invalidate();return;}}ribbon.GapLeft=ribbon.GapRight=-1;}
  private void LayoutPanels(){if(split==null||split.Width<D(400))return;try{split.SplitterDistance=Math.Min(split.Width-D(300),D(260));leftSplit.SplitterDistance=Math.Max(D(120),leftSplit.Height-D(160));}catch{}UpdateGap();}
  protected override void OnResize(EventArgs e){base.OnResize(e);if(chrome!=null){var max=chrome.Controls.OfType<CaptionButton>().FirstOrDefault(c=>c.Glyph=="\uE922"||c.Glyph=="\uE923");if(max!=null){max.Glyph=WindowState==FormWindowState.Maximized?"\uE923":"\uE922";max.Invalidate();}chrome.Invalidate();}}
  private void AddMenu(ToolStripDropDownItem parent,string text,string icon,Action action){var item=new ToolStripMenuItem(L.T(text),icon==null?null:Art.Icon(icon));item.Click+=(s,e)=>{try{action();}catch(Exception ex){UI.Error(ex);}};parent.DropDownItems.Add(item);}
  private void ToggleMax(){WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
  private void ToggleCompact(){ribbonHost.Visible=!ribbonHost.Visible;}
  private void ChangeLanguage(bool zh){L.Chinese=zh;Settings.Language=zh?"zh-CN":"en-US";Settings.Save();Build();}
  private void SelectRibbon(string tab){activeRibbon=tab;foreach(ToolStripItem i in menu.Items){var b=i as ToolStripButton;if(b!=null&&b.Tag as string=="tab")b.Checked=b.Name==tab;}if(!ribbonHost.Visible)ribbonHost.Visible=true;BuildRibbon();UpdateGap();menu.Invalidate();}
  private void BuildRibbon(){if(ribbon==null)return;ribbon.SuspendLayout();foreach(Control c in ribbon.Controls.Cast<Control>().ToArray())c.Dispose();ribbon.Controls.Clear();Action<RibbonGroup> add=g=>ribbon.Controls.Add(g);
   if(activeRibbon=="Home"){
    add(new RibbonGroup("Results").Add("Load\nResults","load",LoadSnapshot).AddD("Save\nResults","save",SaveSnapshot).AddD("Compare","compare",CompareSnapshot).AddD("Print","print",Print));
    var reports=new RibbonButton("File\nReports","report",null){Menu=ReportsMenu()};
    add(new RibbonGroup("Analysis").Add(reports).Add("Disk\nReporter","sysdisk",ShowDiskReporter).Add("Search","search",ShowSearch).Add("Scan\nFilter","filter",ShowFilter));
    add(new RibbonGroup("Export").AddD("Details","details",ExportDetails).AddD("Chart","chart",ExportChart).Add("Drives","disk",ExportDrives).AddD("Clipboard","clipboard",()=>{if(RequireData())Clipboard.SetText(Exporter.ToCsv(details.Data,true));}));
    add(new RibbonGroup("Options").Add("Options","settings",ShowOptions).AddD("Event\nLog","event",ShowEvents));
    add(new RibbonGroup("Layout").Add("Hide All\nPanels","layout",()=>split.Panel1Collapsed=true).Add("Pin All\nPanels","pin",()=>{split.Panel1Collapsed=false;leftSplit.Panel2Collapsed=false;}).Add("Reset\nLayout","refresh",()=>{split.Panel1Collapsed=false;leftSplit.Panel2Collapsed=false;LayoutPanels();}).Add("Panels","panel",()=>split.Panel1Collapsed=!split.Panel1Collapsed));
   }else if(activeRibbon=="Tools"){
    add(new RibbonGroup("Snapshots").AddD("Create\nSnapshot","camera",SaveSnapshot).Add("Open\nSnapshot","load",LoadSnapshot).AddD("Compare\nSnapshots","compare",CompareSnapshot).Add("Trend\nAnalyzer","trend",ShowTrends));
    add(new RibbonGroup("Automation").Add("Task\nScheduler","clock",ShowScheduler).Add("Disk\nHistory","history",ShowHistory).AddD("Send\nEmail","mail",SendMail));
    add(new RibbonGroup("Tools").Add("Filter\n&& Zip","zip",ShowFilterZip).Add("Network\nShares","network",ShowNetwork).Add("File\nExtensions","file",()=>OpenReports("File Types")));
    add(new RibbonGroup("Settings").Add("Options","settings",ShowOptions).Add("Run as\nAdmin","admin",()=>{try{Process.Start(new ProcessStartInfo(Application.ExecutablePath){Verb="runas",UseShellExecute=true});}catch(Exception ex){UI.Error(ex);}}));
   }else if(activeRibbon=="Help")add(new RibbonGroup("Support").Add("User\nGuide","help",ShowHelp).Add("About","info",ShowAbout));
   else {
    add(new RibbonGroup("View").Add("Details","details",()=>ShowPage(0)).Add("Bar Chart","chart",()=>ShowPage(1)).Add("Pie Chart","pie",()=>ShowPage(2)).Add("Sunburst Chart","sunburst",()=>ShowPage(3)).Add("Folder Map","map",()=>ShowPage(4)));
    add(new RibbonGroup("Layout").Add("Columns","columns",()=>details.ColumnMenu(details,new Point(D(40),D(10)))).AddD("Refresh","refresh",RefreshScan));
   }
   ribbon.ResumeLayout();ribbon.Relayout();UpdateCommandStates();
  }
  private void ShowPage(int i){content.SelectedIndex=i;}
  private ContextMenuStrip ReportsMenu(){var m=new ContextMenuStrip{ImageScalingSize=new Size(D(16),D(16))};foreach(string n in Reports.Names){string r=n;var item=new ToolStripMenuItem(L.T(n),Art.Icon("report"));item.Click+=(s,e)=>{try{OpenReports(r);}catch(Exception ex){UI.Error(ex);}};m.Items.Add(item);}return m;}
  private void UpdateCommandStates(){if(ribbon==null)return;foreach(RibbonGroup g in ribbon.Controls.OfType<RibbonGroup>())foreach(RibbonButton b in g.Controls.OfType<RibbonButton>())if(b.NeedsData)b.Enabled=Data!=null;}

  private string IconKey(string path,bool special){string key=special?"p:"+path:"folder";if(!treeImages.Images.ContainsKey(key))treeImages.Images.Add(key,special?Art.Shell(path):Art.Type(null,true));return key;}
  private void PopulateBrowser(){
   folderTree.Nodes.Clear();
   string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   AddLive(folderTree.Nodes,L.Chinese?"桌面":"Desktop",desktop,IconKey(desktop,true));
   string docs=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);AddLive(folderTree.Nodes,L.T("Documents"),docs,IconKey(docs,true));
   string downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");if(Directory.Exists(downloads))AddLive(folderTree.Nodes,L.Chinese?"下载":"Downloads",downloads,IconKey(downloads,true));
   string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);AddLive(folderTree.Nodes,Environment.UserName,profile,IconKey(profile,true));
   if(!treeImages.Images.ContainsKey("pc"))treeImages.Images.Add("pc",Art.Icon("computer",SystemInformation.SmallIconSize.Width));
   var pc=new TreeNode(L.T("This PC")){ImageKey="pc",SelectedImageKey="pc"};folderTree.Nodes.Add(pc);
   foreach(var d in DriveInfo.GetDrives()){string name=d.Name;try{if(d.DriveType!=DriveType.Network&&d.IsReady)name=(string.IsNullOrEmpty(d.VolumeLabel)?L.T("Local Disk"):d.VolumeLabel)+" ("+d.Name.TrimEnd('\\')+")";}catch{}string key=IconKey(d.Name,true);AddLive(pc.Nodes,name,d.Name,key);}
   pc.Expand();
  }
  private void AddLive(TreeNodeCollection nodes,string name,string path,string icon){var n=new TreeNode(name){Tag=path,ImageKey=icon,SelectedImageKey=icon};n.Nodes.Add(new TreeNode("…"));nodes.Add(n);}
  private void ExpandLive(TreeNode node){if(node.Nodes.Count!=1||node.Nodes[0].Tag!=null||node.Nodes[0].Text!="…")return;node.Nodes.Clear();string p=node.Tag as string;if(p==null)return;try{foreach(string d in Directory.EnumerateDirectories(p).OrderBy(x=>x))AddLive(node.Nodes,Path.GetFileName(d),d,IconKey(d,false));}catch(Exception ex){node.Nodes.Add("["+ex.Message+"]");}}
  private void PopulateScan(){scanTree.Nodes.Clear();if(Data==null)return;foreach(string root in Data.Roots){Entry e;if(Data.Index.TryGetValue(root,out e)){var n=ScanNode(e);scanTree.Nodes.Add(n);ExpandScan(n);n.Expand();}}}
  private TreeNode ScanNode(Entry e){string key=IconKey(e.Path,false);var n=new TreeNode(e.Name+"  ("+Util.Size(e.Size)+")"){Tag=e,ImageKey=key,SelectedImageKey=key};if(Data.Direct(e.Path).Any(x=>x.Folder))n.Nodes.Add("…");return n;}
  private void ExpandScan(TreeNode n){Entry e=n.Tag as Entry;if(e==null||n.Nodes.Count!=1||n.Nodes[0].Text!="…")return;n.Nodes.Clear();foreach(Entry child in Data.Direct(e.Path).Where(x=>x.Folder).OrderByDescending(x=>x.Size))n.Nodes.Add(ScanNode(child));}
  private void FillDrives(){drives.Rows.Clear();long total=0,free=0;foreach(var d in DriveInfo.GetDrives())try{if(d.DriveType==DriveType.Network)continue;if(!d.IsReady)continue;int i=drives.Rows.Add(d.Name.TrimEnd('\\'),Util.Size(d.TotalSize),Util.Size(d.AvailableFreeSpace),((d.TotalSize-d.AvailableFreeSpace)*100.0/d.TotalSize).ToString("0")+"%");drives.Rows[i].Tag=d.Name;total+=d.TotalSize;free+=d.AvailableFreeSpace;}catch{}
   drives.ClearSelection();if(driveTotals!=null)driveTotals.Text=L.T("TOTALS")+":    "+Util.Size(total)+"    "+Util.Size(free);}
  private void PaintDriveCell(object sender,DataGridViewCellPaintingEventArgs e){
   if(e.RowIndex<0||e.ColumnIndex<0)return;var g=e.Graphics;var b=e.CellBounds;bool sel=(e.State&DataGridViewElementStates.Selected)!=0;
   using(var br=new SolidBrush(sel?Palette.Selection:Palette.Background))g.FillRectangle(br,b);
   if(e.ColumnIndex==0){string root=Convert.ToString(drives.Rows[e.RowIndex].Tag);bool sys=string.Equals(root,Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase);int s=D(16);g.DrawImage(Art.Icon(sys?"sysdisk":"disk",s),b.X+D(4),b.Y+(b.Height-s)/2,s,s);Draw.Text(g,Convert.ToString(e.Value),drives.Font,new Rectangle(b.X+s+D(8),b.Y,b.Width-s-D(8),b.Height),Palette.Text,Draw.Left);}
   else if(e.ColumnIndex==3){double pct;double.TryParse(Convert.ToString(e.Value).TrimEnd('%'),out pct);var r=new Rectangle(b.X+D(3),b.Y+D(4),b.Width-D(8),b.Height-D(8));using(var br=new SolidBrush(Palette.BarTrack))g.FillRectangle(br,r);Color fc=pct>=90?Palette.Blend(Palette.Danger,Color.White,0.45f):Palette.BarFill;using(var br=new SolidBrush(fc))g.FillRectangle(br,r.X,r.Y,(int)(r.Width*Math.Min(100,pct)/100),r.Height);using(var p=new Pen(Palette.Line))g.DrawRectangle(p,r.X,r.Y,r.Width-1,r.Height-1);Draw.Text(g,Convert.ToString(e.Value),drives.Font,r,Palette.Text,Draw.Center);}
   else e.PaintContent(b);
   e.Handled=true;
  }
  private void SetAddress(string text,Image icon){pathBox.Text=text;address.Icon=icon??Art.Icon("computer");address.Invalidate();}
  private static string Memory(){try{return Util.Size((long)new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory);}catch{return "-";}}
  private void ShowComputer(){
   CurrentPath=null;if(computer==null)return;computer.Visible=true;content.Visible=false;SetAddress(L.T("This PC"),null);
   tiles.SuspendLayout();foreach(Control c in tiles.Controls.Cast<Control>().ToArray())c.Dispose();tiles.Controls.Clear();
   string sysRoot=Path.GetPathRoot(Environment.SystemDirectory);
   foreach(DriveInfo drive in DriveInfo.GetDrives())try{if(drive.DriveType==DriveType.Network||!drive.IsReady)continue;var t=new DriveTile{Root=drive.Name,Label=drive.VolumeLabel,Format=drive.DriveFormat,Total=drive.TotalSize,Free=drive.AvailableFreeSpace,System=string.Equals(drive.Name,sysRoot,StringComparison.OrdinalIgnoreCase),Font=Font};t.Activated+=r=>StartScan(new[]{r});tiles.Controls.Add(t);}catch{}
   tiles.ResumeLayout();
   hint.Text=L.T("Select a drive or folder to create a detailed report.");
   info.Set(Art.Icon("computer",D(32)),Environment.MachineName,"["+L.T("No description")+"]",L.T("Domain"),Environment.UserDomainName,L.T("Memory"),Memory(),L.T("Processors"),Environment.ProcessorCount.ToString(),L.T("User"),Environment.UserDomainName+"\\"+Environment.UserName);
   UpdateCommandStates();
  }
  private void Browse(){string p=UI.Folder(CurrentPath??Settings.LastPath);if(p!=null)StartScan(new[]{p});}
  private void MultiplePaths(){using(var f=UI.Dialog("Paths",650,440)){var text=new TextBox{Multiline=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle,Text=Data==null?"":string.Join(Environment.NewLine,Data.Roots)};var label=new Label{Text=L.T("Scan paths (one per line)"),Dock=DockStyle.Top,Height=34,Padding=new Padding(8)};var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(6)};buttons.Controls.Add(UI.Button("Start",()=>{f.DialogResult=DialogResult.OK;f.Close();}));buttons.Controls.Add(UI.Button("Browse",()=>{string p=UI.Folder("");if(p!=null)text.AppendText((text.Text.Length>0?Environment.NewLine:"")+p);}));f.Controls.Add(text);f.Controls.Add(label);f.Controls.Add(buttons);Palette.Apply(f);if(f.ShowDialog(this)==DialogResult.OK)StartScan(text.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries));}}
  public async void StartScan(IEnumerable<string> paths){if(busy){UI.Info(L.T("Scan already running."));return;}string[] roots=paths.ToArray();try{busy=true;Offline=false;scanner=new Scanner();cancel=new CancellationTokenSource();progress.Visible=true;elapsed.Restart();status.Text=L.T("Scanning...");var opt=UI.Clone(Settings.Scan);Snapshot result=await Task.Run(()=>scanner.Run(roots,opt,cancel.Token));if(IsDisposed)return;Data=result;CurrentPath=result.Roots[0];back.Clear();forward.Clear();PopulateScan();Navigate(CurrentPath,false);FillDrives();var hist=DiskHistory.Load();hist.Record();status.Text=L.T(result.Cancelled?"Partial scan":"Scan complete")+"  ·  "+EngineText(result.Method)+"  ·  "+result.ElapsedSeconds.ToString("0.0")+(L.Chinese?" 秒":" s")+(scanner.MftNeedsAdmin?"  ·  "+L.T("Run as administrator to enable NTFS MFT fast scan"):"");}catch(Exception ex){if(!IsDisposed)UI.Error(ex);}finally{busy=false;elapsed.Stop();if(!IsDisposed){progress.Visible=false;if(content.Visible)ShowFolderInfo();else if(CurrentPath==null)ShowComputer();UpdateCounts();UpdateCommandStates();}}}
  private static string EngineText(string method){return L.T(method=="MFT"?"NTFS MFT direct read":method=="Mixed"?"MFT + directory enumeration":"Directory enumeration");}
  private void UpdateProgress(){if(!busy||scanner==null)return;status.Text=L.T(scanner.Pause.IsSet?"Scanning...":"Paused");folderCount.Text=L.F("Folders: {0}",scanner.FolderCount.ToString("N0"));fileCount.Text=L.F("Files: {0}",scanner.FileCount.ToString("N0"));sizeLabel.Text=Util.Size(scanner.ByteCount);timeLabel.Text=elapsed.Elapsed.ToString(@"h\:mm\:ss");
   info.Set(Art.Icon("search",D(32)),scanner.Current,L.T(scanner.Pause.IsSet?"Scanning...":"Paused"),L.T("Files"),scanner.FileCount.ToString("N0"),L.T("Folders"),scanner.FolderCount.ToString("N0"),L.T("Size"),Util.Size(scanner.ByteCount),L.T("Elapsed"),elapsed.Elapsed.ToString(@"h\:mm\:ss"));}
  private void UpdateCounts(){if(Data==null)return;long size=Data.Roots.Sum(p=>Data.Index.ContainsKey(p)?Data.Index[p].Size:0);folderCount.Text=L.F("Folders: {0}",(Data.Entries.LongCount(e=>e.Folder)-Data.Roots.Count).ToString("N0"));fileCount.Text=L.F("Files: {0}",Data.Entries.LongCount(e=>!e.Folder).ToString("N0"));sizeLabel.Text=Util.Size(size);timeLabel.Text=TimeSpan.FromSeconds(Data.ElapsedSeconds).ToString(@"h\:mm\:ss");timeLabel.ToolTipText=L.F("{0} files · {1} folders · {2} · {3:0.0}s · {4} events",Data.Entries.LongCount(e=>!e.Folder).ToString("N0"),(Data.Entries.LongCount(e=>e.Folder)-Data.Roots.Count).ToString("N0"),Util.Size(size),Data.ElapsedSeconds,Data.Events.Count);UpdateFilter();}
  private void UpdateFilter(){bool on=Settings.Scan.Include!="*"||Settings.Scan.Exclude!=""||Settings.Scan.ExcludeFolders!=""||Settings.Scan.Rules.Count>0||Settings.Scan.MinimumSize>0||Settings.Scan.MaximumSize>0||Settings.Scan.SkipHidden||Settings.Scan.SkipSystem;filter.Text=L.T(on?"Filter: On":"Filter: Off");}
  public void Navigate(string path,bool remember){
   if(Data==null||!Data.Index.ContainsKey(path))return;if(remember&&CurrentPath!=null&&CurrentPath!=path){back.Push(CurrentPath);forward.Clear();}
   CurrentPath=path;Entry root=Data.Index[path];var rows=Data.Direct(path).OrderByDescending(e=>e.Size).Select(Reports.Row).ToList();foreach(var r in rows)r.Percent=root.Size>0?r.Size*100.0/root.Size:0;
   details.SetData(rows);computer.Visible=false;content.Visible=true;SetAddress(path,Offline?Art.Type(null,true):Art.Shell(path));
   foreach(var c in charts){c.Data=Data;c.Folder=path;c.Invalidate();}
   status.Text=ShowFolderInfo();UpdateHint();UpdateCounts();UpdateCommandStates();
  }
  private string ShowFolderInfo(){
   Entry root;if(Data==null||CurrentPath==null||!Data.Index.TryGetValue(CurrentPath,out root))return "";
   string state=L.T(Offline?"Offline snapshot":Data.Cancelled||root.Incomplete?"Partial scan":"Scan complete");
   info.Set(Art.Icon("folder",D(32)),CurrentPath,state,L.T("Size"),Util.Size(root.Size),L.T("Size on Disk"),Util.Size(root.Allocated),L.T("Files"),root.Files.ToString("N0"),L.T("Folders"),root.Folders.ToString("N0"),L.T("Modified"),Util.Date(root.Modified),L.T("Captured"),Util.Date(Data.Captured));
   return state;
  }
  private void UpdateHint(){if(hint==null||CurrentPath==null)return;hint.Text=Offline?L.T("Offline snapshot")+"  ·  ":"";hint.Text+=L.T(content.SelectedIndex<=0?"Double-click a folder to explore. Right-click for file operations.":"Double-click a chart region to explore its folder.");}
  private void OpenPath(string p){if(Data!=null&&Data.Index.ContainsKey(p))Navigate(p,true);else StartScan(new[]{p});}
  private void GoBack(){if(back.Count==0)return;if(CurrentPath!=null)forward.Push(CurrentPath);Navigate(back.Pop(),false);}
  private void GoForward(){if(forward.Count==0)return;if(CurrentPath!=null)back.Push(CurrentPath);Navigate(forward.Pop(),false);}
  private void GoUp(){if(CurrentPath==null)return;Entry e;if(Data!=null&&Data.Index.TryGetValue(CurrentPath,out e)&&!string.IsNullOrEmpty(e.Parent))Navigate(e.Parent,true);else {string p=Path.GetDirectoryName(CurrentPath.TrimEnd('\\'));if(!string.IsNullOrEmpty(p))OpenPath(p);else ShowComputer();}}
  public void RefreshScan(){if(Offline||Data==null){if(CurrentPath!=null)StartScan(new[]{CurrentPath});return;}StartScan(Data.Roots.ToArray());}
  public bool RequireData(){if(Data!=null)return true;UI.Info(L.T("No scan data. Choose a folder and scan first."));return false;}
  private void SaveSnapshot(){if(!RequireData())return;using(var d=new SaveFileDialog{Filter="FolderScope snapshot (*.fsscan)|*.fsscan",FileName="Scan-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),OverwritePrompt=true})if(d.ShowDialog(this)==DialogResult.OK){Snapshot.Save(Data,d.FileName);status.Text=L.F("Saved: {0}",d.FileName);}}
  private void LoadSnapshot(){using(var d=new OpenFileDialog{Filter="FolderScope snapshot (*.fsscan)|*.fsscan|XML (*.xml)|*.xml"})if(d.ShowDialog(this)==DialogResult.OK)try{if(busy)throw new InvalidOperationException(L.T("Scan already running."));Data=Snapshot.Load(d.FileName);Offline=true;PopulateScan();Navigate(Data.Roots.First(),false);status.Text=L.F("Loaded: {0}",d.FileName);}catch(Exception ex){UI.Error(ex);}}
  private void CompareSnapshot(){if(!RequireData())return;using(var d=new OpenFileDialog{Filter="FolderScope snapshot (*.fsscan)|*.fsscan"})if(d.ShowDialog(this)==DialogResult.OK)try{Snapshot older=Snapshot.Load(d.FileName);Dialogs.Rows(this,"Comparison",Reports.Compare(older,Data));}catch(Exception ex){UI.Error(ex);}}
  private void ExportDetails(){if(RequireData())UI.Export(this,L.T("Folder details"),details.Data);}
  private void ExportChart(){if(!RequireData())return;SizeChart c=charts[Math.Max(0,content.SelectedIndex-1)];using(var d=new SaveFileDialog{Filter="PNG (*.png)|*.png",FileName="FolderScope-chart.png"})if(d.ShowDialog(this)==DialogResult.OK){int w=Math.Max(400,c.Width),h=Math.Max(300,c.Height);bool hidden=!c.Visible;if(hidden){c.Size=new Size(w,h);}using(var bmp=new Bitmap(w,h)){c.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(d.FileName,System.Drawing.Imaging.ImageFormat.Png);}status.Text=L.F("Saved: {0}",d.FileName);}}
  private List<ReportRow> DriveRows(){var rows=new List<ReportRow>();foreach(var d in DriveInfo.GetDrives())try{if(d.DriveType!=DriveType.Network&&d.IsReady)rows.Add(new ReportRow{Name=d.Name,Size=d.TotalSize,Allocated=d.TotalSize-d.AvailableFreeSpace,Percent=(d.TotalSize-d.AvailableFreeSpace)*100.0/d.TotalSize,Path=d.Name,Extra=d.DriveFormat+" · "+L.T("Free")+": "+Util.Size(d.AvailableFreeSpace)});}catch{}return rows;}
  private void ExportDrives(){UI.Export(this,L.T("Drive Space"),DriveRows());}
  private void Print(){if(RequireData())UI.Print(this,L.T("Folder details"),details.Data);}
  public void OpenReports(string report){using(var f=new FileReportForm(this,report))f.ShowDialog(this);}
  private void ShowDiskReporter(){Dialogs.Rows(this,"Disk Reporter",DriveRows());}
  private void ShowSearch(){using(var f=new SearchForm(this,false))f.ShowDialog(this);}
  private void ShowFilterZip(){using(var f=new SearchForm(this,true))f.ShowDialog(this);}
  private void ShowFilter(){using(var f=new OptionsForm(Settings,true))if(f.ShowDialog(this)==DialogResult.OK){Settings=f.Result;Settings.Save();UpdateFilter();UI.Info(L.T("New scan required for changed filters."));}}
  private void ShowOptions(){using(var f=new OptionsForm(Settings,false))if(f.ShowDialog(this)==DialogResult.OK){Settings=f.Result;Settings.Save();L.Chinese=Settings.Language!="en-US";Palette.Set(Settings.Theme);ToolStripManager.Renderer=new ModernRenderer();Build();}}
  private void ShowEvents(){if(!RequireData())return;Dialogs.Rows(this,"Event Log",Data.Events.Select(e=>new ReportRow{Name=L.ErrorText(L.T(e.Message)),Path=e.Path,Modified=e.Time,Extra=L.ErrorText(L.T(e.Message))}).ToList());}
  private void ShowTrends(){Dialogs.Trends(this);}
  private void ShowHistory(){Dialogs.History(this);}
  private void ShowScheduler(){using(var f=new ScheduleForm(Settings,CurrentPath??""))f.ShowDialog(this);}
  private void ShowNetwork(){Dialogs.Network(this,p=>StartScan(new[]{p}));}
  private async void SendMail(){if(!RequireData())return;if(!UI.Confirm(L.F("Send this report to {0}?",Settings.MailTo)))return;string file=Path.Combine(Util.DataDir,"mail-report.html");try{Exporter.Save(file,L.T("Folder details"),details.Data);await Task.Run(()=>Jobs.Send(Settings,"FolderScope storage report",file));UI.Info(L.T("Email sent."));}catch(Exception ex){UI.Error(ex);}}
  private void ShowHelp(){string file=Path.Combine(Application.StartupPath,L.Chinese?"使用说明.html":"UserGuide.html");if(File.Exists(file))UI.Shell(file);else ShowAbout();}
  private void ShowAbout(){UI.Info("FolderScope 1.0\n"+L.T("Independent personal storage analyzer")+"\n\n"+L.T("Chinese / English can be switched at any time.")+"\n"+L.T("Snapshot files use FolderScope's own format."));}
  private void ShowWelcome(){
   using(var f=UI.Dialog("Welcome to FolderScope",640,540)){
    f.FormBorderStyle=FormBorderStyle.FixedDialog;f.MaximizeBox=false;f.MinimizeBox=false;f.MinimumSize=Size.Empty;
    var header=new Panel{Dock=DockStyle.Top,Height=86,BackColor=Palette.Accent,Tag="keep"};
    header.Paint+=(s,e)=>{var g=e.Graphics;int s0=D(44);g.DrawImage(Art.Icon("logo-white",s0),D(20),(header.Height-s0)/2,s0,s0);int x=D(20)+s0+D(14);using(var big=new Font("Segoe UI Light",16f))Draw.Text(g,L.T("Welcome to FolderScope"),big,new Rectangle(x,header.Height/2-D(30),header.Width-x-D(10),D(34)),Color.White,Draw.Left);Draw.Text(g,L.T("Independent personal storage analyzer"),f.Font,new Rectangle(x,header.Height/2+D(5),header.Width-x-D(10),D(22)),Color.FromArgb(225,255,255,255),Draw.Left);};
    var body=new Panel{Dock=DockStyle.Fill,Tag="keep",BackColor=Palette.Panel};
    var sec1=new Label{Text=L.T("Analyze Disk Space"),Font=new Font("Segoe UI Semibold",9.5f),AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};
    var sec2=new Label{Text=L.T("File Reports"),Font=new Font("Segoe UI Semibold",9.5f),AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};
    Action<Action> go=a=>{f.Close();a();};
    var t=new List<TileButton>{
     new TileButton("Analyze my system drive","Scan the Windows drive to see what uses space","sysdisk",()=>go(()=>StartScan(new[]{Path.GetPathRoot(Environment.SystemDirectory)}))),
     new TileButton("Choose a folder...","Pick any folder or drive to analyze","browse",()=>go(Browse)),
     new TileButton("Find my largest files","Locate the files that use the most space","report",()=>go(()=>OpenReports("Largest Files"))),
     new TileButton("Find duplicate files","Find identical copies that waste space","copy",()=>go(()=>OpenReports("Duplicate Files"))),
     new TileButton("Show file type distribution","See space usage grouped by file type","pie",()=>go(()=>OpenReports("File Types"))),
     new TileButton("Build a custom search","Combine rules to find exactly what you need","search",()=>go(ShowSearch))};
    foreach(var tile in t){tile.Font=f.Font;body.Controls.Add(tile);}body.Controls.Add(sec1);body.Controls.Add(sec2);
    body.Layout+=(s,e)=>{int pad=D(20),gap=D(10),w=(body.ClientSize.Width-pad*2-gap)/2,h=D(62),y=D(10);
     sec1.SetBounds(pad,y,body.ClientSize.Width-pad*2,D(28));y+=D(30);
     t[0].SetBounds(pad,y,w,h);t[1].SetBounds(pad+w+gap,y,w,h);y+=h+D(12);
     sec2.SetBounds(pad,y,body.ClientSize.Width-pad*2,D(28));y+=D(30);
     for(int i=2;i<6;i++){t[i].SetBounds(pad+((i-2)%2)*(w+gap),y+((i-2)/2)*(h+gap),w,h);}};
    var footer=new Panel{Dock=DockStyle.Bottom,Height=46,Tag="keep",BackColor=Palette.Panel};
    var welcome=new CheckBox{Text=L.T("Show welcome at startup"),Checked=Settings.Welcome,AutoSize=true};welcome.CheckedChanged+=(s,e)=>{Settings.Welcome=welcome.Checked;Settings.Save();};
    var note=new Label{Text=L.T("Chinese / English can be switched at any time."),AutoSize=true};
    footer.Controls.Add(welcome);footer.Controls.Add(note);footer.Layout+=(s,e)=>{welcome.Location=new Point(D(20),(footer.Height-welcome.Height)/2);note.Location=new Point(footer.Width-note.Width-D(20),(footer.Height-note.Height)/2);};
    footer.Paint+=(s,e)=>{using(var p=new Pen(Palette.Line))e.Graphics.DrawLine(p,0,0,footer.Width,0);};
    f.Controls.Add(body);f.Controls.Add(footer);f.Controls.Add(header);Palette.Apply(f);note.ForeColor=Palette.SubText;f.ShowDialog(this);
   }
  }
  private void Shortcut(object s,KeyEventArgs e){if(e.KeyCode==Keys.F5){RefreshScan();e.Handled=true;}if(e.KeyCode==Keys.F1){ShowHelp();e.Handled=true;}if(e.Control&&e.KeyCode==Keys.F){ShowSearch();e.Handled=true;}if(e.Control&&e.KeyCode==Keys.O){LoadSnapshot();e.Handled=true;}if(e.Control&&e.KeyCode==Keys.S){SaveSnapshot();e.Handled=true;}if(e.Alt&&e.KeyCode==Keys.Left){GoBack();e.Handled=true;}if(e.Alt&&e.KeyCode==Keys.Up){GoUp();e.Handled=true;}if(e.KeyCode==Keys.Escape&&busy){cancel.Cancel();scanner.Pause.Set();}}
 }
}
