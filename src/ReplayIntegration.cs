using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows.Forms;

public static class ReplayInstanceIdentity
{
    public static string Key(string role)
    {
        string identity="CNManualReplayWorkspace/1|"+Environment.UserName+"|"+
            System.Diagnostics.Process.GetCurrentProcess().SessionId+"|"+DesktopSessionFiles.Common.ToUpperInvariant();
        using(var hash=SHA256.Create())return "CNReplay_"+role+"_"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").Substring(0,24);
    }
}

public static class ReplayLauncherHost
{
    public static int Run(bool records,string session){return RunAction(records?"records":"show",session);}
    public static int RunPanel(string session)
    {try{return RunAction("panel",session);}catch(Exception ex){MessageBox.Show(ex.Message,"历史回放操作浮窗");return 1;}}
    public static int RunDemo(){return RunAction("demo","");}
    public static int RunAction(string action,string session,string instanceKey=null)
    {
        string key=instanceKey??ReplayInstanceIdentity.Key("Workspace");
        using(var mutex=new Mutex(false,@"Local\"+key))
        {
            for(int attempt=0;attempt<3;attempt++)
            {
                bool acquired=false;
                try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                if(acquired)
                {
                    try{using(var context=new ReplayWorkspaceContext(action,session,key))Application.Run(context);return 0;}
                    finally{mutex.ReleaseMutex();}
                }
                try{Request(key,action,session);return 0;}
                catch(IOException){if(attempt==2)break;}
                catch(TimeoutException){if(attempt==2)break;}
                Thread.Sleep(100);
            }
        }
        MessageBox.Show("现有面板暂未响应，请稍后再试。没有创建重复窗口。","手动回放");return 1;
    }
    public static void Request(string key,string action,string session)
    {
        using(var pipe=new NamedPipeClientStream(".",key,PipeDirection.InOut,PipeOptions.Asynchronous))
        {
            pipe.Connect(4000);
            using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true))
            using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,1024,true))
            {
                writer.AutoFlush=true;writer.WriteLine(action+"|"+(DesktopSessionFiles.ValidId(session)?session:""));
                var reply=reader.ReadLineAsync();
                if(Task.WaitAny(reply,Task.Delay(6000))!=0)throw new TimeoutException("面板切换超时");
                string response=reply.GetAwaiter().GetResult();
                if(response!="OK")throw new IOException(response??"现有面板正在退出");
            }
        }
    }
}

