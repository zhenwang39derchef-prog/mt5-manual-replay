using System;
using System.Drawing;
using System.Windows.Forms;

public sealed partial class DesktopReplayForm
{
    readonly Label selectedOrderTitle=new ReplayStableLabel(),selectedEntryPrice=new ReplayStableLabel(),selectedStopLoss=new ReplayStableLabel(),selectedTakeProfit=new ReplayStableLabel(),selectedEntryCaption=new ReplayStableLabel();
    TableLayoutPanel intradayPositions,selectedOrderDetails,intradayPlanSection;
    Control intradayPlanHeading,selectedPrices;

    void UpdateIntradayListVisibility(int count=-1)
    {
        if(intradayPositions==null)return;if(count<0)count=snapshot==null?0:ChartItem.Parse(snapshot.Get("chart_items")).Count;
        bool show=count>0 && (intradayCompact || count>1);intradayGrid.Visible=show;
        intradayPositions.RowStyles[1].SizeType=show?SizeType.Percent:SizeType.Absolute;intradayPositions.RowStyles[1].Height=show?100:0;
    }

    Control BuildSelectedOrderDetails()
    {
        selectedOrderDetails=PanelGrid(new[]{0f},2);selectedOrderDetails.Dock=DockStyle.Top;selectedOrderDetails.MinimumSize=new Size(0,84);selectedOrderDetails.MaximumSize=new Size(0,84);selectedOrderDetails.Height=84;selectedOrderDetails.RowStyles.Add(new RowStyle(SizeType.Absolute,28));selectedOrderDetails.RowStyles.Add(new RowStyle(SizeType.Percent,100));selectedOrderDetails.Padding=new Padding(2,4,2,4);
        PrepareLabel(selectedOrderTitle,Ink);selectedOrderTitle.AccessibleName="SelectedOrderTitle";selectedOrderDetails.Controls.Add(selectedOrderTitle,0,0);
        var prices=PanelGrid(new[]{0f,0f,0f},2);prices.RowStyles.Add(new RowStyle(SizeType.Absolute,22));prices.RowStyles.Add(new RowStyle(SizeType.Percent,100));selectedPrices=prices;
        PrepareLabel(selectedEntryCaption,Muted);selectedEntryCaption.Text="入场价";
        prices.Controls.Add(selectedEntryCaption,0,0);prices.Controls.Add(PanelLabel("实际止损",Muted),1,0);prices.Controls.Add(PanelLabel("实际止盈",Muted),2,0);
        var values=new[]{selectedEntryPrice,selectedStopLoss,selectedTakeProfit};string[] names={"SelectedEntryPrice","SelectedStopLoss","SelectedTakeProfit"};
        for(int i=0;i<3;i++){PrepareLabel(values[i],Ink);values[i].Font=new Font("Segoe UI",11F);values[i].AccessibleName=names[i];prices.Controls.Add(values[i],i,1);}
        selectedOrderDetails.Controls.Add(prices,0,1);return selectedOrderDetails;
    }
    Control BuildIntradayPlanSection()
    {
        intradayPlanSection=PanelGrid(new[]{0f,106f},2);intradayPlanSection.RowStyles.Add(new RowStyle(SizeType.Absolute,28));intradayPlanSection.RowStyles.Add(new RowStyle(SizeType.Percent,100));intradayPlanSection.Padding=new Padding(0,5,0,5);
        var heading=PanelGrid(new[]{0f,110f},1);heading.Controls.Add(PanelLabel("交易计划",Ink),0,0);
        var view=PanelButton("查看当前计划 ↗",Card);((DesktopActionButton)view).Quiet=true;view.Click+=(s,e)=>{try{training.ShowCurrentPlan(this);}catch(Exception ex){status.Text=ex.Message;}};heading.Controls.Add(view,1,0);intradayPlanHeading=heading;
        intradayPlanSection.Controls.Add(heading,0,0);intradayPlanSection.SetColumnSpan(heading,2);
        PrepareLabel(trainingBrief,GraphiteTheme.Muted);trainingBrief.Text="计划：等待回放连接";intradayPlanSection.Controls.Add(trainingBrief,0,1);trainingPlanShortcut.Text="＋ 新交易计划";intradayPlanSection.Controls.Add(trainingPlanShortcut,1,1);
        GraphiteTheme.AddSeparators(intradayPlanSection);return intradayPlanSection;
    }
    void RefreshSelectedOrderDetails()
    {
        if(selectedOrderDetails==null || snapshot==null)return;var item=CurrentIntradayItem();
        if(item==null){selectedOrderTitle.Text="暂无持仓 / 挂单";selectedEntryPrice.Text=selectedStopLoss.Text=selectedTakeProfit.Text="—";selectedPrices.Visible=false;return;}
        selectedPrices.Visible=true;selectedOrderTitle.Text=(item.Kind=="O"?"挂单 · ":"持仓 · ")+(item.Side=="buy"?"买入":"卖出")+"  #"+item.Ticket+"   "+item.Lots.ToString("0.###")+" 手";
        selectedEntryCaption.Text=item.Kind=="O"?"委托价格":"入场价";string format="F"+(int)Math.Max(0,Math.Min(8,snapshot.Int("digits")));
        selectedEntryPrice.Text=item.Entry.ToString(format);selectedStopLoss.Text=item.SL>0?item.SL.ToString(format):"未设置";selectedTakeProfit.Text=item.TP>0?item.TP.ToString(format):"未设置";
    }
}
