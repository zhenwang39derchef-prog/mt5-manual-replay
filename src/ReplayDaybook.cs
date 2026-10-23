using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

public static class IntradayRisk
{
    // Informational linear XAUUSD estimate in account currency, before fees/slippage.
    public static decimal? Loss(DesktopSnapshot state,string side,decimal volume,decimal reference,decimal stop,bool entry)
    {
        if(state==null || !(state.Get("symbol").StartsWith("XAUUSD",StringComparison.OrdinalIgnoreCase)) ||
            state.Get("account_currency")!="USD" || volume<=0 || reference<=0 || stop<=0 || (side!="buy" && side!="sell"))return null;
        decimal step=state.Number("tick_size"),value=state.Number("tick_value_loss");if(step<=0 || value<=0)return null;
        decimal distance=side=="buy"?reference-stop:stop-reference;
        if(entry && distance<=0)return null;
        // A stop must still be on the permissible side of the current executable quote.
        if(!entry){decimal quote=state.Number(side=="buy"?"bid":"ask");if(quote<=0 || (side=="buy"?stop>=quote:stop<=quote))return null;}
        try{return Math.Round(Math.Max(0,distance)/step*value*volume,2);}catch(OverflowException){return null;}
    }
    public static Dictionary<string,string> Plans(DesktopSnapshot state)
    {
        var result=new Dictionary<string,string>();if(state==null)return result;
        foreach(string row in state.Get("item_plans").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
        {var p=row.Split(',');ulong ticket;if(p.Length!=3 || !new[]{"P","O"}.Contains(p[0]) || !UInt64.TryParse(p[1],out ticket) || ticket==0 || (!String.IsNullOrEmpty(p[2]) && !DesktopSessionFiles.ValidId(p[2])))continue;string key=p[0]+":"+p[1];if(!result.ContainsKey(key))result.Add(key,p[2]);else result[key]="";}
        return result;
    }
    public static decimal? ItemLoss(DesktopSnapshot state,ChartItem item)
    {
        if(item==null)return null;
        try{return Loss(state,item.Side,item.Lots,item.Kind=="O"?(decimal)item.Entry:state.Number(item.Side=="buy"?"bid":"ask"),(decimal)item.SL,item.Kind=="O");}
        catch(OverflowException){return null;}
    }
}

public sealed class ReplayDayObservation
{
    public DateTime Day {get;set;}
    public DateTime FirstHistorical {get;set;}
    public DateTime LastHistorical {get;set;}
    public decimal FirstBalance {get;set;}
    public decimal LastBalance {get;set;}
    public decimal LastEquity {get;set;}
    public decimal PeakEquity {get;set;}
    public decimal MaxObservedDrawdown {get;set;}
    public decimal MaxObservedDrawdownPct {get;set;}
    public bool BeganAfterMidnight {get;set;}
}
public sealed class ReplayDayCheckpoint
{
    public string Id {get;set;}
    public DateTime CreatedUtc {get;set;}
    public DateTime Historical {get;set;}
    public decimal Balance {get;set;}
    public decimal Equity {get;set;}
    public int Positions {get;set;}
    public int Pending {get;set;}
    public string Items {get;set;}
    public string ItemPlans {get;set;}
    public string Mode {get;set;}
}
public sealed class ReplayDayData
{
    public string Session {get;set;}
    public string Boot {get;set;}
    public List<ReplayDayObservation> Days {get;set;}
    public List<ReplayDayCheckpoint> Checkpoints {get;set;}
    public ReplayDayData(){Days=new List<ReplayDayObservation>();Checkpoints=new List<ReplayDayCheckpoint>();}
}
public sealed class ReplayDaybook
{
    readonly string session,boot,folder,file;ReplayDayData data;long savedHour=-1;
    static readonly object Gate=new object();
    public static string FilePath(string session,string boot,string folder=null)
    {
        if(!DesktopSessionFiles.ValidId(session) || String.IsNullOrEmpty(boot) || boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-'))throw new ArgumentException("日内记录标识无效。");
        return Path.Combine(folder??DesktopSessionFiles.Folder(session),"daily","runs",boot,"daybook.json");
    }
    static ReplayDayData Read(string path,string session,string boot)
    {
        if(!File.Exists(path))return new ReplayDayData{Session=session,Boot=boot};
        ReplayDayData value;
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true))value=new JavaScriptSerializer().Deserialize<ReplayDayData>(reader.ReadToEnd());
        if(value==null || value.Session!=session || value.Boot!=boot || value.Days==null || value.Checkpoints==null ||
           value.Days.Any(d=>d==null || d.Day!=d.Day.Date || d.FirstHistorical.Date!=d.Day || d.LastHistorical.Date!=d.Day || d.FirstHistorical>d.LastHistorical || d.PeakEquity<d.LastEquity || d.MaxObservedDrawdown<0 || d.MaxObservedDrawdownPct<0) ||
           value.Days.Select(d=>d.Day).Distinct().Count()!=value.Days.Count || value.Checkpoints.Any(c=>c==null || !DesktopSessionFiles.ValidId(c.Id) || c.Mode!="paused"))throw new FormatException("日内记录损坏或与本场不匹配，未覆盖原资料。");
        return value;
    }
    public ReplayDaybook(string session,string boot,string folder=null)
    {this.session=session;this.boot=boot;this.folder=folder;file=FilePath(session,boot,folder);data=Read(file,session,boot);}
    public ReplayDayObservation Current(DateTime day){return data.Days.FirstOrDefault(d=>d.Day==day.Date);}
    public ReplayDayObservation Observe(DesktopSnapshot state)
    {
        if(state==null || state.Get("session")!=session || state.Get("boot")!=boot)throw new FormatException("日内快照与本场不匹配。");
        DateTime now=state.Now;var row=Current(now.Date);
        if(row==null){row=new ReplayDayObservation{Day=now.Date,FirstHistorical=now,LastHistorical=now,FirstBalance=state.Number("balance"),LastBalance=state.Number("balance"),LastEquity=state.Number("equity"),PeakEquity=state.Number("equity"),BeganAfterMidnight=now.TimeOfDay!=TimeSpan.Zero};data.Days.Add(row);}
        if(now<row.LastHistorical)throw new FormatException("日内历史时间倒退，未合并到原日记录。");
        row.LastHistorical=now;row.LastBalance=state.Number("balance");row.LastEquity=state.Number("equity");row.PeakEquity=Math.Max(row.PeakEquity,row.LastEquity);
        decimal drawdown=Math.Max(0,row.PeakEquity-row.LastEquity);row.MaxObservedDrawdown=Math.Max(row.MaxObservedDrawdown,drawdown);
        if(row.PeakEquity>0)row.MaxObservedDrawdownPct=Math.Max(row.MaxObservedDrawdownPct,drawdown/row.PeakEquity*100);
        long hour=state.Int("now")/3600;if(hour!=savedHour){Save();savedHour=hour;}return row;
    }
    static void Merge(ReplayDayData destination,ReplayDayData incoming)
    {
        foreach(var row in incoming.Days)
        {
            var previous=destination.Days.FirstOrDefault(d=>d.Day==row.Day);if(previous==null){destination.Days.Add(row);continue;}
            if(row.FirstHistorical<previous.FirstHistorical){previous.FirstHistorical=row.FirstHistorical;previous.FirstBalance=row.FirstBalance;previous.BeganAfterMidnight=row.BeganAfterMidnight;}
            if(row.LastHistorical>=previous.LastHistorical){previous.LastHistorical=row.LastHistorical;previous.LastBalance=row.LastBalance;previous.LastEquity=row.LastEquity;}
            previous.PeakEquity=Math.Max(previous.PeakEquity,row.PeakEquity);previous.MaxObservedDrawdown=Math.Max(previous.MaxObservedDrawdown,row.MaxObservedDrawdown);previous.MaxObservedDrawdownPct=Math.Max(previous.MaxObservedDrawdownPct,row.MaxObservedDrawdownPct);
            decimal combined=Math.Max(0,previous.PeakEquity-previous.LastEquity);previous.MaxObservedDrawdown=Math.Max(previous.MaxObservedDrawdown,combined);if(previous.PeakEquity>0)previous.MaxObservedDrawdownPct=Math.Max(previous.MaxObservedDrawdownPct,combined/previous.PeakEquity*100);
        }
        destination.Days=destination.Days.OrderBy(d=>d.Day).ToList();destination.Checkpoints=destination.Checkpoints.Concat(incoming.Checkpoints).GroupBy(c=>c.Id).Select(g=>g.First()).OrderBy(c=>c.Historical).ToList();
    }
    void Save()
    {
        lock(Gate)
        {
            string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant()))).Replace("-","").Substring(0,32);
            using(var mutex=new Mutex(false,@"Global\CNReplayDay_"+key))
            {bool held=false;try{try{held=mutex.WaitOne(3000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("日内资料正在保存，请稍后重试。");var existing=Read(file,session,boot);Merge(existing,data);TrainingStore.AtomicJson(file,existing);data=existing;}finally{if(held)mutex.ReleaseMutex();}}
        }
    }
    public ReplayDayCheckpoint Checkpoint(DesktopSnapshot state)
    {
        if(state==null || !state.Fresh || state.Get("training_mode")!="paused")throw new InvalidOperationException("测试器尚未确认暂停，收工现场未保存。");
        Observe(state);var row=new ReplayDayCheckpoint{Id=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow,Historical=state.Now,Balance=state.Number("balance"),Equity=state.Number("equity"),Positions=(int)state.Int("count"),Pending=(int)state.Int("pending_count"),Items=state.Get("chart_items"),ItemPlans=state.Get("item_plans"),Mode="paused"};
        data.Checkpoints.Add(row);try{Save();}catch{data.Checkpoints.RemoveAll(c=>c.Id==row.Id);throw;}return row;
    }
    public void Flush(){Save();}
    public static void Export(string session,string boot,string target,string sourceFolder=null)
    {
        string path=FilePath(session,boot,sourceFolder);if(!File.Exists(path))return;
        var value=Read(path,session,boot);string targetPath=FilePath(session,boot,target);if(Path.GetFullPath(path)!=Path.GetFullPath(targetPath))TrainingStore.AtomicJson(targetPath,value);
        string observation="回放ID,运行ID,历史日期,首次接入历史时间,最后观测历史时间,首次余额,最后余额,余额变化,最后净值,观测峰值净值,观测最大回撤金额,观测最大回撤百分比,口径\r\n";
        foreach(var d in value.Days)observation+=Line(new[]{session,boot,d.Day.ToString("yyyy.MM.dd"),Hist(d.FirstHistorical),Hist(d.LastHistorical),N(d.FirstBalance),N(d.LastBalance),N(d.LastBalance-d.FirstBalance),N(d.LastEquity),N(d.PeakEquity),N(d.MaxObservedDrawdown),N(d.MaxObservedDrawdownPct),"从本程序首次接入起，抽样观测；不能代替逐Tick最大回撤"});
        string checkpoints="回放ID,运行ID,现场ID,保存UTC,历史时间,余额,净值,持仓数,挂单数,阶段,持仓挂单快照,计划关联\r\n";
        foreach(var c in value.Checkpoints)checkpoints+=Line(new[]{session,boot,c.Id,c.CreatedUtc.ToString("o"),Hist(c.Historical),N(c.Balance),N(c.Equity),c.Positions.ToString(),c.Pending.ToString(),"已确认暂停",c.Items,c.ItemPlans});
        Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"日内观测.csv"),observation,new UTF8Encoding(true));File.WriteAllText(Path.Combine(target,"收工现场.csv"),checkpoints,new UTF8Encoding(true));
    }
    static string Hist(DateTime v){return v.ToString("yyyy.MM.dd HH:mm:ss",CultureInfo.InvariantCulture);}
    static string N(decimal v){return v.ToString(CultureInfo.InvariantCulture);}
    static string Line(string[] cells){return String.Join(",",cells.Select(v=>TrainingStore.CsvCell(v,DecimalValue(v))).ToArray())+"\r\n";}
    static bool DecimalValue(string value){decimal number;return Decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out number);}
}
