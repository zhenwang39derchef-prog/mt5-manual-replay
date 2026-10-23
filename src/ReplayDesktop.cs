using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public sealed class DesktopSessionInfo
{
    public string Id { get; set; }
    public string Symbol { get; set; }
    public string Period { get; set; }
    public bool IntradayLayout {get;set;}
    public DateTime CreatedUtc { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public static class DesktopSessionFiles
{
    public static readonly string Common=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"MetaQuotes\Terminal\Common\Files\CN_Manual_Replay");
    public static bool ValidId(string id) { return id!=null && id.Length==32 && id.All(c=>"0123456789abcdef".Contains(c)); }
    public static string Folder(string id) { if(!ValidId(id)) throw new Exception("浮窗会话无效，请通过入口重新启动回放。"); return Path.Combine(Common,id); }
    public static void AtomicText(string file,string text)
    {
        string tmp=file+".tmp";
        File.WriteAllText(tmp,text,Encoding.Unicode);
        if(File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
    }
    public static void Create(string id,ReplayOptions o)
    {
        string dir=Folder(id); Directory.CreateDirectory(dir);
        var info=new DesktopSessionInfo{CreatedUtc=DateTime.UtcNow,Id=id,Symbol=o.Symbol,Period=o.Timeframe,IntradayLayout=o.IntradayLayout,From=o.From.Date,To=o.To.Date.AddDays(1)};
        string json=new JavaScriptSerializer().Serialize(info);
        File.WriteAllText(Path.Combine(dir,"session.json"),json,new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(ReplayFiles.Home,"last-session.json"),json,new UTF8Encoding(true));
    }
    public static DesktopSessionInfo Read(string id)
    {
        var info=new JavaScriptSerializer().Deserialize<DesktopSessionInfo>(File.ReadAllText(Path.Combine(Folder(id),"session.json")));
        if(info==null || info.Id!=id || info.To<=info.From) throw new Exception("回放会话信息不完整，请重新启动回放。");
        if(info.CreatedUtc==default(DateTime))info.CreatedUtc=File.GetLastWriteTimeUtc(Path.Combine(Folder(id),"session.json"));
        return info;
    }
    public static string LastId()
    {
        string file=Path.Combine(ReplayFiles.Home,"last-session.json");
        if(!File.Exists(file))throw new Exception("请先启动一次手动回放。");
        var info=new JavaScriptSerializer().Deserialize<DesktopSessionInfo>(File.ReadAllText(file));
        if(info==null || !ValidId(info.Id))throw new Exception("回放会话信息不完整。");
        return info.Id;
    }
    public static void OpenLast()
    {
        string id=LastId();Read(id);
        var p=new ProcessStartInfo(Application.ExecutablePath,"--panel "+id){UseShellExecute=false,WorkingDirectory=ReplayFiles.Home};Process.Start(p);
    }
    public static int RunPanel(string id){return ReplayLauncherHost.RunPanel(id);}

}

public sealed class DesktopSnapshot
{
    public readonly Dictionary<string,string> Values;
    public string Get(string key) { string v; return Values.TryGetValue(key,out v)?v:""; }
    public long Int(string key) { long v; return Int64.TryParse(Get(key),out v)?v:0; }
    public decimal Number(string key) { decimal v; return Decimal.TryParse(Get(key),NumberStyles.Number,CultureInfo.InvariantCulture,out v)?v:0; }
    public DateTime Now { get { return Epoch.AddSeconds(Int("now")); } }
    public static readonly DateTime Epoch=new DateTime(1970,1,1);
    DesktopSnapshot(Dictionary<string,string> values) { Values=values; }
    public static DesktopSnapshot Parse(string text,string session)
    {
        var d=new Dictionary<string,string>();
        foreach(string raw in text.Split('\n'))
        {
            string line=raw.TrimEnd('\r'); int at=line.IndexOf('=');
            if(at<=0 || d.ContainsKey(line.Substring(0,at))) throw new FormatException("状态内容不完整");
            d.Add(line.Substring(0,at),line.Substring(at+1));
        }
        var s=new DesktopSnapshot(d);
        if(s.Get("protocol")!="1" || s.Get("session")!=session || s.Get("complete")!="1" || s.Get("boot")=="" || s.Int("stamp")<=0 ||
           (s.Get("phase")!="running" && s.Get("phase")!="ending" && s.Get("phase")!="ended")) throw new FormatException("状态会话不匹配");
        return s;
    }
    public double Progress
    {
        get { long a=Int("from"),b=Int("to"),n=Int("now"); return b<=a?0:Math.Max(0,Math.Min(1,(double)(n-a)/(b-a))); }
    }
    public bool Fresh
    {
        get
        {
            // GetTickCount64 in MQL and Environment.TickCount use the same OS uptime;
            // unsigned subtraction also works across the 32-bit TickCount wrap.
            uint elapsed=unchecked((uint)Environment.TickCount-(uint)Int("stamp"));
            return Get("phase")=="running" && elapsed<1200;
        }
    }
}

public sealed class ReadOnlyReplayProgress : Control
{
    double value;
    public double Value { get { return value; } set { double next=Math.Max(0,Math.Min(1,value));if(this.value==next)return;this.value=next;Invalidate(); } }
    public ReadOnlyReplayProgress()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        TabStop=false; Cursor=Cursors.Default; AccessibleName="历史日期进度，只读"; AccessibleRole=AccessibleRole.ProgressBar;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(GraphiteTheme.Soft);
        using(var b=new SolidBrush(GraphiteTheme.Accent)) e.Graphics.FillRectangle(b,0,0,(int)(Width*Value),Height);
    }
    // Intentionally no click, drag, keyboard or seek handlers.
}

public sealed class DesktopActionButton : Button
{
    public bool Quiet {get;set;}
    public DesktopActionButton(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint,true);}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        GraphiteTheme.PaintButton(this,e.Graphics,Focused && ShowFocusCues);
    }
}

