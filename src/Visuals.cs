using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FolderScope {
 /// <summary>Marker for owner-drawn controls that read the palette at paint time (Palette.Apply leaves their colors alone).</summary>
 public interface IThemed {}

 public static class Palette {
  public static bool Dark=false;
  public static Color Accent=Color.FromArgb(43,121,215);
  static Color C(int r,int g,int b){return Color.FromArgb(r,g,b);}
  public static Color Background {get{return Dark?C(32,32,32):Color.White;}}
  /// <summary>Office-style window backdrop (tab row, navigation bar, dock headers, dialogs).</summary>
  public static Color Panel {get{return Dark?C(45,45,45):C(243,243,243);}}
  public static Color Text {get{return Dark?C(232,232,232):C(38,38,38);}}
  public static Color SubText {get{return Dark?C(160,160,160):C(110,110,110);}}
  public static Color Line {get{return Dark?C(72,72,72):C(212,212,212);}}
  public static Color LightLine {get{return Dark?C(56,56,56):C(234,234,234);}}
  public static Color Hover {get{return Dark?C(62,62,62):Blend(Accent,Color.White,0.87f);}}
  public static Color HoverBorder {get{return Dark?C(96,96,96):Blend(Accent,Color.White,0.6f);}}
  public static Color Pressed {get{return Dark?C(78,78,78):Blend(Accent,Color.White,0.74f);}}
  public static Color Selection {get{return Dark?Blend(Accent,C(32,32,32),0.55f):Blend(Accent,Color.White,0.8f);}}
  public static Color RowHover {get{return Dark?C(44,44,44):Blend(Accent,Color.White,0.93f);}}
  public static Color Link {get{return Dark?Blend(Accent,Color.White,0.45f):Accent;}}
  public static Color BarFill {get{return Dark?Blend(Accent,C(32,32,32),0.35f):Blend(Accent,Color.White,0.62f);}}
  public static Color BarTrack {get{return Dark?C(52,52,52):C(242,242,242);}}
  public static readonly Color Good=C(16,124,16),Danger=C(196,43,28),Warn=C(202,80,16);
  public static readonly Color[] Series={C(68,114,196),C(237,125,49),C(112,173,71),C(255,192,0),C(91,155,213),C(165,105,189),C(38,166,154),C(232,93,117),C(140,140,140),C(158,72,14),C(99,99,205),C(67,104,43)};
  public static Color Blend(Color a,Color b,float t){t=Math.Max(0,Math.Min(1,t));return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
  public static double Luma(Color c){return (0.299*c.R+0.587*c.G+0.114*c.B)/255;}
  public static void Set(string theme){
   theme=theme??"";Dark=theme.StartsWith("Dark");
   Accent=theme.Contains("Red")?C(184,52,48):theme.Contains("Green")?C(33,115,70):theme.Contains("Orange")?C(202,80,16):theme.Contains("Purple")?C(128,57,123):C(43,121,215);
   Art.ClearCache();
  }
  public static void Apply(Control c){Apply(c,false);}
  private static void Apply(Control c,bool surface){
   surface|=c is TabPage;
   if(!(c is IThemed)&&!(c.Tag is string&&(string)c.Tag=="keep")){
    bool list=c is TextBoxBase||c is TreeView||c is ListView||c is DataGridView||c is ListBox||c is ComboBox||c is UpDownBase;
    c.BackColor=list||surface?Background:Panel;c.ForeColor=Text;
    var tab=c as TabPage;if(tab!=null){tab.UseVisualStyleBackColor=false;tab.BackColor=Background;}
   }
   var g=c as DataGridView;if(g!=null)UI.StyleGrid(g);
   var t=c as TreeView;if(t!=null)UI.StyleTree(t);
   var s=c as ToolStrip;if(s!=null){s.ImageScalingSize=new Size(UI.D(16),UI.D(16));}
   c.Invalidate();
   foreach(Control child in c.Controls)Apply(child,surface);
  }
 }

 public static class Draw {
  public static GraphicsPath Round(RectangleF r,float radius){
   var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
   if(d<1){p.AddRectangle(r);return p;}
   p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
  }
  public static void Fill(Graphics g,RectangleF r,float radius,Color c){if(r.Width<=0||r.Height<=0)return;var old=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Round(r,radius))using(var b=new SolidBrush(c))g.FillPath(b,p);g.SmoothingMode=old;}
  public static void Stroke(Graphics g,RectangleF r,float radius,Color c,float width=1){if(r.Width<=0||r.Height<=0)return;var old=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Round(r,radius))using(var pen=new Pen(c,width))g.DrawPath(pen,p);g.SmoothingMode=old;}
  public static void Box(Graphics g,Rectangle r,Color fill,Color border){using(var b=new SolidBrush(fill))g.FillRectangle(b,r);using(var p=new Pen(border))g.DrawRectangle(p,r.X,r.Y,r.Width-1,r.Height-1);}
  public static void Text(Graphics g,string s,Font f,Rectangle r,Color c,TextFormatFlags flags){TextRenderer.DrawText(g,s,f,r,c,flags|TextFormatFlags.NoPrefix);}
  public const TextFormatFlags Left=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
  public const TextFormatFlags Center=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
  public const TextFormatFlags Right=TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
 }

 /// <summary>Icons: Windows system icons (imageres / shell32), shell file icons and a few hand-drawn chart icons. Fluent glyphs are used for chrome.</summary>
 public static class Art {
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint PrivateExtractIcons(string file,int index,int cx,int cy,IntPtr[] icons,int[] ids,uint count,uint flags);
  [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct SHFILEINFO {public IntPtr hIcon;public int iIcon;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type;}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,ref SHFILEINFO info,uint size,uint flags);
  private static Dictionary<string,Image> cache=new Dictionary<string,Image>();
  private static string glyphFont;
  private static readonly Dictionary<string,string> System_=new Dictionary<string,string>{
   {"folder","imageres.dll,3"},{"load","imageres.dll,244"},{"browse","imageres.dll,205"},{"save","shell32.dll,258"},{"disk","imageres.dll,30"},{"sysdisk","imageres.dll,31"},{"computer","imageres.dll,104"},
   {"search","imageres.dll,168"},{"trend","imageres.dll,144"},{"clock","shell32.dll,249"},{"history","shell32.dll,239"},{"settings","shell32.dll,314"},{"print","imageres.dll,71"},{"refresh","imageres.dll,229"},
   {"event","shell32.dll,65"},{"help","imageres.dll,94"},{"info","imageres.dll,76"},{"pin","imageres.dll,234"},{"compare","imageres.dll,262"},{"delete","shell32.dll,131"},{"zip","imageres.dll,165"},
   {"panel","imageres.dll,299"},{"export","shell32.dll,264"},{"file","imageres.dll,2"},{"copy","shell32.dll,134"},{"rename","imageres.dll,247"},{"mail","imageres.dll,15"},{"admin","imageres.dll,73"},
   {"network","imageres.dll,141"},{"clipboard","shell32.dll,260"},{"columns","imageres.dll,306"},{"details","imageres.dll,75"},{"map","imageres.dll,249"},{"user","imageres.dll,209"},{"camera","imageres.dll,52"},
   {"layout","shell32.dll,251"},{"move","imageres.dll,166"},{"warning","imageres.dll,79"},{"desktop","imageres.dll,105"},{"open","imageres.dll,13"},{"properties","imageres.dll,82"}
  };
  public static void ClearCache(){cache.Clear();}
  public static string GlyphFont {get{if(glyphFont==null){glyphFont="Segoe MDL2 Assets";try{using(var f=new Font("Segoe Fluent Icons",10))if(f.Name=="Segoe Fluent Icons")glyphFont=f.Name;}catch{}}return glyphFont;}}
  public static void Glyph(Graphics g,string glyph,Rectangle r,float px,Color color){using(var f=new Font(GlyphFont,px,GraphicsUnit.Pixel))TextRenderer.DrawText(g,glyph,f,r,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);}
  public static Image GlyphImage(string glyph,int px,Color color){
   string key="g|"+glyph+"|"+px+"|"+color.ToArgb();Image img;if(cache.TryGetValue(key,out img))return img;
   var b=new Bitmap(px,px);using(var g=Graphics.FromImage(b))using(var f=new Font(GlyphFont,px*0.82f,GraphicsUnit.Pixel))using(var br=new SolidBrush(color)){g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;var sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(glyph,f,br,new RectangleF(0,0,px,px+px*0.04f),sf);}
   cache[key]=b;return b;
  }
  public static Image Icon(string kind){return Icon(kind,UI.D(16));}
  public static Image Icon(string kind,int px){
   string key=kind+"|"+px;Image img;if(cache.TryGetValue(key,out img))return img;
   img=Drawn(kind,px);
   string source;if(img==null&&System_.TryGetValue(kind,out source)){var p=source.Split(',');img=Extract(p[0],int.Parse(p[1]),px);}
   if(img==null)img=GlyphImage("\uE8A5",px,Palette.SubText);
   cache[key]=img;return img;
  }
  public static Image Extract(string dll,int index,int px){
   try{var h=new IntPtr[1];var id=new int[1];string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),dll);
    if(PrivateExtractIcons(path,index,px,px,h,id,1,0)==0||h[0]==IntPtr.Zero)return null;
    try{using(var ic=System.Drawing.Icon.FromHandle(h[0]))return ic.ToBitmap();}finally{DestroyIcon(h[0]);}
   }catch{return null;}
  }
  /// <summary>Real shell icon for a path (special folders, drives). Small = system small-icon size.</summary>
  public static Image Shell(string path){
   string key="sp|"+path;Image img;if(cache.TryGetValue(key,out img))return img;
   img=null;try{var info=new SHFILEINFO();if(SHGetFileInfo(path,0,ref info,(uint)Marshal.SizeOf(info),0x100|0x1)!=IntPtr.Zero&&info.hIcon!=IntPtr.Zero){using(var ic=System.Drawing.Icon.FromHandle(info.hIcon))img=ic.ToBitmap();DestroyIcon(info.hIcon);}}catch{}
   if(img==null)img=Icon("folder",SystemInformation.SmallIconSize.Width);
   cache[key]=img;return img;
  }
  /// <summary>Generic shell icon for a file extension or folder (no disk access).</summary>
  public static Image Type(string extension,bool folder){
   string ext=folder?"<folder>":(extension??"").ToLowerInvariant();
   if(!folder&&(ext==".exe"||ext==".ico"||ext==".lnk"||ext==".url"))ext=ext+"#";
   string key="st|"+ext;Image img;if(cache.TryGetValue(key,out img))return img;
   img=null;try{var info=new SHFILEINFO();string name=folder?"folder":"file"+(extension??"");if(SHGetFileInfo(name,folder?0x10u:0x80u,ref info,(uint)Marshal.SizeOf(info),0x100|0x1|0x10)!=IntPtr.Zero&&info.hIcon!=IntPtr.Zero){using(var ic=System.Drawing.Icon.FromHandle(info.hIcon))img=ic.ToBitmap();DestroyIcon(info.hIcon);}}catch{}
   if(img==null)img=Icon(folder?"folder":"file",SystemInformation.SmallIconSize.Width);
   cache[key]=img;return img;
  }
  public static Image For(Entry e){if(e==null)return null;return Type(e.Extension,e.Folder);}
  private static Bitmap Drawn(string kind,int px){
   Bitmap b=null;Graphics g=null;
   Func<Graphics> start=()=>{b=new Bitmap(px,px);g=Graphics.FromImage(b);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;return g;};
   float s=px/32f;Func<float,float> k=v=>v*s;
   switch(kind){
    case "play":case "stop":case "pause":{
     start();Color c=kind=="play"?Color.FromArgb(46,160,67):kind=="stop"?Color.FromArgb(214,57,48):Color.FromArgb(232,160,20);
     var r=new RectangleF(k(1.5f),k(1.5f),px-k(3),px-k(3));
     using(var br=new LinearGradientBrush(r,Palette.Blend(c,Color.White,0.25f),c,90))g.FillEllipse(br,r);
     using(var pen=new Pen(Palette.Blend(c,Color.Black,0.2f),Math.Max(1,k(1))))g.DrawEllipse(pen,r);
     if(kind=="play")g.FillPolygon(Brushes.White,new[]{new PointF(k(12.5f),k(9)),new PointF(k(23.5f),k(16)),new PointF(k(12.5f),k(23))});
     else if(kind=="stop")Draw.Fill(g,new RectangleF(k(10.5f),k(10.5f),k(11),k(11)),k(1.5f),Color.White);
     else{g.FillRectangle(Brushes.White,k(11),k(10),k(3.5f),k(12));g.FillRectangle(Brushes.White,k(17.5f),k(10),k(3.5f),k(12));}
     break;}
    case "filter":{
     start();var pts=new[]{new PointF(k(3),k(5)),new PointF(k(29),k(5)),new PointF(k(19),k(17)),new PointF(k(19),k(28)),new PointF(k(13),k(24)),new PointF(k(13),k(17))};
     using(var br=new LinearGradientBrush(new RectangleF(0,0,px,px),Color.FromArgb(190,198,206),Color.FromArgb(118,128,140),90))g.FillPolygon(br,pts);
     using(var pen=new Pen(Color.FromArgb(88,98,110),Math.Max(1,k(1.2f))))g.DrawPolygon(pen,pts);
     using(var pen=new Pen(Color.FromArgb(150,255,255,255),Math.Max(1,k(1.2f))))g.DrawLine(pen,k(6.5f),k(7.5f),k(25.5f),k(7.5f));
     break;}
    case "pie":{
     start();var r=new RectangleF(k(2),k(2),px-k(4),px-k(4));
     using(var br=new SolidBrush(Palette.Series[0]))g.FillPie(br,r.X,r.Y,r.Width,r.Height,-90,230);
     using(var br=new SolidBrush(Palette.Series[1]))g.FillPie(br,r.X,r.Y,r.Width,r.Height,140,80);
     using(var br=new SolidBrush(Palette.Series[2]))g.FillPie(br,r.X+k(1.5f),r.Y-k(1.5f),r.Width,r.Height,220,50);
     break;}
    case "sunburst":{
     start();var r=new RectangleF(k(2),k(2),px-k(4),px-k(4));
     float[] a={-90,140,250};Color[] c={Palette.Series[0],Palette.Series[1],Palette.Series[2]};
     for(int i=0;i<3;i++){float sw=(i<2?a[i+1]:270)-a[i];using(var br=new SolidBrush(Palette.Blend(c[i],Color.White,0.25f)))g.FillPie(br,r.X,r.Y,r.Width,r.Height,a[i],sw);}
     var m=r;m.Inflate(-k(6),-k(6));for(int i=0;i<3;i++){float sw=(i<2?a[i+1]:270)-a[i];using(var br=new SolidBrush(c[i]))g.FillPie(br,m.X,m.Y,m.Width,m.Height,a[i],sw);}
     using(var pen=new Pen(Color.White,Math.Max(1,k(1.2f)))){g.DrawEllipse(pen,m);}
     var h=r;h.Inflate(-k(11),-k(11));g.FillEllipse(Brushes.White,h);
     break;}
    case "chart":case "report":{
     start();
     if(kind=="report"){var doc=Extract("imageres.dll",2,px);if(doc!=null)g.DrawImage(doc,0,0,px,px);}
     float bx=kind=="report"?k(9):k(4),bw=kind=="report"?k(4):k(6),gap=kind=="report"?k(1.5f):k(2.5f),bottom=kind=="report"?k(26):k(28);
     float[] hts=kind=="report"?new[]{k(6),k(11),k(8)}:new[]{k(10),k(20),k(14)};
     for(int i=0;i<3;i++){var r=new RectangleF(bx+i*(bw+gap),bottom-hts[i],bw,hts[i]);using(var br=new LinearGradientBrush(new RectangleF(r.X,r.Y-1,r.Width,r.Height+2),Palette.Blend(Palette.Series[i],Color.White,0.3f),Palette.Series[i],90))g.FillRectangle(br,r);}
     if(kind=="chart")using(var pen=new Pen(Color.FromArgb(110,110,110),Math.Max(1,k(1.2f))))g.DrawLine(pen,k(2),k(28.5f),k(30),k(28.5f));
     break;}
    case "logo":{
     start();var r=new RectangleF(0,0,px,px);
     using(var p=Draw.Round(new RectangleF(k(1),k(1),px-k(2),px-k(2)),k(7)))using(var br=new LinearGradientBrush(r,Color.FromArgb(70,150,235),Color.FromArgb(28,96,190),60))g.FillPath(br,p);
     float[] hts={k(8),k(15),k(11)};Color[] cs={Color.FromArgb(255,214,102),Color.White,Color.FromArgb(160,226,140)};
     for(int i=0;i<3;i++)Draw.Fill(g,new RectangleF(k(8)+i*k(6),k(24)-hts[i],k(4.5f),hts[i]),k(1),cs[i]);
     break;}
    case "logo-white":{
     start();Draw.Fill(g,new RectangleF(k(2),k(2),px-k(4),px-k(4)),k(6),Color.White);
     float[] hts={k(8),k(15),k(11)};for(int i=0;i<3;i++)Draw.Fill(g,new RectangleF(k(8.5f)+i*k(6),k(23.5f)-hts[i],k(4.5f),hts[i]),k(1),Palette.Accent);
     break;}
    case "check":{
     start();var r=new RectangleF(k(1),k(1),px-k(2),px-k(2));using(var br=new SolidBrush(Color.FromArgb(46,160,67)))g.FillEllipse(br,r);using(var pen=new Pen(Color.White,Math.Max(1.5f,k(3.5f))){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})g.DrawLines(pen,new[]{new PointF(k(8.5f),k(16.5f)),new PointF(k(13.5f),k(21.5f)),new PointF(k(23.5f),k(10.5f))});
     break;}
   }
   if(g!=null)g.Dispose();return b;
  }
  public static Icon AppIcon(){try{var b=(Bitmap)Icon("logo",64);return System.Drawing.Icon.FromHandle(b.GetHicon());}catch{return SystemIcons.Application;}}
 }

 public class ModernColors:ProfessionalColorTable {
  public override Color ToolStripDropDownBackground {get{return Palette.Background;}}
  public override Color ImageMarginGradientBegin {get{return Palette.Background;}}
  public override Color ImageMarginGradientMiddle {get{return Palette.Background;}}
  public override Color ImageMarginGradientEnd {get{return Palette.Background;}}
  public override Color MenuBorder {get{return Palette.Line;}}
  public override Color MenuItemBorder {get{return Palette.HoverBorder;}}
  public override Color MenuItemSelected {get{return Palette.Hover;}}
  public override Color SeparatorDark {get{return Palette.Line;}}
  public override Color SeparatorLight {get{return Palette.Background;}}
  public override Color StatusStripGradientBegin {get{return Palette.Panel;}}
  public override Color StatusStripGradientEnd {get{return Palette.Panel;}}
 }

 /// <summary>Flat Office-like renderer for every ToolStrip / menu / status bar in the app.</summary>
 public class ModernRenderer:ToolStripProfessionalRenderer {
  public ModernRenderer():base(new ModernColors()){RoundedEdges=false;}
  static string Tag(ToolStripItem i){return i.Tag as string;}
  protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){
   using(var b=new SolidBrush(e.ToolStrip is ToolStripDropDown?Palette.Background:e.ToolStrip.BackColor))e.Graphics.FillRectangle(b,e.AffectedBounds);
  }
  protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){
   var ts=e.ToolStrip;
   using(var p=new Pen(Palette.Line)){
    if(ts is ToolStripDropDown){e.Graphics.DrawRectangle(p,0,0,ts.Width-1,ts.Height-1);return;}
    if(ts is StatusStrip){e.Graphics.DrawLine(p,0,0,ts.Width,0);return;}
    if(ts.Tag is string&&(string)ts.Tag=="noline")return;
    if(ts.Dock==DockStyle.Top)e.Graphics.DrawLine(p,0,ts.Height-1,ts.Width,ts.Height-1);
   }
  }
  void Button(Graphics g,ToolStripItem item,bool isChecked){
   var r=new Rectangle(0,0,item.Width-1,item.Height-1);string tag=Tag(item);
   if(tag=="tab"){
    if(isChecked){using(var b=new SolidBrush(Palette.Background))g.FillRectangle(b,0,0,item.Width,item.Height);using(var p=new Pen(Palette.Line)){g.DrawLine(p,0,0,0,item.Height);g.DrawLine(p,0,0,item.Width-1,0);g.DrawLine(p,item.Width-1,0,item.Width-1,item.Height);}}
    else if(item.Selected)using(var b=new SolidBrush(Palette.Blend(Palette.Panel,Palette.Text,0.06f)))g.FillRectangle(b,0,0,item.Width,item.Height);
    return;
   }
   if(tag=="file"){using(var b=new SolidBrush(item.Pressed||item.Selected?Palette.Blend(Palette.Accent,Color.White,0.12f):Palette.Accent))g.FillRectangle(b,0,0,item.Width,item.Height);return;}
   if(tag=="link")return;
   if(tag=="primary"){Color c=item.Pressed?Palette.Blend(Palette.Accent,Color.Black,0.15f):item.Selected?Palette.Blend(Palette.Accent,Color.White,0.12f):Palette.Accent;Draw.Fill(g,new RectangleF(1,2,item.Width-2,item.Height-4),UI.D(2),c);return;}
   if(!item.Enabled)return;
   if(item.Pressed)Draw.Box(g,r,Palette.Pressed,Palette.HoverBorder);
   else if(item.Selected)Draw.Box(g,r,Palette.Hover,Palette.HoverBorder);
   else if(isChecked)Draw.Box(g,r,Palette.Pressed,Palette.HoverBorder);
  }
  protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e){var b=e.Item as ToolStripButton;Button(e.Graphics,e.Item,b!=null&&b.Checked);}
  protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e){Button(e.Graphics,e.Item,false);}
  protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e){Button(e.Graphics,e.Item,false);var sb=e.Item as ToolStripSplitButton;if(sb!=null)DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics,sb,sb.DropDownButtonBounds,Palette.SubText,ArrowDirection.Down));}
  protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
   if(!e.Item.IsOnDropDown){Button(e.Graphics,e.Item,false);return;}
   if(e.Item.Selected&&e.Item.Enabled){var r=new Rectangle(UI.D(3),0,e.Item.Width-UI.D(6),e.Item.Height);Draw.Box(e.Graphics,r,Palette.Hover,Palette.HoverBorder);}
  }
  protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){
   string tag=Tag(e.Item);
   if(!e.Item.Enabled)e.TextColor=Palette.Blend(Palette.SubText,Palette.Panel,0.35f);
   else if(tag=="file"||tag=="primary"||tag=="title")e.TextColor=Color.White;
   else if(tag=="tab"){var b=e.Item as ToolStripButton;e.TextColor=b!=null&&b.Checked?Palette.Link:Palette.Text;}
   else if(tag=="link"){e.TextColor=Palette.Link;if(e.Item.Selected){e.TextFont=new Font(e.TextFont,FontStyle.Underline);}}
   else if(tag=="sub")e.TextColor=Palette.SubText;
   else e.TextColor=Palette.Text;
   base.OnRenderItemText(e);
  }
  protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){string tag=e.Item==null?null:Tag(e.Item);e.ArrowColor=tag=="file"||tag=="title"?Color.White:Palette.SubText;base.OnRenderArrow(e);}
  protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){
   using(var p=new Pen(Palette.Line)){var s=e.Item.Size;
    if(e.Vertical){int x=s.Width/2;e.Graphics.DrawLine(p,x,UI.D(4),x,s.Height-UI.D(4));}
    else{int y=s.Height/2;e.Graphics.DrawLine(p,e.Item.IsOnDropDown?UI.D(30):0,y,s.Width,y);}
   }
  }
  protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){using(var b=new SolidBrush(Palette.Background))e.Graphics.FillRectangle(b,e.AffectedBounds);}
  protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){
   var r=e.ImageRectangle;r.Inflate(UI.D(2),UI.D(2));Draw.Box(e.Graphics,r,Palette.Pressed,Palette.HoverBorder);
   Art.Glyph(e.Graphics,"\uE73E",r,UI.D(12),Palette.Link);
  }
  protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e){}
 }

 public class BufferedPanel:Panel {public BufferedPanel(){DoubleBuffered=true;ResizeRedraw=true;}}

 /// <summary>Bar / donut / sunburst / squarified treemap visualisations.</summary>
 public class SizeChart:Control,IThemed {
  public string Mode="Bar Chart";public Snapshot Data;public string Folder;public List<ReportRow> Rows;public event Action<Entry> Navigate;
  private class Hit {public GraphicsPath Shape;public Entry Entry;public string Key;public string Text;}
  private class Item {public string Name;public long Size;public Entry Entry;public Color Color;public string Key{get{return Entry!=null?Entry.Path:"#"+Name;}}}
  private List<Hit> hits=new List<Hit>();private ToolTip tip=new ToolTip{InitialDelay=250,ReshowDelay=100};private string last="",hover;
  public SizeChart(){SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Font=new Font("Segoe UI",9);Dock=DockStyle.Fill;}
  static int D(float v){return UI.D(v);}
  private List<Item> Items(){
   if(Rows!=null)return Rows.Where(r=>r.Size>0).Take(30).Select((r,i)=>new Item{Name=r.DisplayName,Size=r.Size,Entry=r.Source,Color=Palette.Series[i%Palette.Series.Length]}).ToList();
   if(Data==null||string.IsNullOrEmpty(Folder))return new List<Item>();
   return Data.Direct(Folder).Where(e=>e.Size>0).OrderByDescending(e=>e.Size).Select((e,i)=>new Item{Name=e.Name,Size=e.Size,Entry=e,Color=Palette.Series[i%Palette.Series.Length]}).ToList();
  }
  protected override void OnMouseMove(MouseEventArgs e){
   base.OnMouseMove(e);Hit h=hits.LastOrDefault(x=>x.Shape.IsVisible(e.Location));string text=h==null?"":h.Text;
   Cursor=h!=null&&h.Entry!=null&&h.Entry.Folder?Cursors.Hand:Cursors.Default;
   if(text!=last){tip.SetToolTip(this,text);last=text;}
   string key=h==null?null:h.Key;if(key!=hover){hover=key;Invalidate();}
  }
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hover!=null){hover=null;Invalidate();}}
  protected override void OnDoubleClick(EventArgs e){base.OnDoubleClick(e);Point p=PointToClient(Cursor.Position);Hit h=hits.LastOrDefault(x=>x.Shape.IsVisible(p));if(h!=null&&h.Entry!=null&&h.Entry.Folder&&Navigate!=null)Navigate(h.Entry);}
  private void AddHit(GraphicsPath path,Item item,double total){hits.Add(new Hit{Shape=path,Entry=item.Entry,Key=item.Key,Text=item.Name+"\n"+Util.Size(item.Size)+(total>0?"  ·  "+(item.Size*100/total).ToString("0.0")+"%":"")});}
  private void HitRect(RectangleF r,Item item,double total){var path=new GraphicsPath();path.AddRectangle(r);AddHit(path,item,total);}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);foreach(var h in hits)h.Shape.Dispose();hits.Clear();
   var g=e.Graphics;g.Clear(Palette.Background);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
   var items=Items();
   if(items.Count==0){var r0=ClientRectangle;Art.Glyph(g,"\uE9D2",new Rectangle(0,r0.Height/2-D(56),r0.Width,D(48)),D(40),Palette.Line);Draw.Text(g,L.T("No items to show."),Font,new Rectangle(0,r0.Height/2,r0.Width,D(24)),Palette.SubText,Draw.Center);return;}
   double total=items.Sum(i=>(double)i.Size);
   using(var title=new Font("Segoe UI Semibold",11f)){
    string name=Rows!=null?L.T(Mode):(Folder??"");
    Draw.Text(g,name,title,new Rectangle(D(16),D(8),Width-D(220),D(28)),Palette.Text,Draw.Left);
    Draw.Text(g,L.F("{0} items",items.Count.ToString("N0"))+"  ·  "+Util.Size((long)total),Font,new Rectangle(Width-D(216),D(8),D(200),D(28)),Palette.SubText,Draw.Right);
   }
   RectangleF area=new RectangleF(D(16),D(44),Math.Max(1,Width-D(32)),Math.Max(1,Height-D(44)-D(12)));
   if(Mode=="Bar Chart")Bars(g,area,items,total);else if(Mode=="Pie Chart")Pie(g,area,items,total);else if(Mode=="Sunburst Chart")Sun(g,area,items,total);else Map(g,area,items,0,total);
  }
  private void Bars(Graphics g,RectangleF r,List<Item> items,double total){
   int fit=Math.Max(1,(int)(r.Height/D(30)));var top=items.Take(Math.Min(fit,25)).ToList();
   float row=Math.Min(D(34),r.Height/Math.Max(1,top.Count));float label=Math.Min(D(260),r.Width*.3f);float value=D(150);long max=top.Max(i=>i.Size);
   for(int i=0;i<top.Count;i++){
    Item a=top[i];float y=r.Y+i*row;var rowRect=new RectangleF(r.X,y,r.Width,row);bool hot=a.Key==hover;
    if(hot)Draw.Fill(g,rowRect,D(3),Palette.RowHover);
    var icon=Art.For(a.Entry);int isz=D(16);int tx=(int)r.X+D(6);
    if(icon!=null){g.DrawImage(icon,tx,(int)(y+(row-isz)/2),isz,isz);tx+=isz+D(6);}
    Draw.Text(g,a.Name,Font,new Rectangle(tx,(int)y,(int)(r.X+label-tx-D(8)),(int)row),Palette.Text,Draw.Left);
    float bh=Math.Max(D(6),Math.Min(D(16),row-D(14)));var track=new RectangleF(r.X+label,y+(row-bh)/2,r.Width-label-value,bh);
    Draw.Fill(g,track,D(2),Palette.BarTrack);
    var bar=new RectangleF(track.X,track.Y,Math.Max(D(3),track.Width*(float)(a.Size/(double)max)),bh);
    using(var br=new LinearGradientBrush(new RectangleF(bar.X,bar.Y-1,bar.Width+1,bar.Height+2),Palette.Blend(a.Color,Color.White,hot?0.35f:0.18f),hot?Palette.Blend(a.Color,Color.White,0.12f):a.Color,90))using(var p=Draw.Round(bar,D(2)))g.FillPath(br,p);
    Draw.Text(g,Util.Size(a.Size)+"   "+(a.Size*100/total).ToString("0.0")+"%",Font,new Rectangle((int)(track.Right+D(8)),(int)y,(int)value-D(10),(int)row),Palette.SubText,Draw.Right);
    HitRect(rowRect,a,total);
   }
  }
  private void Legend(Graphics g,RectangleF r,List<Item> items,double total){
   float row=Math.Min(D(28),r.Height/Math.Max(1,items.Count));
   for(int i=0;i<items.Count;i++){
    Item a=items[i];var rr=new RectangleF(r.X,r.Y+i*row,r.Width,row);if(rr.Bottom>r.Bottom+1)break;
    if(a.Key==hover)Draw.Fill(g,rr,D(3),Palette.RowHover);
    float dot=D(10);using(var b=new SolidBrush(a.Color))g.FillEllipse(b,rr.X+D(8),rr.Y+(row-dot)/2,dot,dot);
    int valueW=D(150);
    Draw.Text(g,a.Name,Font,new Rectangle((int)rr.X+D(26),(int)rr.Y,(int)rr.Width-D(26)-valueW,(int)row),Palette.Text,Draw.Left);
    Draw.Text(g,Util.Size(a.Size)+"   "+(a.Size*100/total).ToString("0.0")+"%",Font,new Rectangle((int)(rr.Right-valueW),(int)rr.Y,valueW-D(8),(int)row),Palette.SubText,Draw.Right);
    HitRect(rr,a,total);
   }
  }
  private List<Item> TopItems(List<Item> items,int n){var top=items.Take(n).ToList();if(items.Count>n)top.Add(new Item{Name=L.T("Other"),Size=items.Skip(n).Sum(i=>i.Size),Color=Color.FromArgb(170,170,170)});return top;}
  private static GraphicsPath Ring(PointF c,float inner,float outer,float start,float sweep){
   var p=new GraphicsPath();p.AddArc(c.X-outer,c.Y-outer,2*outer,2*outer,start,sweep);
   if(inner>0.5f)p.AddArc(c.X-inner,c.Y-inner,2*inner,2*inner,start+sweep,-sweep);else p.AddLine(c,c);
   p.CloseFigure();return p;
  }
  private void Center(Graphics g,PointF c,float radius,double total,int count){
   using(var big=new Font("Segoe UI Semibold",radius>D(70)?15f:11f)){
    var size=TextRenderer.MeasureText(Util.Size((long)total),big);
    Draw.Text(g,Util.Size((long)total),big,new Rectangle((int)(c.X-radius),(int)(c.Y-size.Height+D(4)),(int)(radius*2),size.Height),Palette.Text,Draw.Center);
    Draw.Text(g,L.F("{0} items",count.ToString("N0")),Font,new Rectangle((int)(c.X-radius),(int)(c.Y+D(4)),(int)(radius*2),D(20)),Palette.SubText,Draw.Center);
   }
  }
  private void Pie(Graphics g,RectangleF r,List<Item> items,double total){
   var top=TopItems(items,12);float d=Math.Min(r.Height-D(16),r.Width*.48f);float radius=d/2;var c=new PointF(r.X+D(12)+radius,r.Y+r.Height/2);float inner=radius*0.58f;
   float a=-90;
   foreach(var i in top){
    float sweep=(float)(360*i.Size/total);bool hot=i.Key==hover;float outer=hot?radius+D(5):radius;
    var path=Ring(c,inner,outer,a,Math.Max(sweep,0.01f));
    using(var b=new SolidBrush(hot?Palette.Blend(i.Color,Color.White,0.15f):i.Color))g.FillPath(b,path);
    using(var p=new Pen(Palette.Background,D(2)))g.DrawPath(p,path);
    AddHit(path,i,total);a+=sweep;
   }
   Center(g,c,inner,total,items.Count);
   float lx=c.X+radius+D(40);Legend(g,new RectangleF(lx,r.Y+D(8),r.Right-lx,r.Height-D(16)),top,total);
  }
  private void Sun(Graphics g,RectangleF r,List<Item> items,double total){
   float radius=Math.Min(r.Height-D(16),r.Width*.52f)/2;var c=new PointF(r.X+D(12)+radius,r.Y+r.Height/2);float hole=radius*0.3f;float unit=(radius-hole)/3;
   DrawRing(g,c,hole,unit,-90,360,items,0,total);
   Center(g,c,hole,total,items.Count);
   float lx=c.X+radius+D(40);Legend(g,new RectangleF(lx,r.Y+D(8),r.Right-lx,r.Height-D(16)),TopItems(items,12),total);
  }
  private void DrawRing(Graphics g,PointF c,float inner,float unit,float start,float sweep,List<Item> items,int depth,double grand){
   double total=items.Sum(i=>(double)i.Size);if(total<=0)return;float a=start;float outer=inner+unit-D(1);
   foreach(Item i in items){
    float angle=(float)(sweep*i.Size/total);if(angle<.25f){a+=angle;continue;}
    var p=Ring(c,inner,outer,a,angle);bool hot=i.Key==hover;
    using(var b=new SolidBrush(hot?Palette.Blend(i.Color,Color.White,0.3f):i.Color))g.FillPath(b,p);
    using(var pen=new Pen(Palette.Background,1.2f))g.DrawPath(pen,p);
    AddHit(p,i,grand);
    if(depth<2&&Data!=null&&i.Entry!=null&&i.Entry.Folder){
     var children=Data.Direct(i.Entry.Path).Where(e=>e.Size>0).OrderByDescending(e=>e.Size).Select((e,k)=>new Item{Name=e.Name,Size=e.Size,Entry=e,Color=Palette.Blend(i.Color,Color.White,0.22f+(k%3)*0.07f)}).ToList();
     DrawRing(g,c,inner+unit,unit,a,angle,children,depth+1,grand);
    }
    a+=angle;
   }
  }
  private void Map(Graphics g,RectangleF r,List<Item> items,int depth,double grand){
   var list=items.Where(i=>i.Size>0).Take(400).ToList();if(list.Count==0||r.Width<4||r.Height<4)return;
   var rects=Squarify(list,r);float gap=depth==0?D(3):D(2);
   for(int n=0;n<list.Count;n++){
    var it=list[n];var cell=rects[n];cell.Inflate(-gap/2,-gap/2);if(cell.Width<2||cell.Height<2)continue;
    bool hot=it.Key==hover;Color fill=hot?Palette.Blend(it.Color,Color.White,0.25f):it.Color;
    Draw.Fill(g,cell,D(3),fill);HitRect(cell,it,grand);
    bool nest=depth<2&&Data!=null&&it.Entry!=null&&it.Entry.Folder&&cell.Width>D(110)&&cell.Height>D(80);
    Color fg=Palette.Luma(fill)>0.62?Color.FromArgb(40,40,40):Color.White;
    if(cell.Width>D(46)&&cell.Height>D(20)){
     using(var f=new Font(depth==0?"Segoe UI Semibold":"Segoe UI",depth==0?9f:8.5f)){
      int lh=f.Height;var tr=new Rectangle((int)cell.X+D(5),(int)cell.Y+D(3),(int)cell.Width-D(10),lh);
      Draw.Text(g,it.Name,f,tr,fg,Draw.Left);
      if(!nest&&cell.Height>lh*2+D(6)){tr.Y+=lh;Draw.Text(g,Util.Size(it.Size),Font,tr,Color.FromArgb(215,fg),Draw.Left);}
      else if(nest){var sz=TextRenderer.MeasureText(it.Name,f);if(sz.Width+D(80)<tr.Width)Draw.Text(g,Util.Size(it.Size),Font,tr,Color.FromArgb(215,fg),Draw.Right);}
     }
    }
    if(nest){
     var inner=new RectangleF(cell.X+D(4),cell.Y+D(23),cell.Width-D(8),cell.Height-D(27));
     var children=Data.Direct(it.Entry.Path).Where(x=>x.Size>0).OrderByDescending(x=>x.Size).Take(300).Select((x,k)=>new Item{Name=x.Name,Size=x.Size,Entry=x,Color=Palette.Blend(it.Color,Color.White,0.2f+(k%4)*0.06f)}).ToList();
     Map(g,inner,children,depth+1,grand);
    }
   }
  }
  private static List<RectangleF> Squarify(List<Item> items,RectangleF r){
   var result=new RectangleF[items.Count];double total=items.Sum(i=>(double)i.Size);if(total<=0)return result.ToList();
   double scale=r.Width*r.Height/total;int start=0;RectangleF rem=r;
   while(start<items.Count){
    float side=Math.Min(rem.Width,rem.Height);if(side<=0.5f)break;
    int end=start;double sum=0,best=double.MaxValue;
    while(end<items.Count){double ns=sum+items[end].Size*scale;double w=Worst(items,start,end,ns,scale,side);if(end>start&&w>best)break;best=w;sum=ns;end++;}
    float thick=(float)(sum/side);float off=0;bool column=rem.Width>=rem.Height;
    for(int i=start;i<end;i++){float len=(float)(items[i].Size*scale/thick);result[i]=column?new RectangleF(rem.X,rem.Y+off,thick,len):new RectangleF(rem.X+off,rem.Y,len,thick);off+=len;}
    rem=column?new RectangleF(rem.X+thick,rem.Y,rem.Width-thick,rem.Height):new RectangleF(rem.X,rem.Y+thick,rem.Width,rem.Height-thick);
    start=end;
   }
   return result.ToList();
  }
  private static double Worst(List<Item> items,int s,int e,double sum,double scale,float side){
   double max=0,min=double.MaxValue;for(int i=s;i<=e;i++){double a=items[i].Size*scale;if(a>max)max=a;if(a<min)min=a;}
   double s2=sum*sum,w2=(double)side*side;if(min<=0||s2<=0)return double.MaxValue;return Math.Max(w2*max/s2,s2/(w2*min));
  }
 }
}
