using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// These views read the sealed archive. They never send trading or tester commands.
public static class FinishViewText
{
    public static string Money(decimal value){return value.ToString("N2",CultureInfo.InvariantCulture);}
    public static string Historical(DateTime value){return value.ToString("yyyy.MM.dd HH:mm:ss",CultureInfo.InvariantCulture);}
    public static string Price(double value){return value<=0?"—":value.ToString("0.##",CultureInfo.InvariantCulture);}
    public static string Get(FinishSnapshot s,string name){string value;return s!=null&&s.Values!=null&&s.Values.TryGetValue(name,out value)?value:"";}
    public static string Label(string code)
    {
        switch(code??"")
        {
            case "followed":return "按计划";case "partial":return "部分偏离";case "deviated":return "明显偏离";case "unknown":return "无法核实";
            case "unplanned":return "计划外入场或加仓";case "over_budget":return "超预算";case "late_exit":return "退出或挂单处理拖延";case "attention":return "注意力与节奏";case "none":return "没有明显问题";case "pending":return "待核实";
            case "keep":return "保持已验证的流程";case "plan":return "只做计划内机会";case "risk_exit":return "下单前核对风险与退出";case "pace":return "减少频次，固定复核节奏";
            case "needs_review":return "需要改进";
            default:return TrainingStore.Label(code??"");
        }
    }
    public static string Orders(FinishSnapshot s)
    {
        var b=new StringBuilder();var items=ChartItem.Parse(s.Items??"");
        if(items.Count==0)return "当时没有持仓或挂单。";
        foreach(var item in items)
        {
            b.AppendLine((item.Kind=="P"?"持仓":"挂单")+"  #"+item.Ticket+"  "+(item.Side=="buy"?"买入":"卖出")+"  "+item.Lots.ToString("0.###",CultureInfo.InvariantCulture)+" 手");
            b.AppendLine("入场 / 挂单价 "+Price(item.Entry)+"    止损 "+Price(item.SL)+"    止盈 "+Price(item.TP));
            b.AppendLine(item.Kind=="P"?"收工时仍持有；退出执行保留为待核实。":"收工时仍有效；后续成交或撤销另见最终结果。");b.AppendLine();
        }
        return b.ToString().TrimEnd();
    }
    public static string PlansAndSettings(FinishSnapshot s)
    {
        var b=new StringBuilder();var data=s.Training;
        b.AppendLine("收工时的计划与设置 · 冻结副本");b.AppendLine();
        if(data==null||data.Plans==null||data.Plans.Count==0)b.AppendLine("当时没有已保存的计划。");
        else foreach(var p in data.Plans)
        {
            b.AppendLine("计划 #"+p.Id+"  ·  "+Historical(p.HistoricalTime));
            b.AppendLine("入场依据："+String.Join("、",(p.Reasons??new string[0]).Select(TrainingStore.Label).ToArray()));
            b.AppendLine("失效条件："+TrainingStore.Label(p.FailureCondition)+"    失效价："+(p.InvalidationPrice<=0?"—":PaperPriceInput.Display(p.InvalidationPrice)));
            b.AppendLine("退出方式："+TrainingStore.Label(p.ExitMethod)+"    目标价："+(p.TargetPrice<=0?"—":PaperPriceInput.Display(p.TargetPrice)));
            if(!String.IsNullOrEmpty(p.ManualExitRule))b.AppendLine("手动退出规则："+TrainingStore.Label(p.ManualExitRule));
            b.AppendLine("风险预算："+p.RiskValue.ToString("0.##",CultureInfo.InvariantCulture)+" "+(p.RiskBasis=="pct"?"% 初始本金":p.RiskBasis=="usd"?"USD":TrainingStore.Label(p.RiskBasis)));
            b.AppendLine("到期："+(p.ValidUntil>0?DesktopSnapshot.Epoch.AddSeconds(p.ValidUntil).ToString("yyyy.MM.dd HH:mm:ss"):"不设期限")+"    复核间隔："+p.ReviewMinutes+" 分钟");
            if(!String.IsNullOrWhiteSpace(p.Notes))b.AppendLine("备注："+p.Notes);b.AppendLine();
        }
        b.AppendLine("订单与计划关联：");b.AppendLine(String.IsNullOrEmpty(s.ItemPlans)?"无":s.ItemPlans.Replace(";",Environment.NewLine));b.AppendLine();
        b.AppendLine("收工时的回放设置");
        foreach(string name in new[]{"playback_mode","playback_target","playback_actual","training_mode","bid","ask","spread"})
        {string value=Get(s,name);if(value!=""){string label=name=="playback_mode"?"速度模式":name=="playback_target"?"观察目标倍率":name=="playback_actual"?"实测倍率":name=="training_mode"?"交易阶段":name=="bid"?"Bid参考报价":name=="ask"?"Ask参考报价":"点差";if(name=="playback_mode")value=value=="mt5"?"MT5快放":"观察";if(name=="training_mode")value=TrainingStore.Label(value);b.AppendLine(label+"："+value);}}
        if(data!=null&&data.Profile!=null)
        {
            var p=data.Profile;b.AppendLine();b.AppendLine("启动设置");b.AppendLine(p.Symbol+" · "+p.Timeframe+" · "+p.From.ToString("yyyy.MM.dd")+" → "+p.To.ToString("yyyy.MM.dd"));
            b.AppendLine("初始本金："+Money(p.StartingBalance));
            if(p.Config!=null)b.AppendLine("训练："+TrainingStore.Label(p.Config.Purpose)+" · "+TrainingStore.Label(p.Config.Strategy)+" · 规则版本 "+p.Config.RuleVersion);
            if(!String.IsNullOrWhiteSpace(p.SettingsJson))
            {
                try{var settings=new JavaScriptSerializer().DeserializeObject(p.SettingsJson) as Dictionary<string,object>;if(settings!=null)foreach(var pair in settings)b.AppendLine(pair.Key+" = "+Convert.ToString(pair.Value,CultureInfo.InvariantCulture));else b.AppendLine(p.SettingsJson);}
                catch{b.AppendLine(p.SettingsJson);}
            }
        }
        return b.ToString().TrimEnd();
    }
    public static string Review(FinishReview r)
    {
        if(r==null)return "本场尚未填写复盘。";
        var b=new StringBuilder();b.AppendLine(r.Completed?"整场复盘已完成":"整场复盘草稿");b.AppendLine();
        b.AppendLine("整体执行："+(String.IsNullOrEmpty(r.Compliance)?"未填写":Label(r.Compliance)));
        b.AppendLine("需要改进："+((r.Issues??new string[0]).Length==0?"未填写":String.Join("、",r.Issues.Select(Label).ToArray())));
        b.AppendLine("下一场调整："+(String.IsNullOrEmpty(r.NextAction)?"未填写":Label(r.NextAction)));
        if(!String.IsNullOrWhiteSpace(r.Notes)){b.AppendLine();b.AppendLine("备注："+r.Notes);}
        if(r.PerTrade!=null&&r.PerTrade.Count>0){b.AppendLine();b.AppendLine("逐笔补充");foreach(var pair in r.PerTrade){var p=pair.Key.Split(':');string title=p.Length>=2?(p[0]=="P"?"收工持仓 #":p[0]=="O"?"收工挂单 #":"交易持仓 #")+p[1]:"交易 #"+pair.Key;if(p.Length>2&&p[2]=="exit")title+=" · 退出执行";b.AppendLine(title+"："+Label(pair.Value));}}
        if(r.SavedUtc!=DateTime.MinValue){b.AppendLine();b.AppendLine("保存于 "+r.SavedUtc.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss"));}return b.ToString();
    }
    public static TextBox ReadOnly(string text)
    {var view=new TextBox{Text=text??"",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=ReplayPaper.Surface,ForeColor=ReplayPaper.Ink,Font=new Font("Microsoft YaHei UI",10F),TabStop=true};view.Enter+=(s,e)=>view.Select(0,0);return view;}
    public static void OpenFolder(IWin32Window owner,string folder)
    {try{Process.Start(new ProcessStartInfo{FileName=folder,UseShellExecute=true});}catch(Exception e){MessageBox.Show(owner,"文件夹暂时无法打开："+e.Message,"收工档案",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
}

public sealed class FinishReviewForm:ReplayBufferedForm
{
    readonly FinishArchive archive;
    readonly PlanAnswerChoices compliance,nextAction;
    readonly PlanReasonChoices issues=new PlanReasonChoices();
    readonly TextBox notes=new TextBox{Multiline=true,MaxLength=2000,ScrollBars=ScrollBars.Vertical};
    readonly Label progress=ReplayPaper.Label(""),question=ReplayPaper.Label("",16,ReplayPaper.Ink),hint=ReplayPaper.Label(""),validation=ReplayPaper.Label(""),receipt=ReplayPaper.Label(""),status=ReplayPaper.Label("");
    readonly Button back=new PaperActionButton{Text="上一步"},next=new PaperActionButton{Text="下一题",Primary=true},draft=new PaperActionButton{Text="保存草稿，稍后复盘",Quiet=true};
    readonly Panel pages=new ReplayBufferedPanel{Dock=DockStyle.Fill};readonly Panel[] page=new Panel[3];readonly TableLayoutPanel content;
    readonly Dictionary<string,string> perTrade=new Dictionary<string,string>();
    int step;bool busy,allowClose,edited,loading=true,exitRetry,savedCompleted,scaled;
    public Func<bool,Task<string>> ExitRequested {get;set;}
    public FinishArchive Archive {get{return archive;}}
    public bool ReviewSaved {get;private set;}
    public bool Completed {get{return archive.Review!=null&&archive.Review.Completed;}}
    public void ReportExitProgress(string message){if(!IsDisposed)status.Text=message??"";}
    public FinishReviewForm(FinishArchive value)
    {
        if(value==null||value.Snapshot==null)throw new ArgumentNullException("value");archive=value;
        Text="本场收工 · 简短复盘";Font=new Font("Microsoft YaHei UI",10);BackColor=ReplayPaper.Background;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(680,650);MinimumSize=new Size(510,480);
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(28)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var r in new[]{new RowStyle(SizeType.Absolute,22),new RowStyle(SizeType.Absolute,40),new RowStyle(SizeType.Absolute,64),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.Absolute,30),new RowStyle(SizeType.Absolute,48),new RowStyle(SizeType.Absolute,30)})root.RowStyles.Add(r);Controls.Add(root);
        root.Controls.Add(ReplayPaper.Label("收工现场已归档",10,ReplayPaper.Accent),0,0);
        var title=ReplayPaper.Label(archive.Snapshot.Symbol+"  "+archive.Snapshot.Period+" · 本场复盘",20,ReplayPaper.Ink);root.Controls.Add(title,0,1);
        var saved=new PaperCard{Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8),Margin=new Padding(0,0,0,10)};
        receipt.Text="历史 "+FinishViewText.Historical(archive.Snapshot.Historical)+"    净值 "+FinishViewText.Money(archive.Snapshot.Equity)+"\r\n已保存到 "+archive.Folder;receipt.AccessibleName="FinishSavedReceipt";receipt.AccessibleDescription="点击打开保存位置："+archive.Folder;receipt.Cursor=Cursors.Hand;receipt.AutoEllipsis=true;receipt.Click+=(s,e)=>FinishViewText.OpenFolder(this,archive.Folder);receipt.Font=new Font(Font.FontFamily,9);saved.Controls.Add(receipt);root.Controls.Add(saved,0,2);
        var body=new PaperCard{Dock=DockStyle.Fill,Padding=new Padding(14),Margin=Padding.Empty,AutoScroll=true};content=new ReplayBufferedTable{Dock=DockStyle.Top,Height=275,ColumnCount=1,RowCount=4,Margin=Padding.Empty};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,23));content.RowStyles.Add(new RowStyle(SizeType.Absolute,56));content.RowStyles.Add(new RowStyle(SizeType.Absolute,40));content.RowStyles.Add(new RowStyle(SizeType.Absolute,150));body.Controls.Add(content);root.Controls.Add(body,0,3);content.Controls.Add(progress,0,0);content.Controls.Add(question,0,1);content.Controls.Add(hint,0,2);content.Controls.Add(pages,0,3);
        for(int i=0;i<page.Length;i++){page[i]=new ReplayBufferedPanel{Dock=DockStyle.Fill,Visible=false,AutoScroll=true};pages.Controls.Add(page[i]);}
        compliance=new PlanAnswerChoices(new[]{C("followed","按计划"),C("partial","部分偏离"),C("deviated","明显偏离"),C("unknown","无法核实")}){AccessibleName="FinishCompliance",BackColor=ReplayPaper.Surface};page[0].Controls.Add(compliance);
        foreach(var c in new[]{C("unplanned","计划外入场或加仓"),C("over_budget","超预算"),C("late_exit","退出或挂单处理拖延"),C("attention","注意力与节奏"),C("none","没有明显问题"),C("pending","待核实")})issues.Items.Add(c);
        issues.AccessibleName="FinishIssues";issues.Dock=DockStyle.Fill;issues.BackColor=ReplayPaper.Surface;page[1].Controls.Add(issues);
        nextAction=new PlanAnswerChoices(new[]{C("keep","保持已验证的流程"),C("plan","只做计划内机会"),C("risk_exit","下单前核对风险与退出"),C("pace","减少频次，固定复核节奏")}){AccessibleName="FinishNextAction",BackColor=ReplayPaper.Surface,Dock=DockStyle.Fill};
        var last=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty};last.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));last.RowStyles.Add(new RowStyle(SizeType.Absolute,128));last.RowStyles.Add(new RowStyle(SizeType.Absolute,20));last.RowStyles.Add(new RowStyle(SizeType.Absolute,44));last.RowStyles.Add(new RowStyle(SizeType.Absolute,32));page[2].Controls.Add(last);last.Controls.Add(nextAction,0,0);last.Controls.Add(ReplayPaper.Label("可补一句具体行动 / 备注",9),0,1);notes.Dock=DockStyle.Fill;notes.AccessibleName="FinishNotes";last.Controls.Add(notes,0,2);
        var trades=new PaperActionButton{Text="展开逐笔复盘（可选）",Quiet=true,Dock=DockStyle.Fill,AccessibleName="FinishPerTrade"};last.Controls.Add(trades,0,3);trades.Click+=(s,e)=>{try{using(var form=new FinishTradeReviewForm(archive,perTrade)){if(form.ShowDialog(this)==DialogResult.OK){perTrade.Clear();foreach(var p in form.Result)perTrade.Add(p.Key,p.Value);edited=true;}}}catch(Exception ex){validation.Text="逐笔记录读取失败："+ex.Message;}};
        validation.ForeColor=Color.FromArgb(176,70,70);validation.AccessibleName="FinishValidation";root.Controls.Add(validation,0,4);
        var buttons=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));foreach(var button in new[]{draft,back,next}){button.Dock=DockStyle.Fill;button.Margin=new Padding(3);}draft.AccessibleName="FinishSaveDraft";back.AccessibleName="FinishBack";next.AccessibleName="FinishNext";buttons.Controls.Add(draft,0,0);buttons.Controls.Add(back,1,0);buttons.Controls.Add(next,2,0);root.Controls.Add(buttons,0,5);status.AccessibleName="FinishExitStatus";status.Font=new Font(Font.FontFamily,9);root.Controls.Add(status,0,6);
        back.Click+=(s,e)=>ShowStep(step-1);next.Click+=async(s,e)=>{if(exitRetry){await SaveAndExitAsync(savedCompleted);return;}if(ValidateStep(step)){if(step<2)ShowStep(step+1);else await SaveAndExitAsync(true);}};draft.Click+=async(s,e)=>await SaveAndExitAsync(false);
        compliance.SelectedIndexChanged+=(s,e)=>Changed();nextAction.SelectedIndexChanged+=(s,e)=>Changed();notes.TextChanged+=(s,e)=>Changed();
        foreach(Control c in Descendants(issues))if(c is PlanChoiceButton)c.Click+=(s,e)=>{Changed();NormalizeIssues((PlanChoiceButton)s);};
        ApplyExisting();ReplayPaper.ApplyInputs(this);question.ForeColor=ReplayPaper.Ink;progress.ForeColor=ReplayPaper.Accent;validation.ForeColor=Color.FromArgb(176,70,70);ShowStep(0);loading=false;ReplayWindowLayout.Scale(this);scaled=true;ShowStep(0);Shown+=(s,e)=>ReplayWindowLayout.Fit(this);FormClosing+=OnClosing;
    }
    static TrainingChoice C(string code,string text){return new TrainingChoice(code,text);}
    static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var d in Descendants(c))yield return d;}}
    void Changed(){if(!loading){edited=true;exitRetry=false;validation.Text="";ShowButtonText();}}
    void NormalizeIssues(PlanChoiceButton source)
    {
        int chosen=issues.Items.ToList().FindIndex(c=>c.Text==source.Text);if(chosen<0)return;
        string code=issues.Items[chosen].Code;
        if(issues.GetItemChecked(chosen)&&code=="none")for(int i=0;i<issues.Items.Count;i++){if(i!=chosen)issues.SetItemChecked(i,false);}
        else if(issues.GetItemChecked(chosen))for(int i=0;i<issues.Items.Count;i++)if(issues.Items[i].Code=="none")issues.SetItemChecked(i,false);
    }
    void ApplyExisting()
    {
        var r=archive.Review;if(r==null)return;
        SetChoice(compliance,r.Compliance);SetChoice(nextAction,r.NextAction);notes.Text=r.Notes??"";
        for(int i=0;i<issues.Items.Count;i++)issues.SetItemChecked(i,(r.Issues??new string[0]).Contains(issues.Items[i].Code));
        if(r.PerTrade!=null)foreach(var pair in r.PerTrade)perTrade[pair.Key]=pair.Value;
    }
    static void SetChoice(PlanAnswerChoices choices,string code){for(int i=0;i<choices.Items.Count;i++)if(choices.Items[i].Code==code){choices.SelectedIndex=i;break;}}
    void ShowStep(int value)
    {
        step=Math.Max(0,Math.Min(2,value));for(int i=0;i<page.Length;i++)page[i].Visible=i==step;page[step].BringToFront();progress.Text="整场复盘  "+(step+1)+" / 3";float factor=scaled?ReplayWindowLayout.DpiScale:1;content.RowStyles[1].Height=(step==2?44:56)*factor;content.RowStyles[2].Height=(step==2?30:40)*factor;content.RowStyles[3].Height=new[]{136,192,228}[step]*factor;content.Height=(int)Math.Ceiling(content.RowStyles.Cast<RowStyle>().Sum(r=>r.Height));
        question.Text=new[]{"本场整体是否按计划执行？","本场最需要改进什么？","下一场准备做哪一项调整？"}[step];
        hint.Text=new[]{"按执行过程判断；盈利和亏损分别展示，不代替执行评价。","可以多选。没有交易也可以记录观察和等待。","选一项具体行动；逐笔复盘可以按需展开。"}[step];
        back.Enabled=step>0&&!busy;validation.Text="";ShowButtonText();
    }
    void ShowButtonText(){next.Text=exitRetry?"重试退出":step==2?"完成复盘并退出":"下一题";}
    public FinishReview CaptureReview(bool completed)
    {
        var trades=new Dictionary<string,string>(perTrade);
        foreach(var item in ChartItem.Parse(archive.Snapshot.Items??""))if(item.Kind=="P")trades[item.Key+":exit"]="pending";
        return new FinishReview{Compliance=compliance.Code,Issues=issues.CheckedItems.Select(c=>c.Code).ToArray(),NextAction=nextAction.Code,Notes=notes.Text.Trim(),PerTrade=trades,Completed=completed,SavedUtc=DateTime.UtcNow};
    }
    bool ValidateStep(int value)
    {
        string error=value==0&&compliance.Code==""?"请选择本场整体执行情况。":value==1&&issues.CheckedItems.Count==0?"请选择需要改进的项目，或选择没有明显问题 / 待核实。":value==2&&nextAction.Code==""?"请选择下一场准备采取的行动。":"";validation.Text=error;return error=="";
    }
    void SetBusy(bool value){busy=value;pages.Enabled=!value;back.Enabled=!value&&step>0;next.Enabled=draft.Enabled=!value;UseWaitCursor=value;}
    public async Task<bool> SaveAndExitAsync(bool completed)
    {
        if(busy)return false;
        if(completed)for(int i=0;i<3;i++)if(!ValidateStep(i)){ShowStep(i);ValidateStep(i);return false;}
        SetBusy(true);ReviewSaved=false;validation.Text="";status.Text="正在保存本场复盘…";
        try
        {
            var review=CaptureReview(completed);FinishStore.SaveReview(archive,review);ReviewSaved=true;savedCompleted=completed;edited=false;
            status.Text=ExitRequested==null?(completed?"复盘已保存":"草稿已保存")+" · "+archive.Folder:"复盘已保存，正在结束本场回放并同步最终结果…";
            string error=ExitRequested==null?"":await ExitRequested(completed);
            if(!String.IsNullOrEmpty(error)){exitRetry=true;validation.Text=error;status.Text="现场和复盘已保存，退出尚未完成。";return false;}
            allowClose=true;DialogResult=DialogResult.OK;Close();return true;
        }
        catch(Exception e){if(ReviewSaved)exitRetry=true;validation.Text=(ReviewSaved?"退出未完成：":"复盘保存失败：")+e.Message;status.Text=ReviewSaved?"已保存的现场与复盘仍保留，可以重试。":"答案仍保留在窗口中，请重试保存。";return false;}
        finally{if(!IsDisposed){SetBusy(false);ShowButtonText();}}
    }
    async void OnClosing(object sender,FormClosingEventArgs e)
    {
        if(allowClose)return;e.Cancel=true;if(busy)return;
        bool preserveCompleted=!edited&&archive.Review!=null&&archive.Review.Completed;
        await SaveAndExitAsync(exitRetry?savedCompleted:preserveCompleted);
    }
}

