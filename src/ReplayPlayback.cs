using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

public sealed class ReplaySpeedControl:UserControl
{
    readonly TrackBar slider=new TrackBar();readonly Label caption=new ReplayStableLabel();
    readonly TableLayoutPanel labels=new ReplayBufferedTable(),fallback=new ReplayBufferedTable();
    readonly Button observeMode=new DesktopActionButton{Text="观察",AccessibleName="ObservationPlaybackMode"},nativeMode=new DesktopActionButton{Text="MT5快放",AccessibleName="NativePlaybackMode"};
    bool synchronizing,editing,local=true,nativeSelected;decimal rate=1;
    public event Action<decimal,bool> RateRequested;
    public event Action<bool> ModeRequested;
    public bool NativeMode {get{return nativeSelected;}}
    public decimal Rate {get{return rate;}}
    public bool Editing {get{return editing;}}
    public TrackBar Slider {get{return slider;}}
    public static decimal FromPosition(int position)
    {return Math.Round((decimal)Math.Pow(100,Math.Max(0,Math.Min(1000,position))/1000.0),3);}
    public static int ToPosition(decimal value)
    {if(value<1 || value>100)throw new ArgumentOutOfRangeException("value");return (int)Math.Round(Math.Log((double)value,100)*1000);}
    public ReplaySpeedControl(Button slow=null,Button fast=null)
    {
        Dock=DockStyle.Fill;Margin=Padding.Empty;AutoScaleMode=AutoScaleMode.None;BackColor=Color.Transparent;AccessibleName="PlaybackSpeedControl";
        var table=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.RowStyles.Add(new RowStyle(SizeType.Absolute,22));table.RowStyles.Add(new RowStyle(SizeType.Percent,100));table.RowStyles.Add(new RowStyle(SizeType.Absolute,18));Controls.Add(table);
        var header=new ReplayBufferedTable{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,46));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));table.Controls.Add(header,0,0);
        caption.Dock=DockStyle.Fill;caption.AutoEllipsis=true;caption.TextAlign=ContentAlignment.MiddleLeft;caption.Margin=Padding.Empty;header.Controls.Add(caption,0,0);
        foreach(var button in new[]{observeMode,nativeMode}){button.Dock=DockStyle.Fill;button.Margin=new Padding(1,0,1,0);button.Font=new Font("Microsoft YaHei UI",8F);button.BackColor=GraphiteTheme.Background;button.ForeColor=GraphiteTheme.Muted;}
        header.Controls.Add(observeMode,1,0);header.Controls.Add(nativeMode,2,0);observeMode.Click+=(s,e)=>RequestMode(false);nativeMode.Click+=(s,e)=>RequestMode(true);
        var trackHost=new ReplayBufferedPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};table.Controls.Add(trackHost,0,1);
        slider.Minimum=0;slider.Maximum=1000;slider.SmallChange=10;slider.LargeChange=100;slider.AutoSize=false;slider.TickStyle=TickStyle.None;slider.Dock=DockStyle.Fill;slider.Margin=Padding.Empty;slider.AccessibleName="PlaybackSpeedSlider";trackHost.Controls.Add(slider);
        labels.Dock=DockStyle.Fill;labels.Margin=Padding.Empty;labels.ColumnCount=3;for(int i=0;i<3;i++)labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333F));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        string[] text={"1×","10×","100×"};for(int i=0;i<3;i++)labels.Controls.Add(new ReplayStableLabel{Text=text[i],Dock=DockStyle.Fill,AutoSize=false,TextAlign=i==0?ContentAlignment.TopLeft:i==1?ContentAlignment.TopCenter:ContentAlignment.TopRight,Margin=Padding.Empty},i,0);table.Controls.Add(labels,0,2);
        fallback.Dock=DockStyle.Fill;fallback.Margin=Padding.Empty;fallback.ColumnCount=2;fallback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fallback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        fallback.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        if(slow!=null)fallback.Controls.Add(slow,0,0);if(fast!=null)fallback.Controls.Add(fast,1,0);fallback.Visible=false;trackHost.Controls.Add(fallback);
        slider.ValueChanged+=(s,e)=>{if(synchronizing)return;rate=FromPosition(slider.Value);caption.Text=(local?"起始 ":"目标 ")+Format(rate)+"×";if(RateRequested!=null)RateRequested(rate,false);};
        slider.MouseDown+=(s,e)=>editing=true;slider.MouseUp+=(s,e)=>{editing=false;if(RateRequested!=null)RateRequested(rate,true);};slider.MouseCaptureChanged+=(s,e)=>{if(!slider.Capture)editing=false;};slider.KeyUp+=(s,e)=>{if(RateRequested!=null)RateRequested(rate,true);};
        SetLocalRate(1);
    }
    public static string Format(decimal value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
    void SetRate(decimal value)
    {rate=value;synchronizing=true;try{slider.Value=ToPosition(value);}finally{synchronizing=false;}}
    public void SetLocalRate(decimal value)
    {local=true;editing=false;slider.Capture=false;SetRate(value);slider.Visible=labels.Visible=true;fallback.Visible=false;slider.Enabled=true;caption.Text="起始 "+Format(value)+"×";}
    public void SetLocalNativeMode(bool native){local=true;nativeSelected=native;slider.Enabled=!native;caption.Text=native?"快放速度请调 MT5":"观察 "+Format(rate)+"×";StyleModes();}
    void RequestMode(bool native){if(local){SetLocalNativeMode(native);return;}if(ModeRequested!=null)ModeRequested(native);}
    void StyleModes(){observeMode.BackColor=nativeSelected?GraphiteTheme.Background:GraphiteTheme.Soft;nativeMode.BackColor=nativeSelected?GraphiteTheme.Soft:GraphiteTheme.Background;observeMode.ForeColor=nativeSelected?GraphiteTheme.Muted:GraphiteTheme.Accent;nativeMode.ForeColor=nativeSelected?GraphiteTheme.Accent:GraphiteTheme.Muted;}
    public void Observe(DesktopSnapshot state,decimal? desired,bool awaiting)
    {
        local=false;bool supported=state!=null && state.Get("playback_protocol")=="1";
        slider.Visible=labels.Visible=supported;fallback.Visible=!supported;
        if(!supported){observeMode.Enabled=nativeMode.Enabled=false;caption.Text="旧场：按报价快慢控制";return;}
        bool modes=state.Get("playback_mode_protocol")=="1";nativeSelected=modes && state.Get("playback_mode")=="mt5";observeMode.Enabled=nativeMode.Enabled=state.Fresh && modes && !awaiting;StyleModes();
        decimal actualTarget=state.Number("playback_target");bool valid=actualTarget>=1 && actualTarget<=100;slider.Enabled=state.Fresh && valid && !nativeSelected;
        if(!editing && valid && !desired.HasValue)SetRate(actualTarget);
        decimal shown=desired??(valid?actualTarget:rate);if(!editing && desired.HasValue && desired.Value>=1 && desired.Value<=100)SetRate(desired.Value);
        string actual=state.Get("playback_actual_ready")=="1"?"实测 "+state.Number("playback_actual").ToString("0.##")+"×":"测速中";
        caption.Text=!valid?"回放倍率资料异常":!state.Fresh?"连接暂停 · 目标 "+Format(shown)+"×":
            nativeSelected?"MT5快放 · "+actual:"观察 "+Format(shown)+"×"+(awaiting?" · 待确认":state.Get("playback_paused")=="1"?" · 已暂停":" · "+actual);
        slider.AccessibleDescription="真实时间倍速。左端1倍，正中10倍，右端100倍。";
    }
}

