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

public sealed class ChartRequest
{
    public string Action="",Kind="",Side="",Ticket="0";
    public decimal Lots;
    public decimal PlaybackSpeed;
    public double Entry,SL,TP;
}
public sealed class ChartItem
{
    public string Kind,Ticket,Side; public decimal Lots; public double Entry,SL,TP;
    public string Key { get { return Kind+":"+Ticket; } }
    public ChartItem Copy() { return (ChartItem)MemberwiseClone(); }
    public override string ToString() { return "#"+Ticket+" "+(Side=="buy"?"买":"卖")+(Kind=="O"?"限":"单")+" "+Lots.ToString("0.###")+"手"; }
    public static List<ChartItem> Parse(string raw)
    {
        var list=new List<ChartItem>();
        foreach(string part in raw.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
        {
            string[] a=part.Split(','); decimal lots; double entry,sl,tp; ulong ticket;
            if(a.Length!=7 || (a[0]!="O" && a[0]!="P") || !UInt64.TryParse(a[1],out ticket) || ticket==0 ||
               (a[2]!="buy" && a[2]!="sell") || !Decimal.TryParse(a[3],NumberStyles.Number,CultureInfo.InvariantCulture,out lots) ||
               !Double.TryParse(a[4],NumberStyles.Float,CultureInfo.InvariantCulture,out entry) || !Double.TryParse(a[5],NumberStyles.Float,CultureInfo.InvariantCulture,out sl) ||
               !Double.TryParse(a[6],NumberStyles.Float,CultureInfo.InvariantCulture,out tp) || !ChartMath.Finite(entry) || !ChartMath.Finite(sl) || !ChartMath.Finite(tp)) continue;
            list.Add(new ChartItem{Kind=a[0],Ticket=a[1],Side=a[2],Lots=lots,Entry=entry,SL=sl,TP=tp});
        }
        return list;
    }
}
public static class ChartMath
{
    public static string N(double n) { return n.ToString("0.########",CultureInfo.InvariantCulture); }
    public static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
    public static double Round(double n,double step) { if(!Finite(n) || !Finite(step) || step<=0) throw new ArgumentException("无效价格单位"); return Math.Round(n/step,MidpointRounding.AwayFromZero)*step; }
    public static string Side(double price,double bid) { return price<bid?"buy":price>bid?"sell":""; }
    public static double PriceAt(int y,int height,double min,double max)
    { if(height<2 || !Finite(min) || !Finite(max) || max<=min) throw new ArgumentException("坐标尚未同步"); return max-(double)y/(height-1)*(max-min); }
    public static int YAt(double price,int height,double min,double max)
    { if(height<2 || max<=min) throw new ArgumentException("坐标尚未同步"); return (int)Math.Round((max-price)/(max-min)*(height-1)); }
    public static bool ValidLimit(string side,double entry,double bid,double ask,double gap)
    { return side=="buy"?entry<bid && ask-entry+1e-9>=gap:side=="sell" && entry>bid && entry-bid+1e-9>=gap; }
    public static bool FitsChart(int actualWidth,int actualHeight,int plotWidth,int plotHeight)
    { return plotWidth>=200 && plotHeight>=150 && actualWidth>=plotWidth-4 && actualWidth<=plotWidth+200 && actualHeight>=plotHeight && actualHeight<plotHeight+1600; }
    public static bool ValidStops(ChartItem item,double bid,double ask,double gap)
    {
        double reference=item.Kind=="O"?item.Entry:item.Side=="buy"?bid:ask;
        if(!Finite(item.SL) || !Finite(item.TP) || item.SL<0 || item.TP<0)return false;
        return item.Side=="buy"?
            (item.SL==0 || (item.SL<reference && reference-item.SL+1e-9>=gap)) && (item.TP==0 || (item.TP>reference && item.TP-reference+1e-9>=gap)):
            (item.SL==0 || (item.SL>reference && item.SL-reference+1e-9>=gap)) && (item.TP==0 || (item.TP<reference && reference-item.TP+1e-9>=gap));
    }
    public static ChartItem Drag(ChartItem original,string part,double price)
    {
        var copy=original.Copy();
        if(part=="SL") copy.SL=price;
        else if(part=="TP") copy.TP=price;
        else if(part=="ENTRY" && original.Kind=="O")
        {
            double delta=price-original.Entry; copy.Entry=price;
            if(copy.SL>0) copy.SL+=delta; if(copy.TP>0) copy.TP+=delta;
        }
        else throw new ArgumentException("该价格不能拖动");
        return copy;
    }
}

public static class ChartNative
{
    public const int MouseMove=0x200,LeftDown=0x201,LeftUp=0x202,RightDown=0x204,RightUp=0x205;
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; public Rectangle ToRectangle(){return Rectangle.FromLTRB(Left,Top,Right,Bottom);} }
    [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point pt; public uint mouseData,flags,time; public UIntPtr extra; }
    public delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    public delegate bool EnumProc(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc,IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent,EnumProc proc,IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window,ref Point point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int capacity);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    public static void SetDpiAware() { try { if(!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); } catch(EntryPointNotFoundException){SetProcessDPIAware();} }
    public static string Title(IntPtr window) { var s=new StringBuilder(1024); GetWindowText(window,s,s.Capacity); return s.ToString(); }
    public static string Class(IntPtr window) { var s=new StringBuilder(256); GetClassName(window,s,s.Capacity); return s.ToString(); }
    public static bool IsChartSurface(string name)
    {return name.IndexOf("Chart",StringComparison.OrdinalIgnoreCase)>=0 || name.StartsWith("AfxFrameOrView",StringComparison.OrdinalIgnoreCase);}
    public static Rectangle ClientScreen(IntPtr window)
    {
        Rect r; if(!GetClientRect(window,out r)) return Rectangle.Empty;
        var p=new Point(0,0); if(!ClientToScreen(window,ref p)) return Rectangle.Empty;
        return new Rectangle(p.X,p.Y,r.Right-r.Left,r.Bottom-r.Top);
    }
    public static bool IsTesterRoot(IntPtr root,DesktopSnapshot state)
    {
        if(root==IntPtr.Zero || !IsWindowVisible(root) || IsIconic(root)) return false;
        string title=Title(root);
        if(!MatchesTesterTitle(title,state)) return false;
        uint id; GetWindowThreadProcessId(root,out id);
        try { return Process.GetProcessById((int)id).ProcessName.Equals("metatester64",StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }
    public static bool MatchesTesterTitle(string title,DesktopSnapshot state)
    {
        if(state==null || title==null || !title.Contains("CN_Manual_Replay_Chart_Preview"))return false;
        string period=state.Get("period");if(period.StartsWith("PERIOD_",StringComparison.Ordinal))period=period.Substring(7);
        if(state.Get("symbol")=="" || period=="" || state.Int("from")<=0 || state.Int("to")<=state.Int("from"))return false;
        try{return title.EndsWith(" on "+state.Get("symbol")+","+period+" from "+DesktopSnapshot.Epoch.AddSeconds(state.Int("from")).ToString("yyyy.MM.dd",CultureInfo.InvariantCulture)+" to "+DesktopSnapshot.Epoch.AddSeconds(state.Int("to")).ToString("yyyy.MM.dd",CultureInfo.InvariantCulture),StringComparison.Ordinal);}
        catch(ArgumentOutOfRangeException){return false;}
    }
    public static bool StartedForSession(IntPtr root,DateTime createdUtc)
    {
        uint id;GetWindowThreadProcessId(root,out id);
        try{using(var process=Process.GetProcessById((int)id))return process.StartTime.ToUniversalTime()>=createdUtc.ToUniversalTime().AddSeconds(-2);}
        catch{return false;}
    }
}

public sealed class ChartLayer : Form
{
    public Action<Graphics> PaintChart;
    public ChartLayer()
    {
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true; AutoScaleMode=AutoScaleMode.None;
        BackColor=Color.Fuchsia; TransparencyKey=Color.Fuchsia;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint,true);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x20|0x80|0x80000|0x8000000; return p; } }
    protected override void WndProc(ref Message m) { if(m.Msg==0x84){m.Result=new IntPtr(-1);return;} base.WndProc(ref m); }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.Fuchsia); if(PaintChart!=null) PaintChart(e.Graphics); }
}

