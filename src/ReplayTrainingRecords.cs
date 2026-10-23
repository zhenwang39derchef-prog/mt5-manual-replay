using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

public sealed class TrainingRecordRow
{
    public RecordRun Run;
    public TrainingData Data;
    public int SourceRow;
    public string Boot,PositionId,PlanId="",IdeaId="",ObservedAttention="unknown",AttentionSource="未记录";
    public bool VerifiedPreEntryPlan;
    public TrainingPlan Plan;
    public TrainingReview Review;
    public CsvTable Trades {get{return Run.Bundle.Tables["交易结果.csv"];}}
    public string Get(string field){return Trades.Get(SourceRow,field);}
    public decimal Number(string field){decimal n;return Decimal.TryParse(Get(field),NumberStyles.Number,CultureInfo.InvariantCulture,out n)?n:0;}
    public bool Closed {get{decimal remaining,opened;return Get("状态")=="已平仓" && Decimal.TryParse(Get("剩余手数"),NumberStyles.Number,CultureInfo.InvariantCulture,out remaining) && Decimal.TryParse(Get("开仓手数"),NumberStyles.Number,CultureInfo.InvariantCulture,out opened) && remaining==0 && opened>0;}}
    public bool ReviewMatchesPlan {get{return Review!=null && VerifiedPreEntryPlan && Review.PlanId==PlanId;}}
    public bool UsesPendingOrder {get{return TrainingRecordService.UsesPendingOrder(Run,PositionId);}}
    public string Grade {get{string grade=Review==null?"pending":TrainingStore.EvaluateReview(Review,ReviewMatchesPlan && TrainingStore.IsCompletePlan(Plan));return grade=="qualified" && (!Closed || UsesPendingOrder && Review.PendingCompliance=="na")?"pending":grade;}}
    public string EffectiveAttention {get{return ObservedAttention!="unknown"?ObservedAttention:Review!=null && TrainingRecordService.Attentions.Contains(Review.Attention)?Review.Attention:"unknown";}}
    public string AttentionOrigin {get{return ObservedAttention!="unknown"?"阶段记录":Review!=null && Review.Attention!="unknown" && TrainingRecordService.Attentions.Contains(Review.Attention)?"事后补记":"未记录";}}
}

public sealed class TrainingRecordGroup
{
    public string Key,Strategy,Version,Timeframe,Fingerprint,PlanSignature,Purpose,Familiarity,Attention,AttentionSource;
    public int Closed,Open,Forced,Qualified,Problem,Pending;
    public decimal ClosedNet;
}

