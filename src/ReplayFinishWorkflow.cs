using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public static class FinishNativeBridge
{
    static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
    public static DesktopSnapshot Read(string session,string source=null)
    {
        using(var stream=new FileStream(Path.Combine(source??DesktopSessionFiles.Folder(session),"state.txt"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Encoding.Unicode))return DesktopSnapshot.Parse(reader.ReadToEnd().TrimEnd('\r','\n'),session);
    }
    public static string IntentPath(string source,string boot)
    {ReplayDaybook.FilePath("00000000000000000000000000000000",boot,source);return Path.Combine(source,"finish-intent-"+boot+".json");}
    public static bool HasIntent(string source,DesktopSnapshot state)
    {return state!=null && (state.Get("finish_sealed")=="1" || File.Exists(IntentPath(source,state.Get("boot"))));}
    static void Match(DesktopSnapshot state,string boot)
    {if(state.Get("boot")!=boot)throw new InvalidOperationException("测试器已切换场次，未对其他场次执行收工或退出。");}
    static long CommandSequence(string source,DesktopSnapshot state)
    {
        string path=Path.Combine(source,"command.txt");if(!File.Exists(path))return 0;
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream,Encoding.Unicode))
        {string raw=reader.ReadToEnd();string sid=Value(raw,"session"),boot=Value(raw,"boot");long seq;if(sid==state.Get("session") && boot==state.Get("boot") && Value(raw,"complete")=="1" && Int64.TryParse(Value(raw,"seq"),out seq))return seq;return 0;}
    }
    static string Value(string raw,string key){foreach(var line in raw.Split('\n'))if(line.StartsWith(key+"=",StringComparison.Ordinal))return line.Substring(key.Length+1).TrimEnd('\r');return "";}
    static async Task<DesktopSnapshot> Available(string session,string boot,string source)
    {
        var clock=Stopwatch.StartNew();while(true)
        {var state=Read(session,source);Match(state,boot);if(state.Get("phase")=="ended")return state;if(state.Fresh || state.Get("phase")=="ending" && unchecked((uint)Environment.TickCount-(uint)state.Int("stamp"))<1500){if(CommandSequence(source,state)<=state.Int("ack"))return state;}if(clock.ElapsedMilliseconds>4500)throw new IOException("上一操作或测试器连接尚未确认，请保持当前窗口并重试。");await Task.Delay(100);}
    }
    static long Send(string action,DesktopSnapshot state,string source)
    {
        if(action!="finish_seal" && action!="finish_end")throw new ArgumentException("未知收工操作。");
        long highest=TrainingStore.NextCommandSequence(state.Get("session"),state.Get("boot"),state.Int("ack"),source);
        long sequence=Math.Max(highest,checked(CommandSequence(source,state)+1));
        DesktopSessionFiles.AtomicText(Path.Combine(source,"command.txt"),"session="+state.Get("session")+"\r\nboot="+state.Get("boot")+"\r\nseq="+sequence+"\r\nstamp="+state.Get("stamp")+"\r\naction="+action+"\r\nlots=0\r\nsl=0\r\ntp=0\r\ncomplete=1\r\n");return sequence;
    }
    public static async Task<FinishArchive> SealAsync(DesktopSessionInfo info,string source,Action<string> progress=null,string archiveRoot=null,string expectedBoot=null)
    {
        await Gate.WaitAsync();try
        {
            var state=Read(info.Id,source);string boot=state.Get("boot");
            if(expectedBoot!=null)Match(state,expectedBoot);
            if(state.Get("phase")=="ended")
            {if(progress!=null)progress("保存测试结束现场与完整计划…");FinishOwnedWindows.Capture(info,state,source);var ended=FinishStore.CreateEnded(info,state,source,archiveRoot);FinishStore.SaveFinal(ended,state,Path.Combine(source,"records",boot));return ended;}
            if(state.Get("finish_protocol")!="1")throw new InvalidOperationException("本场使用旧版EA，尚不支持完整收工。请从1.2.6入口启动新场次；原有记录保留。");
            TrainingStore.AtomicJson(IntentPath(source,boot),new{session=info.Id,boot=boot,confirmed_utc=DateTime.UtcNow});
            if(progress!=null)progress("等待测试器确认暂停并锁定本场交易…");
            state=await Available(info.Id,boot,source);long sequence=0;
            if(state.Get("finish_sealed")!="1" || state.Get("finish_snapshot_ready")!="1" || state.Get("record_error")!="")sequence=Send("finish_seal",state,source);
            var clock=Stopwatch.StartNew();
            while(true)
            {
                state=Read(info.Id,source);Match(state,boot);
                if(state.Get("phase")=="ended")throw new IOException("测试器在收工确认前结束，不能把最终结果标为原收工现场。请重新打开收工入口保存结束记录。");
                if(state.Fresh && state.Get("training_mode")=="paused" && state.Get("finish_sealed")=="1" && state.Get("finish_snapshot_ready")=="1" && state.Int("ack")>=sequence && state.Get("record_error")=="")break;
                if(clock.ElapsedMilliseconds>7000)throw new IOException(state.Get("record_error")!=""?"原生记录未保存："+state.Get("record_error"):"收工暂停尚未确认，请重试；不会恢复交易或自动平仓。");await Task.Delay(100);
            }
            if(progress!=null)progress("保存账户、订单、完整计划与当时设置，并校验档案…");
            FinishOwnedWindows.Capture(info,state,source);return FinishStore.Create(info,state,source,archiveRoot);
        }
        finally{Gate.Release();}
    }
    public static async Task<string> EndAsync(FinishArchive archive,string source=null,Func<FinishArchive,string> closeWindows=null)
    {
        await Gate.WaitAsync();try
        {
            source=source??DesktopSessionFiles.Folder(archive.Snapshot.Session);FinishStore.MarkCloseRequested(archive);
            if(!archive.Progress.FinalSaved)
            {
                var state=Read(archive.Snapshot.Session,source);Match(state,archive.Snapshot.Boot);
                if(state.Get("phase")!="ended")
                {
                    if(state.Get("finish_sealed")!="1")throw new IOException("本场收工锁定尚未确认，未结束测试器。");
                    state=await Available(archive.Snapshot.Session,archive.Snapshot.Boot,source);
                    if(state.Get("phase")!="ended")
                    {
                        Send("finish_end",state,source);var clock=Stopwatch.StartNew();
                        while(true)
                        {state=Read(archive.Snapshot.Session,source);Match(state,archive.Snapshot.Boot);if(state.Get("phase")=="ended")break;if(clock.ElapsedMilliseconds>10000)throw new IOException("测试器尚未正常结束，现场与复盘已保留，请重试退出。"+(state.Get("finish_end_error")==""?"":" "+state.Get("finish_end_error")));await Task.Delay(100);}
                    }
                }
                FinishStore.SaveFinal(archive,state,Path.Combine(source,"records",archive.Snapshot.Boot));
            }
            string error=await Task.Run(()=>(closeWindows??(a=>FinishOwnedWindows.Close(a,source)))(archive));
            if(!String.IsNullOrEmpty(error))throw new IOException(error);
            FinishStore.SaveError(archive,"");return "";
        }
        catch(Exception ex){try{FinishStore.SaveError(archive,ex.Message);}catch(Exception write){return ex.Message+"；退出进度未保存："+write.Message;}return ex.Message;}
        finally{Gate.Release();}
    }
}