public sealed class ChartController : IDisposable
{
    readonly Form owner; readonly string session; readonly Func<decimal> lots;readonly Func<double,decimal> priceLots; readonly Action<ChartRequest> send; readonly Action<string> report;
    readonly Control demoCanvas; readonly ChartLayer layer=new ChartLayer();
    readonly List<ChartItem> demoItems=new List<ChartItem>();
    ChartNative.HookProc hookProc; IntPtr hook=IntPtr.Zero,chartWindow=IntPtr.Zero,root=IntPtr.Zero;
    DesktopSnapshot state; List<ChartItem> items=new List<ChartItem>(); Rectangle area;
    double min,max,step=0.001; bool enabled,demo=true,busy,ready,suppressRightUp,suppressLeftUp,disposed;
    ChartItem cancelClickItem; Rectangle cancelClickRect; string targetReason="";
    string selected="",dragPart="",lastMessage=""; ChartItem dragOriginal,ghost;
    Rectangle dragArea; double dragMin,dragMax,dragEntryOffset; int demoId=0,findAt=0; bool moveQueued;
    System.Drawing.Point dragPoint,dragStart; bool moved; int messageUntil;
    public ChartController(Form owner,string session,Func<decimal> lots,Action<ChartRequest> send,Action<string> report,Control demoCanvas=null,Func<double,decimal> priceLots=null)
    {
        this.owner=owner; this.session=session; this.lots=lots;this.priceLots=priceLots; this.send=send; this.report=report; this.demoCanvas=demoCanvas;
        layer.PaintChart=Paint;
        hookProc=Hook;
        hook=ChartNative.SetWindowsHookEx(14,hookProc,ChartNative.GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) Message("鼠标交互未能启动；普通面板仍可使用。");
    }
    double Num(string key) { return state==null?0:(double)state.Number(key); }
    void Message(string text,bool sticky=true) { if(!sticky && unchecked(Environment.TickCount-messageUntil)<0)return; if(sticky)messageUntil=unchecked(Environment.TickCount+2500); if(text!=lastMessage) {lastMessage=text; report(text);} }
    void FindTarget()
    {
        if(demoCanvas!=null) { chartWindow=demoCanvas.Handle; root=owner.Handle; return; }
        if(state==null) return;
        IntPtr reported=new IntPtr(state.Int("plot_handle"));
        if(ChartNative.IsWindow(reported) && ChartNative.IsTesterRoot(ChartNative.GetAncestor(reported,2),state))
        {chartWindow=reported;root=ChartNative.GetAncestor(reported,2);return;}
        var roots=new List<IntPtr>();
        ChartNative.EnumWindows((w,p)=>{if(ChartNative.IsTesterRoot(w,state)) roots.Add(w);return true;},IntPtr.Zero);
        if(roots.Count>1)
        {
            try{var info=DesktopSessionFiles.Read(session);roots=roots.Where(w=>ChartNative.StartedForSession(w,info.CreatedUtc)).ToList();}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        if(roots.Count!=1) {chartWindow=IntPtr.Zero;root=IntPtr.Zero;return;}
        root=roots[0]; int width=(int)state.Int("plot_width"),height=(int)state.Int("plot_height");
        var candidates=new List<IntPtr>();
        var details=new List<string>();
        ChartNative.EnumChildWindows(root,(w,p)=>
        {
            var r=ChartNative.ClientScreen(w);
            string cls=ChartNative.Class(w);
            if(r.Width>=200 && r.Height>=150)details.Add(w+" "+cls+" "+r.Width+"x"+r.Height);
            if(ChartNative.IsWindowVisible(w) && ChartMath.FitsChart(r.Width,r.Height,width,height) &&
                (ChartNative.IsChartSurface(cls) || Math.Abs(r.Width-width)<=4))candidates.Add(w);
            return true;
        },IntPtr.Zero);
        chartWindow=candidates.OrderBy(w=>ChartNative.IsChartSurface(ChartNative.Class(w))?0:1)
            .ThenBy(w=>Math.Abs(ChartNative.ClientScreen(w).Width-width)).ThenBy(w=>ChartNative.ClientScreen(w).Height).FirstOrDefault();
        targetReason=chartWindow==IntPtr.Zero?"未找到匹配的主图窗口":"";
        try { File.WriteAllText(Path.Combine(ReplayFiles.Home,"图表坐标诊断.txt"),"Expert=CN_Manual_Replay_Chart_Preview\r\nExpected="+width+"x"+height+"\r\nReportedHandle="+state.Get("plot_handle")+"\r\nSelected="+chartWindow+"\r\n"+String.Join("\r\n",details)); }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void Update(DesktopSnapshot snapshot,bool allow,bool rehearsal,bool waiting)
    {
        if(disposed) return;
        bool modeChanged=demo!=rehearsal;
        state=snapshot; enabled=allow; demo=rehearsal; busy=waiting;
        if(modeChanged) {CancelDrag("模式已切换");selected="";cancelClickItem=null;}
        if(!enabled || state==null || !state.Fresh || hook==IntPtr.Zero)
        {
            ready=false; layer.Hide();
            if(dragOriginal!=null) CancelDrag("回放暂停或连接中断，此次拖动未提交");
            Message(!allow?"图表操作已关闭":state==null?"等待MT5回放连接":"暂停/结束时不接收图表交易操作"); return;
        }
        int now=Environment.TickCount;
        if(chartWindow==IntPtr.Zero || !ChartNative.IsWindow(chartWindow) || unchecked(now-findAt)>1000)
        {FindTarget();findAt=now;}
        var client=ChartNative.ClientScreen(chartWindow);
        int height=demoCanvas==null?(int)state.Int("plot_height"):client.Height;
        int width=demoCanvas==null?(int)state.Int("plot_width"):client.Width;
        double nextMin=Num("plot_min"),nextMax=Num("plot_max");
        ready=ChartMath.FitsChart(client.Width,client.Height,width,height) && nextMax>nextMin;
        if(!ready)
        {layer.Hide();CancelDrag("图表尺寸尚未同步");Message("坐标未同步："+(chartWindow==IntPtr.Zero?targetReason:client.Width+"×"+client.Height+" / "+width+"×"+height));return;}
        var nextArea=new Rectangle(client.X,client.Y,Math.Min(client.Width,width),height);
        if(dragOriginal!=null && (nextArea!=dragArea || Math.Abs(nextMin-dragMin)>1e-8 || Math.Abs(nextMax-dragMax)>1e-8)) CancelDrag("坐标轴发生变化，此次拖动未提交；请重试");
        area=nextArea; min=nextMin;max=nextMax;step=Num("tick_size"); if(step<=0)step=0.001;
        items=demo?demoItems:ChartItem.Parse(state.Get("chart_items"));
        if(!items.Any(x=>x.Key==selected)) selected="";
        if(dragOriginal!=null && !items.Any(x=>x.Key==dragOriginal.Key)) CancelDrag("订单状态已改变，请重新选择");
        var foreground=ChartNative.GetForegroundWindow();
        if(foreground!=root && foreground!=owner.Handle) layer.Hide();
        else {layer.Bounds=area; if(!layer.Visible)layer.Show(); layer.Invalidate();}
        if(dragOriginal==null) Message(demo?"仅演练：右键直接挂单，价格后×撤单；拖SL/TP。":"右键直接提交限价单，价格后×撤单；松手修改。",false);
    }
    bool InChart(ChartNative.Point p)
    {
        if(!ready || !enabled || !area.Contains(p.X,p.Y) || p.X>=area.Right-70) return false;
        var at=ChartNative.WindowFromPoint(p); var atRoot=ChartNative.GetAncestor(at,2);
        return atRoot==root; // No input is captured over the floating panel or any unrelated app.
    }
    IntPtr Hook(int code,IntPtr message,IntPtr data)
    {
        try
        {
            if(code<0 || disposed) return ChartNative.CallNextHookEx(hook,code,message,data);
            int kind=message.ToInt32(); var m=(ChartNative.MouseData)Marshal.PtrToStructure(data,typeof(ChartNative.MouseData));
            if(kind==ChartNative.RightUp && suppressRightUp) {suppressRightUp=false;return new IntPtr(1);}
            if(kind==ChartNative.LeftUp && suppressLeftUp)
            {
                suppressLeftUp=false;var candidate=cancelClickItem;cancelClickItem=null;
                var end=new System.Drawing.Point(m.pt.X,m.pt.Y);
                if(candidate!=null && cancelClickRect.Contains(end))owner.BeginInvoke((Action)(()=>FinishCancel(candidate,end)));
                return new IntPtr(1);
            }
            if(dragOriginal!=null && kind==ChartNative.MouseMove)
            {
                dragPoint=new System.Drawing.Point(m.pt.X,m.pt.Y);
                if(!moveQueued){moveQueued=true;owner.BeginInvoke((Action)(()=>{moveQueued=false;MoveDrag(dragPoint);}));}
                // Let Windows move the pointer. The initial left-down was consumed,
                // so the underlying chart never started a pan or level drag.
                return ChartNative.CallNextHookEx(hook,code,message,data);
            }
            if(dragOriginal!=null && kind==ChartNative.LeftUp)
            {var end=new System.Drawing.Point(m.pt.X,m.pt.Y);owner.BeginInvoke((Action)(()=>FinishDrag(end)));return new IntPtr(1);}
            if(!ready || busy || state==null || !state.Fresh || !InChart(m.pt)) return ChartNative.CallNextHookEx(hook,code,message,data);
            if(kind==ChartNative.RightDown)
            {
                suppressRightUp=true;
                double entry=PriceAt(new System.Drawing.Point(m.pt.X,m.pt.Y));string side=ChartMath.Side(entry,Num("bid"));decimal size=priceLots==null?lots():priceLots(entry);
                owner.BeginInvoke((Action)(()=>{if(size<=0)Message("该价格计算手数低于最小手数或参数不可用，本次未挂单。");else Place(entry,side,size);})); return new IntPtr(1);
            }
            if(kind==ChartNative.LeftDown)
            {
                var p=new System.Drawing.Point(m.pt.X,m.pt.Y);
                foreach(var badge in EntryBadges())
                {
                    var candidate=items.FirstOrDefault(x=>x.Key==badge.Key);if(candidate==null || candidate.Kind!="O")continue;
                    var cancel=CancelRect(badge.Value);
                    if(cancel.Contains(p.X-area.Left,p.Y-area.Top))
                    {cancelClickItem=candidate.Copy();cancelClickRect=new Rectangle(area.Left+cancel.X,area.Top+cancel.Y,cancel.Width,cancel.Height);suppressLeftUp=true;return new IntPtr(1);}
                }
                if(BeginDrag(p)) return new IntPtr(1);
            }
        }
        catch(Exception ex) { Message("图表交互已暂停："+ex.Message); ready=false;layer.Hide(); }
        return ChartNative.CallNextHookEx(hook,code,message,data);
    }
    double PriceAt(System.Drawing.Point p) {return ChartMath.Round(ChartMath.PriceAt(p.Y-area.Top,area.Height,min,max),step);}
    List<ChartItem> Candidates(int y) {return items.Where(x=>Math.Abs(ChartMath.YAt(x.Entry,area.Height,min,max)-y)<=5).ToList();}
    Rectangle HandleRect(string part,int y)
    { int x=area.Width-(part=="SL"?240:160);return new Rectangle(x,y-12,65,24); }
    Dictionary<string,Rectangle> EntryBadges()
    {
        var result=new Dictionary<string,Rectangle>();int previous=27;
        foreach(var item in items.OrderBy(x=>ChartMath.YAt(x.Key==selected && ghost!=null?ghost.Entry:x.Entry,area.Height,min,max)))
        {
            var value=item.Key==selected && ghost!=null?ghost:item;
            int y=ChartMath.YAt(value.Entry,area.Height,min,max);if(y<0 || y>=area.Height)continue;
            int top=Math.Max(previous,Math.Max(28,y-12));if(top+25>=area.Height)continue;
            string text=EntryText(value);
            int width=Math.Min(Math.Max(120,area.Width-280),TextRenderer.MeasureText(text,owner.Font).Width+14+(item.Kind=="O"?28:0));
            result[item.Key]=new Rectangle(12,top,width,25);previous=top+27;
        }
        return result;
    }
    string EntryText(ChartItem item) {return (demo?"演练 ":"")+item+"  "+item.Entry.ToString("F"+(int)state.Int("digits"));}
    static Rectangle CancelRect(Rectangle badge) {return new Rectangle(badge.Right-27,badge.Top,27,badge.Height);}
    void Place(double entry,string side,decimal size)
    {
        if(!ready || busy || !enabled || state==null || !state.Fresh || !ChartMath.ValidLimit(side,entry,Num("bid"),Num("ask"),Num("stops_gap")))
        {Message("行情已变化，请重新选价；没有切换买卖方向。");return;}
        if(demo)
        {
            var item=new ChartItem{Kind="O",Ticket=(++demoId).ToString(CultureInfo.InvariantCulture),Side=side,Lots=size,Entry=entry};
            demoItems.Add(item);items=demoItems;selected=item.Key;LogDemo("新增限价演练",item);layer.Invalidate();return;
        }
        send(new ChartRequest{Action="place_limit",Kind="O",Side=side,Lots=size,Entry=entry});
    }
    bool BeginDrag(System.Drawing.Point point)
    {
        var relative=new System.Drawing.Point(point.X-area.Left,point.Y-area.Top);
        var active=items.FirstOrDefault(x=>x.Key==selected); string part="";
        var badge=EntryBadges().FirstOrDefault(x=>x.Value.Contains(relative));
        if(badge.Key!=null){active=items.First(x=>x.Key==badge.Key);selected=active.Key;if(active.Kind=="P"){layer.Invalidate();return true;}part="ENTRY";}
        if(active!=null && part=="")
        {
            int entryY=ChartMath.YAt(active.Entry,area.Height,min,max);
            if(HandleRect("SL",entryY).Contains(relative))part="SL";
            else if(HandleRect("TP",entryY).Contains(relative))part="TP";
            else if(active.SL>0 && Math.Abs(ChartMath.YAt(active.SL,area.Height,min,max)-relative.Y)<=5)part="SL";
            else if(active.TP>0 && Math.Abs(ChartMath.YAt(active.TP,area.Height,min,max)-relative.Y)<=5)part="TP";
        }
        if(part=="")
        {
            var matches=Candidates(relative.Y);
            if(matches.Count==0)return false;
            if(matches.Count>1 && !matches.Any(x=>x.Key==selected)){Message("这个价位有多笔订单，请点击对应价格标签选择。");return true;}
            active=matches.FirstOrDefault(x=>x.Key==selected)??matches[0];selected=active.Key;
            if(active.Kind!="O"){layer.Invalidate();return true;}
            part="ENTRY";
        }
        dragOriginal=active.Copy();ghost=active.Copy();dragPart=part;dragArea=area;dragMin=min;dragMax=max;dragStart=point;moved=false;
        dragEntryOffset=part=="ENTRY"?ChartMath.PriceAt(point.Y-area.Top,area.Height,min,max)-active.Entry:0;
        layer.Invalidate();return true;
    }
    void MoveDrag(System.Drawing.Point p)
    {
        if(dragOriginal==null)return;
        if(!moved && Math.Abs(p.Y-dragStart.Y)<3)return;
        moved=true;
        int y=Math.Max(0,Math.Min(area.Height-1,p.Y-area.Top));
        double price=ChartMath.Round(ChartMath.PriceAt(y,area.Height,min,max)-dragEntryOffset,step);
        ghost=ChartMath.Drag(dragOriginal,dragPart,price);layer.Invalidate();
        Message((demo?"演练预览：":"修改预览：")+dragPart+" "+price.ToString("F"+(int)state.Int("digits"))+"；松手提交一次，移出图表取消。");
    }
    void FinishDrag(System.Drawing.Point point)
    {
        if(dragOriginal==null)return;
        var original=dragOriginal; MoveDrag(point); var result=ghost;
        bool valid=area.Contains(point) && ready && state!=null && state.Fresh && items.Any(x=>x.Key==original.Key && Math.Abs(x.Entry-original.Entry)<step/2 && Math.Abs(x.SL-original.SL)<step/2 && Math.Abs(x.TP-original.TP)<step/2);
        dragOriginal=null;ghost=null;dragPart="";
        if(!moved){layer.Invalidate();return;}
        if(!valid){Message("此次拖动已取消，未提交。");layer.Invalidate();return;}
        if(!ChartMath.ValidStops(result,Num("bid"),Num("ask"),Num("stops_gap")) ||
           (result.Kind=="O" && !ChartMath.ValidLimit(result.Side,result.Entry,Num("bid"),Num("ask"),Num("stops_gap"))))
        {Message("目标价格方向或最小距离不合法，已恢复原位。");layer.Invalidate();return;}
        if(demo)
        {
            int at=demoItems.FindIndex(x=>x.Key==original.Key);if(at>=0)demoItems[at]=result;
            items=demoItems;LogDemo("拖线演练",result);layer.Invalidate();return;
        }
        send(Request(result));layer.Invalidate();
    }
    static ChartRequest Request(ChartItem item)
    {return new ChartRequest{Action=item.Kind=="O"?"modify_limit":"modify_position",Kind=item.Kind,Ticket=item.Ticket,Side=item.Side,Lots=item.Lots,Entry=item.Entry,SL=item.SL,TP=item.TP};}
    void ChangeStop(ChartItem item,string part,double value)
    {
        var changed=ChartMath.Drag(item,part,value);
        if(demo){int at=demoItems.FindIndex(x=>x.Key==item.Key);if(at>=0)demoItems[at]=changed;LogDemo("清除止盈止损演练",changed);layer.Invalidate();}
        else send(Request(changed));
    }
    void CancelItem(ChartItem item)
    {
        if(!ready || busy || !enabled || state==null || !state.Fresh || item.Kind!="O" || !items.Any(x=>x.Key==item.Key && x.Kind=="O"))
        {Message("挂单状态已改变，此次没有撤单或平仓。");return;}
        if(demo){demoItems.RemoveAll(x=>x.Key==item.Key);LogDemo("撤销演练",item);selected="";layer.Invalidate();}
        else send(new ChartRequest{Action="cancel_limit",Kind="O",Ticket=item.Ticket,Side=item.Side,Lots=item.Lots,Entry=item.Entry,SL=item.SL,TP=item.TP});
    }
    void FinishCancel(ChartItem item,System.Drawing.Point point)
    {
        if(!ready)return;Rectangle badge;
        if(EntryBadges().TryGetValue(item.Key,out badge) && CancelRect(badge).Contains(point.X-area.Left,point.Y-area.Top))CancelItem(item);
    }
    void CancelDrag(string text) {if(dragOriginal==null)return;dragOriginal=null;ghost=null;dragPart="";Message(text);layer.Invalidate();}
    void LogDemo(string action,ChartItem item)
    {
        string dir=Path.Combine(ReplayFiles.Home,"图表演练记录");Directory.CreateDirectory(dir);
        string file=Path.Combine(dir,session+".csv");
        Func<string,string> q=x=>"\""+x.Replace("\"","\"\"")+"\"";
        if(!File.Exists(file))File.WriteAllText(file,"操作UTC时间,历史时间,模式,动作,演练编号,方向,手数,入场,止损,止盈\r\n",new UTF8Encoding(true));
        File.AppendAllText(file,String.Join(",",new[]{DateTime.UtcNow.ToString("O"),state.Now.ToString("yyyy.MM.dd HH:mm:ss"),demoCanvas==null?"历史图表仅演练":"离线样例演练",action,item.Ticket,item.Side,item.Lots.ToString(CultureInfo.InvariantCulture),ChartMath.N(item.Entry),ChartMath.N(item.SL),ChartMath.N(item.TP)}.Select(q))+"\r\n",new UTF8Encoding(false));
        Message("演练已保存；没有向测试器发送订单。");
    }
    void Line(Graphics g,double price,Color color,string text,bool dashed=false)
    {
        if(price<=0)return;int y=ChartMath.YAt(price,area.Height,min,max);if(y<0 || y>=area.Height)return;
        using(var p=new Pen(color,1.5F)){if(dashed)p.DashStyle=DashStyle.Dash;g.DrawLine(p,0,y,area.Width-70,y);}
        var rect=new Rectangle(12,Math.Max(0,y-20),400,20);
        using(var fill=new SolidBrush(Color.FromArgb(18,27,42)))g.FillRectangle(fill,rect);
        TextRenderer.DrawText(g,text+"  "+price.ToString("F"+(int)state.Int("digits")),owner.Font,rect,color,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
    }
    void Paint(Graphics g)
    {
        if(!ready || state==null)return;
        if(demoCanvas==null && demo)Line(g,Num("bid"),Color.Goldenrod,"坐标核对 Bid",true);
        var badges=EntryBadges();
        foreach(var item in items)
        {
            bool isSelected=item.Key==selected;var value=isSelected && ghost!=null?ghost:item;
            Color tone=isSelected?Color.DeepSkyBlue:Color.SteelBlue;
            int entryY=ChartMath.YAt(value.Entry,area.Height,min,max);
            if(entryY>=0 && entryY<area.Height)
            {
                using(var p=new Pen(tone,1.5F)){if(ghost!=null && isSelected)p.DashStyle=DashStyle.Dash;g.DrawLine(p,0,entryY,area.Width-70,entryY);}
                Rectangle badge;
                if(badges.TryGetValue(item.Key,out badge))
                {
                    using(var fill=new SolidBrush(Color.FromArgb(18,27,42)))g.FillRectangle(fill,badge);
                    using(var pen=new Pen(tone)){g.DrawRectangle(pen,badge);if(Math.Abs(badge.Top+12-entryY)>14)g.DrawLine(pen,badge.Right,entryY,badge.Right,badge.Top+12);}
                    var text=badge;if(item.Kind=="O")text.Width-=28;
                    TextRenderer.DrawText(g,EntryText(value),owner.Font,text,tone,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    if(item.Kind=="O")
                    {
                        var cancel=CancelRect(badge);
                        using(var fill=new SolidBrush(Color.FromArgb(67,31,39)))g.FillRectangle(fill,cancel);
                        TextRenderer.DrawText(g,"×",owner.Font,cancel,Color.Salmon,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
                    }
                }
            }
            if(!isSelected)continue;
            Line(g,value.SL,Color.Salmon,"#"+value.Ticket+" SL 止损",ghost!=null);
            Line(g,value.TP,Color.LightGreen,"#"+value.Ticket+" TP 止盈",ghost!=null);
            int y=ChartMath.YAt(value.Entry,area.Height,min,max);
            foreach(string part in new[]{"SL","TP"})
            {
                var rect=HandleRect(part,y);
                using(var fill=new SolidBrush(Color.FromArgb(25,38,55)))g.FillRectangle(fill,rect);
                using(var border=new Pen(part=="SL"?Color.Salmon:Color.LightGreen))g.DrawRectangle(border,rect);
                TextRenderer.DrawText(g,part+" ↕",owner.Font,rect,part=="SL"?Color.Salmon:Color.LightGreen,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
        }
        var banner=new Rectangle(12,10,Math.Min(620,area.Width-100),24);
        using(var fill=new SolidBrush(Color.FromArgb(18,27,42)))g.FillRectangle(fill,banner);
        TextRenderer.DrawText(g,demo?"仅演练 · 右键直接挂单 · 价格后×撤单 · 拖SL/TP":"历史模拟 · 右键直接挂单 · 价格后×撤单 · 拖SL/TP",owner.Font,banner,demo?Color.Gold:Color.DeepSkyBlue,TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
    }
    public void Dispose()
    {if(disposed)return;disposed=true;if(hook!=IntPtr.Zero){ChartNative.UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}layer.Dispose();GC.KeepAlive(hookProc);}
}

public sealed class ChartDemoForm : Form
{
    readonly Panel canvas=new Panel();readonly Label status=new Label();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    ChartController controller;
    public ChartDemoForm()
    {
        Text="图表挂单 · 离线演练（不会发送任何订单）";ClientSize=new Size(1180,740);Font=new Font("Microsoft YaHei UI",10F);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(11,17,27);
        canvas.SetBounds(0,0,1180,690);canvas.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;canvas.BackColor=BackColor;Controls.Add(canvas);
        status.SetBounds(12,704,1156,30);status.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;status.ForeColor=Color.Gold;Controls.Add(status);
        canvas.Paint+=(s,e)=>
        {
            using(var pen=new Pen(Color.FromArgb(45,58,75)))for(int i=0;i<=10;i++){int y=i*(canvas.Height-1)/10;e.Graphics.DrawLine(pen,0,y,canvas.Width,y);TextRenderer.DrawText(e.Graphics,(2100-i*20).ToString("F3"),Font,new System.Drawing.Point(canvas.Width-82,y),Color.LightSteelBlue);}
            int mid=(canvas.Height-1)/2;using(var pen=new Pen(Color.Silver))e.Graphics.DrawLine(pen,0,mid,canvas.Width,mid);
            TextRenderer.DrawText(e.Graphics,"离线功能样例 · 当前价 2000.000 · 0.05手\r\n右键直接挂单；价格后×撤单；SL/TP手柄拖线。\r\n重叠订单点击各自价格标签选择；本窗口不连接MT5。",Font,new Rectangle(20,65,760,110),Color.White);
        };
        Shown+=(s,e)=>
        {
            controller=new ChartController(this,"offline-"+Guid.NewGuid().ToString("N"),()=>0.05m,r=>{throw new Exception("离线样例禁止发送请求");},m=>status.Text=m,canvas);
            timer.Interval=100;timer.Tick+=(a,b)=>UpdateDemo();timer.Start();UpdateDemo();
        };
        FormClosed+=(s,e)=>{timer.Dispose();if(controller!=null)controller.Dispose();};
    }
    void UpdateDemo()
    {
        string raw="protocol=1\r\nsession=offline\r\nboot=demo\r\nphase=running\r\nstamp="+unchecked((uint)Environment.TickCount)+
            "\r\nnow=1780272000\r\nbid=2000\r\nask=2000.2\r\ntick_size=0.001\r\nstops_gap=0.1\r\ndigits=3\r\nplot_min=1900\r\nplot_max=2100\r\ncomplete=1";
        controller.Update(DesktopSnapshot.Parse(raw,"offline"),true,true,false);
    }
}

public static class ChartSelfTest
{
    public static int Run()
    {
        try
        {
            int checks=0;
            Action<bool,string> require=(ok,label)=>{if(!ok)throw new Exception(label);checks++;};
            require(ChartMath.Side(1999,2000)=="buy" && ChartMath.Side(2001,2000)=="sell" && ChartMath.Side(2000,2000)=="","自动方向与同价边界");
            require(ChartMath.ValidLimit("buy",1990,2000,2000.2,1) && ChartMath.ValidLimit("sell",2010,2000,2000.2,1),"买卖限价合法");
            require(!ChartMath.ValidLimit("buy",2001,2000,2000.2,0) && !ChartMath.ValidLimit("sell",1999,2000,2000.2,0),"行情变化不能翻转方向");
            require(!ChartMath.ValidLimit("sell",2000.05,2000,2000.2,0.1),"最小距离");
            require(ChartMath.FitsChart(3033,1700,2963,1462),"图表宽度允许原生价格轴边距");
            require(!ChartMath.FitsChart(2900,1700,2963,1462) && !ChartMath.FitsChart(3400,1700,2963,1462),"拒绝不匹配图表宽度");
            require(ChartNative.IsChartSurface("AfxFrameOrView140su") && ChartNative.IsChartSurface("MetaTrader::Chart") && !ChartNative.IsChartSurface("SysListView32"),"原生主图类型与表格区分");
            string bindingRaw="protocol=1\r\nsession=binding\r\nboot=test\r\nphase=running\r\nstamp="+unchecked((uint)Environment.TickCount)+"\r\nperiod=PERIOD_M5\r\nsymbol=XAUUSDm\r\nfrom=1780358400\r\nto=1780444800\r\nnow=1780358400\r\ncomplete=1";
            var binding=DesktopSnapshot.Parse(bindingRaw,"binding");string title="Strategy Tester Visualization : CN_Manual_Replay_Chart_Preview on XAUUSDm,M5 from 2026.06.02 to 2026.06.03";
            require(ChartNative.MatchesTesterTitle(title,binding),"新场按品种周期日期绑定");
            require(!ChartNative.MatchesTesterTitle(title.Replace("2026.06.02 to 2026.06.03","2026.06.01 to 2026.06.02"),binding),"结束旧场不与顺延新场竞争图表");
            require(!ChartNative.MatchesTesterTitle(title.Replace(",M5 ",",H1 "),binding),"不同周期不绑定");
            require(!ChartNative.MatchesTesterTitle(title.Replace("XAUUSDm,","XAUUSD,"),binding),"品种后缀必须完全匹配");
            require(!ChartNative.MatchesTesterTitle(title.Replace("CN_Manual_Replay_Chart_Preview","OtherEA"),binding),"其他EA不绑定");
            require(!ChartNative.MatchesTesterTitle(title,DesktopSnapshot.Parse(bindingRaw.Replace("from=1780358400","from=0"),"binding")),"不完整场次坐标不绑定");
            foreach(int height in new[]{300,690,1000,1531})foreach(double price in new[]{1900.0,1950,2000,2050,2100})
                require(Math.Abs(ChartMath.PriceAt(ChartMath.YAt(price,height,1900,2100),height,1900,2100)-price)<=200.0/(height-1),"像素与价格往返");
            require(Math.Abs(ChartMath.Round(2000.12345,0.001)-2000.123)<1e-8,"报价步长对齐");
            var item=new ChartItem{Kind="O",Ticket="11",Side="buy",Lots=0.05m,Entry=1990,SL=1980,TP=2010};
            var sl=ChartMath.Drag(item,"SL",1975);require(sl.Entry==1990 && sl.SL==1975 && sl.TP==2010,"SL仅修改止损");
            var tp=ChartMath.Drag(item,"TP",2020);require(tp.Entry==1990 && tp.SL==1980 && tp.TP==2020,"TP仅修改止盈");
            var entry=ChartMath.Drag(item,"ENTRY",1985);require(entry.Entry==1985 && entry.SL==1975 && entry.TP==2005,"入场拖动保持止盈止损距离");
            require(item.Entry==1990 && item.SL==1980 && item.TP==2010,"预览不改确认状态");
            require(ChartMath.ValidStops(item,2000,2000.2,1),"买限止盈止损合法");
            var wrong=ChartMath.Drag(item,"SL",1991);require(!ChartMath.ValidStops(wrong,2000,2000.2,1),"买限止损不可超过入场");
            var position=new ChartItem{Kind="P",Side="buy",Entry=1900,SL=1990,TP=2010};require(ChartMath.ValidStops(position,2000,2000.2,1),"买入持仓允许盈利保护止损");
            var parsed=ChartItem.Parse("O,11,buy,0.05,1990,1980,2010;P,12,sell,0.1,2010,2020,1990;O,0,buy,0.1,1,0,0;bad;");
            require(parsed.Count==2 && parsed[0].Key=="O:11" && parsed[1].Kind=="P","挂单与持仓绑定分别解析");
            bool blocked=false;try{ChartMath.Drag(parsed[1],"ENTRY",2000);}catch(ArgumentException){blocked=true;}require(blocked,"已成交入场价格不可拖动");
            File.WriteAllText(Path.Combine(ReplayFiles.Home,"chart-self-test.txt"),"PASS: "+checks+" chart interaction checks; automatic limit direction, locked side recheck, price/coordinate mapping, tick rounding, independent SL/TP, entry-relative shifts, immutable preview, pending/position identity.\r\n");
            return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(ReplayFiles.Home,"chart-self-test-error.txt"),ex.ToString());return 99;}
    }
}