public static class TrainingRecordService
{
    public static readonly string[] Attentions={"active","passive","mixed","unknown"};
    public static readonly string[] SidecarNames={"训练计划.csv","练习阶段.csv","训练命令.csv","执行复盘.csv"};
    public static string Label(string category,string value)
    {
        string[] codes,labels;
        switch(category)
        {
            case "purpose":codes=new[]{"explore","rules","holdout"};labels=new[]{"探索练习","规则训练","留出检验"};break;
            case "familiarity":codes=new[]{"unseen","seen","unknown"};labels=new[]{"声明未看过","已经看过","不确定"};break;
            case "attention":codes=Attentions;labels=new[]{"主动管理","放置运行","混合阶段","未记录 / 不确定"};break;
            case "grade":codes=new[]{"qualified","problem","pending"};labels=new[]{"执行合格","执行问题","待核实"};break;
            case "strategy":codes=new[]{"ema","structure","breakout","other"};labels=new[]{"三EMA","价格结构","突破回踩","其他"};break;
            default:return String.IsNullOrWhiteSpace(value)?"未记录":value;
        }
        int at=Array.IndexOf(codes,value??"");return at>=0?labels[at]:"未记录";
    }
    public static string Boot(RecordRun run)
    {
        string boot=run.Bundle.Tables["回放汇总.csv"].Get(0,"运行ID");
        if(String.IsNullOrEmpty(boot) || boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-'))throw new FormatException("训练记录运行ID无效。");
        return boot;
    }
    public static string SessionFolder(RecordRun run)
    {
        string common=DesktopSessionFiles.Folder(run.Session),boot=Boot(run);
        return ResolveSessionFolder(common,run.Folder,boot);
    }
    public static string ResolveSessionFolder(string common,string exported,string boot)
    {
        // A session profile is not evidence that this particular boot's data survived.
        // Prefer a complete run before falling back to a profile-only session.
        if(File.Exists(Path.Combine(common,"training","runs",boot,"data.json")))return common;
        if(File.Exists(Path.Combine(exported,"training","runs",boot,"data.json")))return exported;
        if(File.Exists(Path.Combine(common,"training","profile.json")))return common;
        if(File.Exists(Path.Combine(exported,"training","profile.json")))return exported;
        return Directory.Exists(common)?common:exported;
    }
    public static TrainingData Load(RecordRun run){return TrainingStore.Load(run.Session,Boot(run),SessionFolder(run));}
    public static void Sync(RecordRun run,string sourceSessionFolder=null)
    {
        try { Export(run,run.Folder,sourceSessionFolder); }
        catch(Exception ex){RecordStore.LastError="交易CSV已保存；训练附加记录待同步："+ex.Message;}
    }
    public static void Export(RecordRun run,string target,string sourceSessionFolder=null)
    {
        string source=sourceSessionFolder??SessionFolder(run),boot=Boot(run);
        // Daily records remain independently portable even if training metadata needs repair.
        string dailySource=File.Exists(ReplayDaybook.FilePath(run.Session,boot,source))?source:
            File.Exists(ReplayDaybook.FilePath(run.Session,boot,DesktopSessionFiles.Folder(run.Session)))?DesktopSessionFiles.Folder(run.Session):run.Folder;
        Exception dailyError=null;try{ReplayDaybook.Export(run.Session,boot,target,dailySource);}catch(Exception ex){dailyError=ex;}
        TrainingStore.ExportCsvVerified(run.Session,boot,target,source,data=>
        {
            var rows=Rows(run,data);
            foreach(var review in data.Reviews)
            {
                var row=rows.FirstOrDefault(x=>x.PositionId==review.PositionId);
                review.Grade=row==null?TrainingStore.EvaluateReview(review,false):row.Grade;
                review.VerifiedPreEntryPlan=row!=null && row.ReviewMatchesPlan;
            }
        });
        if(dailyError!=null)throw new IOException("训练资料已导出；日内附加资料待同步："+dailyError.Message,dailyError);
    }
    static bool ContainsId(string raw,string id)
    {return (raw??"").Split(new[]{';',',','|',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Contains(id);}
    public static bool UsesPendingOrder(RecordRun run,string positionId)
    {
        CsvTable pending;
        if(run.Bundle.Tables.TryGetValue("挂单记录.csv",out pending))
            for(int r=0;r<pending.Rows.Count;r++)if(ContainsId(pending.Get(r,"关联持仓ID"),positionId))return true;
        CsvTable operations;
        if(run.Bundle.Tables.TryGetValue("操作记录.csv",out operations))
            for(int r=0;r<operations.Rows.Count;r++)if(operations.Get(r,"动作代码")=="place_limit" && ContainsId(operations.Get(r,"关联持仓ID"),positionId) && new[]{"ok","accepted","success"}.Contains(operations.Get(r,"训练校验")))return true;
        return false;
    }
    public static long HistoricalSeconds(string raw)
    {
        DateTime time;
        if(!DateTime.TryParseExact(raw,new[]{"yyyy.MM.dd HH:mm:ss.fff","yyyy.MM.dd HH:mm:ss","yyyy.MM.dd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out time))return 0;
        return (long)(time-new DateTime(1970,1,1)).TotalSeconds;
    }
    public static long HistoricalSeconds(DateTime time)
    {return time==default(DateTime)?0:(long)(time-new DateTime(1970,1,1)).TotalSeconds;}
    static List<string> PlanIdsForPosition(RecordRun run,string positionId)
    {
        var ids=new List<string>();var orderIds=new HashSet<string>();CsvTable pending;
        if(run.Bundle.Tables.TryGetValue("挂单记录.csv",out pending))
            for(int r=0;r<pending.Rows.Count;r++)if(ContainsId(pending.Get(r,"关联持仓ID"),positionId))
            {orderIds.Add(pending.Get(r,"订单编号"));string pendingPlan=pending.Get(r,"训练计划ID");if(DesktopSessionFiles.ValidId(pendingPlan))ids.Add(pendingPlan);}
        CsvTable operations;if(!run.Bundle.Tables.TryGetValue("操作记录.csv",out operations))return ids;
        for(int r=0;r<operations.Rows.Count;r++)
        {
            string action=operations.Get(r,"动作代码");
            if(action!="buy" && action!="sell" && action!="place_limit")continue;
            string plan=operations.Get(r,"训练计划ID"),validation=operations.Get(r,"训练校验");
            if(!DesktopSessionFiles.ValidId(plan) || !(validation=="ok" || validation=="accepted" || validation=="success"))continue;
            if(ContainsId(operations.Get(r,"关联持仓ID"),positionId) || orderIds.Contains(operations.Get(r,"关联订单号")))ids.Add(plan);
        }
        return ids.Distinct().ToList();
    }
    static string ObservedAttention(RecordRun run,TrainingData data,string positionId,long start,long end)
    {
        if(start<=0 || end<start)return "unknown";
        var events=new List<Tuple<long,string>>();
        if(data.Moments!=null)
            foreach(var moment in data.Moments)
                if(moment.Mode=="active" || moment.Mode=="passive")events.Add(Tuple.Create(HistoricalSeconds(moment.HistoricalTime),moment.Mode));
        CsvTable operations;
        if(run.Bundle.Tables.TryGetValue("操作记录.csv",out operations))
            for(int r=0;r<operations.Rows.Count;r++)
            {
                string mode=operations.Get(r,"训练阶段");long time=HistoricalSeconds(operations.Get(r,"历史操作时间"));
                if((mode=="active" || mode=="passive") && time>0)events.Add(Tuple.Create(time,mode));
            }
        events=events.OrderBy(x=>x.Item1).ToList();
        var before=events.LastOrDefault(x=>x.Item1<=start);
        if(before==null)return "unknown";
        var modes=new HashSet<string>{before.Item2};
        foreach(var item in events)if(item.Item1>start && item.Item1<=end)modes.Add(item.Item2);
        return modes.Count>1?"mixed":modes.First();
    }
    public static List<TrainingRecordRow> Rows(RecordRun run,TrainingData data=null)
    {
        data=data??Load(run);string boot=Boot(run);var table=run.Bundle.Tables["交易结果.csv"];
        var result=new List<TrainingRecordRow>();
        for(int i=0;i<table.Rows.Count;i++)
        {
            var row=new TrainingRecordRow{Run=run,Data=data,Boot=boot,SourceRow=i,PositionId=table.Get(i,"持仓ID")};
            var ids=PlanIdsForPosition(run,row.PositionId);
            if(ids.Count==1)
            {
                row.PlanId=ids[0];
                row.Plan=data.Plans==null?null:data.Plans.FirstOrDefault(p=>p.Id==row.PlanId && p.SessionId==run.Session && p.Boot==boot);
                long entry=HistoricalSeconds(row.Get("开仓时间"));
                row.VerifiedPreEntryPlan=row.Plan!=null && HistoricalSeconds(row.Plan.HistoricalTime)>0 && HistoricalSeconds(row.Plan.HistoricalTime)<=entry;
                if(row.Plan!=null)row.IdeaId=row.Plan.IdeaId??"";
            }
            row.Review=data.Reviews==null?null:data.Reviews.LastOrDefault(r=>r.PositionId==row.PositionId && r.SessionId==run.Session && r.Boot==boot);
            if(row.Review!=null)row.Review.VerifiedPreEntryPlan=row.ReviewMatchesPlan;
            long from=HistoricalSeconds(row.Get("开仓时间")),to=HistoricalSeconds(row.Get("最后平仓时间"));
            if(!row.Closed || to==0)to=HistoricalSeconds(run.Bundle.Tables["回放汇总.csv"].Get(0,"最新历史时间"));
            row.ObservedAttention=ObservedAttention(run,data,row.PositionId,from,to);
            result.Add(row);
        }
        return result;
    }
    public static List<TrainingRecordGroup> Group(IEnumerable<TrainingRecordRow> rows)
    {
        var groups=new Dictionary<string,TrainingRecordGroup>();
        foreach(var row in rows)
        {
            var p=row.Data.Profile;var config=p==null?null:p.Config;
            string strategy=config==null?"unknown":config.Strategy,version=config==null?"未记录":config.RuleVersion;
            string frame=row.Run.Bundle.Tables["回放汇总.csv"].Get(0,"周期").Replace("PERIOD_","");
            if(String.IsNullOrEmpty(frame))frame=p==null?"未记录":p.Timeframe;
            string fingerprint=p==null?"旧记录配置未知":p.Fingerprint;
            string key=p==null?"历史未标注|"+row.Run.Bundle.Tables["回放汇总.csv"].Get(0,"品种")+"|"+frame:p.GroupKey;
            if(p!=null && frame!=p.Timeframe)key+="|实际周期="+frame;
            string planSignature=PlanSignature(row.Plan);
            string purpose=config==null?"unknown":config.Purpose,familiarity=config==null?"unknown":config.Familiarity,attention=row.EffectiveAttention;
            string attentionSource=row.AttentionOrigin;
            string composite=String.Join("\u001f",new[]{key,planSignature,purpose,familiarity,attention,attentionSource});TrainingRecordGroup group;
            if(!groups.TryGetValue(composite,out group))
            {
                group=new TrainingRecordGroup{Key=key,Strategy=strategy,Version=version,Timeframe=frame,Fingerprint=fingerprint,PlanSignature=planSignature,Purpose=purpose,Familiarity=familiarity,Attention=attention,AttentionSource=attentionSource};groups.Add(composite,group);
            }
            if(row.Closed){group.Closed++;group.ClosedNet+=row.Number("已实现净盈亏");if(row.Get("退出原因")=="测试结束强平")group.Forced++;}else group.Open++;
            switch(row.Grade){case "qualified":group.Qualified++;break;case "problem":group.Problem++;break;default:group.Pending++;break;}
        }
        return groups.Values.OrderBy(x=>x.Strategy).ThenBy(x=>x.Version).ThenBy(x=>x.Timeframe).ThenBy(x=>x.Purpose).ThenBy(x=>x.Attention).ToList();
    }
    public static string PlanSignature(TrainingPlan plan)
    {
        if(plan==null)return "事前计划未核实";
        bool custom=(plan.Reasons??new string[0]).Contains("other") || plan.FailureCondition=="other" || plan.ExitMethod=="other";
        string canonical=String.Join("|",new[]{plan.DetailVersion.ToString(CultureInfo.InvariantCulture),String.Join(",",(plan.Reasons??new string[0]).OrderBy(x=>x).ToArray()),plan.FailureCondition??"",plan.ExitMethod??"",plan.RiskBasis??"",plan.RiskValue.ToString(CultureInfo.InvariantCulture),plan.ExpiryMinutes.ToString(CultureInfo.InvariantCulture),plan.ExitMethod=="manual_exit"?plan.ManualExitRule??"":"",plan.FailureCondition=="time_limit" || plan.ExitMethod=="time_limit"?plan.ReviewMinutes.ToString(CultureInfo.InvariantCulture):"",custom?(plan.Notes??"").Trim().Replace("\r\n","\n"):""});
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","").Substring(0,12);
    }
    public static string PlanText(TrainingPlan plan)
    {
        if(plan==null)return "未记录";
        string details=plan.InvalidationPrice>0?"；失效价 "+plan.InvalidationPrice.ToString("0.###",CultureInfo.InvariantCulture):"";
        if(plan.TargetPrice>0)details+="；目标价 "+plan.TargetPrice.ToString("0.###",CultureInfo.InvariantCulture);
        if(plan.ReviewMinutes!=0)details+="；复核 "+(plan.ReviewMinutes<0?"历史日结束":plan.ReviewMinutes+"分钟后");
        if(plan.ExitMethod=="manual_exit")details+="；手动退出条件："+TrainingStore.Label(plan.ManualExitRule);
        return "理由："+String.Join("、",(plan.Reasons??new string[0]).Select(TrainingStore.Label).ToArray())+"；失效："+TrainingStore.Label(plan.FailureCondition)+"；退出："+TrainingStore.Label(plan.ExitMethod)+details+"；预算："+plan.RiskValue.ToString(CultureInfo.InvariantCulture)+(plan.RiskBasis=="pct"?"%":plan.RiskBasis=="usd"?" USD":"（未确定）")+"；挂单期限："+(plan.ExpiryMinutes<0?"历史日结束":plan.ExpiryMinutes==0?"无期限（探索）":plan.ExpiryMinutes+"分钟")+((plan.Reasons??new string[0]).Contains("other") || plan.FailureCondition=="other" || plan.ExitMethod=="other"?"；自定义："+plan.Notes:"");
    }
    public static string CsvCell(string text,bool numeric=false)
    {
        text=text??"";string probe=text.TrimStart(' ','\t','\r','\n');
        if(!numeric && probe.Length>0 && "=+-@".IndexOf(probe[0])>=0)text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    public static void ExportGroups(IEnumerable<TrainingRecordGroup> groups,string file)
    {
        var rows=new List<string>{"策略,规则版本,周期,配置指纹,计划条件签名,用途,历史熟悉程度,持仓阶段,阶段来源,已平仓数,未平仓数,结束强平数,已全平净盈亏金额合计,执行合格数,执行问题数,待核实数,统计口径"};
        foreach(var g in groups)
        {
            string[] text={Label("strategy",g.Strategy),g.Version,g.Timeframe,g.Fingerprint,g.PlanSignature,Label("purpose",g.Purpose),Label("familiarity",g.Familiarity),Label("attention",g.Attention),g.AttentionSource};
            string[] numbers={g.Closed.ToString(CultureInfo.InvariantCulture),g.Open.ToString(CultureInfo.InvariantCulture),g.Forced.ToString(CultureInfo.InvariantCulture),g.ClosedNet.ToString(CultureInfo.InvariantCulture),g.Qualified.ToString(CultureInfo.InvariantCulture),g.Problem.ToString(CultureInfo.InvariantCulture),g.Pending.ToString(CultureInfo.InvariantCulture)};
            rows.Add(String.Join(",",text.Select(x=>CsvCell(x)).Concat(numbers.Select(x=>CsvCell(x,true))).Concat(new[]{CsvCell("跨场已全平金额合计，非连续账户收益率；重叠持仓不等于独立样本")}).ToArray()));
        }
        File.WriteAllText(file,String.Join("\r\n",rows)+"\r\n",new UTF8Encoding(true));
    }
}

public sealed class TrainingRecordChoice
{
    public string Code,Text;
    public TrainingRecordChoice(string code,string text){Code=code;Text=text;}
    public override string ToString(){return Text;}
}

public sealed class TrainingReviewForm : Form
{
    readonly RecordRun run;
    readonly string sessionFolder;
    TrainingData data;
    List<TrainingRecordRow> rows;
    readonly DataGridView grid=new DataGridView();
    readonly ComboBox entry=new ComboBox(),risk=new ComboBox(),exit=new ComboBox(),pending=new ComboBox(),attention=new ComboBox();
    readonly CheckedListBox issues=new CheckedListBox();
    readonly TextBox notes=new TextBox();
    readonly Label binding=new Label(),grade=new Label(),status=new Label();
    readonly CheckBox sameIdea=new CheckBox();
    TrainingRecordRow current;
    bool filling,dirty,restoreSelectionPending;
    static readonly string[] IssueCodes={"early_entry","unplanned_add","over_budget","widened_stop","late_exit","expired_pending","fatigue_passive","other"};
    static readonly string[] IssueLabels={"提前 / 计划外入场","计划外加仓","超过合计风险预算","扩大止损","延迟退出","过期挂单未撤","违反暂停 / 结束计划，继续放置","其他执行问题"};
    public TrainingReviewForm(RecordRun selected,TrainingData supplied=null,string folder=null)
    {
        run=selected;sessionFolder=folder??TrainingRecordService.SessionFolder(run);data=supplied??TrainingRecordService.Load(run);
        Text="选择题执行复盘 · 收益和执行分开评价";Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.FromArgb(244,247,251);
        ClientSize=new Size(960,760);MinimumSize=new Size(740,600);StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=6};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,150));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(root);
        var introduction=new Label{AutoSize=true,MaximumSize=new Size(1200,0),Text="复盘为事后填写。原始成交和事前计划不改写；未知保持待核实，盈利不会自动判合格。",Margin=new Padding(0,0,0,8)};root.Controls.Add(introduction,0,0);
        ConfigureGrid(grid);grid.CurrentCellChanged+=(s,e)=>SelectRow();root.Controls.Add(grid,0,1);
        binding.AutoSize=true;binding.MaximumSize=new Size(1200,0);binding.Margin=new Padding(0,7,0,7);root.Controls.Add(binding,0,2);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};var form=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,Padding=new Padding(0,0,18,0)};
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(form);root.Controls.Add(scroll,0,4);
        AddChoice(form,"入场是否按计划",entry,new[]{new TrainingRecordChoice("unknown","未选择 / 无法核实"),new TrainingRecordChoice("yes","是，按事前计划入场"),new TrainingRecordChoice("no","否，存在计划外入场")});
        AddChoice(form,"合计风险是否合规",risk,new[]{new TrainingRecordChoice("unknown","未选择 / 无法核实"),new TrainingRecordChoice("yes","是，遵守同一想法的合计预算"),new TrainingRecordChoice("no","否，超预算或计划外加仓")});
        AddChoice(form,"退出是否按计划",exit,new[]{new TrainingRecordChoice("unknown","未选择 / 无法核实"),new TrainingRecordChoice("yes","是，按退出计划处理"),new TrainingRecordChoice("no","否，未按退出计划处理"),new TrainingRecordChoice("test_end","测试终点退出（需要核实计划）")});
        AddChoice(form,"挂单管理是否合规",pending,new[]{new TrainingRecordChoice("unknown","未选择 / 无法核实"),new TrainingRecordChoice("yes","是，按有效期和撤销条件处理"),new TrainingRecordChoice("no","否，旧挂单 / 到期处理不合规"),new TrainingRecordChoice("na","不适用，没有使用挂单")});
        AddChoice(form,"实际操作方式",attention,new[]{new TrainingRecordChoice("unknown","未选择 / 不确定"),new TrainingRecordChoice("active","主动观察与管理"),new TrainingRecordChoice("passive","主要放置运行"),new TrainingRecordChoice("mixed","主动与放置混合")});
        int r=form.RowCount++;form.RowStyles.Add(new RowStyle(SizeType.Absolute,130));form.Controls.Add(new Label{Text="存在的问题（多选）",Dock=DockStyle.Fill,TextAlign=ContentAlignment.TopLeft,Padding=new Padding(0,7,0,0)},0,r);
        issues.CheckOnClick=true;issues.Dock=DockStyle.Fill;issues.Items.AddRange(IssueLabels);form.Controls.Add(issues,1,r);issues.ItemCheck+=(s,e)=>{if(!filling){dirty=true;BeginInvoke(new Action(UpdateGrade));}};
        r=form.RowCount++;form.RowStyles.Add(new RowStyle(SizeType.Absolute,72));form.Controls.Add(new Label{Text="补充说明（可不填）",Dock=DockStyle.Fill,Padding=new Padding(0,7,0,0)},0,r);
        notes.Multiline=true;notes.ScrollBars=ScrollBars.Vertical;notes.MaxLength=1500;notes.Dock=DockStyle.Fill;form.Controls.Add(notes,1,r);notes.TextChanged+=(s,e)=>{if(!filling){dirty=true;UpdateGrade();}};
        r=form.RowCount++;form.RowStyles.Add(new RowStyle(SizeType.AutoSize));sameIdea.Text="将这份复盘同时应用到本场相同交易想法的持仓（仅有准确事前关联时可用）";sameIdea.AutoSize=true;sameIdea.Margin=new Padding(0,8,0,8);form.Controls.Add(sameIdea,0,r);form.SetColumnSpan(sameIdea,2);
        grade.AutoSize=true;grade.MaximumSize=new Size(660,0);grade.Margin=new Padding(0,6,0,10);grade.Font=new Font(Font.FontFamily,10F,FontStyle.Bold);root.Controls.Add(grade,0,3);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));status.Dock=DockStyle.Fill;status.AutoEllipsis=true;status.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(status,0,0);
        var save=new Button{Text="保存这笔执行复盘",Dock=DockStyle.Fill,AccessibleName="SaveTrainingReview"};save.Click+=(s,e)=>SaveCurrent(true);footer.Controls.Add(save,1,0);root.Controls.Add(footer,0,5);
        SizeChanged+=(s,e)=>{binding.MaximumSize=new Size(Math.Max(400,ClientSize.Width-40),0);introduction.MaximumSize=new Size(Math.Max(400,ClientSize.Width-40),0);grade.MaximumSize=new Size(Math.Max(400,ClientSize.Width-40),0);};
        PopulateRows();Shown+=(s,e)=>{ReplayWindowLayout.Scale(this);ReplayWindowLayout.Fit(this);};FormClosing+=(s,e)=>{if(dirty && !SaveCurrent(false))e.Cancel=true;};
    }
    static void ConfigureGrid(DataGridView g)
    {g.Dock=DockStyle.Fill;g.ReadOnly=true;g.AllowUserToAddRows=g.AllowUserToDeleteRows=false;g.RowHeadersVisible=false;g.SelectionMode=DataGridViewSelectionMode.FullRowSelect;g.MultiSelect=false;g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells;g.BackgroundColor=Color.White;}
    void AddChoice(TableLayoutPanel parent,string label,ComboBox combo,TrainingRecordChoice[] choices)
    {
        int row=parent.RowCount++;parent.RowStyles.Add(new RowStyle(SizeType.Absolute,35));parent.Controls.Add(new Label{Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);
        combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Dock=DockStyle.Fill;combo.Items.AddRange(choices);combo.SelectedIndex=0;parent.Controls.Add(combo,1,row);
        combo.SelectedIndexChanged+=(s,e)=>{if(!filling){dirty=true;UpdateGrade();}};
    }
    static string Code(ComboBox c){var choice=c.SelectedItem as TrainingRecordChoice;return choice==null?"unknown":choice.Code;}
    static void Select(ComboBox c,string code){int index=-1;for(int i=0;i<c.Items.Count;i++)if(((TrainingRecordChoice)c.Items[i]).Code==code)index=i;c.SelectedIndex=Math.Max(0,index);}
    void PopulateRows()
    {
        filling=true;try
        {
            rows=TrainingRecordService.Rows(run,data);grid.Columns.Clear();grid.Rows.Clear();
            foreach(string h in new[]{"持仓ID","方向","状态","已实现净盈亏","含浮动参考盈亏","事前计划","阶段记录","执行评价"})grid.Columns.Add(h,h);
            foreach(var row in rows){int at=grid.Rows.Add(row.PositionId,row.Get("方向"),row.Get("状态"),row.Get("已实现净盈亏"),row.Get("含浮动参考盈亏"),row.VerifiedPreEntryPlan?"已关联":"无可核实关联",TrainingRecordService.Label("attention",row.ObservedAttention),TrainingRecordService.Label("grade",row.Grade));grid.Rows[at].Tag=row;}
        }finally{filling=false;}
        SelectRow();
    }
    void SelectRow()
    {
        if(filling || restoreSelectionPending || grid.CurrentRow==null)return;var next=grid.CurrentRow.Tag as TrainingRecordRow;if(next==null || next==current)return;
        if(current!=null && dirty && !SaveCurrent(false))
        {
            // SelectionChanged can fire before DataGridView finishes changing CurrentCell.
            // Restore after that transition so highlighted position and unsaved answers agree.
            restoreSelectionPending=true;
            Action restore=()=>
            {
                if(IsDisposed){restoreSelectionPending=false;return;}
                filling=true;
                try{grid.ClearSelection();foreach(DataGridViewRow row in grid.Rows)if(Object.ReferenceEquals(row.Tag,current)){grid.CurrentCell=row.Cells[0];row.Selected=true;break;}}
                finally{filling=false;restoreSelectionPending=false;}
            };
            if(IsHandleCreated)BeginInvoke(restore);else restore();
            return;
        }
        current=next;filling=true;
        try
        {
            var r=current.Review;
            exit.Items.Clear();exit.Items.Add(new TrainingRecordChoice("unknown",current.Closed?"未选择 / 无法核实":"尚未平仓，退出执行待完成"));
            if(current.Closed)exit.Items.Add(new TrainingRecordChoice("yes","是，按退出计划处理"));
            exit.Items.Add(new TrainingRecordChoice("no","否，未按退出计划处理"));if(current.Closed)exit.Items.Add(new TrainingRecordChoice("test_end","测试终点退出（需要核实计划）"));
            pending.Items.Clear();pending.Items.Add(new TrainingRecordChoice("unknown","未选择 / 无法核实"));pending.Items.Add(new TrainingRecordChoice("yes","是，按有效期和撤销条件处理"));pending.Items.Add(new TrainingRecordChoice("no","否，旧挂单 / 到期处理不合规"));if(!current.UsesPendingOrder)pending.Items.Add(new TrainingRecordChoice("na","不适用，没有使用挂单"));
            bool changedPlan=r!=null && current.VerifiedPreEntryPlan && !current.ReviewMatchesPlan;
            Select(entry,r==null || changedPlan?"unknown":r.EntryCompliance);Select(risk,r==null || changedPlan?"unknown":r.RiskCompliance);Select(exit,r==null || changedPlan?"unknown":r.ExitCompliance);Select(pending,r==null || changedPlan?"unknown":r.PendingCompliance);Select(attention,r==null?"unknown":r.Attention);
            for(int i=0;i<issues.Items.Count;i++)issues.SetItemChecked(i,r!=null && r.Issues!=null && r.Issues.Contains(IssueCodes[i]));notes.Text=r==null?"":r.Notes??"";
            sameIdea.Checked=false;sameIdea.Enabled=current.VerifiedPreEntryPlan && !String.IsNullOrEmpty(current.IdeaId) && rows.Count(x=>x.VerifiedPreEntryPlan && x.IdeaId==current.IdeaId)>1;
            binding.Text=current.VerifiedPreEntryPlan?"已关联下单前保存的计划。本场相同交易想法共 "+rows.Count(x=>x.VerifiedPreEntryPlan && x.IdeaId==current.IdeaId)+" 笔，风险需要合计。":"没有可核实的事前计划关联。可以补记问题，但不能补成事前计划或自动评为合格。";
            if(current.VerifiedPreEntryPlan && !TrainingStore.IsCompletePlan(current.Plan))binding.Text+="\r\n这份探索计划仍有未确定内容，执行评价保持待核实。";
            if(changedPlan)binding.Text+="\r\n旧答案未关联到这份计划，请重新核对四项执行选择；问题和备注已保留。";
            binding.Text+="\r\n记录阶段："+TrainingRecordService.Label("attention",current.ObservedAttention)+"。阶段问题与执行评价分开记录。";
            if(current.Plan!=null)binding.Text+="\r\n"+TrainingRecordService.PlanText(current.Plan);
            dirty=false;
        }finally{filling=false;}
        UpdateGrade();
    }
    TrainingReview ReadReview(TrainingRecordRow row)
    {
        return new TrainingReview{SessionId=run.Session,Boot=row.Boot,PositionId=row.PositionId,PlanId=row.PlanId,
            VerifiedPreEntryPlan=row.VerifiedPreEntryPlan,EntryCompliance=Code(entry),RiskCompliance=Code(risk),ExitCompliance=!row.Closed && Code(exit)=="yes"?"unknown":Code(exit),PendingCompliance=row.UsesPendingOrder && Code(pending)=="na"?"unknown":Code(pending),Attention=Code(attention),
            Issues=issues.CheckedIndices.Cast<int>().Select(i=>IssueCodes[i]).ToArray(),Notes=notes.Text,Grade="pending",SavedUtc=DateTime.UtcNow};
    }
    void UpdateGrade()
    {
        if(current==null)return;var review=ReadReview(current);string code=TrainingStore.EvaluateReview(review,current.VerifiedPreEntryPlan && TrainingStore.IsCompletePlan(current.Plan));
        if(!current.Closed && code=="qualified")code="pending";
        grade.Text="执行评价："+TrainingRecordService.Label("grade",code)+"    本笔已实现净盈亏："+current.Get("已实现净盈亏")+"（结果不参与执行打分）";
        grade.ForeColor=code=="problem"?Color.Firebrick:code=="qualified"?Color.DarkGreen:Color.FromArgb(120,85,15);
    }
    bool SaveCurrent(bool feedback)
    {
        if(current==null){status.Text="请先选择一笔持仓。";return true;}
        try
        {
            var targets=sameIdea.Checked && sameIdea.Enabled?rows.Where(x=>x.VerifiedPreEntryPlan && x.IdeaId==current.IdeaId).ToList():new List<TrainingRecordRow>{current};
            foreach(var row in targets){var review=ReadReview(row);TrainingStore.SaveReview(run.Session,row.Boot,review,sessionFolder);row.Review=review;}
            TrainingRecordService.Export(run,run.Folder,sessionFolder);dirty=false;
            foreach(DataGridViewRow item in grid.Rows){var row=item.Tag as TrainingRecordRow;if(row!=null)item.Cells["执行评价"].Value=TrainingRecordService.Label("grade",row.Grade);}
            status.Text=(feedback?"已保存":"已自动保存复盘草稿")+"，共 "+targets.Count+" 笔；原始成交CSV未改写。";UpdateGrade();return true;
        }
        catch(Exception ex){status.Text="复盘未保存："+ex.Message;if(feedback)MessageBox.Show(this,ex.Message,"保存执行复盘");return false;}
    }
}

public sealed class TrainingGroupForm : Form
{
    readonly List<RecordRun> runs;
    readonly DataGridView grid=new DataGridView();
    readonly ComboBox purpose=new ComboBox(),familiarity=new ComboBox(),attention=new ComboBox();
    readonly Label status=new Label();
    readonly List<string> loadWarnings=new List<string>();
    readonly Button details=new Button{Text="读取问题详情",AutoSize=true,Height=30,Visible=false};
    List<TrainingRecordRow> rows=new List<TrainingRecordRow>();
    List<TrainingRecordGroup> shown=new List<TrainingRecordGroup>();
    public TrainingGroupForm(IEnumerable<RecordRun> source)
    {
        runs=source.ToList();Text="训练分组 · 配置与操作阶段分别统计";Font=new Font("Microsoft YaHei UI",9F);ClientSize=new Size(1100,650);MinimumSize=new Size(760,500);StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=4};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,30));Controls.Add(root);
        var filter=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
        AddFilter(filter,"用途",purpose,new[]{"all","explore","rules","holdout","unknown"},"purpose");AddFilter(filter,"行情熟悉程度",familiarity,new[]{"all","unseen","seen","unknown"},"familiarity");AddFilter(filter,"阶段",attention,new[]{"all","active","passive","mixed","unknown"},"attention");
        var export=new Button{Text="导出当前分组CSV",AutoSize=true,Height=30};export.Click+=(s,e)=>ExportGroups();filter.Controls.Add(export);details.Click+=(s,e)=>MessageBox.Show(this,String.Join("\r\n\r\n",loadWarnings.ToArray()),"训练资料读取问题");filter.Controls.Add(details);root.Controls.Add(filter,0,0);
        var warning=new Label{AutoSize=true,MaximumSize=new Size(1800,0),Margin=new Padding(0,8,0,8),Text="按策略 / 规则版本 / 周期 / 配置指纹分组；用途、熟悉程度和阶段独立区分。金额为跨场已全平净盈亏合计，不能当连续账户收益率。重叠持仓不等于独立样本；未标注旧记录保留。"};root.Controls.Add(warning,0,1);
        grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells;grid.BackgroundColor=Color.White;root.Controls.Add(grid,0,2);status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;status.AutoEllipsis=true;root.Controls.Add(status,0,3);
        foreach(var run in runs)
        {
            try{rows.AddRange(TrainingRecordService.Rows(run));}
            catch(Exception ex)
            {
                loadWarnings.Add(Path.GetFileName(run.Folder)+"："+ex.Message+"。原始成交仍计入，训练分类与执行评价保留未知。");
                rows.AddRange(TrainingRecordService.Rows(run,new TrainingData()));
            }
        }
        details.Visible=loadWarnings.Count>0;
        Populate();Shown+=(s,e)=>{ReplayWindowLayout.Scale(this);ReplayWindowLayout.Fit(this);};
    }
    void AddFilter(Control host,string text,ComboBox combo,string[] codes,string category)
    {
        host.Controls.Add(new Label{Text=text,AutoSize=true,Margin=new Padding(0,8,5,0)});combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Width=145;
        foreach(string code in codes)combo.Items.Add(new TrainingRecordChoice(code,code=="all"?"全部":TrainingRecordService.Label(category,code)));combo.SelectedIndex=0;combo.SelectedIndexChanged+=(s,e)=>Populate();host.Controls.Add(combo);
    }
    static string Code(ComboBox c){var option=c.SelectedItem as TrainingRecordChoice;return option==null?"all":option.Code;}
    void Populate()
    {
        if(grid==null)return;
        var all=TrainingRecordService.Group(rows);
        shown=all.Where(g=>(Code(purpose)=="all" || g.Purpose==Code(purpose)) && (Code(familiarity)=="all" || g.Familiarity==Code(familiarity)) && (Code(attention)=="all" || g.Attention==Code(attention))).ToList();
        grid.Rows.Clear();grid.Columns.Clear();foreach(string h in new[]{"策略","规则版本","周期","配置指纹","计划条件签名","用途","行情熟悉程度","阶段","阶段来源","已平","未平","结束强平","已全平净盈亏合计","执行合格","执行问题","待核实"})grid.Columns.Add(h,h);
        foreach(var g in shown)grid.Rows.Add(TrainingRecordService.Label("strategy",g.Strategy),g.Version,g.Timeframe,g.Fingerprint,g.PlanSignature,TrainingRecordService.Label("purpose",g.Purpose),TrainingRecordService.Label("familiarity",g.Familiarity),TrainingRecordService.Label("attention",g.Attention),g.AttentionSource,g.Closed,g.Open,g.Forced,g.ClosedNet.ToString("0.00",CultureInfo.InvariantCulture),g.Qualified,g.Problem,g.Pending);
        status.Text=(loadWarnings.Count>0?loadWarnings.Count+" 场训练资料异常，成交保留为未知；":"")+"当前 "+shown.Count+" 组；已平 "+shown.Sum(g=>g.Closed)+" 笔，未平/待核对 "+shown.Sum(g=>g.Open)+" 笔。";
        status.ForeColor=loadWarnings.Count>0?Color.Firebrick:SystemColors.ControlText;
    }
    void ExportGroups()
    {
        using(var dialog=new SaveFileDialog{Filter="CSV文件|*.csv",FileName="训练分组统计.csv",DefaultExt="csv"})
            if(dialog.ShowDialog(this)==DialogResult.OK)try{TrainingRecordService.ExportGroups(shown,dialog.FileName);status.Text="已导出当前筛选的分组CSV。"+(loadWarnings.Count>0?loadWarnings.Count+" 场训练资料异常，成交已按未知保留。":"");}catch(Exception ex){status.Text="导出失败："+ex.Message;}
    }
}