public sealed class ReplayWorkspaceContext : ApplicationContext
{
    readonly ReplayForm launcher;
    readonly Func<string,DesktopSessionInfo> readSession;
    readonly Func<DesktopSessionInfo,DesktopReplayForm> createPanel;
    readonly ReplayWorkspaceListener listener;
    DesktopReplayForm panel;
    ChartDemoForm demo;
    DesktopSessionInfo latest;
    bool stopping,switching;
    public ReplayForm Launcher {get{return launcher;}}
    public DesktopReplayForm Panel {get{return panel;}}
    public ReplayWorkspaceContext(string action,string session,string key,
        Func<string,DesktopSessionInfo> sessionReader=null,Func<DesktopSessionInfo,DesktopReplayForm> panelFactory=null)
    {
        readSession=sessionReader??DesktopSessionFiles.Read;
        createPanel=panelFactory??(info=>new DesktopReplayForm(info));
        launcher=new ReplayForm();
        var handle=launcher.Handle; // Stable dispatch handle, including when the launcher is hidden.
        launcher.PanelRequested+=ShowPanel;launcher.FinishExitRequested+=FinishWorkspaceExit;
        launcher.FormClosing+=(s,e)=>
        {
            if(stopping || e.CloseReason!=CloseReason.UserClosing)return;
            if(launcher.Launching){e.Cancel=true;return;}
            if(panel!=null || demo!=null){e.Cancel=true;launcher.Hide();}
        };
        launcher.FormClosed+=(s,e)=>{if(!stopping)ExitThread();};
        listener=new ReplayWorkspaceListener(key,launcher,Dispatch);listener.Start();
        Dispatch(action,session);
    }
    public void Dispatch(string action,string session)
    {
        if(stopping)return;
        if(action=="panel"){ShowPanel(session);return;}
        if(action=="demo")
        {
            if(demo==null || demo.IsDisposed)
            {
                demo=new ChartDemoForm();demo.FormClosed+=(s,e)=>{demo=null;ExitIfHidden();};demo.Show();
            }
            Raise(demo);return;
        }
        Raise(launcher);
        if(action=="records")Observe(launcher.ShowRecordsAsync(session));
    }
    static async void Observe(Task task){try{await task;}catch(ObjectDisposedException){}catch(InvalidOperationException){}}
    public void ShowPanel(string id)
    {
        if(stopping)return;
        if(String.IsNullOrEmpty(id))id=DesktopSessionFiles.LastId();
        var info=readSession(id);
        // A delayed request from a preceding run must never replace the newer session.
        if(latest!=null && info.CreatedUtc<latest.CreatedUtc)info=latest;
        if(panel!=null && !panel.IsDisposed && panel.SessionId==info.Id){Raise(panel);return;}
        latest=info;switching=true;
        System.Drawing.Rectangle? previousBounds=null;bool pinned=true;
        try
        {
            if(panel!=null)
            {
                previousBounds=panel.WindowState==FormWindowState.Normal?panel.Bounds:panel.RestoreBounds;
                pinned=panel.TopMost;var previous=panel;panel=null;
                previous.Close();previous.Dispose(); // Releases timer, mouse hook and chart overlay before rebinding.
            }
            var next=createPanel(info);panel=next;
            next.RecordsRequested+=session=>Dispatch("records",session);next.FinishExitRequested+=FinishWorkspaceExit;
            next.FormClosed+=(s,e)=>{if(Object.ReferenceEquals(panel,next)){panel=null;ExitIfHidden();}};
            if(previousBounds.HasValue)next.RestorePlacement(previousBounds.Value,pinned);
            next.Show();Raise(next);
        }
        finally{switching=false;}
    }
    void FinishWorkspaceExit(string session,string boot)
    {
        if(launcher.Launching)return;
        if(panel!=null)
        {
            if(panel.SessionId!=session)return;
            try{if(FinishNativeBridge.Read(session).Get("boot")!=boot)return;}catch(IOException){return;}
            if(panel.BootId!=boot)return;
        }
        if(demo!=null){demo.Close();}if(panel!=null){panel.Close();}launcher.Close();ExitThread();
    }
    void ExitIfHidden(){if(!stopping && !switching && panel==null && demo==null && !launcher.Visible)ExitThread();}
    static void Raise(Form form)
    {
        if(form.WindowState==FormWindowState.Minimized)form.WindowState=FormWindowState.Normal;
        form.Show();form.BringToFront();form.Activate();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && !stopping)
        {
            stopping=true;listener.Dispose();
            if(panel!=null){var closing=panel;panel=null;closing.Close();closing.Dispose();}
            if(demo!=null){var closing=demo;demo=null;closing.Close();closing.Dispose();}
            launcher.Dispose();
        }
        base.Dispose(disposing);
    }
}

public sealed class ReplayWorkspaceListener : IDisposable
{
    readonly string key;readonly Control dispatcher;readonly Action<string,string> dispatch;
    readonly object gate=new object();volatile bool stopped;NamedPipeServerStream waiting;
    public ReplayWorkspaceListener(string key,Control dispatcher,Action<string,string> dispatch)
    {this.key=key;this.dispatcher=dispatcher;this.dispatch=dispatch;}
    public void Start()
    {
        Task.Run(async()=>
        {
            while(!stopped)
            {
                try
                {
                    using(var pipe=new NamedPipeServerStream(key,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
                    {
                        lock(gate){if(stopped)return;waiting=pipe;}
                        await pipe.WaitForConnectionAsync();
                        using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,1024,true))
                        using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true))
                        {
                            var read=reader.ReadLineAsync();
                            if(await Task.WhenAny(read,Task.Delay(6000))!=read)continue;
                            string message=await read;string[] parts=(message??"").Split('|');
                            string reply="ERROR: 无效的窗口请求";
                            if(message!=null && message.Length<128 && parts.Length==2 && new[]{"show","records","panel","demo"}.Contains(parts[0]) &&
                               (parts[1]=="" || DesktopSessionFiles.ValidId(parts[1])))
                            {
                                var done=new TaskCompletionSource<string>();
                                if(!stopped && !dispatcher.IsDisposed)dispatcher.BeginInvoke((Action)(()=>
                                {
                                    try{dispatch(parts[0],parts[1]);done.TrySetResult("OK");}
                                    catch(Exception ex){done.TrySetResult("ERROR: "+ex.Message);}
                                }));
                                else done.TrySetResult("ERROR: 面板正在退出");
                                reply=await done.Task;
                            }
                            writer.AutoFlush=true;await writer.WriteLineAsync(reply);
                        }
                    }
                }
                catch(IOException){if(stopped)return;}
                catch(ObjectDisposedException){if(stopped)return;}
                catch(InvalidOperationException){if(stopped || dispatcher.IsDisposed)return;}
                finally{lock(gate){waiting=null;}}
            }
        });
    }
    public void Dispose(){stopped=true;lock(gate){if(waiting!=null)waiting.Dispose();}}
}

