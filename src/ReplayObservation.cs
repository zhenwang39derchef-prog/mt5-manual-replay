using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public sealed class ObservationBar
{
    public long Time {get;set;}
    public double Open {get;set;} public double High {get;set;} public double Low {get;set;} public double Close {get;set;}
    public double Ema8 {get;set;} public double Ema34 {get;set;} public double Ema200 {get;set;}
    public bool Forming {get;set;}
}
public sealed class ObservationData
{
    public string Session {get;set;} public string Boot {get;set;} public string Symbol {get;set;}
    public long AsOf {get;set;} public long Stamp {get;set;} public long Generation {get;set;}
    public Dictionary<string,ObservationBar[]> Series {get;set;}
    public Dictionary<string,string> Status {get;set;}
    public static readonly string[] Frames={"M5","M15","H1","H4"};
    public static int Seconds(string frame){switch(frame){case "M5":return 300;case "M15":return 900;case "H1":return 3600;case "H4":return 14400;default:throw new FormatException("观察周期无效。");}}
    static long Integer(string value){return Int64.Parse(value,NumberStyles.None,CultureInfo.InvariantCulture);}
    static double Price(string value){double n=Double.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);if(!ChartMath.Finite(n)||n<=0||n>1e12)throw new FormatException("观察报价无效。");return n;}
    public static ObservationData Parse(string text,string session,string boot,string symbol)
    {
        if(text==null||text.Length>2000000)throw new FormatException("观察数据过大。");
        var d=new Dictionary<string,string>();
        foreach(var raw in text.TrimEnd('\r','\n').Split('\n')){var line=raw.TrimEnd('\r');int at=line.IndexOf('=');if(at<=0||d.ContainsKey(line.Substring(0,at)))throw new FormatException("观察数据不完整。");d.Add(line.Substring(0,at),line.Substring(at+1));}
        if(d["protocol"]!="1"||d["complete"]!="1"||d["session"]!=session||d["boot"]!=boot||d["symbol"]!=symbol)throw new FormatException("观察数据与本场回放不匹配。");
        var result=new ObservationData{Session=session,Boot=boot,Symbol=symbol,AsOf=Integer(d["asof"]),Stamp=Integer(d["stamp"]),Generation=Integer(d["generation"]),Series=new Dictionary<string,ObservationBar[]>(),Status=new Dictionary<string,string>()};
        if(result.AsOf<=0||result.AsOf>253402214400L||result.Stamp<=0||result.Generation<=0)throw new FormatException("观察时间无效。");
        foreach(string f in Frames)
        {
            var bars=new List<ObservationBar>();
            foreach(var raw in d[f].Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
            {
                var a=raw.Split(',');if(a.Length!=9||(a[8]!="0"&&a[8]!="1"))throw new FormatException("K线格式无效。");
                bars.Add(new ObservationBar{Time=Integer(a[0]),Open=Price(a[1]),High=Price(a[2]),Low=Price(a[3]),Close=Price(a[4]),Ema8=Price(a[5]),Ema34=Price(a[6]),Ema200=Price(a[7]),Forming=a[8]=="1"});
            }
            result.Series.Add(f,bars.ToArray());result.Status.Add(f,d[f+"_status"]);
        }
        result.Validate(session,boot,symbol,result.AsOf);return result;
    }
    public void Validate(string session,string boot,string symbol,long asof)
    {
        if(Session!=session||Boot!=boot||Symbol!=symbol||AsOf!=asof||Series==null||Status==null||Series.Count!=4)throw new FormatException("图表快照与收工时点不匹配。");
        foreach(string f in Frames)
        {
            int seconds=Seconds(f);ObservationBar[] bars;
            if(!Series.TryGetValue(f,out bars)||bars==null||bars.Length>480||!Status.ContainsKey(f)||!new[]{"forming","closed_only","history_unavailable"}.Contains(Status[f]))throw new FormatException("图表周期数据不完整。");
            long previous=0;bool forming=false;
            foreach(var b in bars)
            {
                if(b==null||b.Time<=previous||b.Time>AsOf||forming||b.Time<=0||b.Time%seconds!=0||
                   !new[]{b.Open,b.High,b.Low,b.Close,b.Ema8,b.Ema34,b.Ema200}.All(n=>ChartMath.Finite(n)&&n>0&&n<=1e12)||
                   b.High<Math.Max(b.Open,b.Close)||b.Low>Math.Min(b.Open,b.Close)||b.Low>b.High||
                   (b.Forming?b.Time!=AsOf/seconds*seconds:b.Time+seconds>AsOf))throw new FormatException("图表包含超出已知时点或无效的K线。");
                previous=b.Time;forming=b.Forming;
            }
            if((Status[f]=="forming")!=forming||(Status[f]=="history_unavailable"&&bars.Length!=0))throw new FormatException("图表形成状态不一致。");
        }
    }
    public static ObservationData Read(string path,string session,string boot,string symbol)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Encoding.Unicode))return Parse(reader.ReadToEnd(),session,boot,symbol);
    }
    public bool CanDisplay(DesktopSnapshot state){return state!=null&&Session==state.Get("session")&&Boot==state.Get("boot")&&Symbol==state.Get("symbol")&&AsOf<=state.Int("now");}
}
public sealed class ObservationView
{
    public string Boot {get;set;} public string Frame {get;set;} public int Count {get;set;} public long Right {get;set;} public bool Ema {get;set;}
    public ObservationView(){Boot="";Frame="H1";Count=80;Ema=true;}
    public void Normalize(){if(!ObservationData.Frames.Contains(Frame))Frame="H1";Count=Math.Max(25,Math.Min(220,Count));if(Right<0)Right=0;}
    public static string PathFor(string folder,string boot){ReplayDaybook.FilePath("00000000000000000000000000000000",boot,folder);return Path.Combine(folder,"observation-view-"+boot+".json");}
    public static ObservationView Load(string folder,string boot)
    {
        try{var result=new JavaScriptSerializer().Deserialize<ObservationView>(File.ReadAllText(PathFor(folder,boot)));if(result!=null&&result.Boot==boot){result.Normalize();return result;}}catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
        return new ObservationView{Boot=boot};
    }
}
public sealed class ObservationArchive
{
    public ObservationData Data {get;set;} public ObservationView View {get;set;}
    public static ObservationArchive Capture(DesktopSessionInfo info,DesktopSnapshot state,string folder)
    {
        if(state.Get("mtf_protocol")!="1")return null;
        if(state.Get("mtf_seal_ready")!="1")throw new IOException("多周期图表快照尚未保存，请重试收工。");
        var data=ObservationData.Read(Path.Combine(folder,"mtf-seal.txt"),info.Id,state.Get("boot"),info.Symbol);
        data.Validate(info.Id,state.Get("boot"),info.Symbol,state.Int("now"));
        return new ObservationArchive{Data=data,View=ObservationView.Load(folder,state.Get("boot"))};
    }
}

