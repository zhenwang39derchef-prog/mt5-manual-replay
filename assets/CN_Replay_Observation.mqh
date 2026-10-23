#ifndef CN_REPLAY_OBSERVATION_INCLUDED
#define CN_REPLAY_OBSERVATION_INCLUDED
// Read-only tester data channel. It never sends orders or changes chart properties.
// All times below are broker times supplied by the tester, never wall-clock time.
datetime MtfMonthBefore(const datetime value)
{
   MqlDateTime t; TimeToStruct(value,t);
   if(--t.mon==0) { t.mon=12; t.year--; }
   int days[12]={31,28,31,30,31,30,31,31,30,31,30,31};
   if(t.year%4==0 && (t.year%100!=0 || t.year%400==0)) days[1]=29;
   t.day=(int)MathMin(t.day,days[t.mon-1]);
   return StructToTime(t);
}
datetime MtfBucket(const datetime time,const int seconds) { return (datetime)((long)time/seconds*seconds); }
bool MtfClosedAt(const MqlRates &r,const int seconds,const datetime known)
{
   return r.time>0 && r.time+seconds<=known && r.open>0 && r.close>0 &&
          MathIsValidNumber(r.high) && MathIsValidNumber(r.low) &&
          r.high>=MathMax(r.open,r.close) && r.low<=MathMin(r.open,r.close) && r.low>0;
}
void MtfMerge(MqlRates &result,bool &has,const MqlRates &part)
{
   if(!has) { result=part; has=true; return; }
   result.high=MathMax(result.high,part.high); result.low=MathMin(result.low,part.low);
   result.close=part.close; result.tick_volume+=part.tick_volume;
}
double MtfEmaNext(const double previous,const double close,const int period)
{
   double alpha=2.0/(period+1.0); return alpha*close+(1-alpha)*previous;
}

