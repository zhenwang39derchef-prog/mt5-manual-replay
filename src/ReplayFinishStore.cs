using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Globalization;
using System.Web.Script.Serialization;

public sealed class FinishSnapshot
{
    public int Version {get;set;}
    public string Kind {get;set;}
    public string Id {get;set;}
    public string Session {get;set;}
    public string Boot {get;set;}
    public DateTime CreatedUtc {get;set;}
    public DateTime Historical {get;set;}
    public string Symbol {get;set;}
    public string Period {get;set;}
    public decimal Balance {get;set;}
    public decimal Equity {get;set;}
    public int Positions {get;set;}
    public int Pending {get;set;}
    public string Items {get;set;}
    public string ItemPlans {get;set;}
    public Dictionary<string,string> Values {get;set;}
    public TrainingData Training {get;set;}
    public string RecordGeneration {get;set;}
    public ObservationArchive Observation {get;set;}
}
public sealed class FinishReview
{
    public string Compliance {get;set;}
    public string[] Issues {get;set;}
    public string NextAction {get;set;}
    public string Notes {get;set;}
    public Dictionary<string,string> PerTrade {get;set;}
    public bool Completed {get;set;}
    public DateTime SavedUtc {get;set;}
    public FinishReview(){Compliance="";Issues=new string[0];NextAction=Notes="";PerTrade=new Dictionary<string,string>();}
}
public sealed class FinishProgress
{
    public bool ReviewCompleted {get;set;}
    public bool FinalSaved {get;set;}
    public bool CloseRequested {get;set;}
    public string Error {get;set;}
    public DateTime UpdatedUtc {get;set;}
    public FinishProgress(){Error="";}
}
public sealed class FinishArchive
{
    public string Folder {get;set;}
    public FinishSnapshot Snapshot {get;set;}
    public FinishReview Review {get;set;}
    public FinishProgress Progress {get;set;}
}