public sealed partial class DesktopReplayForm : ReplayBufferedForm
{
    readonly ChartController chart;
    readonly Label chartStatus=new ReplayStableLabel(),lotMode=new ReplayStableLabel(),lotPreview=new ReplayStableLabel();
    Button lotReset;
    readonly DesktopSessionInfo session;
    readonly string folder;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Dictionary<string,Button> actions=new Dictionary<string,Button>();
    readonly NumericUpDown lots=new NumericUpDown();
    readonly Label connection=new ReplayStableLabel(),position=new ReplayStableLabel(),actual=new ReplayStableLabel(),summary=new ReplayStableLabel(),account=new GraphiteAccountLabel(),status=new ReplayStableLabel(),progressText=new ReplayStableLabel(),range=new ReplayStableLabel();
    readonly ReadOnlyReplayProgress progress=new ReadOnlyReplayProgress();
    DesktopSnapshot snapshot;
    long pending=0; string pendingAction="",boot=""; int pendingAt;
    bool initialValues=true,updating=false,dirty=false,timedOut=false;
    DateTime showRequest=DateTime.MinValue;
    int recordSyncAt=0;
    static readonly Color Ink=GraphiteTheme.Text,Muted=GraphiteTheme.Muted,Card=GraphiteTheme.Background;
    public string SessionId {get{return session.Id;}}
    public event Action<string> RecordsRequested;
    readonly CheckBox pin=new CheckBox();
    readonly ToolTip tips=new ToolTip();
    readonly TrainingSessionController training;
    Button trainingPauseShortcut,trainingPlanShortcut;
    readonly Label trainingBrief=new ReplayStableLabel();
    bool recoverPending=true;
    string trainingError="";
    public DesktopReplayForm(DesktopSessionInfo info,string stateFolder=null,bool enableChart=true)
    {
        session=info;folder=stateFolder??DesktopSessionFiles.Folder(info.Id);
        training=new TrainingSessionController(info.Id,folder);
        if(info.IntradayLayout)BuildIntradayUi(info);else BuildClassicUi(info);
        GraphiteTheme.Apply(this);position.Font=new Font("Microsoft YaHei UI",10F);
        foreach(var number in new[]{lots})number.ValueChanged+=(s,e)=>{if(!updating)dirty=true;if(session.IntradayLayout)UpdateIntradayRisk();};
        chart=enableChart?new ChartController(this,info.Id,()=>lots.Value,SendChart,message=>chartStatus.Text=message,null,price=>PriceLotSizing.Preview(snapshot,price,lots.Value)):null;
        timer.Interval=100;timer.Tick+=(s,e)=>RefreshState();timer.Start();
        Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
        FormClosed+=(s,e)=>{CloseObservation();timer.Dispose();tips.Dispose();if(chart!=null)chart.Dispose();};
        training.SetDispatchControl(this);RefreshState();ReplayWindowLayout.Scale(this);
    }
    void BuildClassicUi(DesktopSessionInfo info)
    {
        Text="历史回放 · 手动操作";Font=new Font("Microsoft YaHei UI",9F);
        BackColor=GraphiteTheme.Background;ForeColor=Ink;ClientSize=new Size(460,680);
        MinimumSize=new Size(420,500);FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;
        AutoScaleMode=AutoScaleMode.None;
        StartPosition=FormStartPosition.Manual;
        var screen=Screen.PrimaryScreen.WorkingArea;
        Location=new Point(Math.Max(screen.Left,screen.Right-Width-24),screen.Top+24);TopMost=true;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=3};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,96));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,138));
        Controls.Add(root);
        var header=PanelGrid(new[]{0f,58f,58f,52f,54f},3);header.RowStyles.Add(new RowStyle(SizeType.Absolute,28));header.RowStyles.Add(new RowStyle(SizeType.Absolute,22));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var heading=PanelLabel(info.Symbol+" · "+info.Period+" 历史练习",Ink);heading.Font=new Font(Font.FontFamily,11F);
        header.Controls.Add(heading,0,0);header.Controls.Add(ObservationButton(),1,0);
        var recordsButton=PanelButton("记录",Card);header.Controls.Add(recordsButton,2,0);finishDayButton=PanelButton("收工",Card);finishDayButton.AccessibleName="FinishDay";finishDayButton.Click+=(s,e)=>BeginFullFinish();header.Controls.Add(finishDayButton,3,0);
        recordsButton.Click+=(s,e)=>{if(RecordsRequested!=null)RecordsRequested(session.Id);else RecordStore.OpenViewer(session.Id);};
        pin.Text="置顶";pin.Checked=true;pin.AutoSize=true;pin.ForeColor=Muted;pin.Dock=DockStyle.Fill;pin.CheckedChanged+=(s,e)=>TopMost=pin.Checked;header.Controls.Add(pin,4,0);
        PrepareLabel(connection,Muted);connection.Text="等待MT5载入；拖动标题栏可移动";header.Controls.Add(connection,0,1);header.SetColumnSpan(connection,5);
        PrepareLabel(account,Muted);account.Text="余额 —    净值 —";header.Controls.Add(account,0,2);header.SetColumnSpan(account,5);root.Controls.Add(header,0,0);

        var scroller=new ReplayBufferedPanel{Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
        var stack=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(0,0,5,0),Margin=Padding.Empty};
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroller.Controls.Add(stack);root.Controls.Add(scroller,0,1);
        var trainingPanel=training.BuildPanel(()=>snapshot,action=>Send(action));
        var entry=PanelGrid(new[]{42f,0f,66f},1);entry.BackColor=Card;entry.Padding=new Padding(6);
        entry.Controls.Add(PanelLabel("手数",Ink),0,0);ConfigureNumber(lots,3,0.001m,1000,0.05m);entry.Controls.Add(lots,1,0);
        lotReset=PanelButton("初始",Card);lotReset.Click+=(a,b)=>lots.Value=Math.Max(lots.Minimum,Math.Min(lots.Maximum,ReplayFiles.Load().Lots));entry.Controls.Add(lotReset,2,0);AddStack(stack,entry,40);
        var sizing=PanelGrid(new[]{0f},2);sizing.BackColor=Card;sizing.Padding=new Padding(2);sizing.RowStyles.Add(new RowStyle(SizeType.Absolute,20));sizing.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        PrepareLabel(lotMode,Ink);PrepareLabel(lotPreview,Muted);lotMode.Text="固定手数";lotPreview.Text="等待报价确认手数模式";sizing.Controls.Add(lotMode,0,0);sizing.Controls.Add(lotPreview,0,1);AddStack(stack,sizing,44);
        var orders=PanelGrid(new[]{0f,0f},1);
        orders.Controls.Add(PanelAction("sell","卖出 / 做空",GraphiteTheme.SellSurface),0,0);
        orders.Controls.Add(PanelAction("buy","买入 / 做多",GraphiteTheme.BuySurface),1,0);AddStack(stack,orders,40);

        var holding=PanelGrid(new[]{0f},5);holding.BackColor=Card;holding.Padding=new Padding(8);
        foreach(int height in new[]{28,26,22,30})holding.RowStyles.Add(new RowStyle(SizeType.Absolute,height));holding.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var navigation=PanelGrid(new[]{0f,0f},1);
        navigation.Controls.Add(PanelAction("previous","上一单",Card),0,0);navigation.Controls.Add(PanelAction("next","下一单",Card),1,0);holding.Controls.Add(navigation,0,0);
        PrepareLabel(position,Ink);position.Text="未选择持仓";holding.Controls.Add(position,0,1);
        PrepareLabel(actual,Muted);actual.Text="实际止损：—    止盈：—";holding.Controls.Add(actual,0,2);
        var exits=PanelGrid(new[]{0f,0f},1);exits.Controls.Add(PanelAction("close","平仓所选单",Card),0,0);exits.Controls.Add(PanelAction("close_all","全部平仓（点两次）",GraphiteTheme.Background),1,0);holding.Controls.Add(exits,0,3);
        PrepareLabel(summary,Ink);summary.Text="持仓 0 笔";holding.Controls.Add(summary,0,4);AddStack(stack,holding,144);GraphiteTheme.AddSeparators(holding);
        AddStack(stack,trainingPanel,trainingPanel.Height);

        PrepareLabel(status,GraphiteTheme.Accent);status.Text="等待回放连接…";AddStack(stack,status,20);

        var footer=PanelGrid(new[]{0f},5);footer.RowStyles.Add(new RowStyle(SizeType.Absolute,60));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,8));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        footer.Controls.Add(BuildPlaybackStrip(),0,0);playbackFooter=footer;playbackRoot=root;
        PrepareLabel(trainingBrief,GraphiteTheme.Muted);trainingBrief.Text="计划：等待回放连接";footer.Controls.Add(trainingBrief,0,1);
        PrepareLabel(progressText,Ink);progressText.Text="历史日期进度：等待报价";footer.Controls.Add(progressText,0,2);
        progress.Dock=DockStyle.Fill;progress.Margin=new Padding(2,2,2,2);footer.Controls.Add(progress,0,3);
        PrepareLabel(range,Muted);range.Text=info.From.ToString("yyyy.MM.dd")+" → "+info.To.AddDays(-1).ToString("yyyy.MM.dd")+"（含当天）";footer.Controls.Add(range,0,4);root.Controls.Add(footer,0,2);
        tips.SetToolTip(progress,"只读历史时间进度，含休市；训练暂停与恢复可用上方按钮。");
    }
    public void RestorePlacement(Rectangle bounds,bool pinned)
    {Bounds=bounds;pin.Checked=pinned;ReplayWindowLayout.Fit(this);}
    TableLayoutPanel PanelGrid(float[] widths,int rows)
    {
        var grid=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=widths.Length,RowCount=rows,Margin=Padding.Empty};
        int flexible=widths.Count(w=>w==0);foreach(float width in widths)grid.ColumnStyles.Add(new ColumnStyle(width>0?SizeType.Absolute:SizeType.Percent,width>0?width:100f/Math.Max(1,flexible)));
        return grid;
    }
    void AddStack(TableLayoutPanel stack,Control control,int height)
    {int row=stack.RowCount++;stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Dock=DockStyle.Fill;control.Height=height;control.MinimumSize=new Size(0,height);control.Margin=new Padding(0,0,0,6);stack.Controls.Add(control,0,row);}
    Label PanelLabel(string text,Color color){var label=new ReplayStableLabel{Text=text};PrepareLabel(label,color);return label;}
    void PrepareLabel(Label label,Color color){label.Dock=DockStyle.Fill;label.Margin=Padding.Empty;label.ForeColor=color;label.AutoEllipsis=true;label.TextAlign=ContentAlignment.MiddleLeft;}
    void ConfigureNumber(NumericUpDown number,int digits,decimal min,decimal max,decimal value)
    {number.Minimum=min;number.Maximum=max;number.DecimalPlaces=digits;number.Increment=0.01m;number.Value=value;number.Dock=DockStyle.Fill;number.Margin=new Padding(2);number.BackColor=GraphiteTheme.Surface;number.ForeColor=Ink;}
    void PrepareButton(Button button,Color color)
    {button.Dock=DockStyle.Fill;button.Margin=new Padding(2);button.BackColor=color;button.ForeColor=Ink;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=GraphiteTheme.Border;}
    Button PanelButton(string text,Color color){var button=new DesktopActionButton{Text=text};PrepareButton(button,color);return button;}
    Button PanelAction(string action,string text,Color color)
    {var button=PanelButton(text,color);button.AccessibleName=action;button.Click+=(s,e)=>Send(action);actions[action]=button;return button;}

    void AddLabel(Label label,string text,int x,int y,int w,int h,int size,Color color)
    {
        var l=label??new ReplayStableLabel(); l.Text=text; l.ForeColor=color; l.Font=new Font(Font.FontFamily,size); l.SetBounds(x,y,w,h); Controls.Add(l);
    }
    void Number(NumericUpDown n,int x,int y,int width,int digits,decimal min,decimal max,decimal value)
    {
        n.Minimum=min; n.Maximum=max; n.DecimalPlaces=digits; n.Increment=0.01m; n.Value=value; n.SetBounds(x,y,width,30);
        n.Font=new Font(Font.FontFamily,10); Controls.Add(n);
    }
    void Style(Button b,int x,int y,int w,int h,Color color)
    {
        b.SetBounds(x,y,w,h); b.BackColor=color; b.ForeColor=Ink; b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderColor=GraphiteTheme.Border; Controls.Add(b);
    }
    void Action(string key,string text,int x,int y,int w,int h,Color color)
    {
        var b=new DesktopActionButton{Text=text,AccessibleName=key}; Style(b,x,y,w,h,color); b.Click+=(s,e)=>Send(key); actions[key]=b;
    }
    void FieldsFromState(DesktopSnapshot s)
    {
        updating=true;
        try
        {
            lots.Value=Math.Max(lots.Minimum,Math.Min(lots.Maximum,s.Number("lots")));
            dirty=false;
        }
        finally { updating=false; }
    }
    void RefreshState()
    {
        string displayStatus=status.Text;
        try
        {
            string show=Path.Combine(folder,"show.txt");
            if(File.Exists(show) && File.GetLastWriteTimeUtc(show)>showRequest)
            { showRequest=File.GetLastWriteTimeUtc(show); WindowState=FormWindowState.Normal; Show(); Activate(); }
            string file=Path.Combine(folder,"state.txt");
            if(File.Exists(file))
            {
                string text;
                using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var r=new StreamReader(f,Encoding.Unicode)) text=r.ReadToEnd();
                var s=DesktopSnapshot.Parse(text.TrimEnd('\r','\n'),session.Id);
                if(s.Get("boot")!=boot)
                {
                    boot=s.Get("boot"); pending=0; initialValues=true; timedOut=false;recoverPending=true;
                }
                snapshot=s;
                if(recoverPending){RecoverPendingCommand(s);recoverPending=false;}
                try{training.Observe(s);trainingError="";}
                catch(Exception ex){trainingError="训练附加记录异常："+ex.Message;}
                if(unchecked(Environment.TickCount-recordSyncAt)>2000 || recordSyncAt==0)
                { recordSyncAt=Environment.TickCount; RecordStore.ScheduleSync(session.Id); }
                if(pending>0 && s.Int("ack")>=pending)
                {
                    if(pendingAction=="set" || pendingAction=="buy" || pendingAction=="sell" || pendingAction=="apply" || pendingAction=="read_stops") initialValues=true;
                    pending=0; timedOut=false;
                }
                if(initialValues) { FieldsFromState(s); initialValues=false; }
                bool autoLot=s.Get("auto_price_lots")=="1";lots.Enabled=lotReset.Enabled=!autoLot;
                lotMode.Text=autoLot?"金价匹配 · "+s.Get("price_lot_base")+"手 @ "+s.Get("price_lot_reference"):"固定手数 · 使用上方填写值";
                if(!session.IntradayLayout)lotPreview.Text=autoLot?"预计买 "+s.Get("auto_buy_lots")+" / 卖 "+s.Get("auto_sell_lots")+" 手 · "+(s.Number("price_lot_cap")>0?"本场旧版上限 "+s.Get("price_lot_cap"):"无额外上限"):"自动手数可在入口“手数匹配”中设置，下一场生效。";
                tips.SetToolTip(lotMode,lotMode.Text);tips.SetToolTip(lotPreview,lotPreview.Text+"\r\n市价以提交时Ask/Bid重算；挂单按委托价计算，创建后不再改量。低于最小手数时不下单。");
                actions["buy"].Text=autoLot?"买入 / "+s.Get("auto_buy_lots")+"手":"买入 / 做多";
                actions["sell"].Text=autoLot?"卖出 / "+s.Get("auto_sell_lots")+"手":"卖出 / 做空";
                position.Text=s.Get("position"); actual.Text=s.Get("actual");
                account.Text="余额 "+s.Get("balance")+"    净值 "+s.Get("equity")+" USD";
                displayStatus=s.Get("status")+(dirty?"\r\n下单将使用当前填写的手数。":"");
                actions["close_all"].Text=s.Get("armed")=="1"?"再次点击确认全平":"全部平仓（点两次）";
                bool slow=s.Get("slow")=="1";
                actions["play_slow"].Text=slow?"● 慢放 / 观察":"慢放 / 观察";
                actions["play_fast"].Text=slow?"快放 / 浏览":"● 快放 / 浏览";
                if(!session.IntradayLayout){progress.Value=s.Progress;progressText.Text="历史 "+s.Now.ToString("yyyy.MM.dd HH:mm")+"    "+(s.Progress*100).ToString("0.0")+"%";}
            }
        }
        catch(IOException) { } // Atomic replace may race for one poll; retain last complete state.
        catch(UnauthorizedAccessException) { }
        catch(FormatException) { }
        if(pending>0 && unchecked(Environment.TickCount-pendingAt)>1800) timedOut=true;
        bool fresh=snapshot!=null && snapshot.Fresh;
        bool ended=snapshot!=null && snapshot.Get("phase")=="ended";
        if(!session.IntradayLayout)connection.Text=ended?"回放已结束。请在入口开始下一次练习。":!fresh?"等待回放继续 / 连接；暂停时按钮停用":pending>0?"等待回放确认操作…":snapshot.Get("training_mode")=="paused"?"回放已暂停 · 点击底部“继续”恢复":"已连接 · 拖标题栏移动 · 进度只读";
        if(snapshot!=null && snapshot.Get("record_error")!="") displayStatus="记录保存异常："+snapshot.Get("record_error");
        if(timedOut) displayStatus="操作未获确认。请先在MT5继续回放；不会自动重发。";
        if(trainingError!="")displayStatus=trainingError+"；原有平仓、撤单和止盈止损操作仍可使用。";
        if(chart!=null) chart.Update(snapshot,true,false,pending>0);
        if(snapshot!=null)summary.Text="持仓 "+snapshot.Get("count")+" 笔 · 浮盈亏 "+snapshot.Get("profit")+(snapshot.Int("pending_count")>0?" · 挂单 "+snapshot.Get("pending_count")+" 笔":"");
        tips.SetToolTip(position,position.Text);tips.SetToolTip(chartStatus,chartStatus.Text);
        trainingPlanShortcut.Enabled=trainingPauseShortcut.Enabled=fresh && pending==0 && snapshot.Get("training_protocol")=="1" && training.Enabled;
        try{trainingBrief.Text=trainingError!=""?"训练资料异常：请查看上方状态":training.Brief(snapshot);}
        catch(Exception){trainingBrief.Text="计划资料暂无法显示；请查看训练记录，持仓管理仍可使用。";}
        tips.SetToolTip(trainingBrief,trainingBrief.Text);
        trainingPauseShortcut.Text=snapshot!=null && snapshot.Get("training_mode")!="active"?"继续":"暂停";
        foreach(var pair in actions)
            pair.Value.Enabled=fresh && pending==0 && (!new[]{"buy","sell","close","close_all","apply"}.Contains(pair.Key) || snapshot.Get("hedging")=="1") &&
                (!new[]{"buy","sell"}.Contains(pair.Key) || snapshot.Get("training_protocol")!="1" || snapshot.Get("training_mode")=="active");
        UpdatePlaybackState();
        if(session.IntradayLayout)try{RefreshIntraday();}catch(Exception ex){displayStatus="日内附加显示异常："+ex.Message;riskPreview.Text="风险估算暂不可用；持仓管理保留";finishDayButton.Enabled=false;}
        if(session.IntradayLayout){string overlay=IntradayStatusOverride();if(overlay!=null)displayStatus=overlay;}
        RefreshObservation();RefreshFinishLock();status.Text=displayStatus;tips.SetToolTip(status,displayStatus);
    }
    void SendChart(ChartRequest request) { Send(request.Action,request); }
    void RecoverPendingCommand(DesktopSnapshot state)
    {
        string path=Path.Combine(folder,"command.txt");if(!File.Exists(path))return;
        var values=new Dictionary<string,string>();
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Encoding.Unicode))
        foreach(string line in reader.ReadToEnd().Split('\n')){string row=line.TrimEnd('\r');int at=row.IndexOf('=');if(at>0)values[row.Substring(0,at)]=row.Substring(at+1);}
        string sid,run,complete,number,action;long sequence;
        if(!values.TryGetValue("session",out sid) || sid!=session.Id || !values.TryGetValue("boot",out run) || run!=state.Get("boot") || !values.TryGetValue("complete",out complete) || complete!="1" || !values.TryGetValue("seq",out number) || !Int64.TryParse(number,out sequence) || sequence<=state.Int("ack"))return;
        values.TryGetValue("action",out action);pending=sequence;pendingAction=action??"";pendingAt=Environment.TickCount;timedOut=false;
    }
    void Send(string action) { Send(action,null); }
    void Send(string action,ChartRequest request)
    {
        RefreshState();
        if(FinishLocked){status.Text="本场已经收工，交易操作已锁定，请完成复盘和退出。";return;}
        if(snapshot==null || !snapshot.Fresh || pending>0) return;
        // Commit any in-progress NumericUpDown text edit before constructing the command.
        ValidateChildren();
        string preparedBoot=snapshot.Get("boot");
        string trainingSuffix;
        try{if(!training.PrepareCommand(this,action,request,snapshot,out trainingSuffix))return;}
        catch(Exception ex){status.Text="训练计划未读取，本次未发送："+ex.Message;return;}
        RefreshState();
        if(snapshot==null || !snapshot.Fresh || pending>0 || snapshot.Get("boot")!=preparedBoot)
        {status.Text="回放状态已变化，请在本场连接后重新操作。";return;}
        long seq;
        try{seq=training.NextCommandSequence(snapshot,action);}
        catch(Exception ex){status.Text="命令序号未分配，本次未发送："+ex.Message;return;}
        string command="session="+session.Id+"\r\nboot="+snapshot.Get("boot")+"\r\nseq="+seq+
            "\r\nstamp="+snapshot.Get("stamp")+"\r\nticket="+snapshot.Get("selected")+"\r\naction="+action+
            "\r\nlots="+ReplayFiles.Number(lots.Value)+"\r\nsl=0\r\ntp=0\r\ncomplete=1\r\n";
        if(request!=null) command="session="+session.Id+"\r\nboot="+snapshot.Get("boot")+"\r\nseq="+seq+
            "\r\nstamp="+snapshot.Get("stamp")+"\r\nticket="+request.Ticket+"\r\naction="+action+
            "\r\nlots="+ReplayFiles.Number(request.Lots)+"\r\nsl="+ChartMath.N(request.SL)+"\r\ntp="+ChartMath.N(request.TP)+
            "\r\nentry="+ChartMath.N(request.Entry)+"\r\nside="+request.Side+"\r\nkind="+request.Kind+"\r\ncomplete=1\r\n";
        if(action=="set_playback_speed" && request!=null)command=command.Replace("complete=1\r\n","playback_speed="+ReplayFiles.Number(request.PlaybackSpeed)+"\r\ncomplete=1\r\n");
        try
        {
            command=command.Replace("complete=1\r\n",trainingSuffix+"complete=1\r\n");
            // Persist the prospective plan link before exposing the order command to the tester.
            training.CommandQueued(seq,action,request,snapshot,trainingSuffix);
            DesktopSessionFiles.AtomicText(Path.Combine(folder,"command.txt"),command);
            pending=seq; pendingAction=action; pendingAt=Environment.TickCount;
            foreach(var b in actions.Values) b.Enabled=false;
        }
        catch(Exception ex)
        {
            string error=ex.Message;
            try{training.CommandFailed(seq,error);}catch(Exception logError){error+="；训练请求状态未保存："+logError.Message;}
            status.Text="操作未发送："+error;
        }
    }
    public void SavePreview(string path)
    {
        Show(); Application.DoEvents();
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(path); }
    }
}

