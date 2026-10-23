using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class CsvTable
{
    public string[] Header;
    public List<string[]> Rows=new List<string[]>();
    public string Get(int row,string column)
    {
        int at=Array.IndexOf(Header,column);
        return at<0 || row<0 || row>=Rows.Count?"":Rows[row][at];
    }
    public decimal Number(int row,string column)
    {
        decimal value;
        if(!Decimal.TryParse(Get(row,column),NumberStyles.Number,CultureInfo.InvariantCulture,out value)) throw new FormatException("数值缺失或无效："+column);
        return value;
    }
    public static CsvTable Parse(string text)
    {
        text=text.TrimStart('\uFEFF');
        var rows=new List<string[]>(); var cells=new List<string>(); var field=new StringBuilder();
        bool quoted=false,closed=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];
            if(quoted)
            {
                if(c=='"') { if(i+1<text.Length && text[i+1]=='"') { field.Append('"'); i++; } else { quoted=false; closed=true; } }
                else field.Append(c);
            }
            else if(c=='"' && field.Length==0 && !closed) quoted=true;
            else if(c==',' || c=='\r' || c=='\n')
            {
                cells.Add(field.ToString()); field.Length=0; closed=false;
                if(c!=',')
                {
                    if(c=='\r' && i+1<text.Length && text[i+1]=='\n') i++;
                    rows.Add(cells.ToArray()); cells.Clear();
                }
            }
            else { if(closed || c=='"') throw new FormatException("CSV引号格式错误"); field.Append(c); }
        }
        if(quoted) throw new FormatException("CSV尚未写入完成");
        if(field.Length>0 || cells.Count>0 || closed) { cells.Add(field.ToString()); rows.Add(cells.ToArray()); }
        if(rows.Count==0 || rows[0].Length==0) throw new FormatException("CSV缺少表头");
        var table=new CsvTable{Header=rows[0]};
        if(table.Header.Distinct().Count()!=table.Header.Length) throw new FormatException("CSV表头重复");
        foreach(var row in rows.Skip(1)) { if(row.Length!=table.Header.Length) throw new FormatException("CSV列数不一致"); table.Rows.Add(row); }
        return table;
    }
}

public sealed class RecordBundle
{
    public string Generation;
    public readonly Dictionary<string,byte[]> Bytes=new Dictionary<string,byte[]>();
    public readonly Dictionary<string,CsvTable> Tables=new Dictionary<string,CsvTable>();
}
public sealed class RecordRun
{
    public string Folder,Session;
    public RecordBundle Bundle;
    public override string ToString()
    {
        var s=Bundle.Tables["回放汇总.csv"];
        return s.Get(0,"回放开始日期")+" ～ "+s.Get(0,"回放结束日期含当天")+"  "+s.Get(0,"品种")+" "+s.Get(0,"周期")+
            "  | 已平 "+s.Get(0,"已平持仓数")+" 笔 | "+s.Get(0,"记录状态")+"  ["+Path.GetFileName(Folder)+"]";
    }
}

