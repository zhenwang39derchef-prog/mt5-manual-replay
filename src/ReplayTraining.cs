using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public sealed class TrainingConfig
{
    public bool Enabled {get;set;}
    public bool RequirePlan {get;set;}
    public string Purpose {get;set;}
    public string Strategy {get;set;}
    public string RuleVersion {get;set;}
    public string Familiarity {get;set;}
    public string PositionStyle {get;set;}
    public TrainingConfig(){Enabled=true;RequirePlan=true;Purpose="explore";Strategy="ema";RuleVersion="A";Familiarity="unknown";PositionStyle="undecided";}
}
public sealed class TrainingProfile
{
    public string SessionId {get;set;}
    public DateTime CreatedUtc {get;set;}
    public string Symbol {get;set;}
    public string Timeframe {get;set;}
    public DateTime From {get;set;}
    public DateTime To {get;set;}
    public decimal StartingBalance {get;set;}
    public string GroupKey {get;set;}
    public string Fingerprint {get;set;}
    public string SettingsJson {get;set;}
    public TrainingConfig Config {get;set;}
    public string HistoryCheck {get;set;}
}
public sealed class TrainingPlan
{
    public string Id {get;set;}
    public string IdeaId {get;set;}
    public string SessionId {get;set;}
    public string Boot {get;set;}
    public DateTime CreatedUtc {get;set;}
    public DateTime HistoricalTime {get;set;}
    public string[] Reasons {get;set;}
    public string FailureCondition {get;set;}
    public string ExitMethod {get;set;}
    public string RiskBasis {get;set;}
    public decimal RiskValue {get;set;}
    public int ExpiryMinutes {get;set;}
    public long ValidUntil {get;set;}
    public string Notes {get;set;}
    public int DetailVersion {get;set;}
    public decimal InvalidationPrice {get;set;}
    public decimal TargetPrice {get;set;}
    public int ReviewMinutes {get;set;}
    public string ManualExitRule {get;set;}
    public TrainingPlan(){Reasons=new string[0];Notes="";ManualExitRule="";}
}
public sealed class TrainingReview
{
    public string SessionId {get;set;}
    public string Boot {get;set;}
    public string PositionId {get;set;}
    public string PlanId {get;set;}
    public string EntryCompliance {get;set;}
    public string RiskCompliance {get;set;}
    public string ExitCompliance {get;set;}
    public string PendingCompliance {get;set;}
    public string Attention {get;set;}
    public string[] Issues {get;set;}
    public string Notes {get;set;}
    public string Grade {get;set;}
    public bool VerifiedPreEntryPlan {get;set;}
    public DateTime SavedUtc {get;set;}
    public TrainingReview(){EntryCompliance=RiskCompliance=ExitCompliance=PendingCompliance="unknown";Attention="unknown";Issues=new string[0];Notes="";Grade="pending";}
}
public sealed class TrainingMoment
{
    public string Id {get;set;}
    public string SessionId {get;set;}
    public string Boot {get;set;}
    public DateTime HistoricalTime {get;set;}
    public DateTime CreatedUtc {get;set;}
    public string Mode {get;set;}
    public string Reason {get;set;}
    public decimal Balance {get;set;}
    public decimal Equity {get;set;}
    public int Positions {get;set;}
    public int Pending {get;set;}
    public string Items {get;set;}
}
public sealed class TrainingCommand
{
    public long Sequence {get;set;}
    public string Boot {get;set;}
    public string Action {get;set;}
    public string PlanId {get;set;}
    public long Expiry {get;set;}
    public DateTime HistoricalTime {get;set;}
    public DateTime CreatedUtc {get;set;}
    public string Status {get;set;}
    public string[] Tickets {get;set;}
    public string[] BeforeTickets {get;set;}
    public string TargetTicket {get;set;}
    public TrainingCommand(){Tickets=BeforeTickets=new string[0];Status="requested";PlanId="";TargetTicket="0";}
}
public sealed class TrainingData
{
    public TrainingProfile Profile {get;set;}
    public List<TrainingPlan> Plans {get;set;}
    public List<TrainingMoment> Moments {get;set;}
    public List<TrainingReview> Reviews {get;set;}
    public List<TrainingCommand> Commands {get;set;}
    public string ActivePlanId {get;set;}
    public TrainingData(){Plans=new List<TrainingPlan>();Moments=new List<TrainingMoment>();Reviews=new List<TrainingReview>();Commands=new List<TrainingCommand>();ActivePlanId="";}
}
public sealed class TrainingChoice
{
    public readonly string Code,Text;
    public TrainingChoice(string code,string text){Code=code;Text=text;}
    public override string ToString(){return Text;}
}