public static class ReplayWindowLayout
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> scaled=new System.Runtime.CompilerServices.ConditionalWeakTable<Control,object>();
    public static float DpiScale
    {get{using(var graphics=System.Drawing.Graphics.FromHwnd(IntPtr.Zero))return Math.Max(1f,graphics.DpiX/96f);}}
    static System.Drawing.Size Size(System.Drawing.Size value,float factor)
    {return new System.Drawing.Size((int)Math.Round(value.Width*factor),(int)Math.Round(value.Height*factor));}
    static Padding Space(Padding value,float factor)
    {return new Padding((int)Math.Round(value.Left*factor),(int)Math.Round(value.Top*factor),(int)Math.Round(value.Right*factor),(int)Math.Round(value.Bottom*factor));}
    public static void Scale(Form form)
    {
        object value;if(scaled.TryGetValue(form,out value))return;
        try
        {
            string iconFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","manual-replay.ico");
            if(File.Exists(iconFile)){var icon=new System.Drawing.Icon(iconFile);form.Icon=icon;form.FormClosed+=(s,e)=>icon.Dispose();}
        }
        catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
        float factor=DpiScale;var client=form.ClientSize;
        form.SuspendLayout();ScaleControl(form,factor,true);form.ClientSize=Size(client,factor);form.ResumeLayout(true);
    }
    static void ScaleControl(Control control,float factor,bool root=false)
    {
        object value;if(scaled.TryGetValue(control,out value))return;scaled.Add(control,new object());
        control.SuspendLayout();
        control.Padding=Space(control.Padding,factor);control.Margin=Space(control.Margin,factor);
        control.MinimumSize=Size(control.MinimumSize,factor);control.MaximumSize=Size(control.MaximumSize,factor);
        if(!root && control.Dock==DockStyle.None)
            control.Bounds=new System.Drawing.Rectangle((int)Math.Round(control.Left*factor),(int)Math.Round(control.Top*factor),(int)Math.Round(control.Width*factor),(int)Math.Round(control.Height*factor));
        var scroll=control as ScrollableControl;if(scroll!=null)scroll.AutoScrollMinSize=Size(scroll.AutoScrollMinSize,factor);
        var table=control as TableLayoutPanel;
        if(table!=null)
        {
            foreach(ColumnStyle column in table.ColumnStyles)if(column.SizeType==SizeType.Absolute)column.Width*=factor;
            foreach(RowStyle row in table.RowStyles)if(row.SizeType==SizeType.Absolute)row.Height*=factor;
        }
        var grid=control as DataGridView;
        if(grid!=null){grid.RowTemplate.Height=(int)Math.Round(grid.RowTemplate.Height*factor);grid.ColumnHeadersHeight=(int)Math.Round(grid.ColumnHeadersHeight*factor);}
        else if(!(control is NumericUpDown) && !(control is ComboBox))foreach(Control child in control.Controls)ScaleControl(child,factor);
        control.ResumeLayout(true);
    }
    public static void Fit(Form form)
    {
        var area=Screen.FromControl(form).WorkingArea;
        int maxWidth=Math.Max(300,area.Width-16),maxHeight=Math.Max(240,area.Height-16);
        form.MinimumSize=new System.Drawing.Size(Math.Min(form.MinimumSize.Width,maxWidth),Math.Min(form.MinimumSize.Height,maxHeight));
        int width=Math.Min(form.Width,maxWidth),height=Math.Min(form.Height,maxHeight);
        form.Bounds=new System.Drawing.Rectangle(Math.Max(area.Left,Math.Min(form.Left,area.Right-width)),Math.Max(area.Top,Math.Min(form.Top,area.Bottom-height)),width,height);
    }
}

