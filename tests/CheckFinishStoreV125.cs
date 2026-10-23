using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

public static class CheckFinishStoreV125
{
    const string Sid="12512512512512512512512512512512",Boot="finish_store_125";
    static int count;static string qa;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Need(bool value,string reason){if(!value)throw new Exception(reason);count++;}
    static void Reject(Action action,string reason){bool rejected=false;try{action();}catch{rejected=true;}Need(rejected,reason);}
    static DesktopSessionInfo Info(){return new DesktopSessionInfo{Id=Sid,Symbol="XAUUSDm",Period="M5",CreatedUtc=new DateTime(2026,10,23,10,0,0,DateTimeKind.Utc),From=new DateTime(2026,6,1),To=new DateTime(2026,6,8)};}
    static DesktopSnapshot State(string boot=Boot,string phase="running",string mode="paused",string sealedValue="1",string stamp=null,decimal equity=10025m,string session=Sid,string recordError="")
    {
        return DesktopSnapshot.Parse("protocol=1\nsession="+session+"\nboot="+boot+"\nphase="+phase+"\nstamp="+(stamp??unchecked((uint)Environment.TickCount).ToString())+"\ntraining_mode="+mode+"\nfinish_sealed="+sealedValue+"\nnow=1780300000\ncount="+(phase=="ended"?"0":"1")+"\npending_count="+(phase=="ended"?"0":"1")+"\nbalance=10000\nequity="+equity.ToString(CultureInfo.InvariantCulture)+"\nchart_items="+(phase=="ended"?"":"3,position,buy,0.100,2000,1995,2020,25;4,pending,buy_limit,0.100,1980,1975,2000,0")+"\nitem_plans=3:planA;4:planA\nplayback_target=10\nplayback_mode=mt5\nrecord_error="+recordError+"\ncomplete=1",session);
    }
    static string Fixture(string name,string boot=Boot,bool final=false)
    {
        string folder=Path.Combine(qa,name);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"fixture.flag"),"finish store test");
        var options=new ReplayOptions{Symbol="XAUUSDm",Timeframe="M5",Lots=0.1m,PlaybackSpeed=10};
        var profile=new TrainingProfile{SessionId=Sid,Symbol="XAUUSDm",Timeframe="M5",StartingBalance=10000,SettingsJson=Json.Serialize(options),Config=new TrainingConfig()};
        TrainingStore.AtomicJson(Path.Combine(TrainingStore.SessionRoot(Sid,folder),"profile.json"),profile);
        var training=new TrainingData{Profile=profile,ActivePlanId="planA"};
        training.Plans.Add(new TrainingPlan{Id="planA",IdeaId="ideaA",SessionId=Sid,Boot=boot,Reasons=new[]{"ema_up"},FailureCondition="manual_price",InvalidationPrice=1995m,TargetPrice=2020m,ExitMethod="target_price",Notes="冻结前计划全文",CreatedUtc=DateTime.UtcNow,HistoricalTime=new DateTime(2026,6,1),DetailVersion=2});
        training.Commands.Add(new TrainingCommand{Sequence=1,Boot=boot,Action="buy",PlanId="planA",Status="acknowledged",Tickets=new[]{"3"}});
        TrainingStore.SaveData(Sid,boot,training,folder);WriteRecords(folder,boot,1,final);return folder;
    }
    static string WriteRecords(string folder,string boot,int generation,bool final,string session=Sid)
    {
        string records=Path.Combine(folder,"records",boot);Directory.CreateDirectory(records);
        foreach(string name in RecordStore.Names)
        {
            string text="快照号,回放ID,运行ID,品种,周期"+(name=="回放汇总.csv"?",记录状态,余额,净值":",持仓ID,备注")+"\r\n";
            text+=generation+","+session+","+boot+",XAUUSDm,PERIOD_M5,"+(name=="回放汇总.csv"?(final?"记录已结束":"回放中快照")+",10000,"+(final?"9970":"10025"):"3,交易历史")+"\r\n";
            File.WriteAllText(Path.Combine(records,name),text,new UTF8Encoding(true));
        }
        File.WriteAllText(Path.Combine(records,"snapshot.txt"),generation+"\r\n",new UTF8Encoding(true));return records;
    }
    static string SnapshotPath(FinishArchive a){return Path.Combine(a.Folder,"收工现场","snapshot.json");}
    public static int Main(string[] args)
    {
        qa=Path.GetFullPath(args[0]);Directory.CreateDirectory(qa);
        try
        {
            string root=Path.Combine(qa,"archives"),source=Fixture("source");
            Reject(()=>FinishStore.Create(Info(),State(stamp:"1"),source,root),"stale pause cannot seal");
            Reject(()=>FinishStore.Create(Info(),State(mode:"active"),source,root),"active state cannot seal");
            Reject(()=>FinishStore.Create(Info(),State(sealedValue:"0"),source,root),"ordinary pause without native seal cannot seal");
            Reject(()=>FinishStore.Create(Info(),State(phase:"ended"),source,root),"ended state cannot masquerade as manual finish");
            Reject(()=>FinishStore.Create(Info(),State(session:new string('a',32)),source,root),"wrong session cannot seal");
            Reject(()=>FinishStore.Create(Info(),State(recordError:"写入失败"),source,root),"native CSV error cannot report complete archive success");
            Reject(()=>FinishStore.Create(Info(),State(equity:9999),source,root),"stale CSV account facts cannot seal newer state");
            string missing=Fixture("missing-settings");File.Delete(Path.Combine(missing,"training","profile.json"));File.Delete(Path.Combine(missing,"training","runs",Boot,"data.json"));
            Reject(()=>FinishStore.Create(Info(),State(),missing,Path.Combine(qa,"missing-root")),"missing settings cannot report full archive success");
            string mixed=Fixture("mixed-source");WriteRecords(mixed,Boot,2,false,new string('b',32));
            Reject(()=>FinishStore.Create(Info(),State(),mixed,Path.Combine(qa,"mixed-root")),"mixed native CSV cannot seal");
            var archive=FinishStore.Create(Info(),State(),source,root);string original=File.ReadAllText(SnapshotPath(archive));
            Need(archive.Snapshot.Kind=="finish" && archive.Snapshot.Positions==1 && archive.Snapshot.Pending==1 && archive.Snapshot.Equity==10025,"complete opening facts frozen");
            Need(archive.Snapshot.Items.Contains("1995") && archive.Snapshot.ItemPlans.Contains("planA"),"orders stop values and plan associations frozen");
            Need(archive.Snapshot.Training.Plans.Single().Notes=="冻结前计划全文" && archive.Snapshot.Training.Commands.Single().Tickets.Single()=="3","complete plans and commands frozen");
            Need(Json.Deserialize<ReplayOptions>(archive.Snapshot.Training.Profile.SettingsJson).EMAFast==8 && archive.Snapshot.Values["playback_mode"]=="mt5","startup indicator settings and runtime mode frozen together");
            Need(RecordStore.ReadBundle(Path.Combine(archive.Folder,"收工现场","交易记录")).Generation=="1","sealed trade history is consistent native bundle");
            Need(FinishStore.LoadFinal(archive)==null && !archive.Progress.FinalSaved,"no fabricated final result");
            var current=TrainingStore.Load(Sid,Boot,source);current.Profile.SettingsJson="{}";current.Plans.Single().Notes="修改以后";TrainingStore.AtomicJson(Path.Combine(source,"training","profile.json"),current.Profile);TrainingStore.AtomicJson(Path.Combine(source,"training","runs",Boot,"data.json"),current);
            Need(FinishStore.Load(archive.Folder).Snapshot.Training.Plans.Single().Notes=="冻结前计划全文","later plan editing never mutates finish");
            var retry=FinishStore.Create(Info(),State(stamp:"1",equity:9999),source,root);
            Need(retry.Folder==archive.Folder && File.ReadAllText(SnapshotPath(archive))==original && FinishStore.List(root).Count==1,"aged retry idempotently reads original archive");
            archive.Snapshot.Training.Plans[0].Notes="in-memory mutation";
            Need(FinishStore.Load(archive.Folder).Snapshot.Training.Plans[0].Notes=="冻结前计划全文","caller object mutation cannot mutate stored seal");
            var draft=new FinishReview{Compliance="partial",Notes="保留原判断",PerTrade=new Dictionary<string,string>{{"3","收工时未退出"}}};FinishStore.SaveReview(archive,draft);
            Need(!archive.Progress.ReviewCompleted && archive.Review.SavedUtc!=default(DateTime) && archive.Review.PerTrade["3"]=="收工时未退出","incomplete per-trade draft can save");
            Reject(()=>FinishStore.SaveReview(archive,new FinishReview{Completed=true}),"incomplete review cannot claim completion");
            var complete=new FinishReview{Compliance="partial",Issues=new[]{"early_entry"},NextAction="等待确认再入场",Notes="一项改进",Completed=true};FinishStore.SaveReview(archive,complete);
            Need(archive.Progress.ReviewCompleted && FinishStore.Load(archive.Folder).Review.NextAction=="等待确认再入场","completed three-question review persisted");
            Need(File.ReadAllText(SnapshotPath(archive))==original,"review saving does not alter sealed snapshot");
            FinishStore.MarkCloseRequested(archive);Need(archive.Progress.CloseRequested,"exit intent durably saved");
            FinishStore.SaveError(archive,"测试器尚未结束");Need(FinishStore.Load(archive.Folder).Progress.Error=="测试器尚未结束","retry context durable");
            string finals=WriteRecords(source,Boot,3,false);
            Reject(()=>FinishStore.SaveFinal(archive,State(),finals),"running state cannot save final");
            Reject(()=>FinishStore.SaveFinal(archive,State(phase:"ended",boot:"other_boot"),finals),"wrong boot cannot save final");
            Reject(()=>FinishStore.SaveFinal(archive,State(phase:"ended"),finals),"running native records cannot be labelled final");
            WriteRecords(source,Boot,4,true);FinishStore.SaveFinal(archive,State(phase:"ended",equity:9970),finals);
            Need(archive.Progress.FinalSaved && archive.Progress.Error=="" && archive.Progress.ReviewCompleted,"successful final retry preserves review and clears error");
            var final=FinishStore.LoadFinal(archive);Need(final.Kind=="test_end" && final.Equity==9970 && final.Positions==0 && final.RecordGeneration=="4","independent final facts captured");
            Need(File.ReadAllText(SnapshotPath(archive))==original && FinishStore.Load(archive.Folder).Snapshot.Equity==10025,"forced settlement cannot overwrite finish judgment basis");
            string finalOriginal=File.ReadAllText(Path.Combine(archive.Folder,"最终结果","snapshot.json"));WriteRecords(source,Boot,5,true);FinishStore.SaveFinal(archive,State(phase:"ended",equity:8888),finals);
            Need(File.ReadAllText(Path.Combine(archive.Folder,"最终结果","snapshot.json"))==finalOriginal,"final retry never rewrites previously frozen final result");
            Directory.Delete(source,true);
            var recovered=FinishStore.Load(archive.Folder);Need(recovered.Snapshot.Training.Profile.SettingsJson.Contains("EMAFast") && FinishStore.LoadFinal(recovered).Equity==9970,"archive fully recoverable after native source removed");
            Need(FinishStore.Create(Info(),State(stamp:"1"),source,root).Folder==archive.Folder,"retry remains recoverable without source files");
            string failedSource=Fixture("failed-source",boot:"retry_boot"),failedRoot=Path.Combine(qa,"retry-root"),failedFolder=FinishStore.FolderFor(Sid,"retry_boot",failedRoot);Directory.CreateDirectory(failedFolder);File.WriteAllText(Path.Combine(failedFolder,"收工现场"),"blocked");
            Reject(()=>FinishStore.Create(Info(),State(boot:"retry_boot"),failedSource,failedRoot),"publication failure cannot claim success");
            Need(!Directory.GetDirectories(failedFolder,".pending-*").Any() && FinishStore.List(failedRoot).Count==0,"failed unpublished write leaves no successful archive or temp payload");
            File.Delete(Path.Combine(failedFolder,"收工现场"));var repaired=FinishStore.Create(Info(),State(boot:"retry_boot"),failedSource,failedRoot);Need(repaired.Snapshot.Id==FinishStore.ArchiveId(Sid,"retry_boot"),"save retry uses same durable identity");
            Directory.CreateDirectory(Path.Combine(repaired.Folder,"progress.json"));
            Reject(()=>FinishStore.SaveReview(repaired,new FinishReview{Notes="写入后进度失败"}),"progress failure reported with saved answer preserved");
            Need(FinishStore.Load(repaired.Folder).Review.Notes=="写入后进度失败","partial mutable save retains already committed review");
            Directory.Delete(Path.Combine(repaired.Folder,"progress.json"));FinishStore.SaveReview(repaired,new FinishReview{Notes="重试保存成功"});Need(FinishStore.Load(repaired.Folder).Review.Notes=="重试保存成功","mutable save retries without rewriting seal");
            string concurrentSource=Fixture("concurrent",boot:"parallel_boot"),concurrentRoot=Path.Combine(qa,"parallel-root");var tasks=Enumerable.Range(0,4).Select(i=>Task.Factory.StartNew(()=>FinishStore.Create(Info(),State(boot:"parallel_boot"),concurrentSource,concurrentRoot))).ToArray();Task.WaitAll(tasks);
            Need(tasks.Select(t=>t.Result.Folder).Distinct().Count()==1 && FinishStore.List(concurrentRoot).Count==1,"concurrent finish clients publish one immutable seal");
            string naturalSource=Fixture("natural",boot:"natural_boot",final:true);var natural=FinishStore.CreateEnded(Info(),State(boot:"natural_boot",phase:"ended",equity:9970),naturalSource,Path.Combine(qa,"natural-root"));
            Need(natural.Snapshot.Kind=="test_end","natural end has distinct provenance and never masquerades as manual finish");
            FinishStore.SaveFinal(natural,State(boot:"natural_boot",phase:"ended",equity:9970),Path.Combine(naturalSource,"records","natural_boot"));Need(FinishStore.LoadFinal(natural).Kind=="test_end","natural final can also be independently recovered");
            string corrupt=Path.Combine(repaired.Folder,"收工现场","交易记录","交易结果.csv");File.AppendAllText(corrupt,"corruption");
            Reject(()=>FinishStore.Load(repaired.Folder),"CSV corruption blocks verified success");
            Reject(()=>FinishStore.Create(Info(),State(boot:"retry_boot"),failedSource,failedRoot),"retry refuses to silently overwrite corrupt original archive");
            FinishStore.CreateEnded(Info(),State(boot:"natural_boot",phase:"ended",equity:9970),naturalSource,failedRoot);
            Need(FinishStore.List(failedRoot).Count==1 && FinishStore.LastError.Contains("校验失败"),"corrupt archive reported explicitly without preventing other archives listing");
            string manifest=Path.Combine(archive.Folder,"最终结果","manifest.json");File.AppendAllText(manifest,"corruption");Reject(()=>FinishStore.LoadFinal(archive),"corrupted final rejected independently");
            var validOriginal=FinishStore.Load(archive.Folder);Need(validOriginal.Snapshot.Equity==10025 && !validOriginal.Progress.FinalSaved && validOriginal.Progress.Error.Contains("最终结果校验失败"),"corrupt final still permits independent original review access and clear error");
            Reject(()=>FinishStore.SaveFinal(archive,State(phase:"ended"),Path.Combine(qa,"absent")),"corrupt immutable final cannot be silently replaced on retry");
            Need(File.ReadAllText(SnapshotPath(archive))==original,"final corruption never touches original seal");
            File.WriteAllText(Path.Combine(qa,"checks.txt"),"PASS "+count+" durable finish archive checks\r\n",new UTF8Encoding(true));Console.WriteLine("PASS "+count+" durable finish archive checks");return 0;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(qa,"error.txt"),ex.ToString());Console.WriteLine(ex);return 1;}
    }
}