public static class TrainingStore
{
    static readonly object Gate=new object();
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
    public static readonly string[] PurposeCodes={"explore","rules","holdout"};
    static readonly string[] ReasonCodes={"ema_up","ema_down","second_cross","third_cross","trend_pullback","break_retest","support_reversal","unknown","other"};
    static readonly string[] FailureCodes={"reverse_cross","structure_break","manual_price","time_limit","unknown","other"};
    static readonly string[] ExitCodes={"manual_stops","reverse_cross","manual_exit","session_end","time_limit","test_end","unknown","other"};
    static readonly int[] ReviewChoices={15,60,240,1440,-1};
    sealed class FileLease : IDisposable
    {
        readonly System.Threading.Mutex mutex;bool owned;
        public FileLease(string path)
        {
            string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
            mutex=new System.Threading.Mutex(false,@"Global\CNReplayTraining_"+key);
            try{try{owned=mutex.WaitOne(3000);}catch(System.Threading.AbandonedMutexException){owned=true;}if(!owned)throw new IOException("训练资料正在其他窗口保存，请稍后重试。");}
            catch{mutex.Dispose();throw;}
        }
        public void Dispose(){if(owned){owned=false;mutex.ReleaseMutex();}mutex.Dispose();}
    }
    public static string Label(string code)
    {
        switch(code??"")
        {
            case "explore":return "探索练习";case "rules":return "规则训练";case "holdout":return "留出检验";
            case "ema":return "三EMA";case "structure":return "价格结构";case "breakout":return "突破 / 回踩";case "other":return "其他";
            case "unseen":return "自己声明未看过";case "seen":return "已经看过";case "unknown":return "不确定 / 无法核实";
            case "undecided":return "持仓方式探索中";case "intraday":return "日内";case "swing":return "允许跨日";
            case "active":return "主动练习";case "passive":return "放置运行";case "paused":return "已暂停推进";case "mixed":return "主动与放置混合";
            case "qualified":return "执行合格";case "problem":return "执行问题";case "pending":return "待核实";
            case "yes":return "是";case "no":return "否";case "na":return "不适用";case "test_end":return "测试终点退出（需核实）";
            case "ema_up":return "EMA金叉";case "ema_down":return "EMA死叉";case "second_cross":return "第二次同方向交叉";case "third_cross":return "第三次同方向交叉";
            case "trend_pullback":return "顺趋势回踩";case "break_retest":return "突破后回踩";case "support_reversal":return "支撑 / 阻力反转";
            case "reverse_cross":return "反向交叉";case "structure_break":return "原有价格结构被破坏";case "manual_price":return "手动设置的失效价 / 止损";
            case "time_limit":return "到达预定时间复核 / 退出";case "manual_exit":return "按计划手动退出";case "manual_stops":return "手动设置止盈 / 止损";
            case "target_price":return "到达目标价手动退出";
            case "session_end":return "本历史交易日结束前退出";case "fatigue":return "疲劳，需要休息";case "day_end":return "本日收工并保存现场";case "break":return "临时离开";case "time_up":return "本段练习结束";
            case "resume":return "恢复专注观察";case "observe_only":return "让已有交易继续运行";case "none":return "无";
            case "early_entry":return "未满足条件就提前入场";case "unplanned_add":return "计划外加仓";case "over_budget":return "超过合计风险预算";
            case "manual_pause":return "手动暂停";case "widened_stop":return "扩大原定止损";case "late_exit":return "失效后延迟退出";case "expired_pending":return "过期挂单未撤销";case "fatigue_passive":return "违反暂停/结束计划，继续放置";
            default:return String.IsNullOrEmpty(code)?"未选择":code;
        }
    }
    static void CheckBoot(string boot)
    {if(String.IsNullOrEmpty(boot) || boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-'))throw new ArgumentException("训练运行标识无效。");}
    public static string SessionRoot(string sessionId,string sessionFolder=null)
    {
        if(!DesktopSessionFiles.ValidId(sessionId))throw new ArgumentException("训练会话标识无效。");
        return Path.Combine(sessionFolder??DesktopSessionFiles.Folder(sessionId),"training");
    }
    static string DataFile(string sessionId,string boot,string sessionFolder=null)
    {CheckBoot(boot);return Path.Combine(SessionRoot(sessionId,sessionFolder),"runs",boot,"data.json");}
    static T ReadJson<T>(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true))return Json.Deserialize<T>(reader.ReadToEnd());
    }
    public static void AtomicJson(string path,object value)
    {
        lock(Gate)using(var lease=new FileLease(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(true));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    static void NormalizeAndCheck(TrainingData data,string sessionId,string boot)
    {
        if(data==null)throw new FormatException("训练记录为空。");
        data.Plans=data.Plans??new List<TrainingPlan>();data.Moments=data.Moments??new List<TrainingMoment>();data.Reviews=data.Reviews??new List<TrainingReview>();data.Commands=data.Commands??new List<TrainingCommand>();
        if(data.Profile!=null && data.Profile.SessionId!=sessionId)throw new FormatException("训练资料与会话不匹配。");
        if(data.Plans.Any(x=>x==null || x.SessionId!=sessionId || x.Boot!=boot) || data.Moments.Any(x=>x==null || x.SessionId!=sessionId || x.Boot!=boot) || data.Reviews.Any(x=>x==null || x.SessionId!=sessionId || x.Boot!=boot) || data.Commands.Any(x=>x==null || x.Boot!=boot))throw new FormatException("训练资料与运行不匹配。");
    }
    public static TrainingData Load(string sessionId,string boot,string sessionFolder=null)
    {
        lock(Gate)
        {
            string profile=Path.Combine(SessionRoot(sessionId,sessionFolder),"profile.json"),file=DataFile(sessionId,boot,sessionFolder);
            var data=File.Exists(file)?ReadJson<TrainingData>(file):new TrainingData();if(data==null)throw new FormatException("训练记录为空。");
            data.Profile=File.Exists(profile)?ReadJson<TrainingProfile>(profile):data.Profile;
            NormalizeAndCheck(data,sessionId,boot);
            return data;
        }
    }
    public static void SaveData(string sessionId,string boot,TrainingData data,string sessionFolder=null)
    {
        lock(Gate)
        {
            NormalizeAndCheck(data,sessionId,boot);string file=DataFile(sessionId,boot,sessionFolder);
            using(var lease=new FileLease(file))
            {
                // Preserve immutable plans and append-only stage history when a controller writes a stale cache.
                // The named lock also covers independent clients using the same Common directory.
                if(File.Exists(file))
                {
                    var previous=ReadJson<TrainingData>(file);NormalizeAndCheck(previous,sessionId,boot);
                    data.Reviews=previous.Reviews.Concat(data.Reviews).GroupBy(r=>r.PositionId).Select(g=>g.OrderByDescending(r=>r.SavedUtc).First()).ToList();
                    data.Plans=previous.Plans.Concat(data.Plans).GroupBy(p=>p.Id).Select(g=>g.First()).ToList();
                    data.Moments=previous.Moments.Concat(data.Moments).GroupBy(m=>m.Id).Select(g=>g.First()).OrderBy(m=>m.HistoricalTime).ThenBy(m=>m.CreatedUtc).ToList();
                    data.Commands=previous.Commands.Concat(data.Commands).GroupBy(c=>c.Sequence).Select(MergeCommand).OrderBy(c=>c.Sequence).ToList();
                    int oldActive=data.Plans.FindIndex(p=>p.Id==previous.ActivePlanId),incomingActive=data.Plans.FindIndex(p=>p.Id==data.ActivePlanId);
                    if(oldActive>incomingActive)data.ActivePlanId=previous.ActivePlanId;
                }
                AtomicJson(file,data);
            }
        }
    }
    static TrainingCommand MergeCommand(IEnumerable<TrainingCommand> commands)
    {
        var rows=commands.ToList();var first=rows[0];
        if(rows.Any(c=>c.Action!=first.Action || c.PlanId!=first.PlanId || c.Expiry!=first.Expiry || c.TargetTicket!=first.TargetTicket))throw new FormatException("同一命令序号的内容冲突，未覆盖原训练记录。");
        // A requested cache must never undo an acknowledgement or a definite transport failure.
        // At equal priority retain the incoming object: live awaiting entries may still
        // reference it, and replacing it with the deserialized copy loses later updates.
        return rows.AsEnumerable().Reverse().OrderByDescending(c=>c.Status=="requested"?0:(c.Status??"").StartsWith("not_sent:")?1:2).First();
    }
    public static long NextCommandSequence(long acknowledged,TrainingData data)
    {
        if(acknowledged<0)throw new FormatException("测试器命令确认序号无效。");
        long highest=acknowledged;
        foreach(var command in data==null || data.Commands==null?Enumerable.Empty<TrainingCommand>():data.Commands)
        {
            if(command==null || command.Sequence<=0)throw new FormatException("训练命令序号无效，请先核对附加记录。");
            if(command.Sequence>highest)highest=command.Sequence;
        }
        if(highest==Int64.MaxValue)throw new InvalidOperationException("本场命令序号已经耗尽，请重新开始练习。");
        return highest+1;
    }
    public static long NextCommandSequence(string sessionId,string boot,long acknowledged,string sessionFolder=null)
    {
        lock(Gate)using(var lease=new FileLease(DataFile(sessionId,boot,sessionFolder)))
            return NextCommandSequence(acknowledged,Load(sessionId,boot,sessionFolder));
    }
    public static string EvaluateReview(TrainingReview review,bool verifiedPreEntryPlan)
    {
        var answers=new[]{review.EntryCompliance,review.RiskCompliance,review.ExitCompliance,review.PendingCompliance};
        if(answers.Contains("no") || (review.Issues??new string[0]).Any(x=>!String.IsNullOrWhiteSpace(x) && x!="none"))return "problem";
        if(!verifiedPreEntryPlan || review.EntryCompliance!="yes" || review.RiskCompliance!="yes" || review.ExitCompliance!="yes" || (review.PendingCompliance!="yes" && review.PendingCompliance!="na") || !new[]{"active","passive","mixed"}.Contains(review.Attention))return "pending";
        return "qualified";
    }
    public static string PlanProblem(TrainingPlan plan,bool strict)
    {
        if(plan==null)return "请先填写事前交易计划。";
        if(plan.DetailVersion!=2)return "这份旧计划未记录具体条件，请新建计划；旧记录继续保留待核实。";
        if(plan.Reasons==null || plan.Reasons.Length==0 || plan.Reasons.Any(r=>!ReasonCodes.Contains(r)) || plan.Reasons.Distinct().Count()!=plan.Reasons.Length || !FailureCodes.Contains(plan.FailureCondition) || !ExitCodes.Contains(plan.ExitMethod) || !new[]{"pct","usd","unknown"}.Contains(plan.RiskBasis))return "计划含有未选择或无法识别的条件，请重新选择。";
        if(plan.Reasons.Contains("unknown") && plan.Reasons.Length>1)return "尚未确定的入场理由不能与明确理由同时选择。";
        if((plan.Reasons.Contains("other") || plan.FailureCondition=="other" || plan.ExitMethod=="other") && String.IsNullOrWhiteSpace(plan.Notes))return "选择其他条件时，请在备注写出具体规则。";
        if(plan.RiskBasis=="unknown" ? plan.RiskValue!=0 : plan.RiskValue<=0 || (plan.RiskBasis=="pct" && plan.RiskValue>100))return "请填写有效合计风险预算；百分比应大于0且不超过100。";
        if(!new[]{0,15,60,240,1440,-1}.Contains(plan.ExpiryMinutes) || plan.HistoricalTime<=DesktopSnapshot.Epoch)return "计划历史时间或有效期无效。";
        try{if(plan.ValidUntil!=Expiry(plan.HistoricalTime,plan.ExpiryMinutes))return "计划绝对到期时间与填写时间、有效期不一致。";}catch(ArgumentOutOfRangeException){return "计划有效期超出可用时间范围。";}catch(OverflowException){return "计划有效期超出可用时间范围。";}
        if(plan.InvalidationPrice<0 || plan.TargetPrice<0 || (plan.ReviewMinutes!=0 && !ReviewChoices.Contains(plan.ReviewMinutes)) || (!String.IsNullOrEmpty(plan.ManualExitRule) && !new[]{"target_price","reverse_cross","session_end"}.Contains(plan.ManualExitRule)))return "计划的价格、复核时间或手动退出规则无效。";
        if(plan.ReviewMinutes!=0)
        {try{Expiry(plan.HistoricalTime,plan.ReviewMinutes);}catch(ArgumentOutOfRangeException){return "计划复核 / 退出时刻超出可用时间范围。";}catch(OverflowException){return "计划复核 / 退出时刻超出可用时间范围。";}}
        if((plan.FailureCondition=="manual_price" || plan.FailureCondition=="structure_break" || plan.ExitMethod=="manual_stops") && plan.InvalidationPrice<=0)return "请记录明确的失效价 / 手动止损价；这里只记录，不自动设置止损。";
        if((plan.FailureCondition=="time_limit" || plan.ExitMethod=="time_limit") && !ReviewChoices.Contains(plan.ReviewMinutes))return "请选择明确的历史复核 / 退出时限。";
        if(plan.ExitMethod=="manual_exit")
        {
            if(!new[]{"target_price","reverse_cross","session_end"}.Contains(plan.ManualExitRule))return "请为手动退出选择明确规则。";
            if(plan.ManualExitRule=="target_price" && plan.TargetPrice<=0)return "请记录手动退出的目标价。";
        }
        if(strict && (plan.Reasons.Contains("unknown") || plan.FailureCondition=="unknown" || plan.ExitMethod=="unknown" || plan.ExitMethod=="test_end" || plan.RiskBasis=="unknown" || plan.ExpiryMinutes==0))return "规则训练 / 留出检验需要明确的条件、风险和有效期；不能使用测试终点退出。";
        return "";
    }
    public static bool IsCompletePlan(TrainingPlan plan)
    {return PlanProblem(plan,false)=="" && !plan.Reasons.Contains("unknown") && plan.FailureCondition!="unknown" && plan.ExitMethod!="unknown" && plan.ExitMethod!="test_end" && plan.RiskBasis!="unknown";}
    public static void SaveReview(string sessionId,string boot,TrainingReview review,string sessionFolder=null)
    {
        if(review==null || String.IsNullOrWhiteSpace(review.PositionId))throw new ArgumentException("请选择要复盘的持仓。");
        lock(Gate)
        {
          using(var lease=new FileLease(DataFile(sessionId,boot,sessionFolder)))
          {
            var data=Load(sessionId,boot,sessionFolder);review.SessionId=sessionId;review.Boot=boot;
            var plan=data.Plans.FirstOrDefault(p=>p.Id==review.PlanId && p.Boot==boot);
            review.VerifiedPreEntryPlan=review.VerifiedPreEntryPlan && plan!=null;
            review.Grade=EvaluateReview(review,review.VerifiedPreEntryPlan && IsCompletePlan(plan));review.SavedUtc=DateTime.UtcNow;
            data.Reviews.RemoveAll(r=>r.PositionId==review.PositionId);data.Reviews.Add(review);SaveData(sessionId,boot,data,sessionFolder);
          }
        }
    }
    static string StableConfig(ReplayOptions o)
    {
        var c=o.Training??new TrainingConfig();
        return String.Join("|",new[]{"training-group-v1",c.Strategy,c.RuleVersion,o.Symbol,o.Timeframe,c.PositionStyle,
            o.Deposit.ToString(CultureInfo.InvariantCulture),o.Lots.ToString(CultureInfo.InvariantCulture),o.AutoPriceLots.ToString(),o.PriceLotBase.ToString(CultureInfo.InvariantCulture),o.PriceLotReference.ToString(CultureInfo.InvariantCulture),
            o.EMA.ToString(),o.EMAFast.ToString(),o.EMAMiddle.ToString(),o.EMASlow.ToString(),o.Bands.ToString(),o.BandsPeriod.ToString(),o.BandsDeviation.ToString(CultureInfo.InvariantCulture),o.RSI.ToString(),o.RSIPeriod.ToString(),o.MACD.ToString(),o.MACDFast.ToString(),o.MACDSlow.ToString(),o.MACDSignal.ToString(),o.ATR.ToString(),o.ATRPeriod.ToString(),o.BIAS.ToString(),o.BIASPeriod.ToString(),o.Custom1??"",o.Custom2??"",o.Separate1.ToString(),o.Separate2.ToString()});
    }
    public static string Fingerprint(ReplayOptions o)
    {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(StableConfig(o)))).Replace("-","").ToLowerInvariant();}
    public static string UnderlyingSymbol(string symbol)
    {
        string name=(symbol??"").Trim().ToUpperInvariant();
        // Only known XAU/USD spellings are aliases. Never guess that an arbitrary prefix is gold.
        if(new[]{"XAUUSD","XAUUSDM","XAUUSDC","XAUUSDR","XAUUSDS","XAUUSD.PRO","XAUUSD.RAW","XAUUSD.STD","XAUUSD.ECN","XAUUSD#","XAUUSD."}.Contains(name))return "XAUUSD";
        return name;
    }
    public static string CheckSeenOverlap(string symbol,DateTime from,DateTime to,string ignoreSession=null,IEnumerable<string> summaryFiles=null)
    {
        bool unknown=false;var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(summaryFiles!=null){foreach(string file in summaryFiles)files.Add(file);}
        else try
        {
            if(Directory.Exists(RecordStore.ExportRoot))foreach(string folder in Directory.GetDirectories(RecordStore.ExportRoot)){string file=Path.Combine(folder,"回放汇总.csv");if(File.Exists(file))files.Add(file);}
            if(Directory.Exists(DesktopSessionFiles.Common))foreach(string folder in Directory.GetDirectories(DesktopSessionFiles.Common))
            {
                string sid=Path.GetFileName(folder);if(!DesktopSessionFiles.ValidId(sid) || sid==ignoreSession || File.Exists(Path.Combine(folder,"fixture.flag")) || File.Exists(Path.Combine(folder,"records-cleared.flag")))continue;
                string records=Path.Combine(folder,"records");if(Directory.Exists(records))foreach(string run in Directory.GetDirectories(records)){string file=Path.Combine(run,"回放汇总.csv");if(File.Exists(file))files.Add(file);}
            }
        }
        catch(IOException){unknown=true;}catch(UnauthorizedAccessException){unknown=true;}
        if(String.IsNullOrWhiteSpace(symbol) || to.Date<from.Date)return "unknown";
        foreach(string file in files)
        {
            try
            {
                var table=CsvTable.Parse(File.ReadAllText(file,Encoding.UTF8));if(table.Rows.Count!=1){unknown=true;continue;}
                string sid=table.Get(0,"回放ID"),recordSymbol=table.Get(0,"品种");
                if(!String.IsNullOrEmpty(ignoreSession) && sid==ignoreSession)continue;
                if(DesktopSessionFiles.ValidId(sid) && (File.Exists(Path.Combine(DesktopSessionFiles.Folder(sid),"fixture.flag")) || File.Exists(Path.Combine(DesktopSessionFiles.Folder(sid),"records-cleared.flag"))))continue;
                if(String.IsNullOrWhiteSpace(recordSymbol)){unknown=true;continue;}
                if(UnderlyingSymbol(recordSymbol)!=UnderlyingSymbol(symbol))continue;
                DateTime start,end;
                string[] formats={"yyyy.MM.dd HH:mm:ss","yyyy.MM.dd HH:mm:ss.fff"};
                if(!DesktopSessionFiles.ValidId(sid) || !DateTime.TryParseExact(table.Get(0,"实际记录起点"),formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out start) || !DateTime.TryParseExact(table.Get(0,"最新历史时间"),formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out end) || end<start){unknown=true;continue;}
                if(start.Date<=to.Date && end>=from.Date)return "overlap";
            }
            catch(IOException){unknown=true;}catch(UnauthorizedAccessException){unknown=true;}catch(FormatException){unknown=true;}catch(ArgumentException){unknown=true;}
        }
        return unknown?"unknown":"clear";
    }
    public static bool HasSeenOverlap(string symbol,DateTime from,DateTime to,string ignoreSession=null)
    {
        string result=CheckSeenOverlap(symbol,from,to,ignoreSession);
        if(result=="unknown")throw new IOException("部分已有回放资料无法读取或缺少有效日期，无法核实留出区间；请修复资料，或选择探索 / 规则训练。");
        return result=="overlap";
    }
    public static void Validate(ReplayOptions o,bool checkHistory)
    {
        var c=o.Training??new TrainingConfig();
        if(!PurposeCodes.Contains(c.Purpose) || !new[]{"ema","structure","breakout","other"}.Contains(c.Strategy) || !new[]{"A","B","C","D"}.Contains(c.RuleVersion) || !new[]{"unseen","seen","unknown"}.Contains(c.Familiarity) || !new[]{"undecided","intraday","swing"}.Contains(c.PositionStyle))throw new ArgumentException("训练分类选项无效。");
        if(c.Enabled && c.Purpose=="holdout")
        {
            if(c.Familiarity!="unseen")throw new ArgumentException("留出检验需要明确选择“自己声明未看过”；已看过或不确定的行情请归入训练。");
            if(checkHistory && HasSeenOverlap(o.Symbol,o.From,o.To))throw new ArgumentException("这段日期与已有回放实际记录区间重叠（含已识别的同底层黄金品种），不能标为首次留出检验。请换日期，或改为探索/规则训练。");
        }
    }
    public static void CreateSession(string id,ReplayOptions o)
    {
        lock(Gate)
        {
            Validate(o,true);string file=Path.Combine(SessionRoot(id),"profile.json");
          using(var lease=new FileLease(file))
          {
            if(File.Exists(file))throw new IOException("本场训练资料已存在，不能覆盖事前分类。");
            string fingerprint=Fingerprint(o),history=CheckSeenOverlap(o.Symbol,o.From,o.To,id);
            if((o.Training??new TrainingConfig()).Enabled && (o.Training??new TrainingConfig()).Purpose=="holdout" && history!="clear")throw new IOException(history=="overlap"?"准备期间发现已有回放区间重叠，请换日期或改为探索 / 规则训练。":"已有回放资料暂时无法完整核实，请稍后重试或改为探索 / 规则训练。");
            var profile=new TrainingProfile{SessionId=id,CreatedUtc=DateTime.UtcNow,Symbol=o.Symbol,Timeframe=o.Timeframe,From=Historical(o.From.Date),To=Historical(o.To.Date),
                StartingBalance=o.Deposit,Fingerprint=fingerprint,GroupKey=(o.Training??new TrainingConfig()).Strategy+"/"+(o.Training??new TrainingConfig()).RuleVersion+"/"+o.Timeframe+"/"+fingerprint.Substring(0,12),
                Config=Json.Deserialize<TrainingConfig>(Json.Serialize(o.Training??new TrainingConfig())),SettingsJson=Json.Serialize(o),HistoryCheck=history=="clear"?"no_saved_overlap":history};
            AtomicJson(file,profile);
          }
        }
    }
    public static long Expiry(DateTime historical,int minutes)
    {
        if(minutes==0)return 0;DateTime end=minutes==-1?historical.Date.AddDays(1):historical.AddMinutes(minutes);
        return checked((long)(end-DesktopSnapshot.Epoch).TotalSeconds);
    }
    public static DateTime Historical(DateTime wallClock)
    {return DateTime.SpecifyKind(wallClock,DateTimeKind.Utc);}
    public static string CsvCell(string value,bool numeric=false)
    {
        value=value??"";string test=value.TrimStart(' ','\t','\r','\n');
        if(!numeric && test.Length>0 && "=+@-".IndexOf(test[0])>=0)value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    static void CsvWrite(string file,string[] header,IEnumerable<string[]> rows)
    {
        var text=new StringBuilder();text.AppendLine(String.Join(",",header.Select(x=>CsvCell(x)).ToArray()));
        var numeric=new HashSet<string>(new[]{"风险值","有效分钟","有效截至历史秒","余额","净值","持仓数","挂单数","命令序号","到期历史秒","是否核实事前计划","详情版本","失效价","目标价","复核退出分钟"});
        foreach(var row in rows)text.AppendLine(String.Join(",",row.Select((x,i)=>CsvCell(x,numeric.Contains(header[i]))).ToArray()));
        File.WriteAllText(file,text.ToString(),new UTF8Encoding(true));
    }
    static string Hist(DateTime time){return time==default(DateTime)?"":time.ToString("yyyy.MM.dd HH:mm:ss",CultureInfo.InvariantCulture);}
    static string Utc(DateTime time){return time==default(DateTime)?"":time.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture);}
    public static void ExportCsvVerified(string sessionId,string boot,string target,string sessionFolder,Action<TrainingData> prepare)
    {
        lock(Gate)using(var lease=new FileLease(DataFile(sessionId,boot,sessionFolder)))
        {
            // In portable runs source and destination can be the same directory. Keep read,
            // verification and publication in one lease so an independent save cannot be undone.
            var data=Load(sessionId,boot,sessionFolder);
            if(prepare!=null)prepare(data);
            ExportCsv(sessionId,boot,target,sessionFolder,data);
        }
    }
    public static void ExportCsv(string sessionId,string boot,string target,string sessionFolder=null,TrainingData exportData=null)
    {
        lock(Gate)
        {
            var data=exportData??Load(sessionId,boot,sessionFolder);NormalizeAndCheck(data,sessionId,boot);Directory.CreateDirectory(target);
            if(data.Profile==null && data.Plans.Count==0 && data.Moments.Count==0 && data.Reviews.Count==0)return;
            if(data.Profile!=null)AtomicJson(Path.Combine(target,"training","profile.json"),data.Profile);
            AtomicJson(Path.Combine(target,"training","runs",boot,"data.json"),data);
            CsvWrite(Path.Combine(target,"训练计划.csv"),new[]{"回放ID","运行ID","计划ID","交易想法ID","创建UTC","创建历史时间","入场理由","失效条件","退出办法","风险口径","风险值","有效分钟","有效截至历史秒","备注","详情版本","失效价","目标价","复核退出分钟","手动退出规则"},data.Plans.Select(p=>new[]{sessionId,boot,p.Id,p.IdeaId,Utc(p.CreatedUtc),Hist(p.HistoricalTime),String.Join("；",(p.Reasons??new string[0]).Select(Label).ToArray()),Label(p.FailureCondition),Label(p.ExitMethod),p.RiskBasis,p.RiskValue.ToString(CultureInfo.InvariantCulture),p.ExpiryMinutes.ToString(CultureInfo.InvariantCulture),p.ValidUntil.ToString(CultureInfo.InvariantCulture),p.Notes,p.DetailVersion.ToString(CultureInfo.InvariantCulture),p.InvalidationPrice.ToString(CultureInfo.InvariantCulture),p.TargetPrice.ToString(CultureInfo.InvariantCulture),p.ReviewMinutes.ToString(CultureInfo.InvariantCulture),p.ManualExitRule??""}));
            CsvWrite(Path.Combine(target,"练习阶段.csv"),new[]{"回放ID","运行ID","阶段ID","记录UTC","历史时间","阶段","原因","余额","净值","持仓数","挂单数","持仓挂单快照"},data.Moments.Select(m=>new[]{sessionId,boot,m.Id,Utc(m.CreatedUtc),Hist(m.HistoricalTime),Label(m.Mode),Label(m.Reason),m.Balance.ToString(CultureInfo.InvariantCulture),m.Equity.ToString(CultureInfo.InvariantCulture),m.Positions.ToString(CultureInfo.InvariantCulture),m.Pending.ToString(CultureInfo.InvariantCulture),m.Items}));
            CsvWrite(Path.Combine(target,"训练命令.csv"),new[]{"回放ID","运行ID","命令序号","历史时间","动作","计划ID","到期历史秒","状态","关联票号"},data.Commands.Select(c=>new[]{sessionId,boot,c.Sequence.ToString(CultureInfo.InvariantCulture),Hist(c.HistoricalTime),c.Action,c.PlanId,c.Expiry.ToString(CultureInfo.InvariantCulture),c.Status,String.Join(";",c.Tickets??new string[0])}));
            CsvWrite(Path.Combine(target,"执行复盘.csv"),new[]{"回放ID","运行ID","持仓ID","计划ID","入场符合","风险符合","退出符合","挂单符合","注意力","问题","是否核实事前计划","执行评价","记录UTC","备注"},data.Reviews.Select(r=>new[]{sessionId,boot,r.PositionId,r.PlanId,Label(r.EntryCompliance),Label(r.RiskCompliance),Label(r.ExitCompliance),Label(r.PendingCompliance),Label(r.Attention),String.Join("；",(r.Issues??new string[0]).Select(Label).ToArray()),r.VerifiedPreEntryPlan?"1":"0",Label(r.Grade),Utc(r.SavedUtc),r.Notes}));
        }
    }
}

