#property copyright "Local historical practice tool"
#property version "1.00"
#property strict
#property indicator_separate_window
#property indicator_buffers 1
#property indicator_plots 1
#property indicator_label1 "BIAS %"
#property indicator_type1 DRAW_LINE
#property indicator_color1 clrGold
#property indicator_width1 2
#property indicator_level1 0.0
input int BiasPeriod=20;
double bias[];
int average=INVALID_HANDLE;
int OnInit()
{
   if(BiasPeriod<1) return INIT_PARAMETERS_INCORRECT;
   SetIndexBuffer(0,bias,INDICATOR_DATA); ArraySetAsSeries(bias,true);
   PlotIndexSetInteger(0,PLOT_DRAW_BEGIN,BiasPeriod-1);
   PlotIndexSetDouble(0,PLOT_EMPTY_VALUE,EMPTY_VALUE);
   IndicatorSetString(INDICATOR_SHORTNAME,StringFormat("BIAS SMA%d (%%)",BiasPeriod));
   IndicatorSetInteger(INDICATOR_DIGITS,3);
   average=iMA(_Symbol,_Period,BiasPeriod,0,MODE_SMA,PRICE_CLOSE);
   return average==INVALID_HANDLE?INIT_FAILED:INIT_SUCCEEDED;
}
int OnCalculate(const int total,const int previous,const datetime &time[],const double &open[],
                const double &high[],const double &low[],const double &close[],const long &tick_volume[],
                const long &volume[],const int &spread[])
{
   if(total<BiasPeriod || BarsCalculated(average)<total) return previous;
   ArraySetAsSeries(close,true);
   int count=previous<=0 || previous>total?total:total-previous+1;
   double ma[]; ArraySetAsSeries(ma,true);
   if(CopyBuffer(average,0,0,count,ma)!=count) return previous;
   for(int i=0;i<count;i++)
      bias[i]=i>total-BiasPeriod || ma[i]==0 || ma[i]==EMPTY_VALUE?EMPTY_VALUE:(close[i]-ma[i])/ma[i]*100.0;
   return total;
}
void OnDeinit(const int reason) { if(average!=INVALID_HANDLE) IndicatorRelease(average); }