public sealed class FinishTradeReviewForm:ReplayBufferedForm
{
    readonly Dictionary<string,PlanAnswerChoices> choices=new Dictionary<string,PlanAnswerChoices>();readonly Dictionary<string,string> original;
    sealed class TradeFact{public string Key,Title,Detail;public bool ExitPending;}
    public Dictionary<string,string> Result {get;private set;}
    public FinishTradeReviewForm(FinishSnapshot snapshot,Dictionary<string,string> existing):this(snapshot,existing,new List<TradeFact>()){}
    public FinishTradeReviewForm(FinishArchive archive,Dictionary<string,string> existing):this(archive.Snapshot,existing,ClosedTrades(archive)){}
    static List<TradeFact> ClosedTrades(FinishArchive archive)
    {
        // Read the verified seal again: later final settlement is never the review's source.
        var current=FinishStore.Load(archive.Folder);if(current.Snapshot.Id!=archive.Snapshot.Id)throw new FormatException("逐笔记录与收工现场不匹配。");
        var bundle=RecordStore.ReadBundle(Path.Combine(archive.Folder,"收工现场","交易记录"));var table=bundle.Tables["交易结果.csv"];var rows=new List<TradeFact>();var seen=new HashSet<string>();
        for(int i=0;i<table.Rows.Count;i++)
        {
            if(table.Get(i,"状态")!="已平仓")continue;
            string id=table.Get(i,"持仓ID");ulong number;if(!UInt64.TryParse(id,out number)||number==0||!seen.Add(id))throw new FormatException("已平仓交易标识缺失或重复。");
            string reason=table.Get(i,"退出原因"),entry=table.Get(i,"开仓均价"),exit=table.Get(i,"平仓均价");decimal value;
            if(Decimal.TryParse(entry,NumberStyles.Number,CultureInfo.InvariantCulture,out value))entry=PaperPriceInput.Display(value);
            if(Decimal.TryParse(exit,NumberStyles.Number,CultureInfo.InvariantCulture,out value))exit=PaperPriceInput.Display(value);
            string lots=table.Get(i,"开仓手数");if(Decimal.TryParse(lots,NumberStyles.Number,CultureInfo.InvariantCulture,out value))lots=value.ToString("0.###",CultureInfo.InvariantCulture);
            string profit=table.Get(i,"已实现净盈亏");if(Decimal.TryParse(profit,NumberStyles.Number,CultureInfo.InvariantCulture,out value))profit=FinishViewText.Money(value);
            bool uncertain=reason.IndexOf("测试",StringComparison.Ordinal)>=0||reason.IndexOf("强制",StringComparison.Ordinal)>=0;
            rows.Add(new TradeFact{Key="T:"+id,Title="已平仓 #"+id+" · "+table.Get(i,"方向")+" "+lots+" 手 · "+entry+" → "+exit,Detail="已实现净盈亏 "+profit+" "+table.Get(i,"账户币种")+" · "+(reason==""?"退出原因待核实":reason)+(uncertain?" · 退出执行待核实":""),ExitPending=uncertain});
        }
        return rows;
    }
    FinishTradeReviewForm(FinishSnapshot snapshot,Dictionary<string,string> existing,List<TradeFact> closed)
    {
        original=new Dictionary<string,string>(existing??new Dictionary<string,string>());Result=new Dictionary<string,string>(original);Text="逐笔补充 · 收工现场";Font=new Font("Microsoft YaHei UI",10);BackColor=ReplayPaper.Background;StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(620,560);MinimumSize=new Size(510,420);AutoScaleMode=AutoScaleMode.None;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,62));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));Controls.Add(root);root.Controls.Add(ReplayPaper.Label("逐笔复盘（可选）\r\n已平仓、仍持有及挂单均以收工时封存的记录为准。",12,ReplayPaper.Ink),0,0);
        var scroll=new ReplayBufferedPanel{Dock=DockStyle.Fill,AutoScroll=true};root.Controls.Add(scroll,0,1);var list=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(list);
        var items=ChartItem.Parse(snapshot.Items??"");var facts=new List<TradeFact>();foreach(var item in items)facts.Add(new TradeFact{Key=item.Key,Title=(item.Kind=="P"?"持仓":"挂单")+" #"+item.Ticket+" · "+(item.Side=="buy"?"买入":"卖出")+" "+item.Lots.ToString("0.###")+" 手 · "+FinishViewText.Price(item.Entry),Detail=item.Kind=="P"?"收工时仍持有 · 退出执行：待核实":"收工时挂单仍有效 · 最终处理另存",ExitPending=item.Kind=="P"});facts.AddRange(closed);int row=0;
        if(facts.Count==0){list.RowStyles.Add(new RowStyle(SizeType.Absolute,90));list.Controls.Add(ReplayPaper.Label("收工记录中没有可复盘的交易或挂单。\r\n本场仍可完成观察、等待与执行节奏的整场复盘。"),0,row++);}
        foreach(var fact in facts)
        {
            var card=new PaperCard{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=new Padding(0,0,0,10)};var t=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,36));t.RowStyles.Add(new RowStyle(SizeType.Absolute,62));t.RowStyles.Add(new RowStyle(SizeType.Absolute,32));card.Controls.Add(t);
            t.Controls.Add(ReplayPaper.Label(fact.Title,11,ReplayPaper.Ink),0,0);
            var choice=new PlanAnswerChoices(new[]{new TrainingChoice("followed","按计划"),new TrainingChoice("needs_review","需要改进"),new TrainingChoice("pending","待核实")}){BackColor=ReplayPaper.Surface};choices.Add(fact.Key,choice);string stored;if(original.TryGetValue(fact.Key,out stored))for(int i=0;i<choice.Items.Count;i++)if(choice.Items[i].Code==stored)choice.SelectedIndex=i;
            t.RowStyles[1].Height=128;choice.Dock=DockStyle.Fill;t.Controls.Add(choice,0,1);t.Controls.Add(ReplayPaper.Label(fact.Detail,9),0,2);list.RowStyles.Add(new RowStyle(SizeType.Absolute,220));list.Controls.Add(card,0,row++);
        }
        list.RowCount=Math.Max(1,row);var buttons=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));var cancel=new PaperActionButton{Text="取消",DialogResult=DialogResult.Cancel,Dock=DockStyle.Fill,Quiet=true};var save=new PaperActionButton{Text="保留补充",Primary=true,Dock=DockStyle.Fill};buttons.Controls.Add(cancel,1,0);buttons.Controls.Add(save,2,0);root.Controls.Add(buttons,0,2);save.Click+=(s,e)=>{Result=new Dictionary<string,string>(original);foreach(var pair in choices){if(pair.Value.Code=="")Result.Remove(pair.Key);else Result[pair.Key]=pair.Value.Code;}foreach(var fact in facts)if(fact.ExitPending)Result[fact.Key+":exit"]="pending";DialogResult=DialogResult.OK;Close();};ReplayPaper.ApplyInputs(this);ReplayWindowLayout.Scale(this);Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
    }
}