public static class RecordStore
{
    public static readonly string[] Names={"交易结果.csv","成交明细.csv","操作记录.csv","回放汇总.csv","挂单记录.csv","净值过程.csv"};
    public static readonly string ExportRoot=Path.Combine(ReplayFiles.Home,"交易记录");
    static readonly object Gate=new object(),QueueGate=new object();
    static readonly HashSet<string> Queued=new HashSet<string>();
    public static string LastError="";
    static byte[] SharedRead(string path)
    {
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var memory=new MemoryStream()) { f.CopyTo(memory); return memory.ToArray(); }
    }
    public static RecordBundle ReadBundle(string folder)
    {
        string marker=Path.Combine(folder,"snapshot.txt");
        string generation=File.ReadAllText(marker).Trim('\uFEFF','\r','\n',' '); long sequence;
        if(!Int64.TryParse(generation,out sequence) || sequence<=0) throw new FormatException("CSV快照标记无效");
        var bundle=new RecordBundle{Generation=generation};
        foreach(string name in Names)
        {
            byte[] bytes;
            if(name=="净值过程.csv" && !File.Exists(Path.Combine(folder,name)) &&
               bundle.Tables.ContainsKey("回放汇总.csv") && bundle.Tables["回放汇总.csv"].Header.Contains("最大金额回撤峰值时间"))
                throw new FormatException("本场净值过程CSV尚未就绪，请稍后刷新");
            if((name=="挂单记录.csv" || name=="净值过程.csv") && !File.Exists(Path.Combine(folder,name)))
                bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(name=="净值过程.csv"?"快照号,回放ID,运行ID,品种,周期,小时起点,末次观察时间,余额,净值,小时最高净值,小时最低净值,当前回撤百分比,持仓数,时段状态\r\n":"快照号,回放ID,运行ID,品种,周期,订单编号,状态\r\n")).ToArray();
            else bytes=SharedRead(Path.Combine(folder,name));
            if(bytes.Length<3 || bytes[0]!=239 || bytes[1]!=187 || bytes[2]!=191) throw new FormatException("CSV不是带BOM的UTF-8格式");
            var table=CsvTable.Parse(new UTF8Encoding(false,true).GetString(bytes));
            int generationColumn=Array.IndexOf(table.Header,"快照号");
            if(generationColumn<0) throw new FormatException("CSV缺少快照号");
            foreach(var row in table.Rows)
                if(row[generationColumn]!=generation) throw new FormatException("CSV快照仍在更新，请稍后刷新");
            bundle.Bytes.Add(name,bytes); bundle.Tables.Add(name,table);
        }
        if(bundle.Tables["回放汇总.csv"].Rows.Count!=1 || File.ReadAllText(marker).Trim('\uFEFF','\r','\n',' ')!=generation)
            throw new FormatException("CSV快照仍在更新，请稍后刷新");
        var summary=bundle.Tables["回放汇总.csv"];
        string session=summary.Get(0,"回放ID"),boot=summary.Get(0,"运行ID");
        if(!DesktopSessionFiles.ValidId(session) || String.IsNullOrEmpty(boot) || boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-'))
            throw new FormatException("CSV回放或运行标识无效");
        foreach(string field in new[]{"回放ID","运行ID","品种","周期"})
        {
            string expected=summary.Get(0,field);if(String.IsNullOrEmpty(expected))throw new FormatException("CSV汇总缺少"+field);
            foreach(var table in bundle.Tables.Values)
            {
                int column=Array.IndexOf(table.Header,field);if(column<0)throw new FormatException("CSV缺少"+field);
                if(table.Rows.Any(row=>row[column]!=expected))throw new FormatException("CSV包含不同场次或品种周期的混合记录："+field);
            }
        }
        bundle.Bytes.Add("snapshot.txt",new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(generation+"\r\n")).ToArray());
        return bundle;
    }
    static void WriteBundle(RecordBundle bundle,string target)
    {
        Directory.CreateDirectory(target);
        foreach(string name in Names.Concat(new[]{"snapshot.txt"}))
        {
            string file=Path.Combine(target,name),temp=file+".tmp";
            File.WriteAllBytes(temp,bundle.Bytes[name]);
            if(File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file);
        }
    }
    static void SyncUnlocked(string id)
    {
        if(!DesktopSessionFiles.ValidId(id)) return;
        if(File.Exists(Path.Combine(DesktopSessionFiles.Folder(id),"fixture.flag")) || File.Exists(Path.Combine(DesktopSessionFiles.Folder(id),"records-cleared.flag"))) return;
        string root=Path.Combine(DesktopSessionFiles.Folder(id),"records");
        if(!Directory.Exists(root)) return;
        foreach(string folder in Directory.GetDirectories(root))
        {
            string boot=Path.GetFileName(folder);
            if(boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-')) continue;
            try
            {
                var bundle=ReadBundle(folder);
                if(bundle.Tables["回放汇总.csv"].Get(0,"回放ID")!=id || bundle.Tables["回放汇总.csv"].Get(0,"运行ID")!=boot) throw new FormatException("记录与回放ID或运行目录不一致");
                string target=Path.Combine(ExportRoot,id+"_"+boot);
                string marker=Path.Combine(target,"snapshot.txt");
                if(File.Exists(marker) && File.ReadAllText(marker).Trim('\uFEFF','\r','\n',' ')==bundle.Generation)
                {TrainingRecordService.Sync(new RecordRun{Folder=target,Session=id,Bundle=bundle});continue;}
                WriteBundle(bundle,target);
                TrainingRecordService.Sync(new RecordRun{Folder=target,Session=id,Bundle=bundle});
            }
            catch(IOException ex) { LastError="有原始CSV尚未同步："+boot+"；"+ex.Message; }
            catch(FormatException ex) { LastError="有原始CSV尚未同步："+boot+"；"+ex.Message; }
        }
    }
    public static void SyncSession(string id) { lock(Gate) { SyncUnlocked(id); } }
    public static void ScheduleSync(string id)
    {
        lock(QueueGate) { if(!Queued.Add(id)) return; }
        Task.Run(()=>
        {
            try { SyncSession(id); }
            catch(Exception ex) { LastError="CSV副本未保存："+ex.Message; }
            finally { lock(QueueGate) Queued.Remove(id); }
        });
    }
    public static List<RecordRun> LoadRuns(string preferred)
    {
        lock(Gate)
        {
            LastError="";
            if(Directory.Exists(DesktopSessionFiles.Common))
                foreach(string folder in Directory.GetDirectories(DesktopSessionFiles.Common))
                    if(DesktopSessionFiles.ValidId(Path.GetFileName(folder))) SyncUnlocked(Path.GetFileName(folder));
            var list=new List<RecordRun>();
            if(!Directory.Exists(ExportRoot)) return list;
            foreach(string folder in Directory.GetDirectories(ExportRoot))
            {
                try
                {
                    var bundle=ReadBundle(folder); string id=bundle.Tables["回放汇总.csv"].Get(0,"回放ID");
                    if(DesktopSessionFiles.ValidId(id) && (File.Exists(Path.Combine(DesktopSessionFiles.Folder(id),"fixture.flag")) || File.Exists(Path.Combine(DesktopSessionFiles.Folder(id),"records-cleared.flag")))) continue;
                    list.Add(new RecordRun{Folder=folder,Session=id,Bundle=bundle});
                }
                catch(IOException ex) { LastError="有CSV未计入列表："+Path.GetFileName(folder)+"；"+ex.Message; }
                catch(FormatException ex) { LastError="有CSV未计入列表："+Path.GetFileName(folder)+"；"+ex.Message; }
            }
            return list.OrderByDescending(x=>x.Session==preferred).ThenByDescending(x=>Directory.GetLastWriteTimeUtc(x.Folder)).ToList();
        }
    }
    public static void Export(RecordRun run,string target)
    {
        lock(Gate)
        {
            string source=Path.GetFullPath(run.Folder).TrimEnd('\\'),dest=Path.GetFullPath(target).TrimEnd('\\');
            var current=ReadBundle(run.Folder);
            if(current.Tables["回放汇总.csv"].Get(0,"回放ID")!=run.Session)throw new FormatException("导出来源已变更，请刷新记录列表后重试。");
            var freshRun=new RecordRun{Folder=run.Folder,Session=run.Session,Bundle=current};
            if(String.Equals(source,dest,StringComparison.OrdinalIgnoreCase)){TrainingRecordService.Export(freshRun,dest);return;}
            WriteBundle(current,dest);
            TrainingRecordService.Export(freshRun,dest);
        }
    }
    public static int ClearAll()
    {
        var testers=Process.GetProcessesByName("metatester64");
        bool visualizerOpen=false;
        try {foreach(var process in testers)try{if(process.MainWindowHandle!=IntPtr.Zero)visualizerOpen=true;}catch(InvalidOperationException){}}
        finally {foreach(var process in testers)process.Dispose();}
        lock(Gate){return ReplayHistoryCleaner.Clear(ReplayFiles.Home,DesktopSessionFiles.Common,visualizerOpen);}
    }
    public static void OpenViewer(string session)
    {
        var p=new ProcessStartInfo(Application.ExecutablePath,"--records"+(DesktopSessionFiles.ValidId(session)?" "+session:""));
        p.UseShellExecute=false; p.WorkingDirectory=ReplayFiles.Home; Process.Start(p);
    }
}