public static class DesktopSelfTest
{
    public static int Run()
    {
        try
        {
            string id="0123456789abcdef0123456789abcdef";
            string raw="protocol=1\r\nsession="+id+"\r\nboot=test\r\nphase=running\r\nstamp="+unchecked((uint)Environment.TickCount)+"\r\nack=0\r\nfrom=100\r\nto=200\r\nnow=150\r\ncomplete=1";
            var s=DesktopSnapshot.Parse(raw,id);
            if(s.Progress!=0.5 || !s.Fresh) throw new Exception("进度中点或连接时效错误");
            if(DesktopSnapshot.Parse(raw.Replace("now=150","now=50"),id).Progress!=0 || DesktopSnapshot.Parse(raw.Replace("now=150","now=250"),id).Progress!=1) throw new Exception("进度边界错误");
            bool rejected=false; try { DesktopSnapshot.Parse(raw,"fedcba9876543210fedcba9876543210"); } catch(FormatException) { rejected=true; }
            if(!rejected) throw new Exception("跨会话状态未拦截");
            rejected=false; try { DesktopSessionFiles.Folder("../escape"); } catch { rejected=true; } if(!rejected) throw new Exception("目录边界未拦截");
            string stale=raw.Replace("stamp="+s.Get("stamp"),"stamp=1");
            if(DesktopSnapshot.Parse(stale,id).Fresh) throw new Exception("过期状态被接受");
            string integration="";
            string last=Path.Combine(ReplayFiles.Home,"last-session.json");
            if(File.Exists(last))
            {
                var live=new JavaScriptSerializer().Deserialize<DesktopSessionInfo>(File.ReadAllText(last));
                string state=Path.Combine(DesktopSessionFiles.Folder(live.Id),"state.txt");
                if(File.Exists(state))
                {
                    var actual=DesktopSnapshot.Parse(File.ReadAllText(state).TrimEnd('\r','\n'),live.Id);
                    double uptimeAge=unchecked((uint)Environment.TickCount-(uint)actual.Int("stamp"))/1000.0;
                    double fileAge=(DateTime.UtcNow-File.GetLastWriteTimeUtc(state)).TotalSeconds;
                    if(Math.Abs(uptimeAge-fileAge)>3) throw new Exception("MQL与桌面连接时钟不一致");
                    if(actual.Get("symbol")!=live.Symbol || actual.Progress<0 || actual.Progress>1) throw new Exception("MQL状态解析失败");
                    integration="PASS: parsed actual MQL Unicode state; common OS heartbeat clock verified.\r\n";
                }
            }
            var info=new DesktopSessionInfo{Id=id,Symbol="XAUUSDm",Period="H1",From=new DateTime(2026,6,1),To=new DateTime(2026,8,1)};
            using(var f=new DesktopReplayForm(info)) f.SavePreview(Path.Combine(ReplayFiles.Home,"desktop-panel-preview.png"));
            File.WriteAllText(Path.Combine(ReplayFiles.Home,"desktop-self-test.txt"),"PASS: progress 0 / 50 / 100 percent clamp; session validation; stale heartbeat rejection; standard Windows caption and read-only footer rendered.\r\n"+integration);
            return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(ReplayFiles.Home,"desktop-self-test-error.txt"),ex.ToString()); return 99; }
    }
}