public sealed class FinishOwner
{public int Pid {get;set;}public long StartedUtcTicks {get;set;}public string Terminal {get;set;}public string Session {get;set;}public string Profile {get;set;}}
public static class FinishOwnedWindows
{
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessEntry
    {public uint Size,Usage,Pid;public UIntPtr DefaultHeap;public uint ModuleId,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string File;}
    [DllImport("kernel32.dll")]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    public static void RecordLaunch(string session,Process process,string terminal,string profile)
    {if(process==null)throw new IOException("MT5启动未返回进程，未记录本场窗口所有权。");TrainingStore.AtomicJson(Path.Combine(DesktopSessionFiles.Folder(session),"finish-owner.json"),new FinishOwner{Pid=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks,Terminal=Path.GetFullPath(terminal),Session=session,Profile=profile});}
    static string Terminal(FinishSnapshot snapshot)
    {var options=Json.Deserialize<ReplayOptions>(snapshot.Training.Profile.SettingsJson);return Path.GetFullPath(options.Terminal);}
    static bool ChildOf(int pid,int parent)
    {
        IntPtr handle=CreateToolhelp32Snapshot(2,0);if(handle==new IntPtr(-1))throw new IOException("无法核对测试器进程归属。");
        try{var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};if(Process32First(handle,ref entry))do{if(entry.Pid==(uint)pid)return entry.Parent==(uint)parent;}while(Process32Next(handle,ref entry));return false;}finally{CloseHandle(handle);}
    }
    public static void Capture(DesktopSessionInfo info,DesktopSnapshot state,string source)
    {
        state.Values["finish_session_created_utc"]=info.CreatedUtc.ToString("O",CultureInfo.InvariantCulture);
        var training=TrainingStore.Load(info.Id,state.Get("boot"),source);var options=Json.Deserialize<ReplayOptions>(training.Profile.SettingsJson);string expected=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Terminal)),"metatester64.exe");
        long handle=state.Int("finish_chart_hwnd");IntPtr root=handle==0?IntPtr.Zero:GetAncestor(new IntPtr(handle),2);
        if(root==IntPtr.Zero || !IsWindow(root))
        {
            var matches=new System.Collections.Generic.List<IntPtr>();
            ChartNative.EnumWindows((window,parameter)=>
            {
                if(!ChartNative.MatchesTesterTitle(ChartNative.Title(window),state) || !ChartNative.StartedForSession(window,info.CreatedUtc))return true;
                uint processId;GetWindowThreadProcessId(window,out processId);
                try{using(var candidate=Process.GetProcessById((int)processId)){if(String.Equals(candidate.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))matches.Add(window);}}catch(ArgumentException){}
                return true;
            },IntPtr.Zero);
            if(matches.Count>1 || matches.Count==0 && File.Exists(Path.Combine(source,"finish-owner.json")))throw new IOException("无法唯一核对本场可视测试器窗口，请保留本场窗口并重试保存。");
            if(matches.Count==0)return;root=matches[0];
            state.Values["finish_visual_capture"]="unique_expert_symbol_period_dates_path_start";
        }
        uint pid;GetWindowThreadProcessId(root,out pid);if(pid==0 || !IsWindow(root))throw new IOException("本场可视测试器窗口已变化，请重试保存。");
        using(var process=Process.GetProcessById((int)pid))
        {
            if(!String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)||!ChartNative.StartedForSession(root,info.CreatedUtc))throw new IOException("无法核对本场可视测试器窗口，收工档案尚未确认完整。");
            string ownerFile=Path.Combine(source,"finish-owner.json");
            if(File.Exists(ownerFile))
            {
                var owner=Json.Deserialize<FinishOwner>(File.ReadAllText(ownerFile));
                using(var terminal=Process.GetProcessById(owner.Pid))
                {if(owner.Session!=info.Id||terminal.StartTime.ToUniversalTime().Ticks!=owner.StartedUtcTicks||!String.Equals(terminal.MainModule.FileName,Path.GetFullPath(options.Terminal),StringComparison.OrdinalIgnoreCase)||!ChildOf((int)pid,owner.Pid))throw new IOException("可视测试器不属于本场启动的MT5，未关闭或封存其他场次。");}
                state.Values["finish_visual_parent_pid"]=owner.Pid.ToString(CultureInfo.InvariantCulture);
            }
            state.Values["finish_visual_root"]=root.ToInt64().ToString(CultureInfo.InvariantCulture);state.Values["finish_visual_pid"]=pid.ToString(CultureInfo.InvariantCulture);state.Values["finish_visual_start"]=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        }
    }
    public static bool IsLatestKnownScene(FinishArchive archive,string source,string common=null)
    {
        DateTime created;string value;
        if(!archive.Snapshot.Values.TryGetValue("finish_session_created_utc",out value) || !DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out created))return false;
        string parent=common??DesktopSessionFiles.Common;
        if(!Directory.Exists(parent))return false;
        foreach(string folder in Directory.GetDirectories(parent))
        {
            string id=Path.GetFileName(folder);if(!DesktopSessionFiles.ValidId(id) || id==archive.Snapshot.Session)continue;
            string file=Path.Combine(folder,"session.json");if(!File.Exists(file))continue;
            var other=Json.Deserialize<DesktopSessionInfo>(File.ReadAllText(file));
            if(other==null || other.Id!=id)throw new IOException("无法核对其他回放场次，未关闭窗口。");
            if(other.CreatedUtc>created)return false;
        }
        return true;
    }
    public static string Close(FinishArchive archive,string source)
    {
        // A historical archive may be opened after another run: never close that run's windows.
        DesktopSnapshot current;try{current=FinishNativeBridge.Read(archive.Snapshot.Session,source);}catch(IOException){return "";}
        if(current.Get("boot")!=archive.Snapshot.Boot)return "";
        if(!IsLatestKnownScene(archive,source))return "";
        string terminal=Terminal(archive.Snapshot),expected=Path.Combine(Path.GetDirectoryName(terminal),"metatester64.exe");string value;long rootId,pid,started;
        var values=archive.Snapshot.Values;
        if(values.TryGetValue("finish_visual_root",out value)&&Int64.TryParse(value,out rootId)&&values.TryGetValue("finish_visual_pid",out value)&&Int64.TryParse(value,out pid)&&values.TryGetValue("finish_visual_start",out value)&&Int64.TryParse(value,out started))
        {
            var hwnd=new IntPtr(rootId);if(IsWindow(hwnd))
            {
                uint actual;GetWindowThreadProcessId(hwnd,out actual);if(actual!=(uint)pid)return "本场可视窗口标识已变化，未关闭其他窗口。";
                try{using(var process=Process.GetProcessById((int)pid)){if(process.StartTime.ToUniversalTime().Ticks!=started||!String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))return "本场测试器进程已变化，未关闭其他窗口。";if(!PostMessage(hwnd,0x0010,IntPtr.Zero,IntPtr.Zero))return "本场测试器窗口尚未正常关闭，请重试退出。";var wait=Stopwatch.StartNew();while(IsWindow(hwnd)&&wait.ElapsedMilliseconds<4500)Thread.Sleep(50);if(IsWindow(hwnd))return "现场、复盘与最终结果已保存；本场可视测试器尚未正常关闭，请处理提示后重试退出。";}}
                catch(ArgumentException){}
            }
        }
        string file=Path.Combine(source,"finish-owner.json");if(!File.Exists(file))return "";
        var owner=Json.Deserialize<FinishOwner>(File.ReadAllText(file));if(owner==null||owner.Session!=archive.Snapshot.Session||!String.Equals(Path.GetFullPath(owner.Terminal),terminal,StringComparison.OrdinalIgnoreCase))return "本场MT5进程所有权无法核实，未关闭其他MT5窗口。";
        try
        {using(var process=Process.GetProcessById(owner.Pid)){if(process.HasExited)return "";if(process.StartTime.ToUniversalTime().Ticks!=owner.StartedUtcTicks||!String.Equals(process.MainModule.FileName,terminal,StringComparison.OrdinalIgnoreCase))return "";if(!process.CloseMainWindow() || !process.WaitForExit(4500))return "现场、复盘与最终结果已保存；本场MT5尚未正常关闭，请处理MT5提示后重试退出。";}}
        catch(ArgumentException){}return "";
    }
}