public sealed class FinishArchiveForm:ReplayBufferedForm
{
    readonly FinishArchive archive;readonly TextBox reviewText;
    public Action<FinishArchive> ReviewRequested {get;set;}
    public FinishArchiveForm(FinishArchive value)
    {
        if(value==null||value.Snapshot==null)throw new ArgumentNullException("value");archive=value;Text="收工档案 · "+archive.Snapshot.Symbol;Font=new Font("Microsoft YaHei UI",10);BackColor=ReplayPaper.Background;ClientSize=new Size(740,680);MinimumSize=new Size(510,480);StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(24)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(var r in new[]{new RowStyle(SizeType.Absolute,40),new RowStyle(SizeType.Absolute,48),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.Absolute,52)})root.RowStyles.Add(r);Controls.Add(root);
        root.Controls.Add(ReplayPaper.Label(archive.Snapshot.Symbol+" "+archive.Snapshot.Period+" · 收工档案",20,ReplayPaper.Ink),0,0);root.Controls.Add(ReplayPaper.Label("收工历史时间 "+FinishViewText.Historical(archive.Snapshot.Historical)+"\r\n"+archive.Folder,9),0,1);
        var tabs=new PaperTabControl{Dock=DockStyle.Fill,AccessibleName="FinishArchiveTabs"};root.Controls.Add(tabs,0,2);
        AddPage(tabs,"收工现场",SnapshotText(archive.Snapshot,false));AddPage(tabs,"计划与设置",FinishViewText.PlansAndSettings(archive.Snapshot));reviewText=AddPage(tabs,"整场复盘",FinishViewText.Review(archive.Review));
        string finalText;try{var final=FinishStore.LoadFinal(archive);finalText=final==null?"测试最终结果尚未保存。\r\n\r\n收工现场已独立保留，后续平仓与结算不会覆盖它。":SnapshotText(final,true)+"\r\n\r\n收工原净值："+FinishViewText.Money(archive.Snapshot.Equity)+"\r\n最终测试净值："+FinishViewText.Money(final.Equity)+"\r\n两个时点分别保存。";}catch(Exception e){finalText="最终结果读取失败："+e.Message+"\r\n收工现场仍独立保留。";}AddPage(tabs,"最终结果",finalText);
        if(archive.Snapshot.Observation!=null)
        {
            var page=new TabPage("多周期图表"){Padding=new Padding(18),BackColor=ReplayPaper.Surface};
            var open=new PaperActionButton{Text="打开收工时的多周期图表",Dock=DockStyle.Top,Height=52,Primary=true,AccessibleName="OpenArchivedObservation"};
            page.Controls.Add(open);tabs.TabPages.Add(page);
            open.Click+=(s,e)=>{var saved=archive.Snapshot.Observation;var v=new JavaScriptSerializer().Deserialize<ObservationView>(new JavaScriptSerializer().Serialize(saved.View));using(var form=new ObservationForm(v,true)){form.StartPosition=FormStartPosition.CenterParent;form.SetData(saved.Data,null);form.ShowDialog(this);}};
        }
        var buttons=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));root.Controls.Add(buttons,0,3);
        var folder=new PaperActionButton{Text="打开文件夹",Quiet=true,Dock=DockStyle.Fill};var review=new PaperActionButton{Text=archive.Review!=null&&archive.Review.Completed?"查看 / 修改复盘":"继续本场复盘",Primary=true,Dock=DockStyle.Fill,AccessibleName="FinishResumeReview"};var close=new PaperActionButton{Text="关闭",Dock=DockStyle.Fill,DialogResult=DialogResult.OK};buttons.Controls.Add(folder,0,0);buttons.Controls.Add(review,2,0);buttons.Controls.Add(close,3,0);folder.Click+=(s,e)=>FinishViewText.OpenFolder(this,archive.Folder);review.Click+=(s,e)=>{if(ReviewRequested!=null)ReviewRequested(archive);else using(var form=new FinishReviewForm(archive)){form.ShowDialog(this);reviewText.Text=FinishViewText.Review(archive.Review);}};ReplayPaper.ApplyInputs(this);foreach(TabPage p in tabs.TabPages)foreach(Control c in p.Controls){var view=c as TextBox;if(view!=null)view.BorderStyle=BorderStyle.None;}ReplayWindowLayout.Scale(this);Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
    }
    static TextBox AddPage(TabControl tabs,string title,string text){var page=new TabPage(title){Padding=new Padding(18),BackColor=ReplayPaper.Surface};var view=FinishViewText.ReadOnly(text);view.AccessibleName="FinishArchive_"+title;page.Controls.Add(view);tabs.TabPages.Add(page);return view;}
    public static string SnapshotText(FinishSnapshot s,bool final)
    {
        var b=new StringBuilder();b.AppendLine(final?"测试最终结束结果":"决定收工时的原始现场");b.AppendLine();b.AppendLine("历史时间："+FinishViewText.Historical(s.Historical));b.AppendLine("余额："+FinishViewText.Money(s.Balance)+"    净值："+FinishViewText.Money(s.Equity));b.AppendLine("持仓："+s.Positions+"    挂单："+s.Pending);
        string currency=FinishViewText.Get(s,"account_currency");if(currency!="")b.AppendLine("账户币种："+currency);b.AppendLine();b.AppendLine(FinishViewText.Orders(s));b.AppendLine();b.AppendLine("保存时间："+s.CreatedUtc.ToLocalTime().ToString("yyyy.MM.dd HH:mm:ss"));b.AppendLine("档案编号："+s.Id);return b.ToString();
    }
}