public static class ReplayHistoryCleaner
{
    sealed class Target {public string Root,Boundary;public List<string> Files=new List<string>(),Directories=new List<string>();}
    static string Full(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);}
    static void Plain(string path)
    {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("记录目录包含链接，已停止清理："+path);}
    static void Gather(Target target,string directory)
    {
        foreach(string entry in Directory.GetFileSystemEntries(directory))
        {
            string full=Full(entry);
            if(!full.StartsWith(target.Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("记录路径超出清理范围。");
            Plain(full);
            if(Directory.Exists(full)){Gather(target,full);target.Directories.Add(full);}else target.Files.Add(full);
        }
    }
    static void Add(List<Target> targets,string root,string boundary)
    {
        if(!Directory.Exists(root))return;
        root=Full(root);boundary=Full(boundary);
        Plain(boundary);
        if(!root.StartsWith(boundary+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("记录清理范围无效。");
        if(targets.Any(x=>String.Equals(x.Root,root,StringComparison.OrdinalIgnoreCase)))return;
        for(string cursor=root;!String.Equals(cursor,boundary,StringComparison.OrdinalIgnoreCase);cursor=Path.GetDirectoryName(cursor))
        {if(String.IsNullOrEmpty(cursor))throw new IOException("记录清理路径无效。");Plain(cursor);}
        var target=new Target{Root=root,Boundary=boundary};Gather(target,root);targets.Add(target);
    }
    public static int Clear(string applicationHome,string common,bool visualizerOpen=false)
    {
        if(visualizerOpen)throw new InvalidOperationException("请先结束并关闭MT5回放窗口，再清空记录（暂停也需先结束）。");
        string home=Full(applicationHome);common=Full(common);
        if(Path.GetFileName(common)!="CN_Manual_Replay")throw new IOException("公共记录目录无效。");
        var sessions=new List<string>();var targets=new List<Target>();
        if(Directory.Exists(common))
        {
            Plain(common);
            foreach(string folder in Directory.GetDirectories(common))
            {
                string id=Path.GetFileName(folder);if(!DesktopSessionFiles.ValidId(id))continue;Plain(folder);
                string state=Path.Combine(folder,"state.txt");
                if(File.Exists(state))
                {
                    DesktopSnapshot snapshot=null;
                    try{snapshot=DesktopSnapshot.Parse(File.ReadAllText(state).TrimEnd('\r','\n'),id);}catch(FormatException){}
                    if(snapshot!=null && snapshot.Fresh)throw new InvalidOperationException("回放正在运行，请先结束这场回放，再清空记录。");
                }
                sessions.Add(folder);Add(targets,Path.Combine(folder,"records"),common);
                Add(targets,Path.Combine(folder,"training"),common);
            }
        }
        var homes=new List<string>{home};var parent=Directory.GetParent(home);
        if(parent!=null && parent.Name=="outputs")
            foreach(string folder in Directory.GetDirectories(parent.FullName,"手动回放快速入口*"))
                if(!homes.Any(x=>String.Equals(Full(x),Full(folder),StringComparison.OrdinalIgnoreCase)))homes.Add(folder);
        foreach(string folder in homes)foreach(string name in new[]{"交易记录","图表演练记录"})Add(targets,Path.Combine(folder,name),folder);
        // Preflight every directory before writing markers or deleting any files.
        foreach(string session in sessions)File.WriteAllText(Path.Combine(session,"records-cleared.flag"),DateTime.UtcNow.ToString("O"),Encoding.UTF8);
        int count=0;
        foreach(var target in targets)
        {
            foreach(string file in target.Files){File.Delete(file);count++;}
            foreach(string directory in target.Directories)Directory.Delete(directory,false);
        }
        return count;
    }
}


public sealed class ReplayPracticeCheckpoint
{
    public string Session {get;set;}
    public string Symbol {get;set;}
    public DateTime From {get;set;}
    public DateTime To {get;set;}
}

public sealed class ReplayPracticeSequence
{
    readonly string file;
    readonly Func<ReplayPracticeCheckpoint> legacyReader;
    public ReplayPracticeSequence(string folder,Func<ReplayPracticeCheckpoint> reader=null)
    {file=Path.Combine(folder,"practice-sequence.json");legacyReader=reader??ReadConfirmedLegacy;}
    public static Tuple<DateTime,DateTime> Next(DateTime from,DateTime to)
    {
        from=from.Date;to=to.Date;
        if(to<from)throw new ArgumentException("开始日期不能晚于结束日期。");
        int days=(to-from).Days+1;
        if(to>DateTimePicker.MaximumDateTime.Date.AddDays(-days))throw new ArgumentOutOfRangeException("to","顺延日期超出可选范围，请重新选择日期。");
        DateTime next=to.AddDays(1);
        return Tuple.Create(next,next.AddDays(days-1));
    }
    public static void ValidateHistoricalRange(ReplayOptions options,DateTime today)
    {
        if(options.To.Date>=today.Date)throw new ArgumentException("本段结束日期尚未完整成为历史；请选择今天之前的区间。日期没有顺延。");
    }
    public static void WaitStarted(ReplayOptions options,string session,string folder=null,int timeoutMilliseconds=30000)
    {
        string path=Path.Combine(folder??DesktopSessionFiles.Folder(session),"state.txt");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        do
        {
            try
            {
                if(File.Exists(path))
                {
                    string text;
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var reader=new StreamReader(stream,Encoding.Unicode))text=reader.ReadToEnd();
                    var state=DesktopSnapshot.Parse(text.TrimEnd('\r','\n'),session);
                    if(state.Get("expert")=="CN_Manual_Replay_Chart_Preview" && state.Get("symbol")==options.Symbol &&
                       state.Get("record_dir")!="" && DesktopSnapshot.Epoch.AddSeconds(state.Int("from"))==options.From.Date &&
                       DesktopSnapshot.Epoch.AddSeconds(state.Int("to"))==options.To.Date.AddDays(1))return;
                }
            }
            catch(IOException){}
            catch(UnauthorizedAccessException){}
            catch(FormatException){}
            catch(ArgumentOutOfRangeException){}
            if(clock.ElapsedMilliseconds>=timeoutMilliseconds)break;
            Thread.Sleep(Math.Min(100,timeoutMilliseconds));
        }while(clock.ElapsedMilliseconds<timeoutMilliseconds);
        throw new Exception("尚未确认MT5载入本场行情，日期保持原样。请检查MT5载入情况；入口没有停止回放。");
    }
    public bool Restore(ReplayOptions options)
    {
        try
        {
            var previous=File.Exists(file)?new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ReplayPracticeCheckpoint>(File.ReadAllText(file)):legacyReader();
            if(previous==null || !DesktopSessionFiles.ValidId(previous.Session) || previous.To<previous.From ||
               !String.Equals(previous.Symbol,options.Symbol,StringComparison.OrdinalIgnoreCase) ||
               options.From.Date!=previous.From.Date || options.To.Date!=previous.To.Date)return false;
            var next=Next(previous.From,previous.To);options.From=next.Item1;options.To=next.Item2;return true;
        }
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
        catch(ArgumentException){return false;}
        catch(InvalidOperationException){return false;}
        catch(FormatException){return false;}
    }
    public void Started(ReplayOptions options,string session)
    {
        if(!DesktopSessionFiles.ValidId(session))throw new Exception("回放已启动，但顺延会话编号无效。");
        var data=new ReplayPracticeCheckpoint{Session=session,Symbol=options.Symbol,From=options.From.Date,To=options.To.Date};
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temp=file+".tmp";
        File.WriteAllText(temp,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data),new UTF8Encoding(true));
        if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    static ReplayPracticeCheckpoint ReadConfirmedLegacy()
    {
        // A prepared configuration alone is not evidence of a started replay.
        string path=Path.Combine(ReplayFiles.Home,"last-session.json");
        if(!File.Exists(path))return null;
        var info=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<DesktopSessionInfo>(File.ReadAllText(path));
        if(info==null || !DesktopSessionFiles.ValidId(info.Id) || info.To<=info.From)return null;
        string stateFile=Path.Combine(DesktopSessionFiles.Folder(info.Id),"state.txt");
        if(!File.Exists(stateFile))return null;
        string text;
        using(var stream=new FileStream(stateFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Encoding.Unicode))text=reader.ReadToEnd();
        var state=DesktopSnapshot.Parse(text.TrimEnd('\r','\n'),info.Id);
        if(state.Get("expert")!="CN_Manual_Replay_Chart_Preview" || state.Get("symbol")!=info.Symbol ||
           DesktopSnapshot.Epoch.AddSeconds(state.Int("from"))!=info.From ||
           DesktopSnapshot.Epoch.AddSeconds(state.Int("to"))!=info.To)return null;
        return new ReplayPracticeCheckpoint{Session=info.Id,Symbol=info.Symbol,From=info.From,To=info.To.AddDays(-1)};
    }
}