public sealed class FinishSaveForm:ReplayBufferedForm
{
    readonly Func<Action<string>,Task<FinishArchive>> save;readonly Label feedback=new ReplayStableLabel();readonly PaperActionButton confirm=new PaperActionButton{Text="确认收工",Primary=true};bool attempted,busy;
    public FinishArchive Archive {get;private set;}
    public FinishSaveForm(DesktopSnapshot state,Func<Action<string>,Task<FinishArchive>> saveAction)
    {
        save=saveAction;Text="收工并保存现场";Font=new Font("Microsoft YaHei UI",10);BackColor=ReplayPaper.Background;AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(600,400);MinimumSize=new Size(480,370);StartPosition=FormStartPosition.CenterParent;
        var root=new ReplayBufferedTable{Dock=DockStyle.Fill,Padding=new Padding(26),ColumnCount=1,RowCount=5};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int h in new[]{42,44,104})root.RowStyles.Add(new RowStyle(SizeType.Absolute,h));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));Controls.Add(root);
        root.Controls.Add(ReplayPaper.Label("收工并保存现场",17,ReplayPaper.Ink),0,0);root.Controls.Add(ReplayPaper.Label("确认后结束本场交易，保存现场并进入复盘。",10),0,1);
        var card=new PaperCard{Dock=DockStyle.Fill,Margin=Padding.Empty};card.Controls.Add(ReplayPaper.Label("历史 "+state.Now.ToString("yyyy.MM.dd HH:mm:ss")+"\r\n净值 "+state.Get("equity")+" USD · 持仓 "+state.Get("count")+" 笔 · 挂单 "+state.Get("pending_count")+" 笔",11,ReplayPaper.Ink));root.Controls.Add(card,0,2);
        feedback.Dock=DockStyle.Fill;feedback.Margin=new Padding(0,16,0,8);feedback.ForeColor=ReplayPaper.Muted;feedback.Text="将保存账户、订单、完整计划与当时设置。测试结束结果随后单独保留。";root.Controls.Add(feedback,0,3);
        var footer=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));var cancel=new PaperActionButton{Text="返回交易",Quiet=true,Dock=DockStyle.Fill};cancel.Click+=(s,e)=>{if(!attempted){DialogResult=DialogResult.Cancel;Close();}};footer.Controls.Add(cancel,0,0);confirm.Dock=DockStyle.Fill;footer.Controls.Add(confirm,2,0);root.Controls.Add(footer,0,4);
        confirm.Click+=async(s,e)=>{if(busy)return;attempted=busy=true;confirm.Enabled=cancel.Enabled=false;feedback.ForeColor=ReplayPaper.Muted;try{Archive=await save(message=>{feedback.Text=message;});DialogResult=DialogResult.OK;Close();}catch(Exception ex){feedback.ForeColor=GraphiteTheme.SellText;feedback.Text="现场尚未完整保存："+ex.Message;confirm.Text="重试保存";attempted=false;cancel.Text="稍后重试";}finally{busy=false;if(!IsDisposed)confirm.Enabled=cancel.Enabled=true;}};
        FormClosing+=(s,e)=>{if(Archive==null&&busy)e.Cancel=true;};
        ReplayWindowLayout.Scale(this);Shown+=(s,e)=>ReplayWindowLayout.Fit(this);
    }
}