// Paints only previously exported tester bars; no trading or broker API is reachable here.
public sealed class ObservationChart:Control
{
    public ObservationData Data {get;private set;} public ObservationView View {get;private set;}
    public Action Changed;Point pointer=new Point(-1,-1);int dragX;long dragRight;bool dragging;
    Rectangle plot;int first,last;double low,high;
    static readonly Color Up=Color.FromArgb(24,139,116),Down=Color.FromArgb(203,83,101);
    static readonly Color[] EmaColors={Color.FromArgb(46,109,165),Color.FromArgb(203,151,53),Color.FromArgb(145,111,173)};
    public ObservationChart(ObservationView view)
    {
        View=view;View.Normalize();BackColor=ReplayPaper.Surface;ForeColor=ReplayPaper.Ink;Font=new Font("Microsoft YaHei UI",9F);Dock=DockStyle.Fill;TabStop=true;AccessibleName="ObservationChartReadOnly";
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        MouseMove+=(s,e)=>{pointer=e.Location;if(dragging&&Data!=null){View.Right=dragRight;MoveBars((int)Math.Round((dragX-e.X)/Math.Max(2.0,plot.Width/(double)View.Count)));}else Invalidate();};
        MouseLeave+=(s,e)=>{pointer=new Point(-1,-1);Invalidate();};
        MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left&&plot.Contains(e.Location)){Focus();dragging=true;Capture=true;dragX=e.X;dragRight=View.Right;}};
        MouseUp+=(s,e)=>{dragging=false;Capture=false;Notify();};
        MouseWheel+=(s,e)=>Zoom(e.Delta>0?-10:10);
        KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Left)MoveBars(-10);else if(e.KeyCode==Keys.Right)MoveBars(10);else if(e.KeyCode==Keys.End)Current();};
    }
    public void UpdateData(ObservationData data){if(Object.ReferenceEquals(Data,data))return;Data=data;Invalidate();}
    public ObservationBar[] Bars{get{return Data!=null&&Data.Series.ContainsKey(View.Frame)?Data.Series[View.Frame]:new ObservationBar[0];}}
    public int EndIndex{get{var bars=Bars;if(View.Right==0)return bars.Length-1;int n=Array.FindLastIndex(bars,b=>b.Time<=View.Right);return Math.Max(0,n);}}
    void Notify(){Invalidate();if(Changed!=null)Changed();}
    public void SetFrame(string frame){ObservationData.Seconds(frame);View.Frame=frame;Notify();}
    public void Zoom(int change){View.Count=Math.Max(25,Math.Min(220,View.Count+change));Notify();}
    public void Current(){View.Right=0;Notify();}
    public void MoveBars(int count){var bars=Bars;if(bars.Length==0)return;int index=Math.Max(0,Math.Min(bars.Length-1,EndIndex+count));View.Right=index==bars.Length-1?0:bars[index].Time;Notify();}
    float Y(double price){return plot.Top+(float)((high-price)/(high-low)*plot.Height);}
    float X(int i){return plot.Left+(float)((i-first+.5)*plot.Width/Math.Max(1,last-first+1));}
    string Price(double value){return value.ToString("N2",CultureInfo.InvariantCulture);}
    void DrawText(Graphics g,string text,float x,float y,Color color){using(var b=new SolidBrush(color))g.DrawString(text,Font,b,x,y);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);var bars=Bars;
        if(Data==null||bars.Length==0){DrawText(g,Data==null?"正在等候测试器图表数据…":"当前周期历史数据尚未就绪，可切换其他周期。",18,28,ReplayPaper.Muted);return;}
        float scale=ReplayWindowLayout.DpiScale;plot=new Rectangle((int)(14*scale),(int)(80*scale),Math.Max(40,Width-(int)(104*scale)),Math.Max(50,Height-(int)(122*scale)));
        last=EndIndex;first=Math.Max(0,last-View.Count+1);var visible=bars.Skip(first).Take(last-first+1).ToArray();
        low=visible.Min(b=>b.Low);high=visible.Max(b=>b.High);
        if(View.Ema){low=Math.Min(low,visible.Min(b=>Math.Min(b.Ema8,Math.Min(b.Ema34,b.Ema200))));high=Math.Max(high,visible.Max(b=>Math.Max(b.Ema8,Math.Max(b.Ema34,b.Ema200))));}
        double pad=Math.Max((high-low)*.08,Math.Abs(high)*.00003);low-=pad;high+=pad;
        int selected=pointer.X>=plot.Left&&pointer.X<=plot.Right?Math.Max(first,Math.Min(last,first+(pointer.X-plot.Left)*visible.Length/Math.Max(1,plot.Width))):last;
        var selectedBar=bars[selected];
        DrawText(g,DesktopSnapshot.Epoch.AddSeconds(selectedBar.Time).ToString("MM.dd HH:mm")+"   "+(selectedBar.Forming?"形成中":"已收盘"),plot.Left,8*scale,ReplayPaper.Ink);
        DrawText(g,"开 "+Price(selectedBar.Open)+"   高 "+Price(selectedBar.High),plot.Left,28*scale,ReplayPaper.Muted);
        DrawText(g,"低 "+Price(selectedBar.Low)+"   收 "+Price(selectedBar.Close),plot.Left,49*scale,ReplayPaper.Muted);
        using(var grid=new Pen(Color.FromArgb(233,237,242)))for(int n=0;n<=4;n++){float y=plot.Top+plot.Height*n/4f;g.DrawLine(grid,plot.Left,y,plot.Right,y);DrawText(g,Price(high-(high-low)*n/4),plot.Right+8,y-8,ReplayPaper.Muted);}
        var saved=g.Save();g.SetClip(plot);float candle=Math.Max(1,Math.Min(12*scale,plot.Width/(float)visible.Length*.62f));
        for(int i=first;i<=last;i++){var b=bars[i];var color=b.Close>=b.Open?Up:Down;float x=X(i);using(var pen=new Pen(color,scale))g.DrawLine(pen,x,Y(b.High),x,Y(b.Low));using(var fill=new SolidBrush(color))g.FillRectangle(fill,x-candle/2,Math.Min(Y(b.Open),Y(b.Close)),candle,Math.Max(scale,Math.Abs(Y(b.Open)-Y(b.Close))));}
        if(View.Ema)for(int n=0;n<3;n++)using(var pen=new Pen(EmaColors[n],1.2f*scale)){for(int i=first+1;i<=last;i++){var a=bars[i-1];var b=bars[i];g.DrawLine(pen,X(i-1),Y(n==0?a.Ema8:n==1?a.Ema34:a.Ema200),X(i),Y(n==0?b.Ema8:n==1?b.Ema34:b.Ema200));}}
        if(plot.Contains(pointer)){using(var cross=new Pen(Color.FromArgb(120,145,166))){cross.DashStyle=DashStyle.Dash;g.DrawLine(cross,X(selected),plot.Top,X(selected),plot.Bottom);g.DrawLine(cross,plot.Left,pointer.Y,plot.Right,pointer.Y);}}
        g.Restore(saved);
        for(int n=0;n<3;n++){int index=first+(last-first)*n/2;DrawText(g,DesktopSnapshot.Epoch.AddSeconds(bars[index].Time).ToString("MM.dd HH:mm"),Math.Max(plot.Left,X(index)-35*scale),plot.Bottom+8,ReplayPaper.Muted);}
        if(View.Ema)for(int n=0;n<3;n++)DrawText(g,"EMA "+new[]{8,34,200}[n],plot.Left+n*94*scale,Height-22*scale,EmaColors[n]);
    }
}

