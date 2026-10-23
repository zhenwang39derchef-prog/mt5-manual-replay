using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

public sealed class ReplayOptions
{
    public string Terminal { get; set; }
    public string DataFolder { get; set; }
    public string Symbol { get; set; }
    public string Timeframe { get; set; }
    public bool IntradayLayout {get;set;}
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime RandomFrom { get; set; }
    public DateTime RandomTo { get; set; }
    public int RandomDays { get; set; }
    public bool RandomWeekdays { get; set; }
    public decimal Deposit { get; set; }
    public decimal Lots { get; set; }
    public bool AutoPriceLots {get;set;}
    public decimal PriceLotBase {get;set;}
    public decimal PriceLotReference {get;set;}
    public int LotSizingRevision {get;set;}
    public TrainingConfig Training {get;set;}
    public bool EMA { get; set; }
    public int EMAFast { get; set; }
    public int EMAMiddle { get; set; }
    public int EMASlow { get; set; }
    public bool Bands { get; set; }
    public int BandsPeriod { get; set; }
    public decimal BandsDeviation { get; set; }
    public bool RSI { get; set; }
    public int RSIPeriod { get; set; }
    public bool MACD { get; set; }
    public int MACDFast { get; set; }
    public int MACDSlow { get; set; }
    public int MACDSignal { get; set; }
    public bool ATR { get; set; }
    public int ATRPeriod { get; set; }
    public bool BIAS { get; set; }
    public int BIASPeriod { get; set; }
    public bool Slow { get; set; }
    public int SlowDelay { get; set; }
    public decimal PlaybackSpeed {get;set;}
    public bool UseObservationPacing {get;set;}
    public int PlaybackSpeedRevision {get;set;}
    public string Custom1 { get; set; }
    public string Custom2 { get; set; }
    public bool Separate1 { get; set; }
    public bool Separate2 { get; set; }
    public ReplayOptions()
    {
        Terminal = @"C:\Program Files\MetaTrader 5 EXNESS\terminal64.exe";
        DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"MetaQuotes\Terminal\53785E099C927DB68A545C249CDBCE06");
        Symbol="XAUUSDm"; Timeframe="H1"; From=new DateTime(2026,6,1); To=new DateTime(2026,7,31);
        RandomFrom=new DateTime(2026,6,1); RandomTo=new DateTime(2026,9,30); RandomDays=7; RandomWeekdays=true;
        AutoPriceLots=true;PriceLotBase=0.10m;PriceLotReference=2000m;LotSizingRevision=2;
        Training=new TrainingConfig();
        Deposit=10000; Lots=0.05m; EMA=true; EMAFast=8; EMAMiddle=34; EMASlow=200;
        BandsPeriod=20; BandsDeviation=2; RSI=true; RSIPeriod=14;
        MACDFast=12; MACDSlow=26; MACDSignal=9; ATRPeriod=14; BIASPeriod=20;
        UseObservationPacing=true;PlaybackSpeed=1;PlaybackSpeedRevision=1;Slow=true; SlowDelay=50; Custom1=""; Custom2=""; Separate1=true; Separate2=true;
    }
}

public static class ReplayDateRandomizer
{
    public static readonly int[] Days={7,14,30,60};
    public static readonly string[] Labels={"一周（7天）","两周（14天）","一个月（30天）","两个月（60天）"};
    static bool Weekday(DateTime date) { return date.DayOfWeek!=DayOfWeek.Saturday && date.DayOfWeek!=DayOfWeek.Sunday; }
    public static Tuple<DateTime,DateTime> Draw(DateTime from,DateTime to,int days,bool weekdays)
    {
        from=from.Date; to=to.Date;
        if(!Days.Contains(days)) throw new Exception("请选择一周、两周、一个月或两个月档位。");
        if(from>to) throw new Exception("随机范围的开始日期不能晚于结束日期。");
        int length=(to-from).Days+1;
        if(length<days) throw new Exception("随机范围不足"+days+"天，请扩大范围或缩短时长。");
        int count=length-days+1;
        bool possible=!weekdays;
        for(int i=0;!possible && i<Math.Min(count,7);i++) possible=Weekday(from.AddDays(i));
        if(!possible) throw new Exception("范围内没有符合条件的周一至周五起点，请扩大范围或取消避开周末。");
        using(var random=RandomNumberGenerator.Create())
        {
            byte[] bytes=new byte[4];
            ulong limit=((ulong)UInt32.MaxValue+1)/((uint)count)*((uint)count);
            for(;;)
            {
                uint value;
                do { random.GetBytes(bytes); value=BitConverter.ToUInt32(bytes,0); } while((ulong)value>=limit);
                DateTime start=from.AddDays(value%(uint)count);
                if(weekdays && !Weekday(start)) continue;
                return Tuple.Create(start,start.AddDays(days-1));
            }
        }
    }
    public static int Verify()
    {
        int checkedRanges=0;
        DateTime first=new DateTime(2026,6,1),last=new DateTime(2026,9,30);
        foreach(int days in Days)
            for(int i=0;i<300;i++)
            {
                var r=Draw(first,last,days,true);
                if(r.Item1<first || r.Item2>last || (r.Item2-r.Item1).Days+1!=days || !Weekday(r.Item1)) throw new Exception("随机日期边界或时长检查失败。");
                checkedRanges++;
            }
        var leap=Draw(new DateTime(2024,2,26),new DateTime(2024,3,3),7,true);
        if(leap.Item1!=new DateTime(2024,2,26) || leap.Item2!=new DateTime(2024,3,3)) throw new Exception("跨闰日范围检查失败。");
        var year=Draw(new DateTime(2025,12,20),new DateTime(2026,2,17),60,false);
        if((year.Item2-year.Item1).Days+1!=60 || year.Item1.Year==year.Item2.Year) throw new Exception("跨年范围检查失败。");
        bool rejected=false;
        try { Draw(first,first.AddDays(5),7,false); } catch { rejected=true; }
        if(!rejected) throw new Exception("短范围未被拦截。");
        rejected=false; try { Draw(last,first,7,false); } catch { rejected=true; }
        if(!rejected) throw new Exception("倒置范围未被拦截。");
        rejected=false; try { Draw(new DateTime(2026,6,6),new DateTime(2026,6,12),7,true); } catch { rejected=true; }
        if(!rejected) throw new Exception("仅周末起点范围未被拦截。");
        var weekend=Draw(new DateTime(2026,6,6),new DateTime(2026,6,12),7,false);
        if(weekend.Item1.DayOfWeek!=DayOfWeek.Saturday) throw new Exception("允许周末起点检查失败。");
        return checkedRanges;
    }
}