class CReplayMtfSeries
{
private:
   string m_symbol;
   ENUM_TIMEFRAMES m_tf;
   int m_seconds,m_p0,m_p1,m_p2;
   datetime m_floor,m_closed_bucket,m_minute_cache,m_live_bucket;
   MqlRates m_rates[],m_live,m_live_base;
   double m_e0[],m_e1[],m_e2[];
   bool m_loaded,m_has_live,m_has_base;
public:
   CReplayMtfSeries(void) { m_loaded=false; m_has_live=false; m_minute_cache=0; m_live_bucket=0; m_closed_bucket=0; }
   void Configure(const string symbol,const ENUM_TIMEFRAMES tf,const datetime floor,const int a,const int b,const int c)
   {
      m_symbol=symbol; m_tf=tf; m_seconds=PeriodSeconds(tf); m_floor=floor;
      m_p0=a; m_p1=b; m_p2=c;
      m_loaded=false; m_has_live=false; m_minute_cache=0; m_live_bucket=0; m_closed_bucket=0;
      ArrayResize(m_rates,0); ArrayResize(m_e0,0); ArrayResize(m_e1,0); ArrayResize(m_e2,0);
   }
   // Central point-in-time gate used by both live loading and executable tests.
   bool AcceptClosed(const MqlRates &r,const datetime known)
   {
      if(!MtfClosedAt(r,m_seconds,known)) return false;
      int n=ArraySize(m_rates);
      if(n>0 && r.time<=m_rates[n-1].time) return false;
      ArrayResize(m_rates,n+1,4096); m_rates[n]=r;
      ArrayResize(m_e0,n+1,4096); ArrayResize(m_e1,n+1,4096); ArrayResize(m_e2,n+1,4096);
      m_e0[n]=n==0?r.close:MtfEmaNext(m_e0[n-1],r.close,m_p0);
      m_e1[n]=n==0?r.close:MtfEmaNext(m_e1[n-1],r.close,m_p1);
      m_e2[n]=n==0?r.close:MtfEmaNext(m_e2[n-1],r.close,m_p2);
      return true;
   }
   bool SyncClosed(const datetime known)
   {
      datetime bucket=MtfBucket(known,m_seconds);
      if(!m_loaded)
      {
         MqlRates seed[],history[];
         int warmup=(int)MathMin(30000,MathMax(600,MathMax(m_p0,MathMax(m_p1,m_p2))*6));
         int a=CopyRates(m_symbol,m_tf,m_floor-1,warmup,seed);
         int b=CopyRates(m_symbol,m_tf,m_floor,bucket-1,history);
         if(a<0 || b<0) return false;
         for(int i=0;i<a;i++) if(seed[i].time<m_floor) AcceptClosed(seed[i],known);
         for(int i=0;i<b;i++) AcceptClosed(history[i],known);
         if(ArraySize(m_rates)==0) return false;
         m_loaded=true; m_closed_bucket=bucket;
         PrintFormat("MTF_HISTORY: tf=%s floor=%s first=%s last_closed=%s bars=%d known=%s",EnumToString(m_tf),
                     TimeToString(m_floor),TimeToString(m_rates[0].time),TimeToString(m_rates[ArraySize(m_rates)-1].time),ArraySize(m_rates),TimeToString(known));
      }
      if(bucket!=m_closed_bucket)
      {
         int n=ArraySize(m_rates); MqlRates more[];
         datetime from=n>0?m_rates[n-1].time+m_seconds:m_floor;
         if(from<bucket)
         {
            int copied=CopyRates(m_symbol,m_tf,from,bucket-1,more);
            if(copied<0) return false;
            for(int i=0;i<copied;i++) AcceptClosed(more[i],known);
         }
         m_closed_bucket=bucket;
      }
      return true;
   }
   bool UpdateLive(const datetime known,const MqlRates &minute,const bool minute_complete)
   {
      m_has_live=false;
      datetime start=MtfBucket(known,m_seconds),minute_start=MtfBucket(known,60);
      if(start!=m_live_bucket || minute_start!=m_minute_cache)
      {
         m_has_base=false;
         if(minute_start>start)
         {
            MqlRates parts[]; int count=CopyRates(m_symbol,PERIOD_M1,start,minute_start-1,parts);
            if(count<0) return false;
            for(int i=0;i<count;i++)
               if(parts[i].time>=start && MtfClosedAt(parts[i],60,minute_start)) MtfMerge(m_live_base,m_has_base,parts[i]);
         }
         m_live_bucket=start; m_minute_cache=minute_start;
      }
      // Never read OHLC from the unfinished native higher-timeframe bar.
      // The unfinished minute is made only from tick timestamps <= the replay tick.
      if(!minute_complete || minute.time!=minute_start) return false;
      bool has=m_has_base;
      if(has) m_live=m_live_base;
      MtfMerge(m_live,has,minute); m_live.time=start; m_has_live=has;
      return has;
   }
   int Total(void) { return ArraySize(m_rates)+(m_has_live?1:0); }
   int ClosedTotal(void) { return ArraySize(m_rates); }
   int FirstVisible(void)
   {
      int low=0,high=ArraySize(m_rates);
      while(low<high) { int mid=(low+high)/2; if(m_rates[mid].time<m_floor) low=mid+1; else high=mid; }
      return low;
   }
   bool Get(const int index,MqlRates &r)
   {
      int n=ArraySize(m_rates);
      if(index>=0 && index<n) { r=m_rates[index]; return true; }
      if(index==n && m_has_live) { r=m_live; return true; }
      return false;
   }
   double Ema(const int index,const int line)
   {
      int n=ArraySize(m_rates); if(n==0 || index<0 || index>Total()-1) return EMPTY_VALUE;
      if(index<n) return line==0?m_e0[index]:(line==1?m_e1[index]:m_e2[index]);
      return MtfEmaNext(line==0?m_e0[n-1]:(line==1?m_e1[n-1]:m_e2[n-1]),m_live.close,line==0?m_p0:(line==1?m_p1:m_p2));
   }
   int EndAt(const datetime right)
   {
      if(right==0) return Total()-1;
      int low=FirstVisible(),high=Total(); MqlRates r;
      while(low<high) { int mid=(low+high)/2; Get(mid,r); if(r.time<=right) low=mid+1; else high=mid; }
      return low-1;
   }
   bool IsLive(const int index) { return m_has_live && index==ArraySize(m_rates); }
};


CReplayMtfSeries observation_series[4];
string observation_labels[4]={"M5","M15","H1","H4"};
MqlRates observation_minute;
bool observation_minute_complete=false,observation_seal_ready=false;
datetime observation_tick_time=0,observation_last_time=0;
ulong observation_poll_at=0,observation_write_at=0,observation_generation=0;
ulong observation_tick_revision=0,observation_written_revision=0;
bool observation_enabled=false;

