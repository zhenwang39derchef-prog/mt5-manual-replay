#property copyright "Local historical practice tool"
#property version "1.00"
#property strict
#property indicator_chart_window
#property indicator_buffers 3
#property indicator_plots 3
#property indicator_type1 DRAW_LINE
#property indicator_color1 clrOrange
#property indicator_width1 2
#property indicator_type2 DRAW_LINE
#property indicator_color2 clrDeepSkyBlue
#property indicator_width2 2
#property indicator_type3 DRAW_LINE
#property indicator_color3 clrViolet
#property indicator_width3 2

input int FastEMA=8;
input int MiddleEMA=34;
input int SlowEMA=200;
double fast[],middle[],slow[];
int handles[3]={INVALID_HANDLE,INVALID_HANDLE,INVALID_HANDLE};

int OnInit()
{
   if(FastEMA<1 || MiddleEMA<1 || SlowEMA<1) return INIT_PARAMETERS_INCORRECT;
   SetIndexBuffer(0,fast,INDICATOR_DATA);
   SetIndexBuffer(1,middle,INDICATOR_DATA);
   SetIndexBuffer(2,slow,INDICATOR_DATA);
   ArraySetAsSeries(fast,true); ArraySetAsSeries(middle,true); ArraySetAsSeries(slow,true);
   int periods[3]; periods[0]=FastEMA; periods[1]=MiddleEMA; periods[2]=SlowEMA;
   for(int i=0;i<3;i++)
   {
      handles[i]=iMA(_Symbol,_Period,periods[i],0,MODE_EMA,PRICE_CLOSE);
      if(handles[i]==INVALID_HANDLE) return INIT_FAILED;
      PlotIndexSetInteger(i,PLOT_DRAW_BEGIN,periods[i]-1);
      PlotIndexSetString(i,PLOT_LABEL,"EMA "+IntegerToString(periods[i]));
      PlotIndexSetDouble(i,PLOT_EMPTY_VALUE,EMPTY_VALUE);
   }
   IndicatorSetString(INDICATOR_SHORTNAME,StringFormat("三条EMA (%d/%d/%d)",FastEMA,MiddleEMA,SlowEMA));
   IndicatorSetInteger(INDICATOR_DIGITS,_Digits);
   return INIT_SUCCEEDED;
}

int OnCalculate(const int rates_total,const int prev_calculated,const datetime &time[],
                const double &open[],const double &high[],const double &low[],const double &close[],
                const long &tick_volume[],const long &volume[],const int &spread[])
{
   for(int i=0;i<3;i++) if(BarsCalculated(handles[i])<rates_total) return prev_calculated;
   int count=(prev_calculated<=0 || prev_calculated>rates_total)?rates_total:rates_total-prev_calculated+1;
   if(CopyBuffer(handles[0],0,0,count,fast)!=count) return prev_calculated;
   if(CopyBuffer(handles[1],0,0,count,middle)!=count) return prev_calculated;
   if(CopyBuffer(handles[2],0,0,count,slow)!=count) return prev_calculated;
   return rates_total;
}

void OnDeinit(const int reason)
{
   for(int i=0;i<3;i++) if(handles[i]!=INVALID_HANDLE) IndicatorRelease(handles[i]);
}