public sealed class ObservationActionButton:Button
{
    public bool Selected {get;set;}
    public ObservationActionButton(){FlatStyle=FlatStyle.Flat;UseVisualStyleBackColor=false;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(ReplayPaper.Background);g.SmoothingMode=SmoothingMode.AntiAlias;
        bool hover=ClientRectangle.Contains(PointToClient(Control.MousePosition));
        using(var path=ReplayPaper.Round(new RectangleF(1,1,Math.Max(1,Width-2),Math.Max(1,Height-2)),7*ReplayWindowLayout.DpiScale))using(var fill=new SolidBrush(Selected?ReplayPaper.Accent:hover?ReplayPaper.Soft:ReplayPaper.Background))g.FillPath(fill,path);
        TextRenderer.DrawText(g,Text,Font,ClientRectangle,Selected?Color.White:ReplayPaper.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}
public sealed class ObservationForm:ReplayBufferedForm
{
    public readonly ObservationChart Chart;readonly Label time=new ReplayStableLabel(),notice=new ReplayStableLabel();
    readonly Dictionary<string,Button> periods=new Dictionary<string,Button>();
    public Action ViewChanged;public bool Archived {get;private set;}
    public ObservationForm(ObservationView view,bool archived)
    {
        Archived=archived;Text=archived?"多周期观察 · 收工现场":"多周期观察 · 与回放同步";Font=new Font("Microsoft YaHei UI",9);BackColor=ReplayPaper.Background;ForeColor=ReplayPaper.Ink;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(630,620);MinimumSize=new Size(430,340);StartPosition=FormStartPosition.Manual;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(16)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(root);
        var toolbar=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=7,RowCount=1,Margin=Padding.Empty};foreach(float width in new[]{50f,56f,50f,50f,0f,66f,88f})toolbar.ColumnStyles.Add(new ColumnStyle(width==0?SizeType.Percent:SizeType.Absolute,width==0?100:width));root.Controls.Add(toolbar,0,0);
        Chart=new ObservationChart(view);root.Controls.Add(Chart,0,2);int col=0;
        foreach(string f in ObservationData.Frames){string frame=f;var button=new ObservationActionButton{Text=f,Dock=DockStyle.Fill,AccessibleName="Observe"+f};button.Click+=(s,e)=>Chart.SetFrame(frame);periods.Add(f,button);toolbar.Controls.Add(button,col++,0);}
        var ema=new CheckBox{Text="EMA",Checked=view.Ema,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AccessibleName="ObserveEMA"};ema.CheckedChanged+=(s,e)=>{view.Ema=ema.Checked;Changed();};toolbar.Controls.Add(ema,5,0);
        var current=new PaperActionButton{Text="回到当前",Quiet=true,Dock=DockStyle.Fill,AccessibleName="ObserveCurrent"};current.Click+=(s,e)=>Chart.Current();toolbar.Controls.Add(current,6,0);
        time.Dock=DockStyle.Fill;time.TextAlign=ContentAlignment.MiddleLeft;time.AutoEllipsis=true;time.ForeColor=ReplayPaper.Muted;root.Controls.Add(time,0,1);
        var footer=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=5,RowCount=1,Margin=Padding.Empty};foreach(float w in new[]{40f,40f,0f,40f,40f})footer.ColumnStyles.Add(new ColumnStyle(w==0?SizeType.Percent:SizeType.Absolute,w==0?100:w));
        var prev=new ObservationActionButton{Text="‹",Dock=DockStyle.Fill,AccessibleName="ObserveEarlier"};prev.Click+=(s,e)=>Chart.MoveBars(-Math.Max(10,view.Count/2));footer.Controls.Add(prev,0,0);
        var next=new ObservationActionButton{Text="›",Dock=DockStyle.Fill,AccessibleName="ObserveLater"};next.Click+=(s,e)=>Chart.MoveBars(Math.Max(10,view.Count/2));footer.Controls.Add(next,1,0);
        notice.Dock=DockStyle.Fill;notice.TextAlign=ContentAlignment.MiddleCenter;notice.ForeColor=ReplayPaper.Muted;notice.AutoEllipsis=true;footer.Controls.Add(notice,2,0);
        var zin=new ObservationActionButton{Text="＋",Dock=DockStyle.Fill,AccessibleName="ObserveZoomIn"};zin.Click+=(s,e)=>Chart.Zoom(-10);footer.Controls.Add(zin,3,0);var zout=new ObservationActionButton{Text="−",Dock=DockStyle.Fill,AccessibleName="ObserveZoomOut"};zout.Click+=(s,e)=>Chart.Zoom(10);footer.Controls.Add(zout,4,0);root.Controls.Add(footer,0,3);
        Chart.Changed=Changed;Changed();ReplayWindowLayout.Scale(this);
    }
    void Changed(){foreach(var pair in periods){var b=pair.Value as ObservationActionButton;if(b!=null)b.Selected=pair.Key==Chart.View.Frame;pair.Value.Invalidate();}notice.Text=Chart.View.Right==0?"只读观察 · 拖动看历史 · 滚轮缩放":"浏览历史 · 回放时钟保持不变";Chart.Invalidate();if(ViewChanged!=null)ViewChanged();}
    public void SetData(ObservationData data,DesktopSnapshot state)
    {
        Chart.UpdateData(data);time.Text=data.Symbol+" · "+Chart.View.Frame+"   "+DesktopSnapshot.Epoch.AddSeconds(data.AsOf).ToString("yyyy.MM.dd HH:mm:ss")+(Archived?" · 收工时点":state==null||!state.Fresh?" · 连接暂停":state.Get("training_mode")=="paused"?" · 已暂停":" · 同步中");
        if(!Archived&&state!=null&&state.Int("now")>data.AsOf+2)time.Text+="（图表更新中）";
    }
    public void Status(string message){time.Text=message;}
}

