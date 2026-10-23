// Historical elapsed-time pacing. No Sleep(), price substitution or tick deletion.
bool playback_clock_ready=false,playback_clock_paused=false,playback_clock_waiting=false;
bool playback_actual_ready=false,playback_meter_warmup=false;
bool playback_native=false;
double playback_target=1,playback_credit_ms=0,playback_gap_ms=0,playback_actual=0;
long playback_history_ms=0,playback_event_ms=0,playback_meter_history_ms=0;
ulong playback_wall_us=0,playback_active_us=0,playback_meter_active_us=0;
string playback_request_speed="";

void PlaybackClockReset(const double rate,const ulong wall_us)
{
   playback_target=rate;playback_clock_ready=false;playback_clock_paused=false;playback_clock_waiting=false;playback_native=false;
   playback_actual_ready=false;playback_meter_warmup=false;playback_credit_ms=playback_gap_ms=playback_actual=0;
   playback_history_ms=playback_event_ms=playback_meter_history_ms=0;
   playback_wall_us=wall_us;playback_active_us=playback_meter_active_us=0;playback_request_speed="";
}
void PlaybackClockAdvance(const ulong wall_us)
{
   if(wall_us<playback_wall_us){playback_wall_us=wall_us;playback_meter_warmup=true;playback_actual_ready=false;return;}
   ulong elapsed=wall_us-playback_wall_us;playback_wall_us=wall_us;
   if(!playback_clock_ready || playback_clock_paused)return;
   playback_active_us+=elapsed;
   if(!playback_native)playback_credit_ms+=(double)elapsed/1000.0*playback_target;
}
void PlaybackClockMode(const bool native_mode,const ulong wall_us)
{
   if(playback_native==native_mode)return;
   double rate=playback_target;bool paused=playback_clock_paused;
   PlaybackClockReset(rate,wall_us);playback_native=native_mode;playback_clock_paused=paused;
}
bool PlaybackClockSpeed(const double rate,const ulong wall_us)
{
   if(!MathIsValidNumber(rate) || rate<1 || rate>100)return false;
   PlaybackClockAdvance(wall_us);playback_target=rate;playback_actual_ready=false;playback_meter_warmup=true;return true;
}
void PlaybackClockPause(const bool paused,const ulong wall_us)
{
   PlaybackClockAdvance(wall_us);
   if(playback_clock_paused!=paused){playback_clock_paused=paused;playback_actual_ready=false;playback_meter_warmup=true;}
}
bool PlaybackClockBegin(const long history_ms,const ulong wall_us)
{
   if(history_ms<=0)return false;
   if(!playback_clock_ready)
   {
      playback_clock_ready=true;playback_history_ms=playback_event_ms=playback_meter_history_ms=history_ms;
      playback_wall_us=wall_us;playback_credit_ms=0;playback_active_us=playback_meter_active_us=0;
      return false; // Loading/warm-up before the first quote is not replay time.
   }
   PlaybackClockAdvance(wall_us);
   if(history_ms<=playback_history_ms)return false; // Duplicate/coarse timer timestamp: never rewind.
   playback_event_ms=history_ms;playback_gap_ms=(double)(history_ms-playback_history_ms);
   playback_clock_waiting=!playback_native && playback_credit_ms+0.000001<playback_gap_ms;return true;
}
bool PlaybackClockDue(const ulong wall_us)
{
   PlaybackClockAdvance(wall_us);
   playback_clock_waiting=playback_clock_paused || (!playback_native && playback_credit_ms+0.000001<playback_gap_ms);
   return !playback_clock_waiting;
}
void PlaybackClockRelease(const ulong wall_us)
{
   PlaybackClockAdvance(wall_us);playback_history_ms=playback_event_ms;
   playback_clock_waiting=false;playback_gap_ms=0;playback_credit_ms=0;
   // Unused wall-clock credit is discarded: a slow/paused tester must not later burst through history.
   if(playback_meter_warmup)
   {
      playback_meter_warmup=false;playback_meter_history_ms=playback_history_ms;
      playback_meter_active_us=playback_active_us;playback_actual_ready=false;return;
   }
   ulong elapsed=playback_active_us-playback_meter_active_us;
   if(elapsed>=1000000)
   {
      long historical=playback_history_ms-playback_meter_history_ms;
      if(historical>0){playback_actual=(double)historical*1000.0/(double)elapsed;playback_actual_ready=MathIsValidNumber(playback_actual);}
      playback_meter_history_ms=playback_history_ms;playback_meter_active_us=playback_active_us;
   }
}
double PlaybackRemainingWallMs()
{return playback_native?0:MathMax(0,playback_gap_ms-playback_credit_ms)/playback_target;}
string PlaybackTelemetry()
{
   return DesktopLine("playback_protocol","1")+DesktopLine("playback_mode_protocol","1")+DesktopLine("playback_mode",playback_native?"mt5":"observe")+DesktopLine("playback_target",DoubleToString(playback_target,3))+
      DesktopLine("playback_actual",DoubleToString(playback_actual,3))+DesktopLine("playback_actual_ready",playback_actual_ready?"1":"0")+
      DesktopLine("playback_waiting",playback_clock_waiting?"1":"0")+DesktopLine("playback_paused",playback_clock_paused?"1":"0")+
      DesktopLine("playback_remaining_wall_ms",DoubleToString(PlaybackRemainingWallMs(),1))+
      DesktopLine("playback_policy",playback_native?"mt5_native_speed":"historical_elapsed_including_gaps");
}
bool PlaybackControl(const string action)
{
   if(action!="set_playback_speed" && action!="play_slow" && action!="play_fast")return false;
   if(!TesterOnly()){Say("倍速控制仅用于历史回放",clrOrange);return true;}
   if(action=="play_fast")
   {PlaybackClockMode(true,GetMicrosecondCount());slow_playback=false;Say("MT5快放：速度由测试器原生滑块控制，观察倍速已停用",clrLightGreen);return true;}
   if(action=="play_slow")
   {PlaybackClockMode(false,GetMicrosecondCount());slow_playback=true;Say("已返回观察，目标 "+DoubleToString(playback_target,2)+"×",clrLightGreen);return true;}
   double rate=playback_target;
   if(action=="set_playback_speed" && !DesktopNumber(playback_request_speed,rate))
   {Say("回放倍率无效，目标速度未改变",clrOrange);return true;}
   if(!PlaybackClockSpeed(rate,GetMicrosecondCount()))
   {Say("回放倍率须在1倍至100倍之间",clrOrange);return true;}
   PlaybackClockMode(false,GetMicrosecondCount());slow_playback=true;
   Say("目标回放速度 "+DoubleToString(playback_target,2)+"×（按历史时间）",clrLightGreen);return true;
}
