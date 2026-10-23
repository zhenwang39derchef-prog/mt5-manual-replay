using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;
public static class CheckObservationV126
{
    const string Sid="12512512512512512512512512512512",Boot="finish_store_125";const long Now=1780300000;
    static int count;static void Need(bool value,string why){if(!value)throw new Exception(why);count++;}
    static void Reject(Action action,string why){bool rejected=false;try{action();}catch{rejected=true;}Need(rejected,why);}
    public static string Bundle(long now=Now)
    {
        var b=new StringBuilder("protocol=1\nsession="+Sid+"\nboot="+Boot+"\nsymbol=XAUUSDm\nasof="+now+"\nstamp=1000\ngeneration=1\ndigits=3\n");
        foreach(string f in ObservationData.Frames){int seconds=ObservationData.Seconds(f);long bucket=now/seconds*seconds;b.Append(f+"=");for(int i=140;i>=0;i--){if(i<140)b.Append(';');b.Append((bucket-i*seconds)+",2000,2004,1996,2001,2000,1999,1998,"+(i==0?"1":"0"));}b.Append("\n"+f+"_status=forming\n");}return b+"complete=1\n";
    }
    static ObservationData Parse(string text){return ObservationData.Parse(text,Sid,Boot,"XAUUSDm");}
    [STAThread]public static int Main(string[] args)
    {
        string qa=Path.GetFullPath(args[0]);Directory.CreateDirectory(qa);
        try
        {
            string nativeSource=ReplayFiles.SourceText(new ReplayOptions());Need(nativeSource.Contains("void ObservationPump()")&&nativeSource.Contains("class CReplayMtfSeries"),"launcher embeds observation source for custom indicator compile");Need(!nativeSource.Contains("#include \"CN_Replay_Observation.mqh\""),"deployed source has no missing observation include");File.WriteAllText(Path.Combine(qa,"ObservationStandalone.mq5"),nativeSource,Encoding.UTF8);
            string raw=Bundle();var data=Parse(raw);Need(data.AsOf==Now,"exact replay time");Need(data.Series.Count==4,"four direct periods");
            foreach(string f in ObservationData.Frames){var bars=data.Series[f];Need(bars.Length==141,f+" history count");Need(bars.Last().Forming,f+" current candle marked forming");Need(bars.Take(140).All(b=>b.Time+ObservationData.Seconds(f)<=Now),f+" closed candles known");Need(bars.All(b=>b.Time<=Now),f+" no future bar");}
            Reject(()=>Parse(raw.Replace("asof="+Now,"asof="+(Now-15000))),"reject future candle");
            Reject(()=>Parse(raw.Replace("session="+Sid,"session=other")),"reject foreign session");Reject(()=>Parse(raw.Replace("boot="+Boot,"boot=other")),"reject foreign boot");Reject(()=>Parse(raw.Replace("symbol=XAUUSDm","symbol=EURUSD")),"reject foreign symbol");
            Reject(()=>Parse(raw.Replace("complete=1","complete=0")),"reject incomplete envelope");Reject(()=>Parse(raw+"protocol=1"),"reject duplicate key");Reject(()=>Parse(raw.Replace(",2000,2004,1996,2001,",",2000,1999,1996,2001,")),"reject impossible high");Reject(()=>Parse(raw.Replace(",2000,2004,1996,2001,",",NaN,2004,1996,2001,")),"reject NaN");Reject(()=>Parse(raw.Replace(",2000,2004,1996,2001,",",2000,2004,2002,2001,")),"reject impossible low");Reject(()=>Parse(raw.Replace("_status=forming","_status=closed_only")),"forming status must agree");
            Reject(()=>data.Validate(Sid,Boot,"XAUUSDm",Now+1),"archive requires exact asof");
            string stateRaw="protocol=1\nsession="+Sid+"\nboot="+Boot+"\nsymbol=XAUUSDm\nphase=running\nstamp=1000\nnow="+Now+"\ncomplete=1";
            Need(data.CanDisplay(DesktopSnapshot.Parse(stateRaw,Sid)),"matching cache can display");Need(!data.CanDisplay(DesktopSnapshot.Parse(stateRaw.Replace("boot="+Boot,"boot=new_boot"),Sid)),"old cache cannot reopen into new boot");Need(!data.CanDisplay(DesktopSnapshot.Parse(stateRaw.Replace("now="+Now,"now="+(Now-1)),Sid)),"cache never ahead of replay clock");Need(!data.CanDisplay(DesktopSnapshot.Parse(stateRaw.Replace("symbol=XAUUSDm","symbol=EURUSD"),Sid)),"cached chart symbol isolation");
            var v=new ObservationView{Boot=Boot,Frame="bogus",Count=10000,Right=-1};v.Normalize();Need(v.Frame=="H1"&&v.Count==220&&v.Right==0,"normalize preferences");TrainingStore.AtomicJson(ObservationView.PathFor(qa,Boot),v);Need(ObservationView.Load(qa,Boot).Count==220,"remember zoom");Need(ObservationView.Load(qa,"other").Frame=="H1","boot-specific preference");
            using(var chart=new ObservationChart(new ObservationView{Boot=Boot}))
            {
                chart.Size=new Size(800,520);chart.UpdateData(data);Need(chart.EndIndex==140,"follow current");chart.MoveBars(-40);long edge=chart.View.Right;Need(chart.EndIndex==100&&edge>0,"browse history");chart.SetFrame("H4");Need(chart.View.Right==edge,"switch period retains historical edge");chart.Current();Need(chart.EndIndex==140&&chart.View.Right==0,"return current");chart.Zoom(-1000);Need(chart.View.Count==25,"max zoom bounded");chart.Zoom(1000);Need(chart.View.Count==220,"min zoom bounded");
                using(var bitmap=new Bitmap(800,520)){chart.DrawToBitmap(bitmap,chart.ClientRectangle);bitmap.Save(Path.Combine(qa,"chart.png"));Need(bitmap.GetPixel(100,100).A==255,"opaque chart paint");}
                Need(typeof(ObservationChart).GetFields(BindingFlags.Public|BindingFlags.Instance).All(f=>f.FieldType!=typeof(ChartController)&&f.FieldType!=typeof(ChartRequest)),"chart has no trading controller/request");
            }
            Rectangle main,observe;Need(ObservationLayout.Partition(new Rectangle(0,0,2560,1440),660,out main,out observe),"wide screen side by side");Need(main.Right<observe.Left&&observe.Right<=1884,"nonoverlapping panel reserve");Need(!ObservationLayout.Partition(new Rectangle(0,0,1024,768),660,out main,out observe),"small screen fallback");
            using(var form=new ObservationForm(new ObservationView{Boot=Boot},false)){form.SetData(data,null);Need(form.Controls.Count==1,"observer hierarchy");Need(form.Chart.Data==data,"observer receives only bundle");form.Close();Need(data.Series["H1"].Length==141,"close preserves market data");}
            var type=typeof(CheckFinishStoreV125);type.GetField("qa",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,qa);
            string folder=(string)type.GetMethod("Fixture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"archive-source",Boot,false});
            var info=(DesktopSessionInfo)type.GetMethod("Info",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var state=(DesktopSnapshot)type.GetMethod("State",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Boot,"running","paused","1",null,10025m,Sid,""});state.Values["mtf_protocol"]="1";state.Values["mtf_seal_ready"]="1";
            Reject(()=>FinishStore.Create(info,state,folder,Path.Combine(qa,"archives")),"missing chart blocks incomplete seal");
            File.WriteAllText(Path.Combine(folder,"mtf-seal.txt"),Bundle(Now+1),Encoding.Unicode);Reject(()=>FinishStore.Create(info,state,folder,Path.Combine(qa,"archives")),"later chart cannot enter earlier seal");
            File.WriteAllText(Path.Combine(folder,"mtf-seal.txt"),raw,Encoding.Unicode);TrainingStore.AtomicJson(ObservationView.PathFor(folder,Boot),new ObservationView{Boot=Boot,Frame="M15",Count=60,Right=0});
            var archive=FinishStore.Create(info,state,folder,Path.Combine(qa,"archives"));Need(archive.Snapshot.Observation.Data.AsOf==Now,"seal stores exact chart time");Need(archive.Snapshot.Observation.View.Frame=="M15","seal stores chosen frame");Need(archive.Snapshot.Observation.Data.Series["H4"].Last().Forming,"seal stores partial H4");
            File.WriteAllText(Path.Combine(folder,"mtf-seal.txt"),Bundle(Now+100),Encoding.Unicode);var loaded=FinishStore.Load(archive.Folder);Need(loaded.Snapshot.Observation.Data.AsOf==Now,"archive independent of later tester files");
            using(var offline=new ObservationForm(loaded.Snapshot.Observation.View,true)){offline.SetData(loaded.Snapshot.Observation.Data,null);offline.Chart.SetFrame("H4");offline.Chart.MoveBars(-20);Need(offline.Archived&&offline.Chart.Data.AsOf==Now,"offline history and TF never advance time");}
            File.WriteAllText(Path.Combine(qa,"checks.txt"),"PASS "+count,Encoding.UTF8);Console.WriteLine("PASS "+count);return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
