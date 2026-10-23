#ifndef CN_REPLAY_EQUITY_MQH
#define CN_REPLAY_EQUITY_MQH

// Observation-only accounting. No order submission or playback controls.
struct ReplayEquityHour
{
   datetime hour,last_time;
   double balance,equity,high,low,drawdown_percent;
   int positions,detail_start,detail_count;
   bool details_observed;
};

struct ReplayPositionMark
{
   ulong id;
   double volume,profit,swap;
};

class CReplayEquityRecorder
{
private:
   ReplayEquityHour hours[];
   ReplayPositionMark marks[];
   double peak,max_amount,max_percent;
   datetime peak_time,last_time;
   double amount_peak,amount_trough,percent_peak,percent_trough;
   datetime amount_peak_time,amount_trough_time,percent_peak_time,percent_trough_time;
   string Q(string value)
   {
      StringReplace(value,"\"","\"\"");
      return "\""+value+"\"";
   }
   string Num(const double value,const int digits=8) { return Q(DoubleToString(value,digits)); }
   string Date(const datetime value) { return Q(value>0?TimeToString(value,TIME_DATE|TIME_SECONDS):""); }

public:
   void Reset(const datetime when,const double initial_equity)
   {
      ArrayResize(hours,0);ArrayResize(marks,0);
      peak=initial_equity;peak_time=when;last_time=0;
      max_amount=0;max_percent=0;
      amount_peak=0;amount_trough=0;percent_peak=0;percent_trough=0;
      amount_peak_time=0;amount_trough_time=0;percent_peak_time=0;percent_trough_time=0;
   }
   void Observe(const datetime when,const double balance,const double equity,const int positions)
   {
      if(when<=0 || when<last_time) return;
      last_time=when;
      if(equity>peak) { peak=equity;peak_time=when; }
      double amount=peak-equity;
      double percent=peak>0?amount/peak*100.0:0;
      if(amount>max_amount)
      {
         max_amount=amount;amount_peak=peak;amount_trough=equity;
         amount_peak_time=peak_time;amount_trough_time=when;
      }
      if(percent>max_percent)
      {
         max_percent=percent;percent_peak=peak;percent_trough=equity;
         percent_peak_time=peak_time;percent_trough_time=when;
      }
      datetime hour=(datetime)(((long)when/3600)*3600);
      int n=ArraySize(hours);
      if(n==0 || hours[n-1].hour!=hour)
      {
         ArrayResize(hours,n+1,1024);
         hours[n].hour=hour;hours[n].high=equity;hours[n].low=equity;
         hours[n].detail_start=ArraySize(marks);hours[n].detail_count=0;hours[n].details_observed=false;
         n++;
      }
      int at=n-1;
      hours[at].last_time=when;hours[at].balance=balance;hours[at].equity=equity;
      hours[at].high=MathMax(hours[at].high,equity);hours[at].low=MathMin(hours[at].low,equity);
      hours[at].drawdown_percent=percent;hours[at].positions=positions;
   }
   // Update numeric observations only. Formatting/file IO remains snapshot-throttled.
   void ObservePositions(const ulong magic)
   {
      int h=ArraySize(hours)-1;if(h<0)return;
      int base=hours[h].detail_start,total=hours[h].positions,n=0;
      if(ArraySize(marks)!=base+total)ArrayResize(marks,base+total,1024);
      for(int i=0;i<total;i++)
      {
         ulong ticket=PositionGetTicket(i);
         if(ticket==0 || (ulong)PositionGetInteger(POSITION_MAGIC)!=magic || PositionGetString(POSITION_SYMBOL)!=_Symbol)continue;
         marks[base+n].id=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
         marks[base+n].volume=PositionGetDouble(POSITION_VOLUME);
         marks[base+n].profit=PositionGetDouble(POSITION_PROFIT);
         marks[base+n].swap=PositionGetDouble(POSITION_SWAP);n++;
      }
      if(ArraySize(marks)!=base+n)ArrayResize(marks,base+n,1024);
      hours[h].detail_count=n;hours[h].details_observed=true;
   }
   // Pure fixture entry point for credential-free arithmetic tests.
   void ObservePositionFixture(ReplayPositionMark &values[])
   {
      int h=ArraySize(hours)-1;if(h<0)return;
      int base=hours[h].detail_start,n=ArraySize(values);
      ArrayResize(marks,base+n,1024);
      for(int i=0;i<n;i++)marks[base+i]=values[i];
      hours[h].detail_count=n;hours[h].details_observed=true;
   }
   string PositionDetails(const int h)
   {
      if(!hours[h].details_observed)return "";
      string result="v1;";
      for(int i=0;i<hours[h].detail_count;i++)
      {
         ReplayPositionMark mark=marks[hours[h].detail_start+i];
         result+=(string)mark.id+"|"+DoubleToString(mark.volume,8)+"|"+DoubleToString(mark.profit,8)+"|"+DoubleToString(mark.swap,8)+";";
      }
      return result;
   }
   double Peak() { return peak; }
   double MaxAmount() { return max_amount; }
   double MaxPercent() { return max_percent; }
   int HourCount() { return ArraySize(hours); }
   string HourHeader()
   {
      return "小时起点,末次观察时间,余额,净值,小时最高净值,小时最低净值,当前回撤百分比,持仓数,时段状态,持仓估值明细";
   }
   string HourRows(const string metadata_prefix,const bool final_snapshot)
   {
      string result="";
      int count=ArraySize(hours);
      for(int i=0;i<count;i++)
      {
         string state=i==count-1?(final_snapshot?"回放结束时段":"当前小时更新中"):"已结束时段";
         result+=metadata_prefix+Date(hours[i].hour)+","+Date(hours[i].last_time)+","+
            Num(hours[i].balance)+","+Num(hours[i].equity)+","+Num(hours[i].high)+","+Num(hours[i].low)+","+
            Num(hours[i].drawdown_percent)+","+Q((string)hours[i].positions)+","+Q(state)+","+Q(PositionDetails(i))+"\r\n";
      }
      return result;
   }
   string DrawdownHeader()
   {
      return "最大金额回撤峰值净值,最大金额回撤峰值时间,最大金额回撤谷值净值,最大金额回撤谷值时间,最大百分比回撤峰值净值,最大百分比回撤峰值时间,最大百分比回撤谷值净值,最大百分比回撤谷值时间";
   }
   string DrawdownFields()
   {
      return (max_amount>0?Num(amount_peak):Q(""))+","+Date(amount_peak_time)+","+
         (max_amount>0?Num(amount_trough):Q(""))+","+Date(amount_trough_time)+","+
         (max_percent>0?Num(percent_peak):Q(""))+","+Date(percent_peak_time)+","+
         (max_percent>0?Num(percent_trough):Q(""))+","+Date(percent_trough_time);
   }
};
#endif