public static class ReplayFiles
{
    public static readonly string Home=AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string[] Frames={"M1","M5","M15","M30","H1","H4","D1"};
    public static string Number(decimal n) { return n.ToString(CultureInfo.InvariantCulture); }
    static string Bool(bool v) { return v?"true":"false"; }
    public static void Validate(ReplayOptions o)
    {
        if(o.From.Date>o.To.Date || o.To.Date>=DateTime.MaxValue.Date) throw new Exception("开始日期必须早于或等于结束日期。");
        if(String.IsNullOrWhiteSpace(o.Symbol) || o.Symbol.IndexOfAny(new[]{'\r','\n','=', '[',']','"'})>=0) throw new Exception("品种名称无效。");
        if(!Frames.Contains(o.Timeframe)) throw new Exception("请选择列表中的周期。");
        if(o.AutoPriceLots && (!o.Symbol.StartsWith("XAUUSD",StringComparison.OrdinalIgnoreCase) || o.PriceLotBase<=0 || o.PriceLotReference<=0))throw new Exception("金价匹配仅用于XAUUSD系列，基准手数和基准金价须为正数。");
        if(o.PlaybackSpeed<1 || o.PlaybackSpeed>100)throw new Exception("回放速度须在1倍至100倍之间。");
        if(o.Lots<=0 || o.Deposit<=0) throw new Exception("手数和练习本金必须大于0。");
        if(o.EMAFast<1 || o.EMAMiddle<1 || o.EMASlow<1 || o.BandsPeriod<1 || o.BandsDeviation<=0 || o.RSIPeriod<1 || o.ATRPeriod<1 || o.BIASPeriod<1 || o.MACDFast<1 || o.MACDSlow<=o.MACDFast || o.MACDSignal<1 || o.SlowDelay<1 || o.SlowDelay>250) throw new Exception("指标周期须为正数，MACD慢周期须大于快周期。");
        ValidateCustom(o.DataFolder,o.Custom1,false); ValidateCustom(o.DataFolder,o.Custom2,false);
    }
    public static string ValidateCustom(string data,string name,bool exists)
    {
        if(String.IsNullOrWhiteSpace(name)) return "";
        if(Path.IsPathRooted(name) || name.IndexOfAny(new[]{'\r','\n','"','=',':','*','?','<','>','|'})>=0 || name.Split('\\','/').Any(x=>x==".." || x==".")) throw new Exception("自定义指标须使用指标目录中的相对名称。");
        string root=Path.GetFullPath(Path.Combine(data,@"MQL5\Indicators"))+Path.DirectorySeparatorChar;
        string full=Path.GetFullPath(Path.Combine(root,name+".ex5"));
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new Exception("指标路径超出MT5指标目录。");
        if(exists && !File.Exists(full)) throw new Exception("未找到指标："+name+"。请先安装到MT5指标目录，再刷新列表。");
        return full;
    }
    public static string PanelSession="";
    public static string InputText(ReplayOptions o)
    {
        Validate(o);
        var p=new Dictionary<string,string>();
        p["DesktopSession"]=PanelSession;
        p["ReplayStart"]=o.From.Date.ToString("yyyy.MM.dd HH:mm:ss",CultureInfo.InvariantCulture);
        p["ReplayEndExclusive"]=o.To.Date.AddDays(1).ToString("yyyy.MM.dd HH:mm:ss",CultureInfo.InvariantCulture);
        p["AutoPriceLots"]=Bool(o.AutoPriceLots);p["PriceLotBase"]=Number(o.PriceLotBase);p["PriceLotReference"]=Number(o.PriceLotReference);
        var training=o.Training??new TrainingConfig();
        p["TrainingEnabled"]=Bool(training.Enabled);p["RequireTrainingPlan"]=Bool(training.RequirePlan || training.Purpose!="explore");
        p["InitialLots"]=Number(o.Lots); p["InitialSLPrice"]="0"; p["InitialTPPrice"]="0";
        p["FastEMA"]=o.EMAFast.ToString(); p["MiddleEMA"]=o.EMAMiddle.ToString(); p["SlowEMA"]=o.EMASlow.ToString();
        p["ReplaySpeed"]=Number(o.PlaybackSpeed);p["UseObservationPacing"]=Bool(o.UseObservationPacing);p["MagicNumber"]="817342013"; p["StartInSlowMode"]=Bool(o.Slow); p["SlowTickDelayMs"]=o.SlowDelay.ToString();
        p["ShowEMA"]=Bool(o.EMA); p["ShowBands"]=Bool(o.Bands); p["BandsPeriod"]=o.BandsPeriod.ToString(); p["BandsDeviation"]=Number(o.BandsDeviation);
        p["ShowRSI"]=Bool(o.RSI); p["RSIPeriod"]=o.RSIPeriod.ToString(); p["ShowMACD"]=Bool(o.MACD);
        p["MACDFast"]=o.MACDFast.ToString(); p["MACDSlow"]=o.MACDSlow.ToString(); p["MACDSignal"]=o.MACDSignal.ToString();
        p["ShowATR"]=Bool(o.ATR); p["ATRPeriod"]=o.ATRPeriod.ToString(); p["ShowBIAS"]=Bool(o.BIAS); p["BIASPeriod"]=o.BIASPeriod.ToString();
        p["UseCustom1"]=Bool(!String.IsNullOrWhiteSpace(o.Custom1)); p["CustomIndicator1"]=o.Custom1??""; p["CustomSeparate1"]=Bool(o.Separate1);
        p["UseCustom2"]=Bool(!String.IsNullOrWhiteSpace(o.Custom2)); p["CustomIndicator2"]=o.Custom2??""; p["CustomSeparate2"]=Bool(o.Separate2);
        return String.Join("\r\n",p.Select(k=>k.Key+"="+k.Value))+"\r\n";
    }
    public static string ConfigText(ReplayOptions o)
    {
        return "[Tester]\r\nExpert=CN_Manual_Replay_Chart_Preview.ex5\r\nExpertParameters=CN_Manual_Replay_Chart_Preview.set\r\nSymbol="+o.Symbol.Trim()+"\r\nPeriod="+o.Timeframe+
            "\r\nOptimization=0\r\nModel=4\r\nExecutionMode=0\r\nFromDate="+o.From.ToString("yyyy.MM.dd")+"\r\nToDate="+o.To.Date.AddDays(1).ToString("yyyy.MM.dd")+
            "\r\nForwardMode=0\r\nDeposit="+Number(o.Deposit)+"\r\nCurrency=USD\r\nProfitInPips=0\r\nLeverage=1:100\r\nUseLocal=1\r\nUseRemote=0\r\nUseCloud=0\r\nVisual=1\r\nShutdownTerminal=0\r\n[TesterInputs]\r\n"+InputText(o);
    }
    public static string SourceText(ReplayOptions o)
    {
        Validate(o);
        var declarations=new List<string>();
        foreach(string n in new[]{o.Custom1,o.Custom2}.Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct())
            declarations.Add("#property tester_indicator \""+n.Replace("\\","\\\\")+".ex5\"");
        string source=File.ReadAllText(Path.Combine(Home,@"assets\CN_Manual_Replay_Chart_Preview.mq5"))
            .Replace("#include \"CN_Replay_Equity.mqh\"",File.ReadAllText(Path.Combine(Home,@"assets\CN_Replay_Equity.mqh")))
            .Replace("#include \"CN_Replay_PriceLots.mqh\"",File.ReadAllText(Path.Combine(Home,@"assets\CN_Replay_PriceLots.mqh")))
            .Replace("// CUSTOM_TESTER_DECLARATIONS",String.Join("\r\n",declarations));
        string trainingInclude=Path.Combine(Home,@"assets\CN_Replay_Training.mqh");
        if(File.Exists(trainingInclude))source=source.Replace("#include \"CN_Replay_Training.mqh\"",File.ReadAllText(trainingInclude));
        source=source.Replace("#include \"CN_Replay_Playback.mqh\"",File.ReadAllText(Path.Combine(Home,@"assets\CN_Replay_Playback.mqh")));
        source=source.Replace("#include \"CN_Replay_Observation.mqh\"",File.ReadAllText(Path.Combine(Home,@"assets\CN_Replay_Observation.mqh")));
        return source;
    }
    public static string Prepare(ReplayOptions o)
    {
        Validate(o);
        if(!File.Exists(o.Terminal) || !File.Exists(Path.Combine(Path.GetDirectoryName(o.Terminal),"MetaEditor64.exe"))) throw new Exception("请在“安装位置”中选择正确的MT5程序。");
        string origin=Path.Combine(o.DataFolder,"origin.txt");
        if(!File.Exists(origin)) throw new Exception("MT5数据文件夹无效：没有origin.txt。请通过MT5的“文件→打开数据文件夹”核对路径。");
        string expected=Path.GetDirectoryName(Path.GetFullPath(o.Terminal)).TrimEnd('\\');
        if(!String.Equals(File.ReadAllText(origin).Trim().TrimEnd('\\'),expected,StringComparison.OrdinalIgnoreCase)) throw new Exception("MT5程序与数据文件夹不属于同一安装。请核对“安装位置”。");
        ValidateCustom(o.DataFolder,o.Custom1,true); ValidateCustom(o.DataFolder,o.Custom2,true);
        string mql=Path.Combine(o.DataFolder,"MQL5"), experts=Path.Combine(mql,"Experts"), indicators=Path.Combine(mql,"Indicators");
        Directory.CreateDirectory(experts); Directory.CreateDirectory(indicators);
        foreach(string n in new[]{"CN_Replay_3EMA","CN_Replay_BIAS"})
            foreach(string ext in new[]{".mq5",".ex5"}) File.Copy(Path.Combine(Home,"assets",n+ext),Path.Combine(indicators,n+ext),true);
        string src=Path.Combine(experts,"CN_Manual_Replay_Chart_Preview.mq5"), exe=Path.ChangeExtension(src,".ex5"), text=SourceText(o);
        bool same=File.Exists(src) && File.ReadAllText(src)==text && File.Exists(exe);
        if(!same)
        {
            File.WriteAllText(src,text,new UTF8Encoding(true));
            if(String.IsNullOrWhiteSpace(o.Custom1) && String.IsNullOrWhiteSpace(o.Custom2)) File.Copy(Path.Combine(Home,@"assets\CN_Manual_Replay_Chart_Preview.ex5"),exe,true);
            else
            {
                string log=Path.Combine(Home,"compile-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".log");
                DateTime before=DateTime.UtcNow;
                var info=new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(o.Terminal),"MetaEditor64.exe"),"/compile:\""+src+"\" /inc:\""+mql+"\" /log:\""+log+"\"");
                info.UseShellExecute=false; info.CreateNoWindow=true; info.WindowStyle=ProcessWindowStyle.Hidden;
                using(var process=Process.Start(info))
                    if(!process.WaitForExit(60000)) throw new Exception("指标登记编译超时，未启动回放。可稍后重试。");
                string result=File.Exists(log)?File.ReadAllText(log):"";
                if(!result.Contains("0 errors, 0 warnings") || !File.Exists(exe) || File.GetLastWriteTimeUtc(exe)<before.AddSeconds(-2)) throw new Exception("自定义指标登记未完成，请查看入口文件夹中的编译日志。未启动回放。");
            }
        }
        PanelSession=Guid.NewGuid().ToString("N");
        DesktopSessionFiles.Create(PanelSession,o);
        TrainingStore.CreateSession(PanelSession,o);
        string sets=Path.Combine(mql,@"Profiles\Tester"); Directory.CreateDirectory(sets);
        File.WriteAllText(Path.Combine(sets,"CN_Manual_Replay_Chart_Preview.set"),InputText(o),Encoding.Unicode);
        string run=Path.Combine(Home,"last-replay.ini"); File.WriteAllText(run,ConfigText(o),Encoding.Unicode);
        Save(o);
        return run;
    }
    public static void Save(ReplayOptions o)
    {
        Validate(o);
        string file=Path.Combine(Home,"settings.json"), tmp=file+".tmp";
        File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(o),new UTF8Encoding(true));
        if(File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
    }
    public static ReplayOptions Load()
    {
        string file=Path.Combine(Home,"settings.json");
        return File.Exists(file)?DeserializeOptions(File.ReadAllText(file)):new ReplayOptions();
    }
    public static ReplayOptions DeserializeOptions(string text)
    {
        var json=new JavaScriptSerializer();
        var saved=json.Deserialize<Dictionary<string,object>>(text);
        var options=json.Deserialize<ReplayOptions>(text);
        if(saved==null || options==null)throw new FormatException("回放设置为空。");
        if(options.Training==null)options.Training=new TrainingConfig();
        // One-time migration: old settings could persist a disabled switch and a 0.10 cap.
        // Explicit choices saved by this version remain respected on later launches.
        if(!saved.ContainsKey("LotSizingRevision") || options.LotSizingRevision<2)
        {
            options.AutoPriceLots=options.Symbol.StartsWith("XAUUSD",StringComparison.OrdinalIgnoreCase);
            options.PriceLotBase=0.10m;options.PriceLotReference=2000m;options.LotSizingRevision=2;
        }
        if(!saved.ContainsKey("PlaybackSpeedRevision") || options.PlaybackSpeedRevision<1){options.PlaybackSpeed=options.Slow?1:100;options.PlaybackSpeedRevision=1;}
        return options;
    }
    public static List<Process> Running(string terminal)
    {
        var list=new List<Process>();
        foreach(var p in Process.GetProcessesByName("terminal64"))
        {
            try { if(String.Equals(Path.GetFullPath(p.MainModule.FileName),Path.GetFullPath(terminal),StringComparison.OrdinalIgnoreCase)) list.Add(p); }
            catch(Exception) { if(!p.HasExited) throw new Exception("无法确认已打开的MT5实例。请先手动退出MT5，再点击启动；入口不会强制结束进程。"); }
        }
        return list;
    }
    public static void Start(ReplayOptions o,string config)
    {
        foreach(var p in Running(o.Terminal))
        {
            if(!p.CloseMainWindow() || !p.WaitForExit(15000)) throw new Exception("MT5尚未正常退出。请处理MT5中的提示或手动退出，再点击启动。入口没有强制关闭MT5。");
        }
        string profile=ReplayStartupProfile.Create(o.DataFolder,PanelSession);
        var info=new ProcessStartInfo(o.Terminal,ReplayStartupProfile.Arguments(config,profile));
        info.UseShellExecute=false; info.WorkingDirectory=Path.GetDirectoryName(o.Terminal);
        using(var process=Process.Start(info))FinishOwnedWindows.RecordLaunch(PanelSession,process,o.Terminal,profile);
    }
}