// A complete snapshot is published by one directory rename. Review/progress are separate,
// atomically replaced documents; neither final settlement nor editing a review writes the seal.
public static class FinishStore
{
    public static readonly string Root=Path.Combine(ReplayFiles.Home,"收工档案");
    public static string LastError="";
    const string SealName="收工现场",FinalName="最终结果",RecordsName="交易记录";
    static readonly object Gate=new object();
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=160};
    sealed class Manifest
    {
        public int Version {get;set;}
        public string Session {get;set;}
        public string Boot {get;set;}
        public Dictionary<string,string> Files {get;set;}
    }
    sealed class Document
    {
        public string Session {get;set;}
        public string Boot {get;set;}
        public string Payload {get;set;}
        public string Sha256 {get;set;}
    }
    sealed class Lease : IDisposable
    {
        readonly Mutex mutex; bool held;
        public Lease(string path)
        {
            mutex=new Mutex(false,@"Global\CNReplayFinish_"+Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
            try{try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("收工资料正在其他窗口保存，请稍后重试。");}
            catch{mutex.Dispose();throw;}
        }
        public void Dispose(){if(held){held=false;mutex.ReleaseMutex();}mutex.Dispose();}
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static byte[] Bytes(object value){return new UTF8Encoding(false,true).GetBytes(Json.Serialize(value));}
    static T ReadJson<T>(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true))return Json.Deserialize<T>(reader.ReadToEnd());
    }
    static void Identity(string session,string boot)
    {
        if(!DesktopSessionFiles.ValidId(session) || String.IsNullOrEmpty(boot) || boot.Length>100 || boot.Any(c=>!Char.IsLetterOrDigit(c) && c!='_' && c!='-'))throw new FormatException("收工场次标识无效。");
    }
    public static string ArchiveId(string session,string boot)
    {Identity(session,boot);return Hash(Encoding.UTF8.GetBytes(session+"\n"+boot));}
    public static string FolderFor(string session,string boot,string root=null)
    {return Path.Combine(root??Root,ArchiveId(session,boot));}
    static void ValidateState(DesktopSessionInfo info,DesktopSnapshot state,bool ended)
    {
        if(info==null || state==null)throw new InvalidOperationException("没有可保存的回放现场。");
        Identity(info.Id,state.Get("boot"));
        if(state.Get("session")!=info.Id)throw new InvalidOperationException("回放场次已变化，未保存旧现场。");
        if(ended){if(state.Get("phase")!="ended")throw new InvalidOperationException("测试器尚未结束，最终结果未保存。");}
        else if(!state.Fresh || state.Get("training_mode")!="paused" || state.Get("finish_sealed")!="1")throw new InvalidOperationException("测试器尚未确认收工暂停，现场未保存，请重试。");
        if(state.Int("count")<0 || state.Int("pending_count")<0)throw new FormatException("持仓或挂单数量无效。");
    }
    static TrainingData FreezeTraining(string session,string boot,string sessionFolder)
    {
        var data=TrainingStore.Load(session,boot,sessionFolder);
        if(data.Profile==null || String.IsNullOrWhiteSpace(data.Profile.SettingsJson))throw new IOException("本场启动设置不完整，收工档案未保存，请重试或核对启动资料。");
        var settings=Json.Deserialize<ReplayOptions>(data.Profile.SettingsJson);
        if(settings==null)throw new FormatException("本场启动设置无法读取。");
        return Json.Deserialize<TrainingData>(Json.Serialize(data));
    }
    static FinishSnapshot Build(DesktopSessionInfo info,DesktopSnapshot state,TrainingData training,string kind)
    {
        return new FinishSnapshot{Version=1,Kind=kind,Id=ArchiveId(info.Id,state.Get("boot")),Session=info.Id,Boot=state.Get("boot"),CreatedUtc=DateTime.UtcNow,Historical=state.Now,
            Symbol=info.Symbol,Period=info.Period,Balance=state.Number("balance"),Equity=state.Number("equity"),Positions=checked((int)state.Int("count")),Pending=checked((int)state.Int("pending_count")),
            Items=state.Get("chart_items"),ItemPlans=state.Get("item_plans"),Values=new Dictionary<string,string>(state.Values),Training=training,RecordGeneration=""};
    }
    static RecordBundle Records(string folder,FinishSnapshot snapshot,bool final)
    {
        var bundle=RecordStore.ReadBundle(folder);var summary=bundle.Tables["回放汇总.csv"];
        if(summary.Get(0,"回放ID")!=snapshot.Session || summary.Get(0,"运行ID")!=snapshot.Boot || summary.Get(0,"品种")!=snapshot.Symbol || summary.Get(0,"周期").Replace("PERIOD_","")!=snapshot.Period.Replace("PERIOD_",""))throw new FormatException("交易记录与收工场次不一致。");
        if(final && summary.Get(0,"记录状态")!="记录已结束")throw new IOException("最终交易记录尚未就绪，请重试同步。");
        decimal balance,equity;
        if(!Decimal.TryParse(summary.Get(0,"余额"),NumberStyles.Number,CultureInfo.InvariantCulture,out balance) || !Decimal.TryParse(summary.Get(0,"净值"),NumberStyles.Number,CultureInfo.InvariantCulture,out equity) || balance!=snapshot.Balance || equity!=snapshot.Equity)throw new IOException("交易记录与现场净值不一致，请重试同步。");
        return bundle;
    }
    static void Publish(string target,FinishSnapshot snapshot,RecordBundle records)
    {
        string parent=Path.GetDirectoryName(target);Directory.CreateDirectory(parent);
        string temp=Path.Combine(parent,".pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var files=new Dictionary<string,byte[]>();files.Add("snapshot.json",Bytes(snapshot));
            if(records!=null)foreach(var file in records.Bytes)files.Add(RecordsName+"/"+file.Key,file.Value);
            var hashes=new Dictionary<string,string>();
            foreach(var file in files)
            {
                string path=Path.Combine(temp,file.Key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(file.Value,0,file.Value.Length);stream.Flush(true);}
                hashes.Add(file.Key,Hash(file.Value));
            }
            byte[] manifest=Bytes(new Manifest{Version=1,Session=snapshot.Session,Boot=snapshot.Boot,Files=hashes});
            using(var stream=new FileStream(Path.Combine(temp,"manifest.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(manifest,0,manifest.Length);stream.Flush(true);}
            ReadSnapshot(temp);Directory.Move(temp,target);ReadSnapshot(target);
        }
        finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
    static FinishSnapshot ReadSnapshot(string path)
    {
        var manifest=ReadJson<Manifest>(Path.Combine(path,"manifest.json"));
        if(manifest==null || manifest.Version!=1 || manifest.Files==null || !manifest.Files.ContainsKey("snapshot.json"))throw new FormatException("收工档案清单损坏。");
        Identity(manifest.Session,manifest.Boot);
        foreach(var file in manifest.Files)
        {
            if(file.Key!="snapshot.json" && !RecordStore.Names.Concat(new[]{"snapshot.txt"}).Select(n=>RecordsName+"/"+n).Contains(file.Key))throw new FormatException("收工档案包含未知文件。");
            if(Hash(File.ReadAllBytes(Path.Combine(path,file.Key.Replace('/',Path.DirectorySeparatorChar))))!=file.Value)throw new FormatException("收工档案校验失败，文件可能损坏："+file.Key);
        }
        var snapshot=ReadJson<FinishSnapshot>(Path.Combine(path,"snapshot.json"));
        if(snapshot==null || snapshot.Version!=1 || snapshot.Session!=manifest.Session || snapshot.Boot!=manifest.Boot || snapshot.Id!=ArchiveId(snapshot.Session,snapshot.Boot) || snapshot.Values==null || snapshot.Training==null || snapshot.Training.Profile==null || String.IsNullOrWhiteSpace(snapshot.Training.Profile.SettingsJson))throw new FormatException("收工档案内容不完整或场次不匹配。");
        if(snapshot.Values["session"]!=snapshot.Session || snapshot.Values["boot"]!=snapshot.Boot)throw new FormatException("收工运行状态与场次不匹配。");
        if(!String.IsNullOrEmpty(snapshot.RecordGeneration))
        {
            var expected=new[]{"snapshot.json"}.Concat(RecordStore.Names.Concat(new[]{"snapshot.txt"}).Select(n=>RecordsName+"/"+n)).ToArray();
            if(manifest.Files.Count!=expected.Length || expected.Any(n=>!manifest.Files.ContainsKey(n)))throw new FormatException("收工档案交易记录清单不完整。");
            var records=Records(Path.Combine(path,RecordsName),snapshot,snapshot.Kind=="test_end");if(records.Generation!=snapshot.RecordGeneration)throw new FormatException("收工交易记录快照号不匹配。");
        }
        if(snapshot.Observation!=null){if(snapshot.Observation.Data==null||snapshot.Observation.View==null||snapshot.Observation.View.Boot!=snapshot.Boot)throw new FormatException("图表快照内容不完整。");snapshot.Observation.Data.Validate(snapshot.Session,snapshot.Boot,snapshot.Symbol,(long)(snapshot.Historical-DesktopSnapshot.Epoch).TotalSeconds);snapshot.Observation.View.Normalize();}
        return snapshot;
    }
    static void AtomicDocument<T>(string path,FinishSnapshot snapshot,T value)
    {
        string payload=Json.Serialize(value);byte[] bytes=Bytes(new Document{Session=snapshot.Session,Boot=snapshot.Boot,Payload=payload,Sha256=Hash(Encoding.UTF8.GetBytes(payload))});
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            ReadDocument<T>(temp,snapshot);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);ReadDocument<T>(path,snapshot);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static T ReadDocument<T>(string path,FinishSnapshot snapshot)
    {
        var document=ReadJson<Document>(path);
        if(document==null || document.Session!=snapshot.Session || document.Boot!=snapshot.Boot || document.Payload==null || Hash(Encoding.UTF8.GetBytes(document.Payload))!=document.Sha256)throw new FormatException("复盘或保存进度校验失败，原收工现场仍独立保留。");
        return Json.Deserialize<T>(document.Payload);
    }
    public static FinishArchive Create(DesktopSessionInfo info,DesktopSnapshot state,string sessionFolder=null,string root=null)
    {return CreateCore(info,state,sessionFolder,root,false);}
    public static FinishArchive CreateEnded(DesktopSessionInfo info,DesktopSnapshot state,string sessionFolder=null,string root=null)
    {return CreateCore(info,state,sessionFolder,root,true);}
    static FinishArchive CreateCore(DesktopSessionInfo info,DesktopSnapshot state,string sessionFolder,string root,bool ended)
    {
        if(info==null || state==null)throw new InvalidOperationException("没有可保存的回放现场。");
        string archiveFolder=FolderFor(info.Id,state.Get("boot"),root);
        lock(Gate)using(var lease=new Lease(archiveFolder))
        {
            // An acknowledgement may age while a user retries. Existing verified seal wins;
            // never regenerate it from later quotes, current plans or final settlement.
            if(Directory.Exists(Path.Combine(archiveFolder,SealName)))return Load(archiveFolder);
            ValidateState(info,state,ended);
            if(!String.IsNullOrEmpty(state.Get("record_error")))throw new IOException("收工交易记录尚未保存："+state.Get("record_error"));
            var training=FreezeTraining(info.Id,state.Get("boot"),sessionFolder);
            var snapshot=Build(info,state,training,ended?"test_end":"finish");
            snapshot.Observation=ObservationArchive.Capture(info,state,sessionFolder??DesktopSessionFiles.Folder(info.Id));
            string source=Path.Combine(sessionFolder??DesktopSessionFiles.Folder(info.Id),"records",snapshot.Boot);
            var records=Records(source,snapshot,ended);snapshot.RecordGeneration=records.Generation;
            Publish(Path.Combine(archiveFolder,SealName),snapshot,records);
            return Load(archiveFolder);
        }
    }
    public static FinishArchive Load(string folder)
    {
        lock(Gate)
        {
            var snapshot=ReadSnapshot(Path.Combine(folder,SealName));
            var review=File.Exists(Path.Combine(folder,"review.json"))?ReadDocument<FinishReview>(Path.Combine(folder,"review.json"),snapshot):new FinishReview();
            var progress=File.Exists(Path.Combine(folder,"progress.json"))?ReadDocument<FinishProgress>(Path.Combine(folder,"progress.json"),snapshot):new FinishProgress();
            progress.ReviewCompleted=review.Completed;
            progress.FinalSaved=false;
            if(Directory.Exists(Path.Combine(folder,FinalName)))
            {
                try{var final=ReadSnapshot(Path.Combine(folder,FinalName));if(final.Session!=snapshot.Session || final.Boot!=snapshot.Boot || final.Kind!="test_end")throw new FormatException("最终结果场次不匹配。");progress.FinalSaved=true;}
                catch(Exception ex){progress.Error="收工现场已保存；最终结果校验失败："+ex.Message;}
            }
            return new FinishArchive{Folder=Path.GetFullPath(folder),Snapshot=snapshot,Review=review,Progress=progress};
        }
    }
    public static List<FinishArchive> List(string root=null)
    {
        string path=root??Root;var list=new List<FinishArchive>();var errors=new List<string>();LastError="";if(!Directory.Exists(path))return list;
        foreach(string folder in Directory.GetDirectories(path).Where(p=>!Path.GetFileName(p).StartsWith(".")))
        {if(!Directory.Exists(Path.Combine(folder,SealName)))continue;try{list.Add(Load(folder));}catch(Exception ex){errors.Add(Path.GetFileName(folder)+"："+ex.Message);}}
        LastError=String.Join("\r\n",errors);
        return list.OrderByDescending(a=>a.Snapshot.CreatedUtc).ToList();
    }
    static void RefreshArchive(FinishArchive target,FinishArchive current){target.Snapshot=current.Snapshot;target.Review=current.Review;target.Progress=current.Progress;}
    public static void SaveReview(FinishArchive archive,FinishReview review)
    {
        if(archive==null || review==null)throw new ArgumentNullException("review");
        lock(Gate)using(var lease=new Lease(archive.Folder))
        {
            var current=Load(archive.Folder);var copy=Json.Deserialize<FinishReview>(Json.Serialize(review));
            copy.Issues=copy.Issues??new string[0];copy.PerTrade=copy.PerTrade??new Dictionary<string,string>();copy.SavedUtc=DateTime.UtcNow;
            if(copy.Completed && (String.IsNullOrWhiteSpace(copy.Compliance) || copy.Issues.Length==0 || String.IsNullOrWhiteSpace(copy.NextAction)))throw new InvalidOperationException("请完成整场复盘的三项内容，或先保存草稿。");
            AtomicDocument(Path.Combine(archive.Folder,"review.json"),current.Snapshot,copy);
            current.Progress.ReviewCompleted=copy.Completed;current.Progress.Error="";current.Progress.UpdatedUtc=DateTime.UtcNow;
            AtomicDocument(Path.Combine(archive.Folder,"progress.json"),current.Snapshot,current.Progress);RefreshArchive(archive,Load(archive.Folder));review.SavedUtc=archive.Review.SavedUtc;
        }
    }
    public static FinishSnapshot LoadFinal(FinishArchive archive)
    {
        if(archive==null)throw new ArgumentNullException("archive");
        string path=Path.Combine(archive.Folder,FinalName);if(!Directory.Exists(path))return null;
        var snapshot=ReadSnapshot(path);var original=ReadSnapshot(Path.Combine(archive.Folder,SealName));
        if(snapshot.Session!=original.Session || snapshot.Boot!=original.Boot || snapshot.Kind!="test_end")throw new FormatException("最终结果与收工场次不匹配。");return snapshot;
    }
    public static void SaveFinal(FinishArchive archive,DesktopSnapshot ended,string nativeRecordsFolder=null)
    {
        if(archive==null || ended==null)throw new ArgumentNullException("ended");
        lock(Gate)using(var lease=new Lease(archive.Folder))
        {
            var current=Load(archive.Folder);var original=current.Snapshot;
            if(ended.Get("session")!=original.Session || ended.Get("boot")!=original.Boot || ended.Get("phase")!="ended")throw new InvalidOperationException("测试器尚未结束或场次不匹配，最终结果未保存。");
            if(Directory.Exists(Path.Combine(archive.Folder,FinalName)))LoadFinal(current);
            else
            {
                if(!String.IsNullOrEmpty(ended.Get("record_error")))throw new IOException("测试器最终记录尚未保存："+ended.Get("record_error"));
                var info=new DesktopSessionInfo{Id=original.Session,Symbol=original.Symbol,Period=original.Period};var snapshot=Build(info,ended,original.Training,"test_end");
                string source=nativeRecordsFolder??Path.Combine(DesktopSessionFiles.Folder(original.Session),"records",original.Boot);var records=Records(source,snapshot,true);snapshot.RecordGeneration=records.Generation;
                Publish(Path.Combine(archive.Folder,FinalName),snapshot,records);
            }
            current.Progress.FinalSaved=true;current.Progress.Error="";current.Progress.UpdatedUtc=DateTime.UtcNow;AtomicDocument(Path.Combine(archive.Folder,"progress.json"),original,current.Progress);RefreshArchive(archive,Load(archive.Folder));
        }
    }
    public static void MarkCloseRequested(FinishArchive archive)
    {
        if(archive==null)throw new ArgumentNullException("archive");
        lock(Gate)using(var lease=new Lease(archive.Folder))
        {var current=Load(archive.Folder);current.Progress.CloseRequested=true;current.Progress.Error="";current.Progress.UpdatedUtc=DateTime.UtcNow;AtomicDocument(Path.Combine(archive.Folder,"progress.json"),current.Snapshot,current.Progress);RefreshArchive(archive,Load(archive.Folder));}
    }
    public static void SaveError(FinishArchive archive,string error)
    {
        if(archive==null)throw new ArgumentNullException("archive");
        lock(Gate)using(var lease=new Lease(archive.Folder))
        {var current=Load(archive.Folder);current.Progress.Error=error??"";current.Progress.UpdatedUtc=DateTime.UtcNow;AtomicDocument(Path.Combine(archive.Folder,"progress.json"),current.Snapshot,current.Progress);RefreshArchive(archive,Load(archive.Folder));}
    }
}