void ObservationInitialize()
{
   ENUM_TIMEFRAMES frames[4]={PERIOD_M5,PERIOD_M15,PERIOD_H1,PERIOD_H4};
   datetime floor=ReplayStart>0?ReplayStart-86400*30:TimeCurrent()-86400*30;
   for(int i=0;i<4;i++)observation_series[i].Configure(_Symbol,frames[i],floor,8,34,200);
   ZeroMemory(observation_minute); observation_minute_complete=false;
   observation_tick_time=0; observation_last_time=0; observation_poll_at=0;
   observation_write_at=0; observation_generation=0; observation_enabled=false; observation_seal_ready=false;
   observation_tick_revision=0; observation_written_revision=0;
}
void ObservationTick(const MqlTick &tick)
{
   if(!TesterOnly() || tick.time<=0 || tick.time<observation_tick_time) return;
   double price=SymbolInfoInteger(_Symbol,SYMBOL_CHART_MODE)==SYMBOL_CHART_MODE_LAST?tick.last:tick.bid;
   if(!MathIsValidNumber(price) || price<=0)return;
   datetime minute=MtfBucket(tick.time,60);
   if(observation_minute.time!=minute)
   {
      // A newly delivered minute is complete from its first tick. On first attachment
      // only trust a minute beginning at the configured test start.
      observation_minute_complete=observation_tick_time>0 || (ReplayStart>0 && minute==MtfBucket(ReplayStart,60));
      ZeroMemory(observation_minute); observation_minute.time=minute;
      observation_minute.open=observation_minute.high=observation_minute.low=price;
   }
   observation_minute.high=MathMax(observation_minute.high,price);
   observation_minute.low=MathMin(observation_minute.low,price);
   observation_minute.close=price; observation_minute.tick_volume++;
   observation_tick_time=tick.time;
   observation_tick_revision++;
}
bool ObservationHasUpdate(const datetime known)
{
   return known!=observation_last_time || observation_tick_revision!=observation_written_revision;
}
bool ObservationAtomic(const string name,const string text)
{
   int handle=FileOpen(desktop_dir+name+".tmp",FILE_WRITE|FILE_TXT|FILE_UNICODE|FILE_COMMON);
   if(handle==INVALID_HANDLE)return false;
   uint written=FileWriteString(handle,text);FileFlush(handle);FileClose(handle);
   return written>0 && FileMove(desktop_dir+name+".tmp",FILE_COMMON,desktop_dir+name,FILE_COMMON|FILE_REWRITE);
}
bool ObservationExport(const bool seal)
{
   if(!desktop_ready || !TesterOnly())return false;
   datetime known=TimeCurrent();
   string s=DesktopLine("protocol","1")+DesktopLine("session",DesktopSession)+DesktopLine("boot",desktop_boot)+
      DesktopLine("symbol",_Symbol)+DesktopLine("asof",(string)(long)known)+DesktopLine("stamp",(string)GetTickCount64())+
      DesktopLine("generation",(string)(++observation_generation))+DesktopLine("digits",(string)_Digits);
   for(int f=0;f<4;f++)
   {
      bool loaded=observation_series[f].SyncClosed(known);
      bool live=loaded && observation_tick_time==known && observation_series[f].UpdateLive(known,observation_minute,observation_minute_complete);
      string rows=""; int total=observation_series[f].Total(),first=(int)MathMax(0,total-480);
      if(loaded)for(int i=first;i<total;i++)
      {
         MqlRates r;if(!observation_series[f].Get(i,r))continue;
         // Get may still contain an old live member when no current tick exists.
         if(observation_series[f].IsLive(i) && !live)continue;
         if(rows!="")rows+=";";
         rows+=(string)(long)r.time+","+DoubleToString(r.open,8)+","+DoubleToString(r.high,8)+","+DoubleToString(r.low,8)+","+DoubleToString(r.close,8)+","+
            DoubleToString(observation_series[f].Ema(i,0),8)+","+DoubleToString(observation_series[f].Ema(i,1),8)+","+DoubleToString(observation_series[f].Ema(i,2),8)+","+
            (observation_series[f].IsLive(i)?"1":"0");
      }
      s+=DesktopLine(observation_labels[f],rows)+DesktopLine(observation_labels[f]+"_status",!loaded?"history_unavailable":(live?"forming":"closed_only"));
   }
   s+="complete=1\r\n";
   bool result=ObservationAtomic(seal?"mtf-seal.txt":"mtf-state.txt",s);
   if(result){observation_last_time=known;observation_written_revision=observation_tick_revision;observation_write_at=GetTickCount64();}
   if(seal)observation_seal_ready=result;
   return result;
}
void ObservationPump()
{
   if(!desktop_ready || !TesterOnly())return;
   ulong wall=GetTickCount64();if(wall-observation_poll_at<250)return;observation_poll_at=wall;
   int handle=FileOpen(desktop_dir+"mtf-request.txt",FILE_READ|FILE_TXT|FILE_UNICODE|FILE_COMMON|FILE_SHARE_READ|FILE_SHARE_WRITE);
   string session="",boot="",protocol="",complete="",enabled="",stamp="";
   if(handle!=INVALID_HANDLE)
   {
      while(!FileIsEnding(handle))
      {
         string line=FileReadString(handle);int at=StringFind(line,"=");if(at<1)continue;
         string key=StringSubstr(line,0,at),value=StringSubstr(line,at+1);
         if(key=="session")session=value;else if(key=="boot")boot=value;else if(key=="protocol")protocol=value;
         else if(key=="complete")complete=value;else if(key=="enabled")enabled=value;else if(key=="stamp")stamp=value;
      }
      FileClose(handle);
   }
   observation_enabled=protocol=="1" && session==DesktopSession && boot==desktop_boot && complete=="1" && enabled=="1" &&
      StringToInteger(stamp)>0 && (uint)((uint)wall-(uint)StringToInteger(stamp))<3500;
   if(observation_enabled && (observation_generation==0 || (ObservationHasUpdate(TimeCurrent()) && wall-observation_write_at>=500)))ObservationExport(false);
}
#endif
