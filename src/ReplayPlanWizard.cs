using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

public sealed class TrainingPlanForm : ReplayBufferedForm
{
    readonly PlanReasonChoices reasons=new PlanReasonChoices();
    readonly PlanAnswerChoices failure,exit,risk,expiry,review,manualRule;
    readonly NumericUpDown riskNumber=new NumericUpDown{Minimum=.01m,Maximum=100000000,DecimalPlaces=2,Value=100};
    readonly PaperPriceInput invalidation=new PaperPriceInput();
    readonly PaperPriceInput target=new PaperPriceInput();
    readonly TextBox notes=new TextBox{MaxLength=500,Multiline=true,ScrollBars=ScrollBars.Vertical};
    readonly Label budgetHint=new ReplayStableLabel(),validation=new ReplayStableLabel(),progress=new ReplayStableLabel(),question=new ReplayStableLabel(),hint=new ReplayStableLabel();
    readonly Button next=new PaperActionButton{Text="下一步",AccessibleName="PlanNext",Primary=true},back=new PaperActionButton{Text="上一步",AccessibleName="PlanBack"};
    readonly TableLayoutPanel table;
    readonly Panel pageHost=new ReplayBufferedPanel{Dock=DockStyle.Fill};
    readonly TrainingProfile profile;readonly DesktopSnapshot snapshot;
    readonly Dictionary<string,PlanQuestion> questions=new Dictionary<string,PlanQuestion>();
    readonly Control invalidationRow,targetRow,reviewRow,manualRuleRow,numberRow;
    string currentQuestion="reasons",previousRisk="";decimal lastUsd=100,lastPct=1;
    sealed class PlanQuestion
    {public string Id,Title,Hint;public Panel Page;public Control Input;}
    public TrainingPlan Result {get;private set;}
    bool Strict {get{return profile!=null && profile.Config!=null && profile.Config.Purpose!="explore";}}