public sealed partial class ReplayForm : ReplayBufferedForm
{
    readonly ReplayOptions initial;
    readonly Dictionary<string,Control> fields=new Dictionary<string,Control>();
    readonly Label status=new Label();
    readonly Button start=new PaperActionButton{Primary=true};
    readonly TabControl tabs=new PaperTabControl();
    readonly Label randomPreview=new Label();
    readonly Label sizingSummary=new Label();
    readonly TrainingSetupControl trainingSetup;
    readonly ReplaySpeedControl initialPlayback;
    readonly Label sequencePreview=new Label();
    readonly ReplayPracticeSequence sequence;
    readonly Action<ReplayOptions> settingsSaver;
    public Action<Exception> ErrorPresenter;

    readonly TabPage recordsPage=new TabPage("交易记录");
    readonly RecordBrowserForm recordsView;
    Button saveButton,panelButton;
    bool suppressRecordSelection,layoutReady,launching;
    TableLayoutPanel launcherLayout,launcherFooter;
    public event Action<string> PanelRequested;
    public Func<ReplayOptions,Task<string>> ReplayStarter;
    public bool Launching {get{return launching;}}

    public ReplayForm(string preferredSession="",bool openRecords=false,ReplayOptions options=null,ReplayPracticeSequence practiceSequence=null,Action<ReplayOptions> saveOptions=null)
    {
        try { initial=options??ReplayFiles.Load(); } catch { initial=new ReplayOptions(); }
        sequence=practiceSequence??new ReplayPracticeSequence(ReplayFiles.Home);
        settingsSaver=saveOptions??ReplayFiles.Save;
        bool restored=sequence.Restore(initial);

        Text="手动交易 · 历史回放快速入口 v1.2.6"; ClientSize=new Size(780,680); MinimumSize=new Size(796,719);
        StartPosition=FormStartPosition.CenterScreen; Font=new Font("Microsoft YaHei UI",10); BackColor=Color.FromArgb(244,247,251);
        AddLabel(this,"手动交易历史回放",24,18,700,34,18);
        AddLabel(this,"选好日期和指标，直接进入MT5历史练习。开仓、平仓和止盈止损由你决定。",26,60,720,34,10);
        tabs.SetBounds(22,104,736,420); Controls.Add(tabs);
        var sizing=new TabPage("手数匹配");
        var basic=new TabPage("回放设置"); var randomDates=new TabPage("随机日期"); var indicators=new TabPage("指标与扩展"); var install=new TabPage("安装位置");
        tabs.TabPages.AddRange(new[]{basic,randomDates,indicators,install,recordsPage,sizing});
        var trainingPage=new TabPage("训练设置");
        trainingSetup=new TrainingSetupControl(initial){Dock=DockStyle.Fill};
        trainingPage.Controls.Add(trainingSetup);tabs.TabPages.Add(trainingPage);
        recordsView=new RecordBrowserForm(preferredSession){TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill,MinimumSize=Size.Empty,LoadOnShow=false,ShowInTaskbar=false};
        recordsPage.Controls.Add(recordsView);
        recordsView.BusyChanged+=busy=>{if(!IsDisposed)start.Enabled=!busy && !launching;};
        tabs.SelectedIndexChanged+=async(s,e)=>{ArrangeLauncher();if(tabs.SelectedTab==recordsPage && !suppressRecordSelection)await ShowRecordsAsync("");};
        Shown+=async(s,e)=>{if(openRecords)await ShowRecordsAsync(preferredSession);};
        foreach(TabPage t in tabs.TabPages) t.BackColor=Color.White;
        AddLabel(basic,"交易品种",22,22,110,25,10); Edit(basic,"Symbol",initial.Symbol,140,18,200);
        AddLabel(basic,"图表周期",390,22,110,25,10); Combo(basic,"Timeframe",initial.Timeframe,500,18,170,ReplayFiles.Frames);
        AddLabel(basic,"开始日期",22,76,110,25,10); Date(basic,"From",initial.From,140,72);
        AddLabel(basic,"结束日期",390,76,110,25,10); Date(basic,"To",initial.To,500,72);
        AddLabel(basic,"练习本金 USD",22,130,112,25,10); Num(basic,"Deposit",initial.Deposit,140,126,200,1,100000000,0);
        AddLabel(basic,"手动初始手数",390,130,100,25,10); Num(basic,"Lots",initial.Lots,500,126,170,0.001m,1000,3);
        sizingSummary.AccessibleName="SizingSummary";sizingSummary.SetBounds(22,168,680,28);sizingSummary.Font=new Font(Font.FontFamily,9);basic.Controls.Add(sizingSummary);
        Check(basic,"Slow","旧版慢放",initial.Slow,22,200,360);fields["Slow"].Visible=false;
        Num(basic,"SlowDelay",initial.SlowDelay,285,231,100,1,250,0);fields["SlowDelay"].Visible=false;
        AddLabel(basic,"起始回放速度",22,207,150,25,10);
        initialPlayback=new ReplaySpeedControl{Dock=DockStyle.None};initialPlayback.SetBounds(180,194,490,70);initialPlayback.SetLocalRate(initial.PlaybackSpeed);initialPlayback.SetLocalNativeMode(!initial.UseObservationPacing);initialPlayback.Slider.BackColor=Color.White;basic.Controls.Add(initialPlayback);
        var randomShortcut=new PaperActionButton();randomShortcut.Text="随机日期 →";randomShortcut.SetBounds(460,265,210,32);randomShortcut.Click+=(s,e)=>tabs.SelectedTab=randomDates;basic.Controls.Add(randomShortcut);
        AddLabel(basic,"1倍=历史与现实同步；10倍=历史1分钟约6秒；100倍约0.6秒。",22,302,680,27,9);
        Check(basic,"IntradayLayout","使用日内侧边面板（图表周期在启动前选择）",initial.IntradayLayout,22,334,680);
        AddLabel(basic,"真实Tick模式；止盈止损仍由你操作。倍速不改变手数。",22,368,680,25,9);
        Check(sizing,"AutoPriceLots","启用金价匹配手数（仅XAUUSD系列）",initial.AutoPriceLots,22,22,660);
        AddLabel(sizing,"基准手数",22,78,110,25,10);Num(sizing,"PriceLotBase",initial.PriceLotBase,145,74,170,0.001m,1000,3);
        AddLabel(sizing,"基准金价",355,78,110,25,10);Num(sizing,"PriceLotReference",initial.PriceLotReference,480,74,210,0.001m,1000000,3);
        AddLabel(sizing,"不设置程序额外手数上限",22,132,675,30,11);
        AddLabel(sizing,"手数 = 基准手数 × 基准金价 ÷ 下单参考价",22,186,675,30,11);
        AddLabel(sizing,"按品种步长向下取整；仅受品种成交规格限制，不足最小手数则不下单。",22,227,675,40,10);
        AddLabel(sizing,"市价买入按Ask、卖出按Bid；挂单创建时按委托价计算。\r\n已有持仓和挂单手数固定；拖动挂单价格不撤单重挂，不重新改手数。",22,272,675,64,10);
        AddLabel(sizing,"设置从下一场练习生效。浮窗显示买卖预估手数；止盈止损仍由你操作。",22,342,675,44,10);
        AddLabel(randomDates,"在以下范围内随机抽取一段历史",22,18,660,30,12);
        AddLabel(randomDates,"范围开始",22,71,110,25,10); Date(randomDates,"RandomFrom",initial.RandomFrom,140,67);
        AddLabel(randomDates,"范围结束",390,71,110,25,10); Date(randomDates,"RandomTo",initial.RandomTo,500,67);
        AddLabel(randomDates,"总时长",22,126,110,25,10);
        int durationIndex=Array.IndexOf(ReplayDateRandomizer.Days,initial.RandomDays);
        Combo(randomDates,"RandomDuration",ReplayDateRandomizer.Labels[Math.Max(0,durationIndex)],140,122,230,ReplayDateRandomizer.Labels);
        Check(randomDates,"RandomWeekdays","起点避开周末（周一至周五）",initial.RandomWeekdays,22,175,480);
        var draw=new PaperActionButton(); draw.Text="随机抽取并填入日期"; draw.AccessibleName="DrawRandomDates"; draw.SetBounds(22,224,320,45);
        draw.Click+=(s,e)=>{try{ApplyRandomDates();}catch(Exception ex){ShowError(ex);}}; randomDates.Controls.Add(draw);
        randomPreview.SetBounds(22,286,680,43); randomPreview.ForeColor=Color.FromArgb(28,99,190); randomPreview.Text="抽取后会显示本次起止日期。"; randomDates.Controls.Add(randomPreview);
        AddLabel(randomDates,"时长按7 / 14 / 30 / 60个自然日计算，含起止当天，期间包含周末。",22,334,680,26,9);
        AddLabel(randomDates,"抽取会替换本次日期；启动成功后按相同天数顺延，不受随机范围约束。",22,362,680,26,9);
        string[] captions={"三条EMA","布林带","RSI","MACD","ATR","BIAS（%）"};
        string[] keys={"EMA","Bands","RSI","MACD","ATR","BIAS"};
        bool[] vals={initial.EMA,initial.Bands,initial.RSI,initial.MACD,initial.ATR,initial.BIAS};
        for(int i=0;i<6;i++) Check(indicators,keys[i],captions[i],vals[i],18,17+i*42,150);
        Num(indicators,"EMAFast",initial.EMAFast,180,15,85,1,5000,0); Num(indicators,"EMAMiddle",initial.EMAMiddle,280,15,85,1,5000,0); Num(indicators,"EMASlow",initial.EMASlow,380,15,85,1,5000,0);
        Num(indicators,"BandsPeriod",initial.BandsPeriod,180,57,85,1,5000,0); Num(indicators,"BandsDeviation",initial.BandsDeviation,280,57,85,0.1m,20,1); AddLabel(indicators,"周期 / 标准差倍数",385,59,250,25,9);
        Num(indicators,"RSIPeriod",initial.RSIPeriod,180,99,85,1,5000,0); AddLabel(indicators,"收盘价",280,101,200,25,9);
        Num(indicators,"MACDFast",initial.MACDFast,180,141,85,1,5000,0); Num(indicators,"MACDSlow",initial.MACDSlow,280,141,85,1,5000,0); Num(indicators,"MACDSignal",initial.MACDSignal,380,141,85,1,5000,0);
        Num(indicators,"ATRPeriod",initial.ATRPeriod,180,183,85,1,5000,0);
        Num(indicators,"BIASPeriod",initial.BIASPeriod,180,225,85,1,5000,0); AddLabel(indicators,"(收盘价−SMA) / SMA × 100",280,227,400,25,9);
        AddLabel(indicators,"扩展指标 1",18,275,125,25,10); Combo(indicators,"Custom1",initial.Custom1,145,271,345,new[]{""}); Check(indicators,"Separate1","副图",initial.Separate1,506,273,80);
        AddLabel(indicators,"扩展指标 2",18,311,125,25,10); Combo(indicators,"Custom2",initial.Custom2,145,307,345,new[]{""}); Check(indicators,"Separate2","副图",initial.Separate2,506,309,80);
        var refresh=new PaperActionButton(); refresh.Text="刷新指标"; refresh.SetBounds(603,272,98,62); refresh.Click+=(s,e)=>RefreshIndicators(); indicators.Controls.Add(refresh);
        AddLabel(indicators,"扩展指标使用已安装EX5的默认参数；不勾“副图”则叠加主图。",18,352,690,30,9);
        AddLabel(install,"MT5程序",18,25,140,25,10); Edit(install,"Terminal",initial.Terminal,18,56,590);
        var browse=new PaperActionButton(); browse.Text="选择…"; browse.SetBounds(618,55,80,31); browse.Click+=(s,e)=>{using(var d=new OpenFileDialog()){d.Filter="MT5程序|terminal64.exe"; if(d.ShowDialog()==DialogResult.OK) fields["Terminal"].Text=d.FileName;}}; install.Controls.Add(browse);
        AddLabel(install,"MT5数据文件夹",18,112,200,25,10); Edit(install,"DataFolder",initial.DataFolder,18,145,590);
        var folder=new PaperActionButton(); folder.Text="选择…"; folder.SetBounds(618,144,80,31); folder.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){if(d.ShowDialog()==DialogResult.OK) fields["DataFolder"].Text=d.SelectedPath;}}; install.Controls.Add(folder);
        AddLabel(install,"在MT5“文件 → 打开数据文件夹”中核对；通常无需修改。",18,198,680,30,10);
        var open=new PaperActionButton(); open.Text="打开指标文件夹"; open.SetBounds(18,254,220,38); open.Click+=(s,e)=>OpenFolder(Path.Combine(fields["DataFolder"].Text,@"MQL5\Indicators")); install.Controls.Add(open);
        AddLabel(install,"新指标放入 Indicators 后，在“指标与扩展”中刷新、选择，再启动。\r\n入口只配置历史测试；不会读取或保存账户密码。",18,312,680,63,10);
        status.SetBounds(26,536,725,57); status.ForeColor=Color.FromArgb(61,77,100); status.Text="MT5已打开时，启动会先正常退出再重新打开；当前未保存的回放会结束。"; Controls.Add(status);
        var save=new PaperActionButton(); saveButton=save; save.Text="保存设置"; save.SetBounds(25,608,125,43); save.Click+=(s,e)=>TrySave(); Controls.Add(save);
        var panel=new PaperActionButton(); panelButton=panel; panel.Text="显示操作浮窗"; panel.SetBounds(165,608,180,43);
        panel.Click+=(s,e)=>{try{RequestPanel("");}catch(Exception ex){ShowError(ex);}}; Controls.Add(panel);
        var help=new PaperActionButton(); help.Text="使用说明"; help.SetBounds(635,62,118,32); help.Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(Path.Combine(ReplayFiles.Home,"使用说明.txt")){UseShellExecute=true});}catch(Exception ex){ShowError(ex);}}; Controls.Add(help);
        start.Text="启动手动回放 / 已开则重启"; start.SetBounds(385,602,369,52); start.BackColor=Color.FromArgb(28,99,190); start.ForeColor=Color.White; start.FlatStyle=FlatStyle.Flat; start.Click+=async(s,e)=>await Launch(); Controls.Add(start);
        save.Anchor=panel.Anchor=AnchorStyles.Bottom|AnchorStyles.Left;
        start.Anchor=AnchorStyles.Bottom|AnchorStyles.Right;help.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        status.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;
        var demo=new PaperActionButton{Text="离线图表演练"};demo.SetBounds(260,254,220,38);
        demo.Click+=(s,e)=>Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--chart-demo"){UseShellExecute=false,WorkingDirectory=ReplayFiles.Home});install.Controls.Add(demo);
        BuildPaperLauncher(help,basic,randomDates);layoutReady=true;ArrangeLauncher();
        Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
        ((DateTimePicker)fields["From"]).ValueChanged+=(s,e)=>UpdateSequencePreview();
        ((DateTimePicker)fields["To"]).ValueChanged+=(s,e)=>UpdateSequencePreview();
        UpdateSequencePreview();
        ((CheckBox)fields["AutoPriceLots"]).CheckedChanged+=(s,e)=>UpdateSizingSummary();
        ((NumericUpDown)fields["PriceLotBase"]).ValueChanged+=(s,e)=>UpdateSizingSummary();
        ((NumericUpDown)fields["PriceLotReference"]).ValueChanged+=(s,e)=>UpdateSizingSummary();
        UpdateSizingSummary();
        if(restored)status.Text="已接到上一场结束日期之后；下方为本次将启动的区间。";

        RefreshIndicators();
    }
    void BuildResponsiveLauncher(Button help)
    {
        var headings=Controls.OfType<Label>().Where(x=>x!=status).OrderBy(x=>x.Top).ToArray();
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,36));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=0;i<headings.Length;i++){headings[i].Dock=DockStyle.Fill;headings[i].Margin=Padding.Empty;headings[i].TextAlign=ContentAlignment.MiddleLeft;header.Controls.Add(headings[i],0,i);if(i>0)header.SetColumnSpan(headings[i],2);}
        help.Dock=DockStyle.Fill;help.Margin=new Padding(8,2,0,2);header.Controls.Add(help,1,0);
        launcherFooter=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1,RowCount=3,Margin=Padding.Empty,Padding=new Padding(0,8,0,0)};
        launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,40));launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,46));launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        sequencePreview.Dock=DockStyle.Fill;sequencePreview.Margin=Padding.Empty;sequencePreview.Font=new Font(Font.FontFamily,9);sequencePreview.ForeColor=Color.FromArgb(28,99,190);launcherFooter.Controls.Add(sequencePreview,0,0);
        status.Dock=DockStyle.Fill;status.Margin=Padding.Empty;launcherFooter.Controls.Add(status,0,1);
        var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,128));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,184));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var button in new[]{saveButton,panelButton,start}){button.Dock=DockStyle.Fill;button.Margin=new Padding(0,2,10,2);buttons.Controls.Add(button);}
        start.Text="启动下一场（自动顺延）";start.Margin=new Padding(8,0,0,0);launcherFooter.Controls.Add(buttons,0,2);
        launcherLayout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,12,20,16),ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        launcherLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));launcherLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));launcherLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));launcherLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tabs.Dock=DockStyle.Fill;tabs.Margin=Padding.Empty;
        foreach(TabPage page in tabs.TabPages)if(page!=recordsPage && !page.Controls.Contains(trainingSetup)){page.AutoScroll=true;page.AutoScrollMinSize=new Size(714,400);}
        launcherLayout.Controls.Add(header,0,0);launcherLayout.Controls.Add(tabs,0,1);launcherLayout.Controls.Add(launcherFooter,0,2);Controls.Add(launcherLayout);
        AutoScaleMode=AutoScaleMode.None;
        ClientSize=new Size(820,620);MinimumSize=new Size(780,570);
        ReplayWindowLayout.Scale(this);
    }
    void ArrangeLauncher()
    {
        if(!layoutReady)return;
        launcherFooter.Visible=tabs.SelectedTab!=recordsPage && tabs.SelectedTab!=finishRecentPage;if(tabs.SelectedTab==finishRecentPage)RefreshRecentFinishes();
        launcherLayout.PerformLayout();
    }
    public async Task ShowRecordsAsync(string session)
    {
        if(IsDisposed)return;
        suppressRecordSelection=true;try{tabs.SelectedTab=recordsPage;}finally{suppressRecordSelection=false;}
        ArrangeLauncher();recordsView.Show();await recordsView.RefreshRecordsAsync(session);
    }
    void RequestPanel(string session)
    {
        if(String.IsNullOrEmpty(session))session=DesktopSessionFiles.LastId();
        if(PanelRequested!=null)PanelRequested(session);else DesktopSessionFiles.OpenLast();
    }

    void AddLabel(Control p,string text,int x,int y,int w,int h,int size) { var a=new Label{Text=text,Font=new Font(Font.FontFamily,size),AutoSize=false}; a.SetBounds(x,y,w,h); p.Controls.Add(a); }
    void Edit(Control p,string k,string value,int x,int y,int w) { var a=new TextBox{Text=value,AccessibleName=k}; a.SetBounds(x,y,w,31); p.Controls.Add(a); fields[k]=a; }
    void Check(Control p,string k,string caption,bool value,int x,int y,int w) { var a=new CheckBox{Text=caption,Checked=value,AccessibleName=k}; a.SetBounds(x,y,w,28); p.Controls.Add(a); fields[k]=a; }
    void Num(Control p,string k,decimal value,int x,int y,int w,decimal min,decimal max,int digits) { var a=new NumericUpDown{Minimum=min,Maximum=max,DecimalPlaces=digits,Increment=digits==0?1:0.01m,AccessibleName=k}; a.Value=Math.Max(min,Math.Min(max,value)); a.SetBounds(x,y,w,29); p.Controls.Add(a); fields[k]=a; }
    void Combo(Control p,string k,string value,int x,int y,int w,string[] items) { var a=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName=k}; a.Items.AddRange(items); if(!a.Items.Contains(value??"")) a.Items.Add(value??""); a.SelectedItem=value??""; a.SetBounds(x,y,w,31); p.Controls.Add(a); fields[k]=a; }
    void Date(Control p,string k,DateTime value,int x,int y) { var a=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM-dd",Value=value,AccessibleName=k}; a.SetBounds(x,y,170,31); p.Controls.Add(a); fields[k]=a; }
    bool B(string k) { return ((CheckBox)fields[k]).Checked; } int I(string k) { return Decimal.ToInt32(D(k)); } decimal D(string k) { return ((NumericUpDown)fields[k]).Value; }
    void UpdateSizingSummary()
    {
        bool automatic=B("AutoPriceLots");fields["Lots"].Enabled=!automatic;
        sizingSummary.ForeColor=ReplayPaper.Muted;
        sizingSummary.Text=automatic?"自动："+D("PriceLotBase").ToString("0.00#")+" × "+D("PriceLotReference").ToString("0.###")+" ÷ 下单价；不设额外上限（手动手数停用）":"固定手数模式：使用上方手动初始手数；未启用金价匹配。";
    }
    void UpdateSequencePreview()
    {
        DateTime from=((DateTimePicker)fields["From"]).Value.Date,to=((DateTimePicker)fields["To"]).Value.Date;
        if(to<from){sequencePreview.Text="请调整日期：开始日期不能晚于结束日期。";return;}
        try
        {
            var next=ReplayPracticeSequence.Next(from,to);
            sequencePreview.Text=String.Format("本次启动：{0:yyyy.MM.dd} → {1:yyyy.MM.dd}（{2}天，含当天）\r\n启动成功后，下次接到：{3:yyyy.MM.dd} → {4:yyyy.MM.dd}",from,to,(to-from).Days+1,next.Item1,next.Item2);
        }
        catch(Exception ex){sequencePreview.Text=ex.Message;}
    }
    void ApplyRandomDates()
    {
        int index=((ComboBox)fields["RandomDuration"]).SelectedIndex;
        if(index<0 || index>=ReplayDateRandomizer.Days.Length) throw new Exception("请先选择随机时长。");
        int days=ReplayDateRandomizer.Days[index];
        var dates=ReplayDateRandomizer.Draw(((DateTimePicker)fields["RandomFrom"]).Value,((DateTimePicker)fields["RandomTo"]).Value,days,B("RandomWeekdays"));
        ((DateTimePicker)fields["From"]).Value=dates.Item1; ((DateTimePicker)fields["To"]).Value=dates.Item2;
        randomPreview.Text=String.Format("本次：{0:yyyy-MM-dd} 至 {1:yyyy-MM-dd}（{2}天）",dates.Item1,dates.Item2,days);
        status.Text="已将随机结果填入“回放设置”。可以重新抽取，或点击启动开始练习。";
    }
    public void VerifyRandomControls()
    {
        ((DateTimePicker)fields["RandomFrom"]).Value=new DateTime(2026,6,1);
        ((DateTimePicker)fields["RandomTo"]).Value=new DateTime(2026,9,30);
        ((CheckBox)fields["RandomWeekdays"]).Checked=true;
        for(int i=0;i<ReplayDateRandomizer.Days.Length;i++)
        {
            ((ComboBox)fields["RandomDuration"]).SelectedIndex=i; ApplyRandomDates();
            var o=Read();
            if((o.To-o.From).Days+1!=ReplayDateRandomizer.Days[i] || o.RandomDays!=ReplayDateRandomizer.Days[i]) throw new Exception("随机结果未正确填入回放控件。");
            string config=ReplayFiles.ConfigText(o);
            if(!config.Contains("ToDate="+o.To.AddDays(1).ToString("yyyy.MM.dd"))) throw new Exception("随机日期结束日未包含当天。");
            var restored=new JavaScriptSerializer().Deserialize<ReplayOptions>(new JavaScriptSerializer().Serialize(o));
            if(restored.RandomDays!=o.RandomDays || restored.From!=o.From || restored.To!=o.To || restored.RandomFrom!=o.RandomFrom || restored.RandomTo!=o.RandomTo) throw new Exception("随机设置保存格式检查失败。");
        }
        tabs.SelectedIndex=1;
    }
    public void RenderRandomPreview(string file)
    {
        // Render this form's own controls without showing a test window to the user.
        ShowInTaskbar=false; Opacity=0; Show(); Application.DoEvents();
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(file); }
        Hide();
    }
    ReplayOptions Read()
    {
        var o=new ReplayOptions{Terminal=fields["Terminal"].Text.Trim(),DataFolder=fields["DataFolder"].Text.Trim(),Symbol=fields["Symbol"].Text.Trim(),Timeframe=fields["Timeframe"].Text,PlaybackSpeed=initialPlayback.Rate,UseObservationPacing=!initialPlayback.NativeMode,PlaybackSpeedRevision=1,IntradayLayout=B("IntradayLayout"),
            From=((DateTimePicker)fields["From"]).Value.Date,To=((DateTimePicker)fields["To"]).Value.Date,Deposit=D("Deposit"),Lots=D("Lots"),AutoPriceLots=B("AutoPriceLots"),PriceLotBase=D("PriceLotBase"),PriceLotReference=D("PriceLotReference"),Slow=B("Slow"),SlowDelay=I("SlowDelay"),
            RandomFrom=((DateTimePicker)fields["RandomFrom"]).Value.Date,RandomTo=((DateTimePicker)fields["RandomTo"]).Value.Date,RandomDays=ReplayDateRandomizer.Days[Math.Max(0,((ComboBox)fields["RandomDuration"]).SelectedIndex)],RandomWeekdays=B("RandomWeekdays"),
            EMA=B("EMA"),EMAFast=I("EMAFast"),EMAMiddle=I("EMAMiddle"),EMASlow=I("EMASlow"),Bands=B("Bands"),BandsPeriod=I("BandsPeriod"),BandsDeviation=D("BandsDeviation"),
            RSI=B("RSI"),RSIPeriod=I("RSIPeriod"),MACD=B("MACD"),MACDFast=I("MACDFast"),MACDSlow=I("MACDSlow"),MACDSignal=I("MACDSignal"),ATR=B("ATR"),ATRPeriod=I("ATRPeriod"),BIAS=B("BIAS"),BIASPeriod=I("BIASPeriod"),
            Custom1=fields["Custom1"].Text,Custom2=fields["Custom2"].Text,Separate1=B("Separate1"),Separate2=B("Separate2")};
        trainingSetup.ReadInto(o);
        ReplayFiles.Validate(o); return o;
    }
    void RefreshIndicators()
    {
        try
        {
            string root=Path.Combine(fields["DataFolder"].Text,@"MQL5\Indicators");
            string[] items=Directory.Exists(root)?Directory.GetFiles(root,"*.ex5",SearchOption.AllDirectories).Select(x=>x.Substring(root.Length+1)).Select(x=>x.Substring(0,x.Length-4)).OrderBy(x=>x).ToArray():new string[0];
            foreach(string key in new[]{"Custom1","Custom2"}) { var box=(ComboBox)fields[key]; string old=box.Text; box.Items.Clear(); box.Items.Add(""); box.Items.AddRange(items); if(!box.Items.Contains(old)) box.Items.Add(old); box.SelectedItem=old; }
        }
        catch(Exception ex) { status.Text="指标列表暂未读取，可核对安装位置。"; File.AppendAllText(Path.Combine(ReplayFiles.Home,"launcher.log"),DateTime.Now+" "+ex.Message+Environment.NewLine); }
    }
    void OpenFolder(string path) { try { Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); } catch(Exception ex){ShowError(ex);} }
    void TrySave() { try { settingsSaver(Read()); status.Text="已保存，下次打开会恢复本次设置。"; } catch(Exception ex){ShowError(ex);} }
    void ShowError(Exception ex) { status.Text=ex.Message; if(ErrorPresenter!=null)ErrorPresenter(ex);else MessageBox.Show(this,ex.Message,"手动回放入口",MessageBoxButtons.OK,MessageBoxIcon.Information); }
    async Task Launch()
    {
        if(launching)return;
        launching=true;start.Enabled=tabs.Enabled=panelButton.Enabled=saveButton.Enabled=false;
        try
        {
            ReplayOptions o=Read();ReplayPracticeSequence.ValidateHistoricalRange(o,DateTime.Today);var next=ReplayPracticeSequence.Next(o.From,o.To);status.Text="正在准备指标与回放设置…";
            string id;
            if(ReplayStarter!=null)id=await ReplayStarter(o);
            else
            {
                string config=await Task.Run(()=>ReplayFiles.Prepare(o));
                status.Text="正在正常重启MT5并载入下一场练习…";
                await Task.Run(()=>ReplayFiles.Start(o,config));id=ReplayFiles.PanelSession;
                status.Text="等待MT5载入本场行情，确认后准备顺延日期…";
                await Task.Run(()=>ReplayPracticeSequence.WaitStarted(o,id));
            }
            string warning="";
            try{sequence.Started(o,id);}catch(Exception ex){warning="顺延记录未保存："+ex.Message;}
            ((DateTimePicker)fields["From"]).Value=next.Item1;((DateTimePicker)fields["To"]).Value=next.Item2;
            try{settingsSaver(Read());}catch(Exception ex){warning+=(warning==""?"":"；")+"下次日期未保存："+ex.Message;}
            RequestPanel(id);
            status.Text=String.Format("已启动 {0:yyyy.MM.dd} 至 {1:yyyy.MM.dd}；面板已备好下一段。",o.From,o.To)+(warning==""?"":"\r\n"+warning);
        }
        catch(Exception ex){ShowError(ex);}
        finally{launching=false;if(!IsDisposed)start.Enabled=tabs.Enabled=panelButton.Enabled=saveButton.Enabled=true;}
    }

}

