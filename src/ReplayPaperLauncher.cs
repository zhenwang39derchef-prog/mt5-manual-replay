using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public sealed partial class ReplayForm
{
    TableLayoutPanel PaperGrid(int columns)
    {var grid=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=columns,Margin=Padding.Empty};for(int i=0;i<columns;i++)grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100F/columns));return grid;}
    Control PaperField(string label,string key)
    {
        var field=PaperGrid(1);field.RowCount=2;field.RowStyles.Add(new RowStyle(SizeType.Absolute,23));field.RowStyles.Add(new RowStyle(SizeType.Percent,100));field.Margin=new Padding(0,0,16,0);
        field.Controls.Add(ReplayPaper.Label(label,9),0,0);var input=fields[key];input.Parent=null;input.Font=new Font(input is TextBox || input is NumericUpDown?"Segoe UI":"Microsoft YaHei UI",11F);Control host=input;if(input is TextBox || input is NumericUpDown)host=new PaperInputFrame(input);host.Dock=DockStyle.Fill;host.Margin=new Padding(0,3,0,1);field.Controls.Add(host,0,1);return field;
    }
    void BuildPaperLauncher(Button help,TabPage basic,TabPage randomDates)
    {
        var headings=Controls.OfType<Label>().Where(x=>x!=status).OrderBy(x=>x.Top).ToArray();
        var header=PaperGrid(2);header.ColumnStyles.Clear();header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,114));header.RowCount=2;header.RowStyles.Add(new RowStyle(SizeType.Absolute,38));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int i=0;i<headings.Length;i++){var label=headings[i];label.Dock=DockStyle.Fill;label.Margin=Padding.Empty;label.TextAlign=ContentAlignment.MiddleLeft;label.Font=new Font("Microsoft YaHei UI",i==0?18:9.5F,FontStyle.Regular);header.Controls.Add(label,0,i);if(i>0)header.SetColumnSpan(label,2);}
        headings[1].Text="设置本次练习的日期与周期，进入 MT5 历史回放。";help.Dock=DockStyle.Fill;help.Margin=new Padding(8,1,0,6);((PaperActionButton)help).Quiet=true;header.Controls.Add(help,1,0);

        foreach(Control control in basic.Controls.Cast<Control>().ToArray())basic.Controls.Remove(control);
        basic.AutoScroll=true;basic.AutoScrollMinSize=Size.Empty;basic.Padding=new Padding(4,12,4,0);
        var stack=new ReplayBufferedTable{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=2,Margin=Padding.Empty};stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));basic.Controls.Add(stack);
        var setupCard=new PaperCard{Dock=DockStyle.Top,Height=270,MinimumSize=new Size(0,270)};var setup=PaperGrid(2);setup.RowCount=4;setup.RowStyles.Add(new RowStyle(SizeType.Absolute,26));for(int i=0;i<3;i++)setup.RowStyles.Add(new RowStyle(SizeType.Absolute,68));setupCard.Controls.Add(setup);
        var title=ReplayPaper.Label("本次练习",11,ReplayPaper.Ink);setup.Controls.Add(title,0,0);setup.SetColumnSpan(title,2);
        string[] labels={"交易品种","图表周期","开始日期","结束日期（含当天）","练习本金 · USD","手动初始手数"};string[] keys={"Symbol","Timeframe","From","To","Deposit","Lots"};
        for(int i=0;i<keys.Length;i++)setup.Controls.Add(PaperField(labels[i],keys[i]),i%2,1+i/2);stack.Controls.Add(setupCard,0,0);
        var paceCard=new PaperCard{Dock=DockStyle.Top,Height=180,MinimumSize=new Size(0,180)};var pace=PaperGrid(1);pace.RowCount=4;pace.RowStyles.Add(new RowStyle(SizeType.Absolute,29));pace.RowStyles.Add(new RowStyle(SizeType.Absolute,66));pace.RowStyles.Add(new RowStyle(SizeType.Absolute,29));pace.RowStyles.Add(new RowStyle(SizeType.Absolute,25));paceCard.Controls.Add(pace);
        var paceHeading=PaperGrid(2);paceHeading.ColumnStyles.Clear();paceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));paceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,122));paceHeading.Controls.Add(ReplayPaper.Label("回放方式",11,ReplayPaper.Ink),0,0);
        var random=new PaperActionButton{Text="随机日期 ↗",Quiet=true,Dock=DockStyle.Fill,Margin=Padding.Empty,AccessibleName="OpenRandomDates"};random.Click+=(s,e)=>tabs.SelectedTab=randomDates;paceHeading.Controls.Add(random,1,0);pace.Controls.Add(paceHeading,0,0);
        initialPlayback.Parent=null;initialPlayback.Dock=DockStyle.Fill;initialPlayback.Margin=new Padding(0,4,0,4);initialPlayback.Slider.BackColor=ReplayPaper.Surface;pace.Controls.Add(initialPlayback,0,1);
        sizingSummary.Parent=null;sizingSummary.Dock=DockStyle.Fill;sizingSummary.Margin=Padding.Empty;sizingSummary.Font=new Font("Microsoft YaHei UI",9);sizingSummary.AutoEllipsis=true;pace.Controls.Add(sizingSummary,0,2);
        var intraday=(CheckBox)fields["IntradayLayout"];intraday.Parent=null;intraday.Dock=DockStyle.Fill;intraday.Text="使用日内侧边交易面板";intraday.Margin=Padding.Empty;intraday.Font=new Font("Microsoft YaHei UI",9);pace.Controls.Add(intraday,0,3);stack.Controls.Add(paceCard,0,1);

        launcherFooter=PaperGrid(1);launcherFooter.AutoSize=true;launcherFooter.RowCount=3;launcherFooter.Padding=new Padding(0,10,0,0);launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,41));launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,36));launcherFooter.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        sequencePreview.Dock=DockStyle.Fill;sequencePreview.Margin=Padding.Empty;sequencePreview.Font=new Font("Microsoft YaHei UI",9);sequencePreview.ForeColor=ReplayPaper.Accent;launcherFooter.Controls.Add(sequencePreview,0,0);
        status.Dock=DockStyle.Fill;status.Margin=new Padding(0,3,0,3);status.Font=new Font("Microsoft YaHei UI",9);launcherFooter.Controls.Add(status,0,1);
        var actions=PaperGrid(4);actions.ColumnStyles.Clear();actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,166));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var recentButton=new PaperActionButton{Text="最近收工",AccessibleName="RecentFinishes"};recentButton.Click+=(s,e)=>tabs.SelectedTab=finishRecentPage;foreach(var button in new[]{saveButton,panelButton,recentButton,start}){button.Dock=DockStyle.Fill;button.Margin=new Padding(0,2,10,2);actions.Controls.Add(button);}start.Text="启动本次练习";start.Margin=new Padding(8,2,0,2);launcherFooter.Controls.Add(actions,0,2);
        launcherLayout=PaperGrid(1);launcherLayout.RowCount=3;launcherLayout.Padding=new Padding(26,20,26,22);launcherLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,82));launcherLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));launcherLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));tabs.Dock=DockStyle.Fill;tabs.Margin=Padding.Empty;
        launcherLayout.Controls.Add(header,0,0);launcherLayout.Controls.Add(tabs,0,1);launcherLayout.Controls.Add(launcherFooter,0,2);Controls.Add(launcherLayout);
        foreach(TabPage page in tabs.TabPages)
        {if(page!=recordsPage){ReplayPaper.ApplyInputs(page);if(page!=basic&&!page.Controls.Contains(trainingSetup)){page.AutoScroll=true;page.AutoScrollMinSize=new Size(714,400);}}}
        ReplayPaper.ApplyInputs(header);headings[0].ForeColor=ReplayPaper.Ink;headings[1].ForeColor=ReplayPaper.Muted;ReplayPaper.ApplyInputs(launcherFooter);sequencePreview.ForeColor=ReplayPaper.Accent;
        BuildRecentFinishesTab();BackColor=ReplayPaper.Background;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(890,800);MinimumSize=new Size(780,570);ReplayWindowLayout.Scale(this);
    }
}