// Arranges only the process/window recorded for this session, and restores its exact placement.
public sealed class ObservationLayout:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct Placement{public int Length,Flags,Show;public NativePoint Min,Max;public ChartNative.Rect Normal;}
    [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr window,ref Placement placement);
    [DllImport("user32.dll")]static extern bool SetWindowPlacement(IntPtr window,ref Placement placement);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
    IntPtr root;int pid;long started;Placement placement;bool restored;
    public static bool Partition(Rectangle area,int panelWidth,out Rectangle native,out Rectangle observer)
    {
        int available=area.Width-panelWidth-16;int observe=Math.Min(720,available/2);int main=available-observe-8;
        native=new Rectangle(area.Left,area.Top,main,area.Height);observer=new Rectangle(native.Right+8,area.Top,observe,area.Height);
        return main>=420&&observe>=430&&area.Height>=430;
    }
    bool SameWindow()
    {
        if(!ChartNative.IsWindow(root))return false;uint actual;ChartNative.GetWindowThreadProcessId(root,out actual);if(actual!=(uint)pid)return false;
        try{using(var p=Process.GetProcessById(pid))return p.StartTime.ToUniversalTime().Ticks==started;}catch(ArgumentException){return false;}
    }
    public bool Arrange(DesktopReplayForm owner,ObservationForm observer,DesktopSessionInfo info,DesktopSnapshot state,string folder)
    {
        if(state==null||!state.Fresh)return false;
        FinishOwnedWindows.Capture(info,state,folder);long value;if(!Int64.TryParse(state.Get("finish_visual_root"),out value)||value<=0)return false;
        root=new IntPtr(value);pid=checked((int)state.Int("finish_visual_pid"));started=state.Int("finish_visual_start");if(!SameWindow())return false;
        placement=new Placement{Length=Marshal.SizeOf(typeof(Placement))};if(!GetWindowPlacement(root,ref placement))return false;
        var area=Screen.FromControl(owner).WorkingArea;Rectangle native,observe;
        if(!Partition(area,owner.Width,out native,out observe)||observe.Width<observer.MinimumSize.Width)return false;
        ShowWindow(root,9);
        if(!SetWindowPos(root,IntPtr.Zero,native.X,native.Y,native.Width,native.Height,0x14)){SetWindowPlacement(root,ref placement);root=IntPtr.Zero;return false;}
        owner.Location=new Point(area.Right-owner.Width,area.Top);owner.Height=area.Height;observer.Bounds=observe;return true;
    }
    public void Dispose(){if(restored)return;restored=true;if(root!=IntPtr.Zero&&SameWindow()&&placement.Length!=0)SetWindowPlacement(root,ref placement);}
}