public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        ChartNative.SetDpiAware();
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--chart-demo")) return ReplayLauncherHost.RunDemo();
        if(args.Contains("--chart-self-test")) return ChartSelfTest.Run();
        if(args.Length>0 && args[0]=="--records") return ReplayLauncherHost.Run(true,args.Length>1?args[1]:"");
        if(args.Contains("--record-self-test")) return RecordSelfTest.Run();
        if(args.Length==2 && args[0]=="--panel") return DesktopSessionFiles.RunPanel(args[1]);
        if(args.Contains("--desktop-self-test")) return DesktopSelfTest.Run();
        if(args.Contains("--self-test"))
        {
            try
            {
            var o=new ReplayOptions(); string c=ReplayFiles.ConfigText(o);
            if(!c.Contains("ToDate=2026.08.01") || !c.Contains("Visual=1") || !c.Contains("InitialSLPrice=0") || !c.Contains("UseCustom1=false") || !c.Contains("UseCustom2=false") || c.Contains("[StartUp]") || c.Contains("Password=")) return 1;
            o.Symbol="XAUUSDm\r\nExpert=other"; bool bad=false; try{ReplayFiles.Validate(o);}catch{bad=true;} if(!bad)return 2;
            o=new ReplayOptions(); o.Custom1=@"..\escape"; bad=false; try{ReplayFiles.Validate(o);}catch{bad=true;} if(!bad)return 3;
            o=new ReplayOptions(); o.Custom1=@"Examples\ATR"; string s=ReplayFiles.SourceText(o); if(!s.Contains("#property tester_indicator \"Examples\\\\ATR.ex5\""))return 4;
            o=new ReplayOptions(); o.From=o.To.AddDays(1); bad=false; try{ReplayFiles.Validate(o);}catch{bad=true;}if(!bad)return 5;
            int randomChecks=ReplayDateRandomizer.Verify();
            var legacy=new JavaScriptSerializer().Deserialize<ReplayOptions>("{\"Symbol\":\"XAUUSDm\"}");
            if(legacy.RandomDays!=7 || legacy.RandomFrom!=new DateTime(2026,6,1)) return 6;
            using(var form=new ReplayForm()) { form.VerifyRandomControls(); form.RenderRandomPreview(Path.Combine(ReplayFiles.Home,"random-date-preview.png")); }
            File.WriteAllText(Path.Combine(ReplayFiles.Home,"self-test.txt"),"PASS: inclusive dates; tester-only config; manual stops; input validation; custom dependency registration.\r\nPASS: "+randomChecks+" random ranges across 7/14/30/60 days; leap day; year boundary; short/reversed/weekend-only range handling; form date filling; settings round trip; legacy settings defaults.\r\n"); return 0;
            }
            catch(Exception ex) { File.WriteAllText(Path.Combine(ReplayFiles.Home,"self-test-error.txt"),ex.ToString()); return 99; }
        }
        try { return ReplayLauncherHost.Run(false,""); } catch(Exception ex){MessageBox.Show(ex.Message,"手动回放入口");return 1;}
    }
}