public sealed partial class DesktopReplayForm
{
    ReplaySpeedControl playbackControl;TableLayoutPanel playbackFooter,playbackRoot;
    decimal? playbackDesired,playbackSent;long playbackSequence;int playbackRequestedAt;bool playbackSending;string playbackBoot="";
    Control BuildPlaybackStrip()
    {
        var strip=PanelGrid(new[]{0f,80f},1);strip.RowStyles.Add(new RowStyle(SizeType.Percent,100));var slow=PanelAction("play_slow","慢放 / 观察",Color.FromArgb(30,64,110));var fast=PanelAction("play_fast","快放 / 浏览",Card);
        playbackControl=new ReplaySpeedControl(slow,fast){Dock=DockStyle.Fill,ForeColor=Ink};playbackControl.Slider.BackColor=BackColor;playbackControl.RateRequested+=(value,commit)=>
        {if(playbackControl.NativeMode)return;playbackDesired=value;playbackRequestedAt=Environment.TickCount;if(commit)FlushPlaybackRate();};playbackControl.ModeRequested+=RequestPlaybackMode;strip.Controls.Add(playbackControl,0,0);
        var actionsPanel=PanelGrid(new[]{0f},session.IntradayLayout?1:2);actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent,session.IntradayLayout?100:50));if(!session.IntradayLayout)actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        trainingPlanShortcut=PanelButton("新计划",Card);trainingPlanShortcut.AccessibleName="TrainingPlanShortcut";trainingPlanShortcut.Click+=(s,e)=>{try{training.RequestPlan(this);}catch(Exception ex){status.Text=ex.Message;}};if(!session.IntradayLayout)actionsPanel.Controls.Add(trainingPlanShortcut,0,0);
        trainingPauseShortcut=PanelButton("暂停",Card);trainingPauseShortcut.AccessibleName="TrainingPauseShortcut";trainingPauseShortcut.Click+=(s,e)=>{try{if(snapshot!=null && snapshot.Get("training_mode")!="active")training.ResumePractice(this);else training.PausePractice(this);}catch(Exception ex){status.Text=ex.Message;}};actionsPanel.Controls.Add(trainingPauseShortcut,0,session.IntradayLayout?0:1);strip.Controls.Add(actionsPanel,1,0);
        tips.SetToolTip(playbackControl,"观察：1–100倍历史时间倍率。MT5快放：解除观察限速，用MT5测试器原生速度滑块调节。切回观察保留上次倍率；两种模式均可暂停。");return strip;
    }
    void RequestPlaybackMode(bool native)
    {
        if(snapshot==null || !snapshot.Fresh || snapshot.Get("playback_mode_protocol")!="1" || pending>0)return;
        playbackDesired=playbackSent=null;playbackSequence=0;Send(native?"play_fast":"play_slow");
    }
    void UpdatePlaybackState()
    {
        if(playbackControl==null)return;
        if(snapshot!=null && playbackBoot!=snapshot.Get("boot")){playbackBoot=snapshot.Get("boot");playbackDesired=playbackSent=null;playbackSequence=0;decimal target=snapshot.Number("playback_target");playbackControl.SetLocalRate(target>=1 && target<=100?target:1);}
        if(playbackSequence>0 && snapshot!=null && snapshot.Int("ack")>=playbackSequence){playbackSent=null;playbackSequence=0;}
        if(snapshot!=null && snapshot.Get("playback_mode")=="mt5"){playbackDesired=playbackSent=null;playbackSequence=0;}
        playbackControl.Observe(snapshot,playbackDesired??playbackSent,playbackDesired.HasValue || playbackSequence>0 || pending>0);
        if(playbackDesired.HasValue && unchecked(Environment.TickCount-playbackRequestedAt)>=120)FlushPlaybackRate();
    }
    void FlushPlaybackRate()
    {
        if(playbackSending || !playbackDesired.HasValue || snapshot==null || !snapshot.Fresh || snapshot.Get("playback_protocol")!="1" || pending>0)return;
        decimal desired=playbackDesired.Value;playbackSending=true;
        try
        {
            Send("set_playback_speed",new ChartRequest{Action="set_playback_speed",PlaybackSpeed=desired});
            if(pending>0 && pendingAction=="set_playback_speed"){playbackSent=desired;playbackSequence=pending;}
            // Do not automatically repeat a failed transport or rejected command.
            if(playbackDesired==desired)playbackDesired=null;
        }
        finally{playbackSending=false;}
    }
}