public sealed class TrainingSetupControl : UserControl
{
    readonly CheckBox enabled=new CheckBox{Text="启用训练记录与阶段标记",AutoSize=true};
    readonly CheckBox require=new CheckBox{Text="下单前先选择交易计划（一次计划可用于同一交易想法）",AutoSize=true};
    readonly Dictionary<string,ComboBox> boxes=new Dictionary<string,ComboBox>();
    public TrainingSetupControl(ReplayOptions initial)
    {
        Dock=DockStyle.Fill;AutoScroll=true;BackColor=Color.White;
        var table=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(16),Margin=Padding.Empty};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(table);
        var c=initial.Training??new TrainingConfig();enabled.Checked=c.Enabled;require.Checked=c.RequirePlan;
        AddWide(table,enabled,34);AddWide(table,require,46);
        Add(table,"用途","Purpose",new[]{new TrainingChoice("explore","探索练习"),new TrainingChoice("rules","规则训练"),new TrainingChoice("holdout","留出检验")},c.Purpose);
        Add(table,"策略","Strategy",new[]{new TrainingChoice("ema","三EMA"),new TrainingChoice("structure","价格结构"),new TrainingChoice("breakout","突破 / 回踩"),new TrainingChoice("other","其他")},c.Strategy);
        Add(table,"规则版本","RuleVersion",new[]{new TrainingChoice("A","A"),new TrainingChoice("B","B"),new TrainingChoice("C","C"),new TrainingChoice("D","D")},c.RuleVersion);
        Add(table,"这段行情","Familiarity",new[]{new TrainingChoice("unknown","不确定"),new TrainingChoice("unseen","自己声明未看过"),new TrainingChoice("seen","已经看过")},c.Familiarity);
        Add(table,"持仓方式","PositionStyle",new[]{new TrainingChoice("undecided","探索中，尚未确定"),new TrainingChoice("intraday","日内"),new TrainingChoice("swing","允许跨日")},c.PositionStyle);
        AddWide(table,new ReplayStableLabel{Text="周期、指标或仓位公式变化会自动分组；用途与熟悉程度单独保存。\r\n留出检验会检查已有回放区间重叠；不能保证你在其他地方未见过行情。",AutoSize=true,MaximumSize=new Size(640,0)},66);
        AddWide(table,new ReplayStableLabel{Text="计划预算用于记录与复盘，不增加手数上限。止盈止损仍由你手动控制。\r\n可用底部按钮暂停本场；不提供关闭MT5后恢复同一测试的承诺。",AutoSize=true,MaximumSize=new Size(640,0)},66);
        enabled.CheckedChanged+=(s,e)=>UpdateEnabled();boxes["Purpose"].SelectedIndexChanged+=(s,e)=>UpdateEnabled();UpdateEnabled();
    }
    static void AddWide(TableLayoutPanel table,Control control,int height){int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Margin=new Padding(4,5,4,5);control.MinimumSize=new Size(0,height-10);table.Controls.Add(control,0,row);table.SetColumnSpan(control,2);}
    void Add(TableLayoutPanel table,string label,string key,TrainingChoice[] choices,string selected)
    {int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.Absolute,42));table.Controls.Add(new ReplayStableLabel{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);var box=ChoiceBox(choices,selected);box.Dock=DockStyle.Fill;box.Margin=new Padding(4,5,4,5);box.AccessibleName="Training"+key;table.Controls.Add(box,1,row);boxes.Add(key,box);}
    public static ComboBox ChoiceBox(IEnumerable<TrainingChoice> choices,string selected)
    {var box=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};foreach(var choice in choices)box.Items.Add(choice);for(int i=0;i<box.Items.Count;i++)if(((TrainingChoice)box.Items[i]).Code==selected)box.SelectedIndex=i;if(box.SelectedIndex<0 && box.Items.Count>0)box.SelectedIndex=0;return box;}
    public static string Code(ComboBox box){return box.SelectedItem is TrainingChoice?((TrainingChoice)box.SelectedItem).Code:"";}
    void UpdateEnabled()
    {
        bool strict=Code(boxes["Purpose"])!="explore";
        if(strict)require.Checked=true;require.Enabled=enabled.Checked && !strict;
        foreach(var box in boxes.Values)box.Enabled=enabled.Checked;
    }
    public void ReadInto(ReplayOptions o)
    {o.Training=new TrainingConfig{Enabled=enabled.Checked,RequirePlan=require.Checked || Code(boxes["Purpose"])!="explore",Purpose=Code(boxes["Purpose"]),Strategy=Code(boxes["Strategy"]),RuleVersion=Code(boxes["RuleVersion"]),Familiarity=Code(boxes["Familiarity"]),PositionStyle=Code(boxes["PositionStyle"])};TrainingStore.Validate(o,true);}
}

