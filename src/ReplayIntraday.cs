using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Forms;

public sealed partial class DesktopReplayForm
{
    readonly DataGridView intradayGrid=new ReplayBufferedGrid();
    readonly Label riskPreview=new ReplayStableLabel();
    Button finishDayButton;TableLayoutPanel intradayBody,intradayEntry,intradayHeader;TabPage intradaySettings;bool intradayCompact;
    string gridSignature="",selectedItemKey="",gridNativeSelection="",dayBoot="",dayError="",dayFinishError="",dayCarryError="",dayFeedback="";bool gridUpdating,dayFinishedFlushed;
    int feedbackUntil=0;ReplayDaybook daybook;
    bool finishRequested;

    void BuildIntradayUi(DesktopSessionInfo info)
    {
        Text="日内历史回放 · 侧边操作";Font=new Font("Microsoft YaHei UI",9F);BackColor=GraphiteTheme.Background;ForeColor=Ink;
        ClientSize=new Size(440,740);MinimumSize=new Size(370,490);FormBorderStyle=FormBorderStyle.Sizable;MaximizeBox=true;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;
        var screen=Screen.PrimaryScreen.WorkingArea;Location=new Point(Math.Max(screen.Left,screen.Right-Width-20),screen.Top+20);TopMost=true;
        var root=PanelGrid(new[]{0f},3);root.Padding=new Padding(14);root.RowStyles.Add(new RowStyle(SizeType.Absolute,100));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,174));Controls.Add(root);
        var header=PanelGrid(new[]{0f,58f,52f,52f,48f},3);intradayHeader=header;header.RowStyles.Add(new RowStyle(SizeType.Absolute,28));header.RowStyles.Add(new RowStyle(SizeType.Absolute,46));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var instrument=PanelLabel(info.Symbol+" · "+info.Period,Ink);instrument.Font=new Font(Font.FontFamily,12F,FontStyle.Bold);header.Controls.Add(instrument,0,0);header.Controls.Add(ObservationButton(),1,0);var records=PanelButton("记录",Card);records.Click+=(s,e)=>OpenIntradayRecords();header.Controls.Add(records,2,0);
        finishDayButton=PanelButton("收工",Card);finishDayButton.AccessibleName="FinishDay";finishDayButton.Click+=(s,e)=>BeginFullFinish();header.Controls.Add(finishDayButton,3,0);
        pin.Text="置顶";pin.Checked=true;pin.ForeColor=Muted;pin.Dock=DockStyle.Fill;pin.CheckedChanged+=(s,e)=>TopMost=pin.Checked;header.Controls.Add(pin,4,0);
        PrepareLabel(account,Muted);PrepareLabel(connection,Muted);header.Controls.Add(account,0,1);header.SetColumnSpan(account,5);header.Controls.Add(connection,0,2);header.SetColumnSpan(connection,5);root.Controls.Add(header,0,0);
        var tabs=new GraphiteTabControl{Dock=DockStyle.Fill,Margin=Padding.Empty};var tradePage=new TabPage("交易 / 持仓"){BackColor=BackColor,Padding=new Padding(4)};intradaySettings=new TabPage("训练 / 设置"){BackColor=BackColor,AutoScroll=true,Padding=new Padding(4)};tabs.TabPages.AddRange(new[]{tradePage,intradaySettings});root.Controls.Add(tabs,0,1);
        intradayBody=PanelGrid(new[]{0f},5);intradayBody.RowStyles.Add(new RowStyle(SizeType.Absolute,116));intradayBody.RowStyles.Add(new RowStyle(SizeType.Absolute,40));intradayBody.RowStyles.Add(new RowStyle(SizeType.Percent,100));intradayBody.RowStyles.Add(new RowStyle(SizeType.Absolute,36));intradayBody.RowStyles.Add(new RowStyle(SizeType.Absolute,22));tradePage.Controls.Add(intradayBody);
        var entry=PanelGrid(new[]{0f},4);intradayEntry=entry;foreach(int height in new[]{30,44,20,20})entry.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        var volume=PanelGrid(new[]{40f,0f,54f},1);volume.Controls.Add(PanelLabel("手数",Ink),0,0);ConfigureNumber(lots,3,.001m,1000,.05m);volume.Controls.Add(lots,1,0);lotReset=PanelButton("初始",Card);lotReset.Click+=(a,b)=>lots.Value=Math.Max(lots.Minimum,Math.Min(lots.Maximum,ReplayFiles.Load().Lots));volume.Controls.Add(lotReset,2,0);entry.Controls.Add(volume,0,0);
        var entries=PanelGrid(new[]{0f,0f},1);entries.Controls.Add(PanelAction("sell","卖出 / 做空",GraphiteTheme.SellSurface),0,0);entries.Controls.Add(PanelAction("buy","买入 / 做多",GraphiteTheme.BuySurface),1,0);entry.Controls.Add(entries,0,1);
        PrepareLabel(lotMode,Ink);PrepareLabel(lotPreview,Muted);entry.Controls.Add(lotMode,0,2);entry.Controls.Add(lotPreview,0,3);intradayBody.Controls.Add(entry,0,0);
        PrepareLabel(riskPreview,GraphiteTheme.Muted);riskPreview.AccessibleName="IntradayRiskPreview";riskPreview.Text="止损亏损估算：等待有效报价";intradayBody.Controls.Add(riskPreview,0,1);
        var positions=PanelGrid(new[]{0f},3);intradayPositions=positions;positions.RowStyles.Add(new RowStyle(SizeType.Absolute,23));positions.RowStyles.Add(new RowStyle(SizeType.Percent,100));positions.RowStyles.Add(new RowStyle(SizeType.Absolute,84));PrepareLabel(summary,Ink);positions.Controls.Add(summary,0,0);PrepareIntradayGrid();positions.Controls.Add(intradayGrid,0,1);positions.Controls.Add(BuildSelectedOrderDetails(),0,2);intradayBody.Controls.Add(positions,0,2);GraphiteTheme.AddSeparators(positions);
        var management=PanelGrid(new[]{0f,0f},1);management.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var close=PanelButton("平仓所选单",Card);close.AccessibleName="close";close.Click+=(a,b)=>CloseIntradayItem();actions["close"]=close;management.Controls.Add(close,0,0);management.Controls.Add(PanelAction("close_all","全平（点两次）",GraphiteTheme.Background),1,0);intradayBody.Controls.Add(management,0,3);
        PrepareLabel(status,GraphiteTheme.Accent);intradayBody.Controls.Add(status,0,4);
        var settingsStack=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};settingsStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));intradaySettings.Controls.Add(settingsStack);
        var trainingPanel=training.BuildPanel(()=>snapshot,action=>Send(action));AddStack(settingsStack,trainingPanel,trainingPanel.Height);training.SetDispatchControl(this);
        var navigation=PanelGrid(new[]{0f,0f},1);navigation.Controls.Add(PanelAction("previous","上一持仓",Card),0,0);navigation.Controls.Add(PanelAction("next","下一持仓",Card),1,0);AddStack(settingsStack,navigation,32);
        PrepareLabel(position,Ink);PrepareLabel(actual,Muted);AddStack(settingsStack,position,25);AddStack(settingsStack,actual,25);
        AddStack(settingsStack,PanelLabel("报价 / 日内日期按回放服务器时间；未换算时区。\r\n主交易周期在启动前选择；“多周期”可切换观察图表。\r\n风险只提示，不另设手数上限或自动设置SL / TP。",Muted),72);
        var footer=PanelGrid(new[]{0f},5);footer.RowStyles.Add(new RowStyle(SizeType.Absolute,64));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,60));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,22));footer.RowStyles.Add(new RowStyle(SizeType.Absolute,8));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        footer.Controls.Add(BuildPlaybackStrip(),0,1);playbackFooter=footer;playbackRoot=root;
        footer.Controls.Add(BuildIntradayPlanSection(),0,0);PrepareLabel(progressText,Ink);footer.Controls.Add(progressText,0,2);progress.Dock=DockStyle.Fill;progress.Margin=new Padding(2,2,2,2);footer.Controls.Add(progress,0,3);PrepareLabel(range,Muted);footer.Controls.Add(range,0,4);root.Controls.Add(footer,0,2);
        tips.SetToolTip(progress,"本历史自然日00:00—24:00的只读进度，含休市；不是可拖动时间轴。");
        ClientSizeChanged+=(s,e)=>FitIntradayDensity();Shown+=(s,e)=>FitIntradayDensity();
        FormClosing+=(s,e)=>{if(daybook!=null)try{daybook.Flush();}catch(Exception ex){dayError="日内资料尚未保存："+ex.Message;}};
    }
    void FitIntradayDensity()
    {
        if(intradayBody==null)return;bool compact=ClientSize.Height<640*ReplayWindowLayout.DpiScale;intradayCompact=compact;
        riskPreview.Visible=!compact;intradayBody.RowStyles[1].Height=compact?0:40*ReplayWindowLayout.DpiScale;
        lotMode.Visible=lotPreview.Visible=!compact;intradayBody.RowStyles[0].Height=(compact?74:116)*ReplayWindowLayout.DpiScale;
        intradayEntry.RowStyles[2].Height=intradayEntry.RowStyles[3].Height=compact?0:20*ReplayWindowLayout.DpiScale;
        if(selectedOrderDetails!=null){selectedOrderDetails.Visible=!compact;intradayPositions.RowStyles[2].Height=compact?0:84*ReplayWindowLayout.DpiScale;}
        if(intradayPlanSection!=null){intradayPlanHeading.Visible=!compact;intradayPlanSection.RowStyles[0].Height=compact?0:28*ReplayWindowLayout.DpiScale;}
        if(playbackFooter!=null){trainingBrief.Visible=true;playbackFooter.RowStyles[0].Height=(compact?32:64)*ReplayWindowLayout.DpiScale;playbackFooter.RowStyles[1].Height=(compact?56:60)*ReplayWindowLayout.DpiScale;playbackFooter.RowStyles[2].Height=(compact?20:22)*ReplayWindowLayout.DpiScale;playbackFooter.RowStyles[3].Height=(compact?6:8)*ReplayWindowLayout.DpiScale;playbackRoot.RowStyles[0].Height=(compact?86:100)*ReplayWindowLayout.DpiScale;playbackRoot.RowStyles[2].Height=(compact?132:174)*ReplayWindowLayout.DpiScale;intradayHeader.RowStyles[0].Height=(compact?26:28)*ReplayWindowLayout.DpiScale;intradayHeader.RowStyles[1].Height=(compact?42:46)*ReplayWindowLayout.DpiScale;intradayBody.RowStyles[3].Height=(compact?32:36)*ReplayWindowLayout.DpiScale;intradayBody.RowStyles[4].Height=(compact?18:22)*ReplayWindowLayout.DpiScale;}
        UpdateIntradayListVisibility();
    }
    void PrepareIntradayGrid()
    {
        intradayGrid.AccessibleName="IntradayItems";intradayGrid.Dock=DockStyle.Fill;intradayGrid.BackgroundColor=BackColor;intradayGrid.BorderStyle=BorderStyle.None;intradayGrid.ReadOnly=true;intradayGrid.MultiSelect=false;intradayGrid.AllowUserToAddRows=false;intradayGrid.AllowUserToDeleteRows=false;intradayGrid.AllowUserToResizeRows=false;intradayGrid.RowHeadersVisible=false;intradayGrid.AutoGenerateColumns=false;intradayGrid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;intradayGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;intradayGrid.EnableHeadersVisualStyles=false;intradayGrid.ColumnHeadersHeight=24;intradayGrid.RowTemplate.Height=25;intradayGrid.GridColor=Card;
        intradayGrid.DefaultCellStyle=new DataGridViewCellStyle{BackColor=BackColor,ForeColor=Ink,SelectionBackColor=GraphiteTheme.Soft,SelectionForeColor=Ink,Font=Font};intradayGrid.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Card,ForeColor=Ink,Font=Font};
        foreach(var spec in new[]{new[]{"单号 / 方向","105"},new[]{"手数","45"},new[]{"入场","65"},new[]{"SL","65"},new[]{"TP","65"}})intradayGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=spec[0],FillWeight=Int32.Parse(spec[1]),SortMode=DataGridViewColumnSortMode.NotSortable,MinimumWidth=35});
        intradayGrid.CellClick+=(s,e)=>{if(e.RowIndex>=0)PickIntradayRow(e.RowIndex);};intradayGrid.CurrentCellChanged+=(s,e)=>{if(!gridUpdating && intradayGrid.CurrentCell!=null)PickIntradayRow(intradayGrid.CurrentCell.RowIndex);};
    }
    ChartItem CurrentIntradayItem()
    {return snapshot==null?null:ChartItem.Parse(snapshot.Get("chart_items")).FirstOrDefault(i=>i.Key==selectedItemKey);}
    void PickIntradayRow(int index)
    {
        if(gridUpdating || snapshot==null || !snapshot.Fresh || pending>0 || index<0 || index>=intradayGrid.Rows.Count)return;
        var item=intradayGrid.Rows[index].Tag as ChartItem;if(item==null)return;selectedItemKey=item.Key;
        if(item.Kind=="P" && snapshot.Get("selected")!=item.Ticket)
        {
            if(snapshot.Get("intraday_protocol")!="1"){status.Text="旧版回放请用训练/设置内上一、下一持仓选择。";return;}
            SendChart(new ChartRequest{Action="select_position",Kind="P",Ticket=item.Ticket,Side=item.Side,Lots=item.Lots});
        }
        RefreshIntradayButtons();
    }
    void CloseIntradayItem()
    {
        RefreshState();var item=CurrentIntradayItem();if(item==null || snapshot==null || !snapshot.Fresh || pending>0)return;ValidateChildren();
        if(item.Kind=="P" && snapshot.Get("selected")!=item.Ticket){status.Text="请先等待该持仓选中确认，再管理此单。";return;}
        SendChart(new ChartRequest{Action=item.Kind=="P"?"close":"cancel_limit",Ticket=item.Ticket,Kind=item.Kind,Side=item.Side,Lots=item.Lots,Entry=item.Entry,SL=item.SL,TP=item.TP});
    }
    void RefreshIntradayGrid()
    {
        string signature=snapshot.Get("chart_items");var items=ChartItem.Parse(signature);
        UpdateIntradayListVisibility(items.Count);
        string nativeSelection=snapshot.Get("selected");bool nativeChanged=nativeSelection!=gridNativeSelection;
        gridNativeSelection=nativeSelection;
        if(nativeChanged && items.Any(i=>i.Kind=="P" && i.Ticket==nativeSelection))selectedItemKey="P:"+nativeSelection;
        if(signature!=gridSignature || intradayGrid.Rows.Count!=items.Count || nativeChanged)
        {
            gridUpdating=true;try
            {
                gridSignature=signature;intradayGrid.Rows.Clear();string format="0."+new string('0',(int)Math.Max(0,Math.Min(8,snapshot.Int("digits"))));
                if(!items.Any(i=>i.Key==selectedItemKey))selectedItemKey=items.Any(i=>i.Kind=="P" && i.Ticket==snapshot.Get("selected"))?"P:"+snapshot.Get("selected"):items.Count>0?items[0].Key:"";
                foreach(var item in items){int r=intradayGrid.Rows.Add((item.Kind=="O"?"挂":"")+(item.Side=="buy"?"买":"卖")+" #"+item.Ticket,item.Lots.ToString("0.###"),item.Entry.ToString(format),item.SL>0?item.SL.ToString(format):"未设",item.TP>0?item.TP.ToString(format):"未设");var row=intradayGrid.Rows[r];row.Tag=item;foreach(DataGridViewCell cell in row.Cells)cell.ToolTipText=item.ToString()+"\r\n入场 "+item.Entry.ToString(format)+" · SL "+(item.SL>0?item.SL.ToString(format):"未设")+" · TP "+(item.TP>0?item.TP.ToString(format):"未设");if(item.Key==selectedItemKey)intradayGrid.CurrentCell=row.Cells[0];}
            }finally{gridUpdating=false;}
        }
    }
    void RefreshIntradayButtons()
    {
        bool available=snapshot!=null && snapshot.Fresh && pending==0;var item=CurrentIntradayItem();bool positionSelected=item!=null && item.Kind=="P" && snapshot.Get("selected")==item.Ticket;
        actions["close"].Enabled=available && item!=null && (item.Kind=="O" || positionSelected) && snapshot.Get("hedging")=="1";
        actions["close"].Text=item!=null && item.Kind=="O"?"撤销所选挂单":"平仓所选单";
        actions["close_all"].Enabled=available && snapshot.Get("hedging")=="1" && snapshot.Int("count")>0;
        finishDayButton.Enabled=available && snapshot.Get("training_protocol")=="1" && training.Enabled && !finishRequested;RefreshSelectedOrderDetails();
    }
    static string RiskText(decimal? value){return value.HasValue?value.Value.ToString("0.00")+" USD":"未定义";}
    void UpdateIntradayRisk()
    {
        if(!session.IntradayLayout || snapshot==null)return;
        if(!snapshot.Fresh){riskPreview.Text="报价已暂停更新；风险估算暂不可用";return;}
        string perEntry="止盈止损：在图表拖线设置与修改";
        if(trainingError!=""){riskPreview.Text=perEntry+"\r\n计划资料异常，合计风险未核实";return;}
        var maps=IntradayRisk.Plans(snapshot);decimal sum=0;int unknown=0,unmapped=0,linked=0;
        foreach(var item in ChartItem.Parse(snapshot.Get("chart_items")))
        {string plan;if(!maps.TryGetValue(item.Key,out plan) || String.IsNullOrEmpty(plan)){unmapped++;continue;}if(!training.BelongsToActiveIdea(plan))continue;linked++;var value=IntradayRisk.ItemLoss(snapshot,item);if(value.HasValue){try{sum+=value.Value;}catch(OverflowException){unknown++;}}else unknown++;}
        var budget=training.ActiveBudget;string aggregate="当前想法 "+(linked==0?"暂无已关联单":sum.ToString("0.00")+" USD"+(unknown>0?"＋"+unknown+"笔未定义":""))+" / 预算 "+RiskText(budget);
        if(unmapped>0)aggregate+="；"+unmapped+"笔归属待核实";riskPreview.Text=perEntry+"\r\n"+aggregate;
        tips.SetToolTip(riskPreview,perEntry+"\r\n"+aggregate+"\r\n现有持仓按当前净值至SL的额外亏损估算；挂单按全部成交后至SL的亏损相加。未计滑点、费用，不抵消对冲仓位。未设SL为未定义，不是零风险。这里只提示，不限仓。");
    }
    void OpenIntradayRecords(){if(RecordsRequested!=null)RecordsRequested(session.Id);else RecordStore.OpenViewer(session.Id);}
    string IntradayStatusOverride()
    {return dayFinishError!=""?dayFinishError:dayError!=""?dayError:dayCarryError!=""?dayCarryError:unchecked(feedbackUntil-Environment.TickCount)>0?dayFeedback:null;}
    void RefreshIntraday()
    {
        if(snapshot==null){finishDayButton.Enabled=false;return;}
        try
        {
            if(dayBoot!=snapshot.Get("boot"))
            {if(daybook!=null)try{daybook.Flush();}catch(Exception ex){dayCarryError="上场日内观测未完全保存："+ex.Message;}dayBoot=snapshot.Get("boot");daybook=null;dayError="";dayFinishError="";dayFinishedFlushed=false;finishRequested=false;gridSignature="\0";selectedItemKey=gridNativeSelection="";}
            if(daybook==null)daybook=new ReplayDaybook(session.Id,dayBoot,folder);
            if(daybook!=null && snapshot.Fresh)
            {var day=daybook.Observe(snapshot);dayError="";range.Text="接入后余额变化 "+(day.LastBalance-day.FirstBalance).ToString("+0.00;-0.00;0.00")+" · 观测回撤 "+(day.PeakEquity>0?day.MaxObservedDrawdownPct.ToString("0.00")+"%":"未定义");tips.SetToolTip(range,"从本程序本历史日首次接入起抽样观测，含浮盈亏；不能替代逐Tick最大回撤。\r\n首次接入："+day.FirstHistorical.ToString("yyyy.MM.dd HH:mm:ss"));}
            if(snapshot.Get("phase")=="ended" && !dayFinishedFlushed && daybook!=null){daybook.Flush();dayFinishedFlushed=true;}
        }
        catch(Exception ex){dayError="日内附加记录异常："+ex.Message;}
        RefreshIntradayGrid();UpdateIntradayRisk();
        RefreshIntradayButtons();
        if(snapshot.Number("bid")>0 && snapshot.Number("ask")>=snapshot.Number("bid"))
        {lotPreview.Text="Bid "+snapshot.Get("bid")+" · Ask "+snapshot.Get("ask")+" · 差 "+(snapshot.Number("ask")-snapshot.Number("bid")).ToString("0.000");tips.SetToolTip(lotPreview,"卖出价Bid："+snapshot.Get("bid")+"；买入价Ask："+snapshot.Get("ask")+"。点差单位：USD/盎司。\r\n自动手数按提交时真实报价重算，不另设程序上限。");}
        trainingPauseShortcut.Text=snapshot.Get("training_mode")!="active"?"继续":"暂停";
        progress.Value=snapshot.Now.TimeOfDay.TotalSeconds/86400;progressText.Text="历史 "+snapshot.Now.ToString("MM-dd HH:mm:ss")+" · 本日 "+(progress.Value*100).ToString("0.0")+"%";
        connection.Text=snapshot.Get("phase")=="ended"?"本场结束 · 请从入口启动下一场":!snapshot.Fresh?"报价停止更新 · 等待回放连接":pending>0?"等待回放确认操作…":snapshot.Get("training_mode")=="paused"?"回放已暂停 · 点击继续恢复":snapshot.Get("training_mode")=="passive"?"放置运行 · 禁止新增交易":"回放中 · 按回放服务器时间";

    }
}

public sealed class ReplayFinishDayForm:ReplayBufferedForm
{
    public ReplayFinishDayForm(DesktopSnapshot state)
    {
        Text="收工并保存现场";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(430,230);MinimumSize=new Size(380,240);StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(14)};root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(root);
        root.Controls.Add(new ReplayStableLabel{Text="历史 "+state.Now.ToString("yyyy.MM.dd HH:mm:ss")+"\r\n仍有 "+state.Get("count")+" 笔持仓、"+state.Get("pending_count")+" 笔挂单。\r\n\r\n暂停确认后保存余额、净值与订单现场。\r\n已有订单保留，暂停期间历史时间不推进。\r\n之后可点击底部“继续”恢复回放。",Dock=DockStyle.Fill},0,0);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Margin=Padding.Empty};var save=new Button{Text="暂停并保存",DialogResult=DialogResult.OK,Width=125,Height=34};var cancel=new Button{Text="返回管理",DialogResult=DialogResult.Cancel,Width=110,Height=34};buttons.Controls.Add(save);buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,1);AcceptButton=save;CancelButton=cancel;ReplayWindowLayout.Scale(this);Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
    }
}
