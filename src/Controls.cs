using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FolderScope {
 internal static class Win {
  [DllImport("user32.dll")] public static extern bool ReleaseCapture();
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,string l);
  [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h,string app,string idList);
  public static void Cue(TextBox box,string text){Action set=()=>SendMessage(box.Handle,0x1501,(IntPtr)1,text);if(box.IsHandleCreated)set();else box.HandleCreated+=(s,e)=>set();}
  public static void Drag(Form f){ReleaseCapture();SendMessage(f.Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
 }

 /// <summary>Base for small owner-drawn controls with hover/pressed tracking.</summary>
 public class HotControl:Control,IThemed {
  protected bool hot,down;
  public HotControl(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);}
  protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;down=false;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){down=true;Invalidate();}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Invalidate();}
  protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
  protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
  protected Color ParentBack {get{return Parent!=null?Parent.BackColor:Palette.Panel;}}
 }

 /// <summary>Office-style push button (secondary = white, primary = accent).</summary>
 public class FlatButton:Button,IThemed {
  public bool Primary;bool hot,down;
  public FlatButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;}
  public override Size GetPreferredSize(Size proposed){var t=TextRenderer.MeasureText(Text,Font);int h=Font.Height;int w=t.Width+h*2+(Image!=null?Image.Width+h/2:0);return new Size(Math.Max(w,(int)(h*5.4)),(int)(h*1.8));}
  protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hot=true;Invalidate();}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hot=false;down=false;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);down=true;Invalidate();}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);down=false;Invalidate();}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Parent!=null?Parent.BackColor:Palette.Panel);
   Color fill,border,text;
   if(!Enabled){fill=Palette.Panel;border=Palette.LightLine;text=Palette.SubText;}
   else if(Primary){fill=down?Palette.Blend(Palette.Accent,Color.Black,0.18f):hot?Palette.Blend(Palette.Accent,Color.Black,0.08f):Palette.Accent;border=Palette.Blend(fill,Color.Black,0.1f);text=Color.White;}
   else{fill=down?Palette.Pressed:hot?Palette.Hover:Palette.Background;border=hot||down?Palette.HoverBorder:Palette.Line;text=Palette.Text;}
   var r=new RectangleF(0.5f,0.5f,Width-2f,Height-2f);Draw.Fill(g,r,UI.D(3),fill);Draw.Stroke(g,r,UI.D(3),border);
   if(Focused&&ShowFocusCues&&Enabled){var f=r;f.Inflate(-UI.D(2),-UI.D(2));Draw.Stroke(g,f,UI.D(2),Primary?Color.FromArgb(160,255,255,255):Palette.Accent);}
   var tr=ClientRectangle;
   if(Image!=null){var sz=TextRenderer.MeasureText(Text,Font);int total=Image.Width+UI.D(6)+sz.Width;int x=(Width-total)/2;g.DrawImage(Image,x,(Height-Image.Height)/2,Image.Width,Image.Height);tr=new Rectangle(x+Image.Width+UI.D(6),0,sz.Width+UI.D(2),Height);Draw.Text(g,Text,Font,tr,text,Draw.Left);}
   else Draw.Text(g,Text,Font,tr,text,Draw.Center);
  }
 }

 /// <summary>Glyph button for the title bar (minimize / maximize / close / quick access).</summary>
 public class CaptionButton:HotControl {
  public string Glyph;public bool IsClose;public float GlyphSize=10;private ToolTip tip;
  public CaptionButton(string glyph,string hint,Action click){Glyph=glyph;Width=UI.D(46);Dock=DockStyle.Right;TabStop=false;if(!string.IsNullOrEmpty(hint)){tip=new ToolTip();tip.SetToolTip(this,L.T(hint));}Click+=(s,e)=>{try{click();}catch(Exception ex){UI.Error(ex);}};}
  protected override void OnPaint(PaintEventArgs e){
   Color back=ParentBack;
   if(hot)back=IsClose?(down?Color.FromArgb(150,30,20):Color.FromArgb(196,43,28)):Palette.Blend(back,Color.White,down?0.28f:0.16f);
   e.Graphics.Clear(back);Art.Glyph(e.Graphics,Glyph,ClientRectangle,UI.D(GlyphSize),Color.White);
  }
 }

 /// <summary>Accent-coloured title bar: logo + quick access buttons on the left, centered caption, window buttons on the right.</summary>
 public class TitleBar:Panel,IThemed {
  public string Caption="";public Action ToggleMax;public int LeftReserve;
  public TitleBar(){DoubleBuffered=true;ResizeRedraw=true;Dock=DockStyle.Top;Height=UI.D(32);}
  protected override void OnPaint(PaintEventArgs e){
   base.OnPaint(e);var g=e.Graphics;int s=UI.D(18);g.DrawImage(Art.Icon("logo-white",s),UI.D(9),(Height-s)/2,s,s);
   int left=LeftReserve,right=Controls.OfType<Control>().Where(c=>c.Dock==DockStyle.Right).Sum(c=>c.Width);int pad=Math.Max(left,right);
   Draw.Text(g,Caption,Font,new Rectangle(pad,0,Math.Max(10,Width-pad*2),Height),Color.White,Draw.Center);
  }
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;if(e.Clicks>=2){if(ToggleMax!=null)ToggleMax();return;}var f=FindForm();if(f!=null)Win.Drag(f);}
  protected override void WndProc(ref Message m){
   if(m.Msg==0x84){var f=FindForm();if(f!=null&&f.WindowState==FormWindowState.Normal){long lp=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(lp&0xFFFF),(short)((lp>>16)&0xFFFF)));if(p.Y<UI.D(5)){m.Result=(IntPtr)(-1);return;}}}
   base.WndProc(ref m);
  }
 }

 /// <summary>Borderless text box with an icon, drawn as a flat Office input field (search box, address bar).</summary>
 public class SearchBox:Panel,IThemed {
  public readonly TextBox Box;public Image Icon;public string Glyph;public Color GlyphColor=Color.Empty;
  public SearchBox(string cue){DoubleBuffered=true;ResizeRedraw=true;Cursor=Cursors.IBeam;Box=new TextBox{BorderStyle=BorderStyle.None};Controls.Add(Box);Win.Cue(Box,L.T(cue));Box.GotFocus+=(s,e)=>Invalidate();Box.LostFocus+=(s,e)=>Invalidate();Click+=(s,e)=>Box.Focus();Height=UI.D(24);}
  public override string Text {get{return Box.Text;}set{Box.Text=value;}}
  int IconRoom {get{return Icon!=null||Glyph!=null?UI.D(26):UI.D(7);}}
  protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);Box.BackColor=Palette.Background;Box.ForeColor=Palette.Text;Box.Font=Font;Box.Location=new Point(IconRoom,(Height-Box.Height)/2);Box.Width=Math.Max(10,Width-IconRoom-UI.D(6));}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Parent!=null?Parent.BackColor:Palette.Panel);var r=new Rectangle(0,0,Width-1,Height-1);
   using(var b=new SolidBrush(Palette.Background))g.FillRectangle(b,r);
   using(var p=new Pen(Box.Focused?Palette.Accent:Palette.Line))g.DrawRectangle(p,r);
   int s=UI.D(16);var ir=new Rectangle(UI.D(6),(Height-s)/2,s,s);
   if(Icon!=null)g.DrawImage(Icon,ir);else if(Glyph!=null)Art.Glyph(g,Glyph,ir,UI.D(13),GlyphColor.IsEmpty?Palette.SubText:GlyphColor);
  }
  protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);PerformLayout();}
 }

 /// <summary>Large ribbon button: 32px icon above a one- or two-line caption, optional drop-down menu.</summary>
 public class RibbonButton:HotControl {
  public Image Icon;public bool NeedsData;public ContextMenuStrip Menu;private Action action;private ToolTip tip=new ToolTip();
  public RibbonButton(string text,string icon,Action a){Text=L.T(text);Icon=Art.Icon(icon,UI.D(32));action=a;Font=new Font("Segoe UI",9f);tip.SetToolTip(this,Text.Replace("\n",""));Cursor=Cursors.Default;
   var lines=Text.Split('\n');int w=lines.Max(l=>TextRenderer.MeasureText(l,Font).Width);Size=new Size(Math.Max(UI.D(46),w+UI.D(12)),UI.D(70));}
  protected override void OnClick(EventArgs e){base.OnClick(e);if(!Enabled)return;if(Menu!=null){Menu.Show(this,new Point(0,Height));return;}try{if(action!=null)action();}catch(Exception ex){UI.Error(ex);}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Background);var r=new Rectangle(0,0,Width-1,Height-1);
   if(Enabled&&(hot||down))Draw.Box(g,new Rectangle(0,0,Width,Height),down?Palette.Pressed:Palette.Hover,Palette.HoverBorder);
   int s=UI.D(32);var img=Icon;if(img!=null){if(Enabled)g.DrawImage(img,(Width-s)/2,UI.D(4),s,s);else ControlPaint.DrawImageDisabled(g,img,(Width-s)/2,UI.D(4),Palette.Background);}
   Color c=Enabled?Palette.Text:Palette.Blend(Palette.SubText,Palette.Background,0.3f);
   var lines=Text.Split('\n');int lh=Font.Height;int y=UI.D(4)+s+UI.D(2);
   foreach(var l in lines){Draw.Text(g,l,Font,new Rectangle(0,y,Width,lh),c,Draw.Center);y+=lh;}
   if(Menu!=null){int ax=Width/2,ay=y+UI.D(3);using(var b=new SolidBrush(c))g.FillPolygon(b,new[]{new Point(ax-UI.D(3),ay),new Point(ax+UI.D(3),ay),new Point(ax,ay+UI.D(3))});}
  }
 }

 /// <summary>Ribbon group: buttons laid out left to right with a caption at the bottom and a separator on the right.</summary>
 public class RibbonGroup:Panel,IThemed {
  public string Caption;
  public RibbonGroup(string name){Caption=L.T(name);DoubleBuffered=true;ResizeRedraw=true;Margin=new Padding(0);Font=new Font("Segoe UI",8.5f);}
  public RibbonGroup Add(string text,string icon,Action a){return Add(new RibbonButton(text,icon,a));}
  public RibbonGroup AddD(string text,string icon,Action a){var b=new RibbonButton(text,icon,a){NeedsData=true};return Add(b);}
  public RibbonGroup Add(RibbonButton b){int x=UI.D(4)+Controls.OfType<Control>().Sum(c=>c.Width+UI.D(1));b.Location=new Point(x,UI.D(3));Controls.Add(b);
   int capW=TextRenderer.MeasureText(Caption,Font).Width+UI.D(16);Width=Math.Max(x+b.Width+UI.D(6),capW);Height=UI.D(94);return this;}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Background);
   Draw.Text(g,Caption,Font,new Rectangle(0,Height-UI.D(20),Width-UI.D(1),UI.D(18)),Palette.SubText,Draw.Center);
   using(var p=new Pen(Palette.LightLine.GetBrightness()>0.5f?Palette.Line:Palette.Line))g.DrawLine(p,Width-1,UI.D(6),Width-1,Height-UI.D(6));
  }
 }

 /// <summary>White ribbon surface below the tab row. Leaves a gap in its top border under the active tab.</summary>
 public class RibbonPanel:Panel,IThemed {
  public int GapLeft=-1,GapRight=-1;public event Action CollapseClicked;private bool chevHot;
  public RibbonPanel(){DoubleBuffered=true;ResizeRedraw=true;Height=UI.D(98);}
  Rectangle Chevron {get{int s=UI.D(18);return new Rectangle(Width-s-UI.D(6),Height-s-UI.D(4),s,s);}}
  protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);Relayout();}
  protected override void OnControlRemoved(ControlEventArgs e){base.OnControlRemoved(e);Relayout();}
  public void Relayout(){int x=UI.D(4);foreach(Control c in Controls){c.Location=new Point(x,UI.D(2));x+=c.Width;}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Background);
   using(var p=new Pen(Palette.Line)){
    g.DrawRectangle(p,0,0,Width-1,Height-1);
    if(GapLeft>=0&&GapRight>GapLeft)using(var b=new Pen(Palette.Background))g.DrawLine(b,GapLeft+1,0,GapRight-2,0);
   }
   Art.Glyph(g,"\uE70E",Chevron,UI.D(10),chevHot?Palette.Link:Palette.SubText);
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=Chevron.Contains(e.Location);if(h!=chevHot){chevHot=h;Invalidate(Chevron);}}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(Chevron.Contains(e.Location)&&CollapseClicked!=null)CollapseClicked();}
 }

 /// <summary>Dock panel caption (e.g. "Folder Browser") with a close glyph.</summary>
 public class DockHeader:HotControl {
  public Action CloseAction;private bool closeHot;
  public DockHeader(string text){Text=L.T(text);Dock=DockStyle.Top;Height=UI.D(26);Font=new Font("Segoe UI Semibold",9f);}
  Rectangle CloseRect {get{int s=UI.D(20);return new Rectangle(Width-s-UI.D(4),(Height-s)/2,s,s);}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Panel);
   Draw.Text(g,Text,Font,new Rectangle(UI.D(6),0,Width-UI.D(60),Height),Palette.Text,Draw.Left);
   if(CloseAction!=null){var r=CloseRect;if(closeHot)Draw.Box(g,r,Palette.Hover,Palette.HoverBorder);Art.Glyph(g,"\uE8BB",r,UI.D(8),Palette.SubText);
    var pin=new Rectangle(r.X-r.Width-UI.D(2),r.Y,r.Width,r.Height);Art.Glyph(g,"\uE718",pin,UI.D(10),Palette.SubText);
    var menu=new Rectangle(pin.X-r.Width,r.Y,r.Width,r.Height);Art.Glyph(g,"\uE70D",menu,UI.D(8),Palette.SubText);}
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=CloseAction!=null&&CloseRect.Contains(e.Location);if(h!=closeHot){closeHot=h;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);closeHot=false;Invalidate();}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(CloseAction!=null&&CloseRect.Contains(e.Location))CloseAction();}
 }

 /// <summary>Document-style tabs at the bottom of a dock panel.</summary>
 public class DockTabs:HotControl {
  private List<string> items=new List<string>();private List<Image> icons=new List<Image>();private List<Rectangle> bounds=new List<Rectangle>();private int selected;
  public event Action<int> Changed;
  public DockTabs(){Dock=DockStyle.Bottom;Height=UI.D(26);}
  public void Add(string text,Image icon){items.Add(L.T(text));icons.Add(icon);Invalidate();}
  public int Selected {get{return selected;}set{selected=value;Invalidate();if(Changed!=null)Changed(value);}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Panel);bounds.Clear();int x=0;int s=UI.D(16);
   using(var p=new Pen(Palette.Line))g.DrawLine(p,0,0,Width,0);
   for(int i=0;i<items.Count;i++){
    int w=TextRenderer.MeasureText(items[i],Font).Width+s+UI.D(18);var r=new Rectangle(x,0,w,Height-UI.D(2));bounds.Add(r);
    if(i==selected){using(var b=new SolidBrush(Palette.Background))g.FillRectangle(b,r);using(var p=new Pen(Palette.Line)){g.DrawLine(p,r.Left,r.Top,r.Left,r.Bottom);g.DrawLine(p,r.Right,r.Top,r.Right,r.Bottom);g.DrawLine(p,r.Left,r.Bottom,r.Right,r.Bottom);}}
    if(icons[i]!=null)g.DrawImage(icons[i],r.X+UI.D(5),r.Y+(r.Height-s)/2,s,s);
    Draw.Text(g,items[i],Font,new Rectangle(r.X+s+UI.D(8),r.Y,r.Width-s-UI.D(8),r.Height),i==selected?Palette.Text:Palette.SubText,Draw.Left);
    x+=w+1;
   }
  }
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);for(int i=0;i<bounds.Count;i++)if(bounds[i].Contains(e.Location)&&i!=selected){Selected=i;break;}}
 }

 /// <summary>Flat tab strip (accent underline) hosting one page at a time.</summary>
 public class PivotView:Panel,IThemed {
  private class Page {public string Text;public Image Icon;public Control Body;public Rectangle Bounds;}
  private List<Page> pages=new List<Page>();private int selected=-1,hover=-1;
  public event EventHandler SelectedIndexChanged;
  public PivotView(){DoubleBuffered=true;ResizeRedraw=true;Padding=new Padding(0,UI.D(36),0,0);}
  public void AddPage(string text,Image icon,Control body){body.Dock=DockStyle.Fill;body.Visible=false;pages.Add(new Page{Text=L.T(text),Icon=icon,Body=body});Controls.Add(body);if(selected<0)SelectedIndex=0;Invalidate();}
  public int Count {get{return pages.Count;}}
  public int SelectedIndex {get{return selected;}set{if(value<0||value>=pages.Count)return;selected=value;for(int i=0;i<pages.Count;i++)pages[i].Body.Visible=i==value;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;int h=Padding.Top;using(var b=new SolidBrush(Palette.Background))g.FillRectangle(b,0,0,Width,h);
   using(var p=new Pen(Palette.Line))g.DrawLine(p,0,h-1,Width,h-1);
   int x=UI.D(8),s=UI.D(16);
   for(int i=0;i<pages.Count;i++){
    var pg=pages[i];int w=TextRenderer.MeasureText(pg.Text,Font).Width+s+UI.D(26);pg.Bounds=new Rectangle(x,0,w,h);
    if(i==hover&&i!=selected)Draw.Fill(g,new RectangleF(x+UI.D(2),UI.D(5),w-UI.D(4),h-UI.D(10)),UI.D(3),Palette.RowHover);
    if(pg.Icon!=null)g.DrawImage(pg.Icon,x+UI.D(10),(h-s)/2,s,s);
    Draw.Text(g,pg.Text,i==selected?new Font(Font,FontStyle.Bold):Font,new Rectangle(x+s+UI.D(15),0,w-s-UI.D(15),h),i==selected?Palette.Text:Palette.SubText,Draw.Left);
    if(i==selected)Draw.Fill(g,new RectangleF(x+UI.D(8),h-UI.D(3),w-UI.D(16),UI.D(3)),UI.D(1.5f),Palette.Accent);
    x+=w;
   }
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int h=pages.FindIndex(p=>p.Bounds.Contains(e.Location));if(h!=hover){hover=h;Invalidate(new Rectangle(0,0,Width,Padding.Top));}}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;Invalidate(new Rectangle(0,0,Width,Padding.Top));}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);int h=pages.FindIndex(p=>p.Bounds.Contains(e.Location));if(h>=0&&h!=selected)SelectedIndex=h;}
 }

 /// <summary>"This PC" drive tile: icon with status overlay, usage bar and details (modelled on the original drive tiles).</summary>
 public class DriveTile:HotControl {
  public string Root,Label,Format;public long Total,Free;public bool System;public event Action<string> Activated;
  public DriveTile(){Size=new Size(UI.D(260),UI.D(96));Cursor=Cursors.Hand;Margin=new Padding(UI.D(6),UI.D(6),UI.D(14),UI.D(10));}
  protected override void OnClick(EventArgs e){base.OnClick(e);if(Activated!=null)Activated(Root);}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Background);
   if(hot)Draw.Box(g,ClientRectangle,down?Palette.Pressed:Palette.RowHover,Palette.HoverBorder);
   int s=UI.D(32),x0=UI.D(8)+s+UI.D(10);g.DrawImage(Art.Icon(System?"sysdisk":"disk",s),UI.D(8),UI.D(8),s,s);
   int c=UI.D(14);g.DrawImage(Art.Icon("check",c),UI.D(8)+s-c+UI.D(2),UI.D(8)+s-c+UI.D(2),c,c);
   string letter=" ("+Root.TrimEnd('\\')+")";string name=string.IsNullOrEmpty(Label)?L.T("Local Disk"):Label;
   int nw=TextRenderer.MeasureText(name,Font).Width;int lh=Font.Height;int y=UI.D(6);
   Draw.Text(g,name,Font,new Rectangle(x0,y,Width-x0-UI.D(8),lh),hot?Palette.Link:Palette.Text,Draw.Left);
   Draw.Text(g,letter,Font,new Rectangle(x0+nw-UI.D(4),y,Width-x0-nw,lh),Palette.SubText,Draw.Left);
   y+=lh+UI.D(2);double used=Total>0?(Total-Free)/(double)Total:0;var bar=new Rectangle(x0,y,Width-x0-UI.D(16),UI.D(12));
   using(var b=new SolidBrush(Palette.BarTrack))g.FillRectangle(b,bar);
   var fill=new Rectangle(bar.X,bar.Y,(int)(bar.Width*used),bar.Height);Color fc=used>=0.9?Palette.Blend(Palette.Danger,Color.White,0.45f):Palette.BarFill;
   using(var br=new LinearGradientBrush(new Rectangle(fill.X,fill.Y-1,Math.Max(1,fill.Width),fill.Height+2),Palette.Blend(fc,Color.White,0.3f),fc,90))g.FillRectangle(br,fill);
   using(var p=new Pen(Palette.Line))g.DrawRectangle(p,bar.X,bar.Y,bar.Width-1,bar.Height-1);
   y+=bar.Height+UI.D(4);
   Draw.Text(g,L.F("{0} free of {1}",Util.Size(Free),Util.Size(Total)),Font,new Rectangle(x0,y,Width-x0,lh),Palette.SubText,Draw.Left);y+=lh;
   Draw.Text(g,L.T("File system")+": "+Format,Font,new Rectangle(x0,y,Width-x0,lh),Palette.SubText,Draw.Left);y+=lh;
   Draw.Text(g,L.F("Used: {0} ({1:0.0}%)",Util.Size(Total-Free),used*100),Font,new Rectangle(x0,y,Width-x0,lh),used>=0.9?Palette.Danger:Palette.Good,Draw.Left);
  }
 }

 /// <summary>Italic hint line with a light-bulb icon.</summary>
 public class HintLine:Control,IThemed {
  public HintLine(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Dock=DockStyle.Bottom;Height=UI.D(28);Font=new Font("Segoe UI",9f,FontStyle.Italic);}
  protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.Clear(Palette.Background);int s=UI.D(16);Art.Glyph(g,"\uEA80",new Rectangle(UI.D(10),(Height-s)/2,s,s),UI.D(14),Color.FromArgb(230,160,20));Draw.Text(g,Text,Font,new Rectangle(UI.D(30),0,Width-UI.D(36),Height),Palette.SubText,Draw.Left);}
 }

 /// <summary>Bottom information panel: computer details or the current folder's statistics.</summary>
 public class InfoPanel:Control,IThemed {
  public Image Icon;public string Title="",Subtitle="";public List<KeyValuePair<string,string>> Fields=new List<KeyValuePair<string,string>>();public event Action TitleClicked;private bool titleHot;private Rectangle titleRect;
  public InfoPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);Dock=DockStyle.Bottom;Height=UI.D(50);}
  public void Set(Image icon,string title,string subtitle,params string[] pairs){Icon=icon;Title=title??"";Subtitle=subtitle??"";Fields.Clear();for(int i=0;i+1<pairs.Length;i+=2)Fields.Add(new KeyValuePair<string,string>(pairs[i],pairs[i+1]));Invalidate();}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(Palette.Background);using(var p=new Pen(Palette.Line))g.DrawLine(p,0,0,Width,0);
   int s=UI.D(32);if(Icon!=null)g.DrawImage(Icon,UI.D(10),(Height-s)/2,s,s);
   int x=UI.D(10)+s+UI.D(10),lh=Font.Height,y=(Height-lh*2)/2;
   int tw=Math.Min(TextRenderer.MeasureText(Title,Font).Width,Math.Max(UI.D(120),Width/2-x));
   titleRect=new Rectangle(x,y,tw,lh);
   using(var f=new Font(Font,titleHot&&TitleClicked!=null?FontStyle.Underline:FontStyle.Regular))Draw.Text(g,Title,f,titleRect,TitleClicked!=null?Palette.Link:Palette.Text,Draw.Left);
   Draw.Text(g,Subtitle,Font,new Rectangle(x,y+lh,Math.Max(tw,UI.D(160)),lh),Palette.SubText,Draw.Left);
   int fx=x+Math.Max(Math.Max(tw,UI.D(160)),TextRenderer.MeasureText(Subtitle,Font).Width)+UI.D(36);
   for(int i=0;i<Fields.Count;i+=2){
    int colW=0;
    for(int j=i;j<Math.Min(i+2,Fields.Count);j++){var kv=Fields[j];string k=kv.Key+": ";int kw=TextRenderer.MeasureText(k,Font).Width;int vw=TextRenderer.MeasureText(kv.Value,Font).Width;int yy=y+(j-i)*lh;
     Draw.Text(g,k,Font,new Rectangle(fx,yy,kw,lh),Palette.SubText,Draw.Left);Draw.Text(g,kv.Value,Font,new Rectangle(fx+kw-UI.D(4),yy,Math.Max(10,Width-fx-kw),lh),Palette.Text,Draw.Left);colW=Math.Max(colW,kw+vw);}
    fx+=colW+UI.D(36);if(fx>Width)break;
   }
  }
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool h=titleRect.Contains(e.Location);if(h!=titleHot){titleHot=h;Cursor=h&&TitleClicked!=null?Cursors.Hand:Cursors.Default;Invalidate();}}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(titleRect.Contains(e.Location)&&TitleClicked!=null)TitleClicked();}
 }

 /// <summary>Large action tile for the welcome dialog.</summary>
 public class TileButton:HotControl {
  public Image Icon;public string Detail;private Action action;
  public TileButton(string title,string detail,string icon,Action a){Text=L.T(title);Detail=L.T(detail);Icon=Art.Icon(icon,UI.D(32));action=a;Cursor=Cursors.Hand;Margin=new Padding(UI.D(4));}
  protected override void OnClick(EventArgs e){base.OnClick(e);try{action();}catch(Exception ex){UI.Error(ex);}}
  protected override void OnPaint(PaintEventArgs e){
   var g=e.Graphics;g.Clear(ParentBack);var r=new RectangleF(0.5f,0.5f,Width-2f,Height-2f);
   Draw.Fill(g,r,UI.D(4),down?Palette.Pressed:hot?Palette.Hover:Palette.Background);Draw.Stroke(g,r,UI.D(4),hot?Palette.HoverBorder:Palette.Line);
   int s=UI.D(32);g.DrawImage(Icon,UI.D(12),(Height-s)/2,s,s);int x=UI.D(12)+s+UI.D(12),lh=Font.Height;
   using(var f=new Font("Segoe UI Semibold",9.5f))Draw.Text(g,Text,f,new Rectangle(x,Height/2-lh-UI.D(1),Width-x-UI.D(8),lh+UI.D(2)),Palette.Text,Draw.Left);
   Draw.Text(g,Detail,Font,new Rectangle(x,Height/2+UI.D(1),Width-x-UI.D(8),lh),Palette.SubText,Draw.Left);
  }
 }
}
