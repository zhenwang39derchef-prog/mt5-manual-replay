using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed partial class ReplayForm
{
    readonly TabPage finishRecentPage=new TabPage("最近收工");
    readonly ReplayBufferedTable finishRecentList=new ReplayBufferedTable();
    bool loadingFinishes;
    public event Action<string,string> FinishExitRequested;
    public string FinishArchiveRoot {get;set;}
    void BuildRecentFinishesTab()
    {
        finishRecentPage.BackColor=ReplayPaper.Background;finishRecentPage.Padding=new Padding(10);finishRecentPage.AutoScroll=true;
        finishRecentList.Dock=DockStyle.Top;finishRecentList.AutoSize=true;finishRecentList.ColumnCount=1;finishRecentList.Margin=Padding.Empty;finishRecentList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));finishRecentPage.Controls.Add(finishRecentList);tabs.TabPages.Add(finishRecentPage);
    }
    public void RefreshRecentFinishes()
    {
        if(loadingFinishes)return;loadingFinishes=true;
        try
        {
            foreach(Control c in finishRecentList.Controls.Cast<Control>().ToArray())c.Dispose();finishRecentList.Controls.Clear();finishRecentList.RowStyles.Clear();finishRecentList.RowCount=0;
            var heading=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));heading.Controls.Add(ReplayPaper.Label("最近收工",17,ReplayPaper.Ink),0,0);var refresh=new PaperActionButton{Text="刷新",Dock=DockStyle.Fill,AccessibleName="RefreshRecentFinishes"};refresh.Click+=(s,e)=>RefreshRecentFinishes();heading.Controls.Add(refresh,1,0);FinishRow(heading,48);
            var archives=FinishStore.List(FinishArchiveRoot);
            if(FinishStore.LastError!="")FinishRow(ReplayPaper.Label("部分档案未能读取，原文件未覆盖。\r\n"+FinishStore.LastError,9,GraphiteTheme.SellText),66);
            if(archives.Count==0){FinishRow(ReplayPaper.Label("还没有收工档案。完成本场收工后，可在这里继续复盘、查看现场和最终结果。",10),82);return;}
            foreach(var archive in archives)
            {
                var saved=archive;var card=new PaperCard{Dock=DockStyle.Fill,MinimumSize=new Size(0,(int)(166*ReplayWindowLayout.DpiScale)),Margin=new Padding(0,8,0,8)};
                var table=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Margin=Padding.Empty};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170*ReplayWindowLayout.DpiScale));foreach(int h in new[]{28,25,33,38})table.RowStyles.Add(new RowStyle(SizeType.Absolute,h*ReplayWindowLayout.DpiScale));card.Controls.Add(table);
                string status=archive.Progress.Error!=""?"有步骤待重试":!archive.Progress.FinalSaved?"最终结果待同步":archive.Review.Completed?"复盘完成":"待完成复盘";
                table.Controls.Add(ReplayPaper.Label(archive.Snapshot.Symbol+" · "+archive.Snapshot.Period,12,ReplayPaper.Ink),0,0);var state=ReplayPaper.Label(status,9,archive.Progress.Error!=""?GraphiteTheme.SellText:ReplayPaper.Accent);state.TextAlign=ContentAlignment.MiddleRight;table.Controls.Add(state,1,0);
                table.Controls.Add(ReplayPaper.Label("历史 "+archive.Snapshot.Historical.ToString("yyyy.MM.dd HH:mm:ss")+(archive.Snapshot.Kind=="test_end"?" · 测试结束记录":""),9),0,1);table.SetColumnSpan(table.GetControlFromPosition(0,1),2);
                table.Controls.Add(ReplayPaper.Label("收工净值 "+archive.Snapshot.Equity.ToString("N2")+" USD · 持仓 "+archive.Snapshot.Positions+" / 挂单 "+archive.Snapshot.Pending,10,ReplayPaper.Ink),0,2);table.SetColumnSpan(table.GetControlFromPosition(0,2),2);
                var open=new PaperActionButton{Text="查看本场档案",Quiet=true,Dock=DockStyle.Fill};open.Click+=(s,e)=>ShowFinishArchive(saved);table.Controls.Add(open,0,3);
                var review=new PaperActionButton{Text=archive.Review.Completed&&archive.Progress.FinalSaved?"查看复盘":"继续复盘 / 重试",Primary=true,Dock=DockStyle.Fill,AccessibleName="ResumeFinishReview"};review.Click+=(s,e)=>ShowFinishReview(saved);table.Controls.Add(review,1,3);FinishRow(card,(int)(182*ReplayWindowLayout.DpiScale));
            }
        }
        finally{loadingFinishes=false;}
    }
    void FinishRow(Control control,int height)
    {int row=finishRecentList.RowCount++;finishRecentList.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Dock=DockStyle.Fill;control.Height=height;control.MinimumSize=new Size(0,height);control.Margin=new Padding(0,4,0,8);finishRecentList.Controls.Add(control,0,row);}
    void ShowFinishArchive(FinishArchive archive)
    {
        try{using(var form=new FinishArchiveForm(FinishStore.Load(archive.Folder))){form.ReviewRequested=a=>ShowFinishReview(a);form.ShowDialog(this);}RefreshRecentFinishes();}
        catch(Exception ex){ShowError(ex);}
    }
    public void ShowFinishReview(FinishArchive archive)
    {
        bool exit=false;
        try
        {
            var saved=FinishStore.Load(archive.Folder);using(var form=new FinishReviewForm(saved))
            {form.ExitRequested=async completed=>{string error=await FinishNativeBridge.EndAsync(saved);if(error=="")exit=true;return error;};form.ShowDialog(Form.ActiveForm??this);}
            if(exit && FinishExitRequested!=null)BeginInvoke((Action)(()=>FinishExitRequested(saved.Snapshot.Session,saved.Snapshot.Boot)));else RefreshRecentFinishes();
        }
        catch(Exception ex){ShowError(ex);}
    }
}