public sealed partial class DesktopReplayForm
{
    Button observationButton;ObservationForm observation;ObservationLayout observationLayout;
    int observationHeartbeat;DateTime observationWritten;ObservationData observationData;
    Button ObservationButton()
    {
        observationButton=PanelButton("多周期",Card);observationButton.AccessibleName="OpenObservation";observationButton.Click+=(s,e)=>OpenObservation();return observationButton;
    }
    void OpenObservation()
    {
        RefreshState();if(snapshot==null||snapshot.Get("mtf_protocol")!="1")return;
        if(observationData!=null&&!observationData.CanDisplay(snapshot))observationData=null;
        if(observation!=null&&!observation.IsDisposed){observation.Activate();return;}
        var view=ObservationView.Load(folder,snapshot.Get("boot"));observation=new ObservationForm(view,false);observation.TopMost=TopMost;
        observation.ViewChanged=()=>{try{TrainingStore.AtomicJson(ObservationView.PathFor(folder,view.Boot),view);}catch(IOException ex){observation.Status("观察偏好保存失败："+ex.Message);}};
        observationLayout=new ObservationLayout();bool arranged=false;try{arranged=observationLayout.Arrange(this,observation,session,snapshot,folder);}catch(IOException){}catch(InvalidOperationException){}
        if(!arranged){var area=Screen.FromControl(this).WorkingArea;observation.Bounds=new Rectangle(area.Left+12,area.Top+12,Math.Min(area.Width-24,observation.Width),Math.Min(area.Height-24,observation.Height));}
        observation.FormClosed+=(s,e)=>{observationLayout.Dispose();observationLayout=null;observation=null;ObservationSubscribe(false);};
        observationWritten=DateTime.MinValue;observationHeartbeat=0;observation.Show(this);RefreshObservation();
    }
    void ObservationSubscribe(bool enabled)
    {
        if(snapshot==null||snapshot.Get("mtf_protocol")!="1")return;
        try{DesktopSessionFiles.AtomicText(Path.Combine(folder,"mtf-request.txt"),"protocol=1\r\nsession="+session.Id+"\r\nboot="+snapshot.Get("boot")+"\r\nenabled="+(enabled?"1":"0")+"\r\nstamp="+unchecked((uint)Environment.TickCount).ToString(CultureInfo.InvariantCulture)+"\r\ncomplete=1\r\n");}catch(IOException ex){if(observation!=null)observation.Status("等待测试器："+ex.Message);}
    }
    void RefreshObservation()
    {
        if(observationButton!=null)observationButton.Enabled=snapshot!=null&&snapshot.Get("mtf_protocol")=="1";
        if(observation==null||observation.IsDisposed)return;
        if(snapshot==null||observation.Chart.View.Boot!=snapshot.Get("boot")){observation.Close();observationData=null;return;}
        if(observationData!=null&&!observationData.CanDisplay(snapshot))observationData=null;
        if(observationHeartbeat==0||unchecked(Environment.TickCount-observationHeartbeat)>1000){observationHeartbeat=Environment.TickCount;ObservationSubscribe(true);}
        try
        {
            string path=Path.Combine(folder,snapshot.Get("finish_sealed")=="1"||snapshot.Get("phase")=="ended"?"mtf-seal.txt":"mtf-state.txt");var written=File.GetLastWriteTimeUtc(path);
            if(written!=observationWritten&&File.Exists(path)){var data=ObservationData.Read(path,session.Id,snapshot.Get("boot"),session.Symbol);if(data.AsOf>snapshot.Int("now"))return;observationData=data;observationWritten=written;}
            if(observationData!=null)observation.SetData(observationData,snapshot);
            else observation.Status("等待测试器生成 M5 / M15 / H1 / H4 图表…");
        }
        catch(IOException ex){observation.Status("图表暂未同步："+ex.Message);}catch(FormatException ex){observation.Status(ex.Message);}catch(KeyNotFoundException){observation.Status("观察数据尚未完整，正在重试…");}
    }
    void CloseObservation(){if(observation!=null&&!observation.IsDisposed)observation.Close();if(observationLayout!=null)observationLayout.Dispose();}
}