public static class PriceLotSizing
{
    public static decimal Calculate(decimal basis,decimal reference,decimal price,decimal min,decimal max,decimal step)
    {
        if(basis<=0 || reference<=0 || price<=0 || min<=0 || max<min || step<=0)return 0;
        try{decimal raw=basis*reference/price;decimal value=decimal.Floor(Math.Min(raw,max)/step)*step;return value>=min?value:0;}
        catch(OverflowException){return 0;}
    }
    public static decimal Preview(DesktopSnapshot state,double price,decimal manual)
    {
        if(state==null || state.Get("auto_price_lots")!="1")return manual;
        if(double.IsNaN(price) || double.IsInfinity(price) || price<=0 || price>(double)decimal.MaxValue)return 0;
        decimal maximum=state.Number("max_lots"),legacyCap=state.Number("price_lot_cap");
        if(state.Get("price_lot_policy")!="inverse_price_uncapped_v2" && legacyCap>0)maximum=Math.Min(maximum,legacyCap);
        return Calculate(state.Number("price_lot_base"),state.Number("price_lot_reference"),(decimal)price,state.Number("min_lots"),maximum,state.Number("lot_step"));
    }
}

public static class ReplayStartupProfile
{
    // A fresh, empty native profile keeps restored ordinary charts out of a replay.
    // Existing profiles are never edited, reused, or removed.
    public static string Create(string dataFolder,string session)
    {
        if(!DesktopSessionFiles.ValidId(session))throw new ArgumentException("回放会话无效，未启动MT5。");
        string profiles=Path.Combine(dataFolder,@"MQL5\Profiles\Charts");
        if(!Directory.Exists(profiles))throw new IOException("未找到MT5图表布局目录，未启动回放。");
        string name="CN_Replay_"+session;
        string folder=Path.Combine(profiles,name);
        if(Directory.Exists(folder) || File.Exists(folder))throw new IOException("本场回放布局已存在，请重新点击启动以创建新会话。");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"order.wnd"),"",Encoding.Unicode);
        File.WriteAllText(Path.Combine(folder,"cn-replay-profile.txt"),"CN manual replay startup profile\r\nsession="+session+"\r\n",new UTF8Encoding(true));
        return name;
    }
    public static string Arguments(string config,string profile)
    {
        if(String.IsNullOrWhiteSpace(config) || config.IndexOf('"')>=0 ||
            profile==null || !profile.StartsWith("CN_Replay_",StringComparison.Ordinal) ||
            !DesktopSessionFiles.ValidId(profile.Substring("CN_Replay_".Length)))
            throw new ArgumentException("回放启动参数无效。");
        return "/profile:"+profile+" /config:\""+config+"\"";
    }
}