public sealed partial class DesktopReplayForm
{
    FinishArchive finishArchive;bool finishDialogOpen,finishExitAccepted;
    public event Action<string,string> FinishExitRequested;
    public string BootId {get{return snapshot==null?"":snapshot.Get("boot");}}
    bool FinishLocked {get{return finishRequested || finishArchive!=null && snapshot!=null && finishArchive.Snapshot.Session==session.Id && finishArchive.Snapshot.Boot==snapshot.Get("boot") || snapshot!=null && FinishNativeBridge.HasIntent(folder,snapshot);}}
    async void BeginFullFinish()
    {
        if(finishDialogOpen)return;RefreshState();if(snapshot==null)return;string expectedBoot=snapshot.Get("boot");finishDialogOpen=true;finishExitAccepted=false;
        try
        {
            using(var save=new FinishSaveForm(snapshot,async progress=>{finishRequested=true;RefreshState();return await FinishNativeBridge.SealAsync(session,folder,progress,null,expectedBoot);})){if(save.ShowDialog(this)!=DialogResult.OK)return;finishArchive=save.Archive;}
            OpenFinishReview(finishArchive);
        }
        finally{finishRequested=false;finishDialogOpen=false;if(!IsDisposed)RefreshState();}
        await Task.FromResult(0);
    }
    void OpenFinishReview(FinishArchive archive)
    {
        using(var review=new FinishReviewForm(archive))
        {review.ExitRequested=async completed=>{string error=await FinishNativeBridge.EndAsync(archive,folder);if(error=="")finishExitAccepted=true;return error;};review.ShowDialog(this);}
        if(finishExitAccepted && FinishExitRequested!=null)BeginInvoke((Action)(()=>FinishExitRequested(archive.Snapshot.Session,archive.Snapshot.Boot)));
    }
    void RefreshFinishLock()
    {
        if(snapshot==null)return;if(finishArchive!=null && (finishArchive.Snapshot.Session!=session.Id || finishArchive.Snapshot.Boot!=snapshot.Get("boot"))){finishArchive=null;finishExitAccepted=false;}bool locked=FinishLocked;
        if(locked){foreach(var button in actions.Values)button.Enabled=false;trainingPlanShortcut.Enabled=trainingPauseShortcut.Enabled=false;if(playbackControl!=null)playbackControl.Enabled=false;if(chart!=null)chart.Update(snapshot,false,false,true);}
        else if(playbackControl!=null)playbackControl.Enabled=true;
        if(finishDayButton!=null){finishDayButton.Text=locked?"复盘":"收工";finishDayButton.Enabled=!finishDialogOpen && (snapshot.Fresh && snapshot.Get("finish_protocol")=="1" || snapshot.Get("phase")=="ended");}
    }
}