public sealed class RecordBrowserForm : Form
{
    readonly ComboBox runs=new ComboBox(); readonly Label summary=new Label(),status=new Label();
    readonly TabControl tabs=new TabControl(); readonly Button refresh=new Button();
    readonly Dictionary<string,DataGridView> grids=new Dictionary<string,DataGridView>();
    string preferred;
    readonly Button clear=new Button();
    readonly Button executionReview=new Button(),trainingGroups=new Button();
    bool loading,preferRequested;
    public bool LoadOnShow=true;
    public event Action<bool> BusyChanged;
    public Func<string,List<RecordRun>> RecordLoader;
    bool preview=false;
    RecordRun Selected { get { return runs.SelectedItem as RecordRun; } }
    public RecordBrowserForm(string session)
    {
        preferred=session; Text="手动回放 · 交易记录与CSV"; Font=new Font("Microsoft YaHei UI",9F);
        ClientSize=new Size(1100,680); MinimumSize=new Size(880,540); StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(244,247,251);
        runs.DropDownStyle=ComboBoxStyle.DropDownList; runs.SetBounds(16,16,1068,29); runs.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
        runs.SelectedIndexChanged+=(s,e)=>Populate(); Controls.Add(runs);
        Button open=Button("打开CSV文件夹",16,59,150); open.Click+=(s,e)=>OpenFolder();
        Button export=Button("另存CSV到…",180,59,150); export.Click+=(s,e)=>Export();
        refresh.Text="刷新记录"; refresh.SetBounds(344,59,130,32); refresh.Click+=async(s,e)=>await Reload(); Controls.Add(refresh);
        clear.Text="清空全部记录";clear.SetBounds(656,59,150,32);clear.ForeColor=Color.Maroon;clear.Click+=async(s,e)=>await ClearRecords();Controls.Add(clear);
        Button help=Button("CSV字段说明",488,59,150); help.Click+=(s,e)=>MessageBox.Show(TopLevelControl??this,"交易结果按持仓ID汇总，可处理多笔持仓和部分平仓。\r\n净盈亏 = 毛盈亏 + 佣金 + 已结算隔夜费 + 其他手续费（保留MT5正负号）。\r\n未平仓浮动盈亏和未结算隔夜费单独列出。\r\n胜率只统计已全平持仓；无亏损时利润因子留空。\r\n回撤按EA收到的每个Tick采样，暂停期间不产生新行情。\r\n净值过程按MT5历史小时保存，每小时一行，包含小时内净值高低；末小时在运行时更新。\r\n旧场次没有净值过程时显示空表，不补造历史数据。\r\n时间采用MT5历史数据时间，不自动转换时区。\r\nCSV自动保存在入口旁“交易记录”文件夹；MT5公共文件夹另有原始副本。\r\n记录从本版本启用后开始，不能自动还原先前回放。","记录口径");
        executionReview.Text="执行复盘";executionReview.AccessibleName="TrainingExecutionReview";executionReview.SetBounds(820,59,115,32);executionReview.Click+=(s,e)=>OpenExecutionReview();Controls.Add(executionReview);
        trainingGroups.Text="训练分组";trainingGroups.AccessibleName="TrainingGroupStatistics";trainingGroups.SetBounds(940,59,115,32);trainingGroups.Click+=(s,e)=>OpenTrainingGroups();Controls.Add(trainingGroups);
        summary.SetBounds(16,108,1068,48); summary.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right; Controls.Add(summary);
        tabs.SetBounds(16,168,1068,458); tabs.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right; Controls.Add(tabs);
        foreach(string name in RecordStore.Names.Concat(new[]{"日内观测.csv","收工现场.csv"}))
        {
            var tab=new TabPage(Path.GetFileNameWithoutExtension(name)); var grid=new DataGridView();
            grid.Dock=DockStyle.Fill; grid.ReadOnly=true; grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells; grid.RowHeadersVisible=false;
            grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; grid.BackgroundColor=Color.White;
            grid.ClipboardCopyMode=DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
            grid.EnableHeadersVisualStyles=false; grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(30,64,110);
            grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.White; grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(241,245,249);
            tab.Controls.Add(grid); tabs.TabPages.Add(tab); grids[name]=grid;
        }
        status.SetBounds(16,641,1068,27); status.Anchor=AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right; status.ForeColor=Color.FromArgb(61,77,100); Controls.Add(status);
        BuildResponsiveRecords();
        Shown+=async(s,e)=>{if(!preview && LoadOnShow) await Reload();};
    }
    void BuildResponsiveRecords()
    {
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=Padding.Empty};
        foreach(var button in Controls.OfType<Button>().OrderBy(x=>x.Left).ToArray())
        {
            int width;
            if(button==refresh){button.Text="刷新";width=60;}
            else if(button==clear){button.Text="清空全部记录";width=95;}
            else if(button.Text.StartsWith("打开")){button.Text="打开文件夹";width=85;}
            else if(button.Text.StartsWith("另存")){button.Text="导出CSV";width=80;}
            else if(button==executionReview || button==trainingGroups){width=85;}
            else{button.Text="字段说明";width=75;}
            button.Size=new Size(width,32);button.Margin=new Padding(0,0,6,6);button.Anchor=AnchorStyles.None;toolbar.Controls.Add(button);
        }
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=5,Margin=Padding.Empty};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,32));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Absolute,38));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
        runs.Dock=DockStyle.Fill;runs.Margin=new Padding(0,0,0,6);root.Controls.Add(runs,0,0);root.Controls.Add(toolbar,0,1);
        summary.Dock=DockStyle.Fill;summary.Margin=new Padding(0,2,0,4);summary.AutoEllipsis=true;root.Controls.Add(summary,0,2);
        tabs.Dock=DockStyle.Fill;tabs.Margin=Padding.Empty;root.Controls.Add(tabs,0,3);
        status.Dock=DockStyle.Fill;status.Margin=new Padding(0,6,0,0);status.AutoEllipsis=true;root.Controls.Add(status,0,4);Controls.Add(root);
        AutoScaleMode=AutoScaleMode.None;
        Shown+=(s,e)=>{if(TopLevel){ReplayWindowLayout.Scale(this);ReplayWindowLayout.Fit(this);}};
        MinimumSize=new Size(640,460);
    }

    Button Button(string text,int x,int y,int w) { var b=new Button{Text=text}; b.SetBounds(x,y,w,32); Controls.Add(b); return b; }
    public Task RefreshRecordsAsync(string session)
    {if(DesktopSessionFiles.ValidId(session)){preferred=session;preferRequested=true;}return Reload();}
    async Task Reload()
    {
        if(loading || IsDisposed)return;
        loading=true;refresh.Enabled=clear.Enabled=executionReview.Enabled=trainingGroups.Enabled=false;if(BusyChanged!=null)BusyChanged(true);
        try
        {
            string selected=Selected==null?"":Selected.Folder;
            var list=await Task.Run(()=>RecordLoader!=null?RecordLoader(preferred):RecordStore.LoadRuns(preferred));
            if(IsDisposed)return;
            runs.Items.Clear(); runs.Items.AddRange(list.Cast<object>().ToArray());
            if(list.Count>0) { int at=preferRequested?list.FindIndex(x=>x.Session==preferred):list.FindIndex(x=>x.Folder==selected);preferRequested=false; runs.SelectedIndex=Math.Max(0,at); }
            else { summary.Text="暂无记录。用新版入口启动一次手动历史回放后，记录会自动生成。"; foreach(var g in grids.Values) { g.Rows.Clear(); g.Columns.Clear(); } }
            status.Text=RecordStore.LastError!=""?RecordStore.LastError:"自动保存位置："+RecordStore.ExportRoot;
        }
        catch(Exception ex) { if(!IsDisposed)status.Text="读取记录失败："+ex.Message; }
        finally { loading=false;if(!IsDisposed){refresh.Enabled=clear.Enabled=true;executionReview.Enabled=trainingGroups.Enabled=runs.Items.Count>0;if(BusyChanged!=null)BusyChanged(false);} }
    }
    async Task ClearRecords()
    {
        if(loading)return;
        if(MessageBox.Show(TopLevelControl??this,"清空全部历史交易和图表演练CSV？\r\n原始副本也会清理，无法撤销。程序和设置保留。\r\n请先结束正在运行的回放。","清空记录",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        clear.Enabled=refresh.Enabled=false;if(BusyChanged!=null)BusyChanged(true);
        try {int count=await Task.Run(()=>RecordStore.ClearAll());await Reload();status.Text="已清空记录（"+count+"个文件）；下一场新回放将正常记录。";}
        catch(Exception ex){if(!IsDisposed)status.Text="未能清空："+ex.Message;}
        finally{if(!IsDisposed){clear.Enabled=refresh.Enabled=true;if(BusyChanged!=null)BusyChanged(false);}}
    }
    void Populate()
    {
        var run=Selected; if(run==null) return;
        var s=run.Bundle.Tables["回放汇总.csv"];
        summary.Text="已实现净盈亏 "+Format(s.Get(0,"本面板已实现净盈亏"))+"  | 净值收益 "+Percent(s.Get(0,"含浮动账户收益率百分比"))+"%  | 胜率 "+Format(s.Get(0,"胜率百分比"))+"%  | 已平 "+s.Get(0,"已平持仓数")+" 笔，未全平 "+s.Get(0,"未全平持仓数")+" 笔"+
            "\r\n最大回撤金额 "+Format(s.Get(0,"最大净值回撤金额逐Tick"))+" | 最大回撤比例 "+Percent(s.Get(0,"最大净值回撤百分比逐Tick"))+"% | 最新历史时间 "+s.Get(0,"最新历史时间")+" | "+s.Get(0,"记录状态");
        foreach(var pair in grids)
        {
            var g=pair.Value;CsvTable t;if(!run.Bundle.Tables.TryGetValue(pair.Key,out t))t=OptionalDaily(run,pair.Key);g.Rows.Clear(); g.Columns.Clear();
            if(pair.Key=="回放汇总.csv")
            {
                g.Columns.Add("metric","项目"); g.Columns.Add("value","数值");
                for(int i=0;i<t.Header.Length;i++) g.Rows.Add(t.Header[i],t.Rows[0][i]);
            }
            else
            {
                foreach(string h in t.Header) { var col=new DataGridViewTextBoxColumn{Name=h,HeaderText=h,SortMode=DataGridViewColumnSortMode.Automatic}; g.Columns.Add(col); }
                foreach(var row in t.Rows) g.Rows.Add(row.Cast<object>().ToArray());
                foreach(string hidden in new[]{"快照号","回放ID","运行ID","品种","周期"}) if(g.Columns.Contains(hidden)) g.Columns[hidden].Visible=false;
                if(pair.Key=="交易结果.csv")
                {
                    string[] priority={"持仓ID","方向","状态","开仓手数","已实现净盈亏","未平仓浮动毛盈亏","开仓均价","平仓均价","开仓时间","最后平仓时间"};
                    int display=0; foreach(string key in priority) if(g.Columns.Contains(key)) g.Columns[key].DisplayIndex=display++;
                }
            }
        }
    }
    static string Format(string raw) { decimal value; return Decimal.TryParse(raw,NumberStyles.Number,CultureInfo.InvariantCulture,out value)?value.ToString("0.00",CultureInfo.InvariantCulture):"—"; }
    static CsvTable OptionalDaily(RecordRun run,string name)
    {
        if(!new[]{"日内观测.csv","收工现场.csv"}.Contains(name))throw new ArgumentException("未知附加记录。");
        string file=Path.Combine(run.Folder,name);
        if(!File.Exists(file))return CsvTable.Parse("说明\r\n本场尚无该项记录；不补造旧场数据。\r\n");
        try
        {
            var table=CsvTable.Parse(File.ReadAllText(file,new UTF8Encoding(false,true)));string boot=run.Bundle.Tables["回放汇总.csv"].Get(0,"运行ID");
            for(int i=0;i<table.Rows.Count;i++)if(table.Get(i,"回放ID")!=run.Session || table.Get(i,"运行ID")!=boot)throw new FormatException("附加记录与本场运行不匹配。");
            return table;
        }
        catch(Exception ex){var table=new CsvTable{Header=new[]{"资料异常"}};table.Rows.Add(new[]{"附加资料未读取："+ex.Message+"；原始成交仍保留。"});return table;}
    }
    static string Percent(string raw) { decimal value; return Decimal.TryParse(raw,NumberStyles.Number,CultureInfo.InvariantCulture,out value)?value.ToString("0.####",CultureInfo.InvariantCulture):"—"; }
    void OpenFolder()
    {
        try { string dir=Selected==null?RecordStore.ExportRoot:Selected.Folder; Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo(dir){UseShellExecute=true}); }
        catch(Exception ex) { status.Text=ex.Message; }
    }
    void OpenExecutionReview()
    {
        if(Selected==null){status.Text="请先选择一场有持仓记录的回放。";return;}
        try{using(var form=new TrainingReviewForm(Selected))form.ShowDialog(TopLevelControl??this);status.Text="执行复盘独立保存；原始成交、盈亏和事前计划未改写。";}
        catch(Exception ex){status.Text="无法打开执行复盘："+ex.Message;}
    }
    void OpenTrainingGroups()
    {
        try{using(var form=new TrainingGroupForm(runs.Items.Cast<RecordRun>()))form.ShowDialog(TopLevelControl??this);}
        catch(Exception ex){status.Text="无法打开训练分组："+ex.Message;}
    }
    void Export()
    {
        if(Selected==null) { status.Text="请先选择一场有记录的回放。"; return; }
        using(var dialog=new FolderBrowserDialog{Description="选择另存CSV的文件夹（将保存本场回放的CSV）"})
            if(dialog.ShowDialog(TopLevelControl??this)==DialogResult.OK)
                try { RecordStore.Export(Selected,dialog.SelectedPath); status.Text="已另存CSV："+dialog.SelectedPath; }
                catch(Exception ex) { status.Text="另存失败："+ex.Message; }
    }
    public void SavePreview(string path,RecordRun run)
    {
        preview=true;
        Show(); Application.DoEvents();
        runs.Items.Clear(); runs.Items.Add(run); runs.SelectedIndex=0;
        summary.Text="【功能测试样例，不是你的交易成绩】\r\n"+summary.Text.Split(new[]{"\r\n"},StringSplitOptions.None)[0];
        Application.DoEvents();
        using(var b=new Bitmap(Width,Height)) { DrawToBitmap(b,new Rectangle(0,0,Width,Height)); b.Save(path); }
    }
}