public sealed class TrainingSessionController
{
    readonly string sessionId,sessionFolder;TrainingData data;string boot="",lastMode="",pendingReason="",pendingMode="";long pendingModeSequence;
    Label title,summary,feedback;Func<DesktopSnapshot> snapshotGetter;Action<string> send;
    Control dispatchControl;IWin32Window planOwner;bool openAfterPause,dialogScheduled,planDialogOpen;string restoreAfterPlan="";
    readonly Dictionary<long,TrainingCommand> awaiting=new Dictionary<long,TrainingCommand>();
    readonly List<Button> liveButtons=new List<Button>();
    public bool Enabled {get{return data!=null && data.Profile!=null && data.Profile.Config!=null && data.Profile.Config.Enabled;}}
    public string Brief(DesktopSnapshot state)
    {
        if(!Enabled)return "训练记录未启用 / 本场为旧版";
        var plan=data.Plans.FirstOrDefault(p=>p.Id==data.ActivePlanId);if(plan==null)return "计划：尚未填写";
        if(String.IsNullOrWhiteSpace(plan.Id) || TrainingStore.PlanProblem(plan,false)!="")return "当前计划不完整，请新建；旧记录继续保留";
        string suffix=plan.ValidUntil==0?"无期限":DesktopSnapshot.Epoch.AddSeconds(plan.ValidUntil).ToString("MM-dd HH:mm")+"到期";
        if(state!=null && plan.ValidUntil>0 && state.Int("now")>=plan.ValidUntil)suffix="已到期，请重新判断";
        return "计划 "+plan.Id.Substring(0,Math.Min(8,plan.Id.Length))+" · "+suffix;
    }
    public TrainingSessionController(string sessionId,string folder=null){this.sessionId=sessionId;sessionFolder=folder;}
    public void SetDispatchControl(Control control){dispatchControl=control;}
    public decimal? ActiveBudget
    {
        get{var p=data==null?null:data.Plans.FirstOrDefault(x=>x.Id==data.ActivePlanId);if(p==null || TrainingStore.PlanProblem(p,false)!="" || p.RiskBasis=="unknown")return null;
            try{return p.RiskBasis=="usd"?p.RiskValue:data.Profile.StartingBalance>0?(decimal?)(data.Profile.StartingBalance/100*p.RiskValue):null;}catch(OverflowException){return null;}}
    }
    public bool BelongsToActiveIdea(string planId)
    {
        if(data==null || String.IsNullOrEmpty(planId))return false;var active=data.Plans.FirstOrDefault(p=>p.Id==data.ActivePlanId);var candidate=data.Plans.FirstOrDefault(p=>p.Id==planId);
        return active!=null && candidate!=null && (active.Id==candidate.Id || !String.IsNullOrEmpty(active.IdeaId) && active.IdeaId==candidate.IdeaId);
    }
    public long NextCommandSequence(DesktopSnapshot state,string action)
    {
        // Position management remains available even when ancillary training JSON cannot be read.
        bool tracked=new[]{"buy","sell","place_limit","modify_limit","training_pause","training_passive","training_active"}.Contains(action);
        if(tracked && state.Get("training_protocol")=="1")return TrainingStore.NextCommandSequence(sessionId,state.Get("boot"),state.Int("ack"),sessionFolder);
        return TrainingStore.NextCommandSequence(state.Int("ack"),boot==state.Get("boot")?data:null);
    }
    void LoadBoot(DesktopSnapshot snapshot)
    {
        if(snapshot==null || snapshot.Get("boot")==boot)return;string nextBoot=snapshot.Get("boot");var loaded=TrainingStore.Load(sessionId,nextBoot,sessionFolder);boot=nextBoot;data=loaded;lastMode=data.Moments.Count>0?data.Moments.Last().Mode:"";awaiting.Clear();foreach(var c in data.Commands.Where(x=>x.Status=="requested"))awaiting[c.Sequence]=c;pendingModeSequence=0;pendingReason=pendingMode="";openAfterPause=false;restoreAfterPlan="";
    }
    public Control BuildPanel(Func<DesktopSnapshot> snapshot,Action<string> sendTrainingCommand)
    {
        snapshotGetter=snapshot;send=sendTrainingCommand;
        var root=new ReplayBufferedTable{ColumnCount=1,RowCount=3,Height=96,MinimumSize=new Size(0,96),Dock=DockStyle.Fill,Padding=new Padding(6)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int height in new[]{22,32,30})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        title=new ReplayStableLabel{Text="交易计划 · 等待回放",Dock=DockStyle.Fill,AutoEllipsis=true};summary=new ReplayStableLabel{Text="先选择交易计划，再下单；同一想法共用预算。",Dock=DockStyle.Fill,AutoEllipsis=true};feedback=null;root.Controls.Add(title,0,0);root.Controls.Add(summary,0,1);dispatchControl=root;
        var planRow=new ReplayBufferedTable{ColumnCount=2,Dock=DockStyle.Fill};planRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));planRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));var create=Button("新交易计划",()=>RequestPlan(root.FindForm()));var show=Button("查看当前计划",()=>ShowPlan(root.FindForm()));planRow.Controls.Add(create,0,0);planRow.Controls.Add(show,1,0);root.Controls.Add(planRow,0,2);liveButtons.Add(create);return root;
    }
    static Button Button(string text,Action clicked){var button=new DesktopActionButton{Text=text,Dock=DockStyle.Fill,Margin=new Padding(2),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,BackColor=GraphiteTheme.Background,ForeColor=GraphiteTheme.Muted,Quiet=true};button.Click+=(s,e)=>{try{clicked();}catch(Exception ex){MessageBox.Show(button.FindForm(),ex.Message,"训练附加记录未读取");}};return button;}
    public void PausePractice(IWin32Window owner){ChangeMode(owner,"training_pause","manual_pause");}
    public void PauseForDayEnd(IWin32Window owner){ChangeMode(owner,"training_pause","day_end");}
    public void ResumePractice(IWin32Window owner){ChangeMode(owner,"training_active");}
    public void RequestPlan(IWin32Window owner)
    {
        var state=snapshotGetter==null?null:snapshotGetter();
        if(state==null || !state.Fresh || state.Get("training_protocol")!="1"){MessageBox.Show(owner,"请在新版回放连接后选择计划。旧场记录不会被补写为事前计划。","训练计划");return;}
        LoadBoot(state);if(data.Profile==null){MessageBox.Show(owner,"本场缺少事前训练分类，请从新版入口开始下一场。","训练计划");return;}
        if(!Enabled){if(feedback!=null)feedback.Text="本场未启用训练功能；可在下次启动前启用。";return;}
        if(pendingModeSequence>state.Int("ack")){if(feedback!=null)feedback.Text="等待上一阶段操作确认后再填写计划。";return;}
        if(openAfterPause || dialogScheduled || planDialogOpen)return;planOwner=owner;string mode=state.Get("training_mode");
        if(mode=="paused"){restoreAfterPlan="";SchedulePlanDialog();return;}
        if(mode!="active" && mode!="passive")return;
        openAfterPause=true;restoreAfterPlan=mode;pendingReason="填写事前计划";pendingMode="paused";
        if(feedback!=null)feedback.Text="先暂停历史时间，再填写选择题…";send("training_pause");
        if(pendingModeSequence==0){openAfterPause=false;restoreAfterPlan="";pendingReason=pendingMode="";if(feedback!=null)feedback.Text="暂停请求暂未发送，请等待上一操作确认后重试。";}
    }
    void SchedulePlanDialog()
    {
        if(dispatchControl==null || dispatchControl.IsDisposed || !dispatchControl.IsHandleCreated || dialogScheduled)return;
        string expectedBoot=boot;
        dialogScheduled=true;dispatchControl.BeginInvoke((Action)(()=>
        {
            dialogScheduled=false;openAfterPause=false;
            try
            {
                var state=snapshotGetter==null?null:snapshotGetter();if(state==null || state.Get("boot")!=expectedBoot || boot!=expectedBoot || state.Get("training_mode")!="paused")return;
                bool saved;planDialogOpen=true;try{saved=NewPlan(planOwner);}finally{planDialogOpen=false;}string restore=restoreAfterPlan;restoreAfterPlan="";
                if(saved && (restore=="active" || restore=="passive"))
                {pendingReason="事前计划已填写";pendingMode=restore;send(restore=="active"?"training_active":"training_passive");}
                else if(feedback!=null)feedback.Text="回放保持暂停；可点击底部“继续”。";
            }
            catch(Exception ex){if(feedback!=null)feedback.Text="计划未保存："+ex.Message;MessageBox.Show(planOwner,ex.Message,"训练计划");}
        }));
    }
    void ChangeMode(IWin32Window owner,string action,string reason=null)
    {
        var state=snapshotGetter==null?null:snapshotGetter();if(state==null || state.Get("training_protocol")!="1"){MessageBox.Show(owner,"本场不支持新版训练协议，请下一场从新版入口启动。","训练阶段");return;}
        LoadBoot(state);if(!Enabled || !state.Fresh)return;
        if(pendingModeSequence>state.Int("ack")){if(feedback!=null)feedback.Text="等待上一阶段操作确认…";return;}
        pendingReason=reason??(action=="training_pause"?"fatigue":action=="training_passive"?"observe_only":"resume");
        pendingMode=action=="training_pause"?"paused":action=="training_passive"?"passive":"active";
        if(feedback!=null)feedback.Text="等待测试器确认阶段变化…";send(action);
        if(pendingModeSequence==0){pendingReason=pendingMode="";if(feedback!=null)feedback.Text="请求暂未发送，请等待上一操作确认后重试。";}
    }
    bool NewPlan(IWin32Window owner)
    {
        var state=snapshotGetter==null?null:snapshotGetter();if(state==null || !state.Fresh || state.Get("training_protocol")!="1"){MessageBox.Show(owner,"请在新版回放连接后选择计划。旧场记录不会被补写为事前计划。","训练计划");return false;}LoadBoot(state);
        if(data.Profile==null){MessageBox.Show(owner,"本场缺少事前训练分类，不能追溯创建。请从新版入口开始下一场。","训练计划");return false;}
        using(var form=new TrainingPlanForm(data.Profile,state))if(form.ShowDialog(owner)==DialogResult.OK)
        {
            var latest=snapshotGetter==null?null:snapshotGetter();
            if(latest==null || !latest.Fresh || latest.Get("boot")!=form.Result.Boot || latest.Get("training_mode")!="paused")
                throw new InvalidOperationException("填写期间回放已结束、重启或恢复运行，计划未保存。请在当前场重新填写。");
            data.Plans.Add(form.Result);data.ActivePlanId=form.Result.Id;TrainingStore.SaveData(sessionId,boot,data,sessionFolder);RefreshLabels(latest);return true;
        }
        return false;
    }
    public void ShowCurrentPlan(IWin32Window owner){ShowPlan(owner);}
    void ShowPlan(IWin32Window owner)
    {
        var plan=data==null?null:data.Plans.FirstOrDefault(p=>p.Id==data.ActivePlanId);if(plan==null){MessageBox.Show(owner,"尚无当前计划。","训练计划");return;}
        string problem=TrainingStore.PlanProblem(plan,false);if(String.IsNullOrWhiteSpace(plan.Id) || problem!=""){MessageBox.Show(owner,"当前计划需重新填写："+problem+"\r\n旧记录继续保留，不会覆盖。","训练计划");return;}
        string risk=plan.RiskBasis=="unknown"?"尚未确定":plan.RiskBasis=="pct"?plan.RiskValue.ToString("0.##")+"% 初始本金":plan.RiskValue.ToString("0.##")+" USD";
        string detail=(plan.InvalidationPrice>0?"\r\n失效 / 止损价："+plan.InvalidationPrice.ToString(CultureInfo.InvariantCulture):"")+(plan.TargetPrice>0?"\r\n目标价："+plan.TargetPrice.ToString(CultureInfo.InvariantCulture):"");
        if(plan.ReviewMinutes!=0)detail+="\r\n复核 / 退出历史时刻："+DesktopSnapshot.Epoch.AddSeconds(TrainingStore.Expiry(plan.HistoricalTime,plan.ReviewMinutes)).ToString("yyyy.MM.dd HH:mm:ss");
        if(!String.IsNullOrEmpty(plan.ManualExitRule))detail+="\r\n手动退出条件："+(plan.ManualExitRule=="target_price"?"到达目标价":TrainingStore.Label(plan.ManualExitRule));
        MessageBox.Show(owner,"计划 "+plan.Id.Substring(0,Math.Min(8,plan.Id.Length))+"\r\n入场："+String.Join("、",plan.Reasons.Select(TrainingStore.Label).ToArray())+"\r\n失效："+TrainingStore.Label(plan.FailureCondition)+"\r\n退出："+TrainingStore.Label(plan.ExitMethod)+detail+"\r\n合计预算："+risk+"\r\n有效至："+(plan.ValidUntil==0?"无期限（探索）":DesktopSnapshot.Epoch.AddSeconds(plan.ValidUntil).ToString("yyyy.MM.dd HH:mm:ss"))+"\r\n备注："+plan.Notes+"\r\n\r\n计划仅记录，不自动设置止盈止损或平仓。保存后不覆盖；修改请创建新的交易想法。","当前事前计划");
    }
    TrainingCommand FindMapping(string ticket){return data==null?null:data.Commands.LastOrDefault(c=>(c.Tickets??new string[0]).Contains(ticket) && c.PlanId!="");}
    public bool PrepareCommand(IWin32Window owner,string action,ChartRequest request,DesktopSnapshot state,out string commandSuffix)
    {
        commandSuffix="";if(state==null)return false;
        bool entry=action=="buy" || action=="sell" || action=="place_limit";
        if(!entry && action!="modify_limit" && !action.StartsWith("training_",StringComparison.Ordinal))return true;
        if(state.Get("training_protocol")!="1")return true;LoadBoot(state);
        if(state.Get("training_protocol")!="1" || data.Profile==null || data.Profile.Config==null || !data.Profile.Config.Enabled)return true;
        if(action=="modify_limit")
        {
            var mapping=FindMapping(request==null?"":request.Ticket);if(mapping!=null)commandSuffix="training_plan="+mapping.PlanId+"\r\ntraining_expiry="+mapping.Expiry.ToString(CultureInfo.InvariantCulture)+"\r\n";
            return true;
        }
        if(!entry)return true;
        var plan=data.Plans.FirstOrDefault(p=>p.Id==data.ActivePlanId);
        bool needsPlan=plan==null ? data.Profile.Config.RequirePlan || data.Profile.Config.Purpose!="explore" :
            String.IsNullOrWhiteSpace(plan.Id) || (plan.ValidUntil>0 && state.Int("now")>=plan.ValidUntil) || TrainingStore.PlanProblem(plan,data.Profile.Config.Purpose!="explore")!="";
        if(needsPlan){RequestPlan(owner);return false;}
        if(state.Get("training_mode")!="active"){MessageBox.Show(owner,"回放尚未继续。点击底部“继续”后才能新增交易。已有仓位仍可平仓、撤单或在图表修改止盈止损。","训练阶段");return false;}
        if(plan!=null)commandSuffix="training_plan="+plan.Id+"\r\ntraining_expiry="+plan.ValidUntil.ToString(CultureInfo.InvariantCulture)+"\r\ntraining_mode=active\r\n";
        return true;
    }
    static string Value(string suffix,string key){foreach(string raw in (suffix??"").Split('\n')){string line=raw.TrimEnd('\r');if(line.StartsWith(key+"=",StringComparison.Ordinal))return line.Substring(key.Length+1);}return "";}
    public void CommandQueued(long sequence,string action,ChartRequest request,DesktopSnapshot state,string suffix)
    {
        if(!new[]{"buy","sell","place_limit","modify_limit","training_pause","training_passive","training_active"}.Contains(action))return;
        if(state==null || state.Get("training_protocol")!="1")return;LoadBoot(state);if(data.Profile==null)return;
        long expiration;Int64.TryParse(Value(suffix,"training_expiry"),out expiration);
        var command=new TrainingCommand{Sequence=sequence,Boot=boot,Action=action,PlanId=Value(suffix,"training_plan"),Expiry=expiration,HistoricalTime=TrainingStore.Historical(state.Now),CreatedUtc=DateTime.UtcNow,TargetTicket=request==null?state.Get("selected"):request.Ticket,BeforeTickets=ChartItem.Parse(state.Get("chart_items")).Select(i=>i.Ticket).ToArray()};
        data.Commands.RemoveAll(c=>c.Sequence==sequence);data.Commands.Add(command);awaiting[sequence]=command;
        if(action.StartsWith("training_",StringComparison.Ordinal))pendingModeSequence=sequence;
        TrainingStore.SaveData(sessionId,boot,data,sessionFolder);
    }
    public void CommandFailed(long sequence,string message)
    {
        TrainingCommand command;if(data==null || !awaiting.TryGetValue(sequence,out command))return;
        command.Status="not_sent: "+message;data.Commands.RemoveAll(c=>c.Sequence==sequence);data.Commands.Add(command);awaiting.Remove(sequence);
        if(pendingModeSequence==sequence){pendingModeSequence=0;pendingReason=pendingMode="";openAfterPause=false;restoreAfterPlan="";}
        TrainingStore.SaveData(sessionId,boot,data,sessionFolder);
    }
    public void Observe(DesktopSnapshot state)
    {
        if(state==null)return;LoadBoot(state);if(data.Profile==null || state.Get("training_protocol")!="1"){RefreshLabels(state);return;}
        bool changed=false;long ack=state.Int("ack");var items=ChartItem.Parse(state.Get("chart_items"));
        foreach(var pair in awaiting.Where(p=>p.Key<=ack).ToList())
        {
            var command=pair.Value;
            // SaveData may merge independent readers into new objects. Persist the updated
            // command explicitly instead of relying on its old reference remaining in data.
            data.Commands.RemoveAll(c=>c.Sequence==pair.Key);data.Commands.Add(command);
            if(pair.Key<ack){command.Status="ack_skipped: 后续命令已确认，本请求结果需以原始交易记录核实";awaiting.Remove(pair.Key);changed=true;continue;}
            command.Status=state.Get("status");string[] newTickets=items.Where(i=>!(command.BeforeTickets??new string[0]).Contains(i.Ticket)).Select(i=>i.Ticket).ToArray();
            if(new[]{"buy","sell","place_limit"}.Contains(command.Action))command.Tickets=newTickets;
            else if(command.TargetTicket!="0")command.Tickets=new[]{command.TargetTicket};
            awaiting.Remove(pair.Key);changed=true;
        }
        string mode=state.Get("training_mode");bool modeMomentRecorded=false;
        if(new[]{"active","passive","paused"}.Contains(mode) && mode!=lastMode)
        {
            bool requested=pendingModeSequence>0 && ack>=pendingModeSequence && mode==pendingMode;
            data.Moments.Add(new TrainingMoment{Id=Guid.NewGuid().ToString("N"),SessionId=sessionId,Boot=boot,HistoricalTime=TrainingStore.Historical(state.Now),CreatedUtc=DateTime.UtcNow,Mode=mode,Reason=requested?pendingReason:lastMode==""?(mode=="active"?"开始主动练习":mode=="paused"?"接入已暂停回放":"接入放置回放"):"测试器确认阶段变化",Balance=state.Number("balance"),Equity=state.Number("equity"),Positions=(int)state.Int("count"),Pending=(int)state.Int("pending_count"),Items=state.Get("chart_items")});lastMode=mode;changed=true;modeMomentRecorded=true;
        }
        if(pendingModeSequence>0 && ack>=pendingModeSequence)
        {
            bool confirmed=mode==pendingMode;
            if(confirmed && !modeMomentRecorded)
            {data.Moments.Add(new TrainingMoment{Id=Guid.NewGuid().ToString("N"),SessionId=sessionId,Boot=boot,HistoricalTime=TrainingStore.Historical(state.Now),CreatedUtc=DateTime.UtcNow,Mode=mode,Reason=pendingReason,Balance=state.Number("balance"),Equity=state.Number("equity"),Positions=(int)state.Int("count"),Pending=(int)state.Int("pending_count"),Items=state.Get("chart_items")});changed=true;}
            if(feedback!=null)feedback.Text=confirmed?"已确认并保存阶段快照。":"阶段未变更："+state.Get("status");
            if(!confirmed){openAfterPause=false;restoreAfterPlan="";}
            pendingModeSequence=0;pendingReason=pendingMode="";
        }
        if(changed)TrainingStore.SaveData(sessionId,boot,data,sessionFolder);
        if(openAfterPause && mode=="paused" && pendingModeSequence==0)SchedulePlanDialog();
        RefreshLabels(state);
    }
    void RefreshLabels(DesktopSnapshot state)
    {
        foreach(var button in liveButtons)button.Enabled=Enabled && state.Fresh && state.Get("training_protocol")=="1" && pendingModeSequence<=state.Int("ack") && !openAfterPause && !dialogScheduled && !planDialogOpen;
        if(title==null)return;
        if(data!=null && data.Profile!=null && data.Profile.Config!=null && !data.Profile.Config.Enabled){title.Text="本场未启用训练功能";summary.Text="未增加计划门槛；原有手动操作保留。";return;}
        if(state.Get("training_protocol")!="1" || data==null || data.Profile==null){title.Text="旧场回放 · 新训练功能下一场生效";summary.Text="历史交易不补写事前计划；原有手动操作保留。";return;}
        var c=data.Profile.Config;title.Text=TrainingStore.Label(c.Purpose)+" · 版本 "+c.RuleVersion;var p=data.Plans.FirstOrDefault(x=>x.Id==data.ActivePlanId);
        string risk=p==null?"暂无计划":p.RiskBasis=="unknown"?"预算未确定":p.RiskBasis=="pct"?"合计预算 "+p.RiskValue.ToString("0.##")+"%":"合计预算 "+p.RiskValue.ToString("0.##")+" USD";
        bool missing=ChartItem.Parse(state.Get("chart_items")).Any(i=>i.SL<=0);summary.Text=risk+(missing?"；有未设SL的持仓/挂单，实际风险未定义":"；预算仅记录，不另设手数上限");
    }
}