    public TrainingPlanForm(TrainingProfile trainingProfile,DesktopSnapshot state)
    {
        profile=trainingProfile;snapshot=state;Text="事前交易计划 · 逐题填写";Font=new Font("Microsoft YaHei UI",10F);
        StartPosition=FormStartPosition.CenterParent;ClientSize=new Size(680,650);MinimumSize=new Size(510,480);AutoScaleMode=AutoScaleMode.None;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(28)};
        foreach(var style in new[]{new RowStyle(SizeType.Absolute,30),new RowStyle(SizeType.Absolute,60),new RowStyle(SizeType.Absolute,54),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.Absolute,44),new RowStyle(SizeType.Absolute,48)})root.RowStyles.Add(style);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
        foreach(var label in new[]{progress,question,hint,validation}){label.Dock=DockStyle.Fill;label.Margin=new Padding(0,2,0,6);}
        progress.ForeColor=GraphiteTheme.Accent;question.Font=new Font(Font.FontFamily,16F,FontStyle.Regular);question.ForeColor=GraphiteTheme.Text;
        root.Controls.Add(progress,0,0);root.Controls.Add(question,0,1);root.Controls.Add(hint,0,2);
        table=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Margin=Padding.Empty};table.RowStyles.Add(new RowStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.Controls.Add(pageHost,0,0);root.Controls.Add(table,0,3);
        validation.AccessibleName="PlanValidation";root.Controls.Add(validation,0,4);
        var buttons=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));
        var cancel=new PaperActionButton{Text="取消",DialogResult=DialogResult.Cancel,Quiet=true};
        foreach(var button in new Button[]{cancel,back,next}){button.Dock=DockStyle.Fill;button.Margin=new Padding(3);}
        buttons.Controls.Add(cancel,0,0);buttons.Controls.Add(back,2,0);buttons.Controls.Add(next,3,0);root.Controls.Add(buttons,0,5);AcceptButton=next;CancelButton=cancel;
        back.Click+=(s,e)=>MoveBack();next.Click+=(s,e)=>MoveNext();

        foreach(var choice in new[]{C("ema_up","EMA金叉"),C("ema_down","EMA死叉"),C("second_cross","第二次同方向交叉"),C("third_cross","第三次同方向交叉"),C("trend_pullback","顺趋势回踩"),C("break_retest","突破后回踩"),C("support_reversal","支撑 / 阻力反转"),C("unknown","尚未确定（探索）"),C("other","其他")})reasons.Items.Add(choice);
        Register("reasons","这次为什么准备入场？","可多选。填写期间回放保持暂停；保存后重新点击下单或选择挂单价格。",reasons);
        failure=Choice("failure","什么情况下原判断失效？","先选判断条件；需要价格或时间时，下一题再填写。",new[]{C("","请选择"),C("reverse_cross","快慢线反向交叉"),C("structure_break","原有价格结构被破坏"),C("manual_price","到达预定失效价 / 止损价"),C("time_limit","到预定时间复核"),C("unknown","尚未确定（探索）"),C("other","其他（最后填写具体条件）")});
        exit=Choice("exit","准备怎样退出？","计划只作记录；止盈止损仍需手动设置。",new[]{C("","请选择"),C("manual_stops","按预定止损 / 止盈管理（手动设置）"),C("reverse_cross","快慢线反向交叉退出"),C("manual_exit","按具体条件手动退出"),C("session_end","本历史交易日结束前退出"),C("time_limit","到预定时间退出"),C("test_end","测试终点退出（仅探索）"),C("unknown","尚未确定（探索）"),C("other","其他（最后填写具体条件）")});
        manualRule=Choice("manualRule","手动退出的具体条件是什么？","选定条件后，需要目标价时再填写。",new[]{C("","请选择"),C("target_price","达到目标价"),C("reverse_cross","快慢线反向交叉"),C("session_end","本历史交易日结束前")});manualRuleRow=questions["manualRule"].Page;
        invalidation.Reference(state);target.Reference(state);invalidation.InputChanged+=(s,e)=>validation.Text="";target.InputChanged+=(s,e)=>validation.Text="";
        invalidationRow=Register("invalidation","明确的失效 / 止损价是多少？","直接输入或粘贴绝对价格；最多两位小数。这里只记录，不自动设置止损。",invalidation);
        targetRow=Register("target","计划目标价是多少？","直接输入或粘贴价格；按止盈止损管理时可留空，按目标价退出时须填写明确价格。",target);
        review=Choice("review","何时复核 / 退出？","从当前历史时间算起；失效和退出均选时间条件时共用这个时刻。",new[]{C("","请选择"),C("15","15分钟后"),C("60","1小时后"),C("240","4小时后"),C("-1","本历史日结束"),C("1440","24小时后")});reviewRow=questions["review"].Page;
        risk=Choice("risk","同一交易想法最多承担多少风险？","多笔单共用这一预算；预算只作记录，不自动限制手数。",new[]{C("","请选择"),C("pct025","初始本金的0.25%"),C("pct050","初始本金的0.50%"),C("pct100","初始本金的1.00%"),C("pct200","初始本金的2.00%"),C("usd","自定义金额（USD）"),C("pct","自定义初始本金百分比"),C("unknown","尚未确定（探索）")});
        numberRow=Register("riskNumber","填写合计风险预算","以本场初始本金为基准；未设置止损的仓位不能当作零风险。",riskNumber);
        budgetHint.Dock=DockStyle.Bottom;budgetHint.Height=44;budgetHint.MinimumSize=new Size(0,44);budgetHint.Padding=new Padding(0,4,0,0);questions["risk"].Page.Controls.Add(budgetHint);budgetHint.BringToFront();
        expiry=Choice("expiry","这份计划与挂单何时到期？","从填写时的历史时间算起，同一计划共用到期时刻。",new[]{C("","请选择"),C("15","15分钟（历史时间）"),C("60","1小时（历史时间）"),C("240","4小时（历史时间）"),C("-1","本历史日结束（次日00:00）"),C("1440","24小时（历史时间）"),C("0","不设期限（仅探索）")});
        Register("notes","还有需要记录的内容吗？","选择“其他”时，请写明具体规则；其余情况可以直接保存。",notes);
        foreach(var box in new[]{failure,exit,manualRule})box.SelectedIndexChanged+=(s,e)=>UpdateDetails();
        risk.SelectedIndexChanged+=(s,e)=>RiskChanged();riskNumber.ValueChanged+=(s,e)=>UpdateBudget();
        GraphiteTheme.Apply(this);
        ReplayPaper.ApplyInputs(this);question.ForeColor=ReplayPaper.Ink;progress.ForeColor=ReplayPaper.Accent;
        reasons.BackColor=ReplayPaper.Background;notes.BackColor=ReplayPaper.Surface;reasons.ForeColor=notes.ForeColor=ReplayPaper.Ink;
        validation.ForeColor=GraphiteTheme.SellText;RiskChanged();ShowQuestion("reasons",false);ReplayWindowLayout.Scale(this);
        Shown+=(s,e)=>{ReplayWindowLayout.Fit(this);FocusQuestion();};
    }
    static TrainingChoice C(string code,string text){return new TrainingChoice(code,text);}
    Control Register(string id,string title,string detail,Control input)
    {
        var page=new ReplayBufferedPanel{Dock=DockStyle.Fill,Visible=false,Margin=Padding.Empty};
        input.AccessibleName="PlanAnswer_"+id;input.Dock=input==reasons || input is PlanAnswerChoices?DockStyle.Fill:DockStyle.Top;
        if(input==notes)input.Height=140;if(input is PaperPriceInput)input.Height=120;
        page.Controls.Add(input);pageHost.Controls.Add(page);questions.Add(id,new PlanQuestion{Id=id,Title=title,Hint=detail,Input=input,Page=page});return page;
    }
    PlanAnswerChoices Choice(string id,string title,string detail,TrainingChoice[] choices)
    {var box=new PlanAnswerChoices(choices);Register(id,title,detail,box);return box;}
    static string AnswerCode(PlanAnswerChoices choices){return choices==null?"":choices.Code;}
    List<string> Steps()
    {
        var result=new List<string>{"reasons","failure"};string fail=AnswerCode(failure),leave=AnswerCode(exit);
        bool price=fail=="manual_price" || fail=="structure_break",timed=fail=="time_limit";
        if(price)result.Add("invalidation");if(timed)result.Add("review");result.Add("exit");
        if(leave=="manual_exit")result.Add("manualRule");
        if(leave=="manual_stops" && !price)result.Add("invalidation");
        if(leave=="manual_stops" || (leave=="manual_exit" && AnswerCode(manualRule)=="target_price"))result.Add("target");
        if(leave=="time_limit" && !timed)result.Add("review");
        result.Add("risk");if(new[]{"usd","pct"}.Contains(AnswerCode(risk)))result.Add("riskNumber");result.Add("expiry");result.Add("notes");return result;
    }
    void ShowQuestion(string id,bool focus)
    {
        currentQuestion=id;foreach(var page in questions.Values)page.Page.Visible=page.Id==id;
        var item=questions[id];item.Page.BringToFront();question.Text=item.Title;hint.Text=item.Hint;validation.Text="";UpdateDetails();if(focus)FocusQuestion();
    }
    void FocusQuestion(){questions[currentQuestion].Input.Focus();}
    void UpdateDetails()
    {
        var steps=Steps();int at=steps.IndexOf(currentQuestion);progress.Text="第 "+(at+1)+" / "+steps.Count+" 题";back.Enabled=at>0;next.Text=at==steps.Count-1?"保存并使用":"下一步";
        if(currentQuestion=="notes")hint.Text=NeedsNotes()?"你选择了“其他”，请写明具体条件后保存。":"可留空；保存后重新点击下单或选择挂单价格。";
    }
    void RiskChanged()
    {
        if(previousRisk=="usd")lastUsd=riskNumber.Value;if(previousRisk=="pct")lastPct=riskNumber.Value;
        string code=AnswerCode(risk);previousRisk=code;
        if(code=="pct"){riskNumber.Value=Math.Max(.01m,Math.Min(riskNumber.Maximum,lastPct));riskNumber.Maximum=100;riskNumber.Value=Math.Max(.01m,Math.Min(100,lastPct));}
        else if(code=="usd"){riskNumber.Maximum=100000000;riskNumber.Value=lastUsd;}
        questions["riskNumber"].Title=code=="pct"?"合计风险占初始本金的百分之几？":"合计风险金额是多少 USD？";riskNumber.Enabled=code=="usd" || code=="pct";UpdateBudget();UpdateDetails();
    }
    decimal BudgetValue(string code){return code=="pct025"?.25m:code=="pct050"?.5m:code=="pct100"?1m:code=="pct200"?2m:code=="unknown"?0:riskNumber.Value;}
    void UpdateBudget()
    {
        string code=AnswerCode(risk);decimal capital=profile==null?0:profile.StartingBalance;
        budgetHint.Text=code=="" || code=="unknown"?"预算待确定；未设置止损的仓位不能当作零风险。":code.StartsWith("pct") && capital<=0?"初始本金无法核实；请检查本场资料后再按百分比填写。":"合计预算 "+(code.StartsWith("pct")?capital/100*BudgetValue(code):BudgetValue(code)).ToString("0.00")+" USD · 初始本金 "+capital.ToString("0.00")+" USD\r\n仅记录预算，不自动限仓或设置止损。";
    }
    bool NeedsNotes(){return reasons.CheckedItems.Cast<TrainingChoice>().Any(x=>x.Code=="other") || AnswerCode(failure)=="other" || AnswerCode(exit)=="other";}
    string QuestionProblem()
    {
        var box=questions[currentQuestion].Input as PlanAnswerChoices;string code=box==null?"":AnswerCode(box);
        switch(currentQuestion)
        {
            case "reasons":var selected=reasons.CheckedItems.Cast<TrainingChoice>().Select(x=>x.Code).ToArray();if(selected.Length==0)return "请至少选择一个入场理由。";if(selected.Contains("unknown") && selected.Length>1)return "“尚未确定”不能与明确的入场理由同时选择。";if(Strict && selected.Contains("unknown"))return "规则训练 / 留出检验需要明确的入场理由。";break;
            case "failure":case "exit":if(code=="")return "请先选择这一题的答案。";if(Strict && (code=="unknown" || code=="test_end"))return "规则训练 / 留出检验需要明确条件，不能使用测试终点退出。";break;
            case "manualRule":if(code=="")return "请选择手动退出的具体条件。";break;
            case "invalidation":if(!invalidation.IsValid)return "请输入有效的失效 / 止损价。";if(invalidation.Value<=0)return "请填写明确的失效 / 止损价。";break;
            case "target":if(!target.IsValid)return "请输入有效的目标价。";if(AnswerCode(exit)=="manual_exit" && target.Value<=0)return "请填写手动退出的目标价。";break;
            case "review":if(code=="")return "请选择历史复核 / 退出时限。";break;
            case "risk":if(code=="")return "请选择合计风险预算。";if(Strict && code=="unknown")return "规则训练 / 留出检验需要明确的风险预算。";break;
            case "expiry":if(code=="")return "请选择计划有效期。";if(Strict && code=="0")return "规则训练 / 留出检验需要明确的有效期。";break;
            case "notes":if(NeedsNotes() && String.IsNullOrWhiteSpace(notes.Text))return "选择“其他”时，请写明具体规则。";break;
        }
        return "";
    }
    void MoveNext()
    {
        ValidateChildren();string problem=QuestionProblem();if(problem!=""){validation.Text=problem;FocusQuestion();return;}
        var steps=Steps();int at=steps.IndexOf(currentQuestion);if(at<steps.Count-1)ShowQuestion(steps[at+1],true);else Save();
    }
    void MoveBack(){var steps=Steps();int at=steps.IndexOf(currentQuestion);if(at>0)ShowQuestion(steps[at-1],true);}
    public TrainingPlan ReadPlan()
    {
        string[] selected=reasons.CheckedItems.Cast<TrainingChoice>().Select(x=>x.Code).ToArray();string fail=AnswerCode(failure),leave=AnswerCode(exit),budget=AnswerCode(risk),validity=AnswerCode(expiry);
        if(selected.Length==0 || fail=="" || leave=="" || budget=="" || validity=="")throw new ArgumentException("请完成入场理由、失效条件、退出方式、合计风险和有效期五项选择。");
        int minutes=Int32.Parse(validity,CultureInfo.InvariantCulture),reviewMinutes=0;
        if(fail=="time_limit" || leave=="time_limit")Int32.TryParse(AnswerCode(review),out reviewMinutes);
        bool hasPrice=fail=="manual_price" || fail=="structure_break" || leave=="manual_stops",hasTarget=leave=="manual_stops" || (leave=="manual_exit" && AnswerCode(manualRule)=="target_price");
        string id=Guid.NewGuid().ToString("N");var candidate=new TrainingPlan{DetailVersion=2,Id=id,IdeaId=id,SessionId=snapshot.Get("session"),Boot=snapshot.Get("boot"),CreatedUtc=DateTime.UtcNow,HistoricalTime=TrainingStore.Historical(snapshot.Now),Reasons=selected,FailureCondition=fail,ExitMethod=leave,RiskBasis=budget.StartsWith("pct")?"pct":budget,RiskValue=BudgetValue(budget),ExpiryMinutes=minutes,ValidUntil=TrainingStore.Expiry(snapshot.Now,minutes),Notes=notes.Text.Trim(),InvalidationPrice=hasPrice?invalidation.Value:0,TargetPrice=hasTarget?target.Value:0,ReviewMinutes=reviewMinutes,ManualExitRule=leave=="manual_exit"?AnswerCode(manualRule):""};
        string error=TrainingStore.PlanProblem(candidate,Strict);if(error!="")throw new ArgumentException(error);return candidate;
    }
    void Save(){try{Result=ReadPlan();DialogResult=DialogResult.OK;Close();}catch(Exception ex){validation.Text=ex.Message;}}
}