public static class RecordSelfTest
{
    public static int Run()
    {
        try
        {
            var escaped=CsvTable.Parse("a,b,c\r\n\"逗号,引号\"\"\",\"换行\r\n内容\",\"-12.50\"\r\n");
            if(escaped.Rows.Count!=1 || escaped.Rows[0][0]!="逗号,引号\"" || escaped.Rows[0][1]!="换行\r\n内容" || escaped.Number(0,"c")!=-12.50m) throw new Exception("CSV转义或负数解析失败");
            bool rejected=false; try { CsvTable.Parse("a,b\r\n\"unfinished"); } catch(FormatException) { rejected=true; } if(!rejected) throw new Exception("未完成CSV未拦截");
            string session=File.ReadAllText(Path.Combine(ReplayFiles.Home,"recorder-smoke-session.txt")).Trim();
            string root=Path.Combine(DesktopSessionFiles.Folder(session),"records");
            string folder=Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).First();
            var bundle=RecordStore.ReadBundle(folder);
            RecordRun run=new RecordRun{Folder=folder,Session=session,Bundle=bundle};
            var trades=bundle.Tables["交易结果.csv"]; var deals=bundle.Tables["成交明细.csv"]; var summary=bundle.Tables["回放汇总.csv"];
            decimal cash=0;
            for(int i=0;i<deals.Rows.Count;i++) cash+=deals.Number(i,"毛盈亏")+deals.Number(i,"佣金")+deals.Number(i,"已结算隔夜费")+deals.Number(i,"其他手续费");
            if(Math.Abs(cash-summary.Number(0,"本面板已实现净盈亏"))>0.0000001m) throw new Exception("成交费用与汇总净盈亏不一致");
            decimal folded=0; for(int i=0;i<trades.Rows.Count;i++) folded+=trades.Number(i,"已实现净盈亏");
            if(Math.Abs(cash-folded)>0.0000001m) throw new Exception("持仓与成交净盈亏不一致");
            string testDir=Path.Combine(ReplayFiles.Home,"record-test-export"); RecordStore.Export(run,testDir);
            var exported=RecordStore.ReadBundle(testDir);
            foreach(string name in RecordStore.Names) if(!exported.Bytes[name].SequenceEqual(bundle.Bytes[name])) throw new Exception("导出改变了CSV内容");
            // Reject a mixed generation rather than publish a partial report.
            File.WriteAllText(Path.Combine(testDir,"snapshot.txt"),"999999999",new UTF8Encoding(true));
            rejected=false; try { RecordStore.ReadBundle(testDir); } catch(FormatException) { rejected=true; } if(!rejected) throw new Exception("混合快照未拦截");
            RecordStore.Export(run,testDir);
            using(var form=new RecordBrowserForm("")) form.SavePreview(Path.Combine(ReplayFiles.Home,"records-preview.png"),run);
            File.WriteAllText(Path.Combine(ReplayFiles.Home,"record-self-test.txt"),"PASS: RFC-style CSV comma/quote/multiline escaping; signed fees; incomplete and mixed snapshot rejection; actual MQL UTF-8 BOM CSV parsing; position/deal/summary net profit reconciliation; byte-identical export; record browser rendering.\r\n");
            return 0;
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(ReplayFiles.Home,"record-self-test-error.txt"),ex.ToString()); return 99; }
    }
}
