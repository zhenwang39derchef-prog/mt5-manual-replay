#property copyright "Local historical practice tool"
#property version "1.45"
#property strict
#property description "中文手动历史回放。仅限策略测试器的可视化模式。"
#property description "开仓及主动平仓由按钮触发；支持多单和快慢切换；默认不设置止损止盈。"
#property tester_indicator "CN_Replay_3EMA.ex5"

// CUSTOM_TESTER_DECLARATIONS
#include <Trade/Trade.mqh>
#include "CN_Replay_Equity.mqh"
#include "CN_Replay_PriceLots.mqh"

input bool AutoPriceLots=true;        // 新单按金价反比计算手数
input double PriceLotBase=0.10;         // 基准手数
input double PriceLotReference=2000;    // 固定基准金价
input double InitialLots=0.01;           // 初始手数
input double InitialSLPrice=0.0;        // 初始止损价格，0表示不设置
input double InitialTPPrice=0.0;        // 初始止盈价格，0表示不设置
input int FastEMA=8;                    // 快EMA（橙色）
input int MiddleEMA=34;                 // 中EMA（蓝色）
input int SlowEMA=200;                  // 慢EMA（紫色）
input ulong MagicNumber=817342001;      // 本面板的测试订单标识
input bool StartInSlowMode=true;        // 开始时使用慢放，便于操作
input int SlowTickDelayMs=50;           // 慢放每个报价的最短处理时间：1至250毫秒
input bool UseObservationPacing=true;   // 观察时按1至100倍计时；快放交给MT5原生滑块
input double ReplaySpeed=1;            // 历史时间/现实时间倍率，1至100；旧Tick延迟不再用于新版倍速


input bool ShowEMA=true;                // 显示三条EMA
input bool ShowBands=false;             // 显示布林带
input int BandsPeriod=20;               // 布林带周期
input double BandsDeviation=2.0;        // 布林带标准差倍数
input bool ShowRSI=true;                // 显示RSI
input int RSIPeriod=14;                 // RSI周期
input bool ShowMACD=false;              // 显示MACD
input int MACDFast=12;                  // MACD快周期
input int MACDSlow=26;                  // MACD慢周期
input int MACDSignal=9;                 // MACD信号周期
input bool ShowATR=false;               // 显示ATR
input int ATRPeriod=14;                 // ATR周期
input bool ShowBIAS=false;              // 显示BIAS百分比
input int BIASPeriod=20;                // BIAS均线周期（SMA）
input bool UseCustom1=false;            // 启用扩展指标1
input string CustomIndicator1="";       // 扩展指标1，默认参数，由入口登记
input bool CustomSeparate1=true;        // 扩展指标1放副图
input bool UseCustom2=false;            // 启用扩展指标2
input string CustomIndicator2="";       // 扩展指标2，默认参数，由入口登记
input bool CustomSeparate2=true;        // 扩展指标2放副图
input datetime ReplayStart=0;             // 回放区间起点，由入口填写
input datetime ReplayEndExclusive=0;      // 回放区间终点（不含），由入口填写
input string DesktopSession="";           // 桌面浮窗会话，由入口自动生成
input bool TrainingEnabled=false;         // 选择题训练协议；旧场默认关闭
input bool RequireTrainingPlan=true;      // 新开单需要事前计划
int extra_handles[];
ulong layout_started=0;
int layout_attempts=0;

void FitPracticeLayout()
{
   if(layout_started==0 || GetTickCount64()-layout_started<(ulong)(2000+layout_attempts*2000) || layout_attempts>=3) return;
   layout_attempts++;
   int windows=(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL),available=0;
   for(int i=0;i<windows;i++) available+=(int)ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS,i);
   if(windows<2 || available<1) return;
   int height=(int)MathMax(45,MathMin(90,(available-560)/(windows-1)));
   bool ok=true;
   for(int i=0;i<windows;i++) ChartSetInteger(0,CHART_HEIGHT_IN_PIXELS,i,-1);
   ChartRedraw(0);
   for(int i=1;i<windows;i++)
   {
      if(!ChartSetInteger(0,CHART_HEIGHT_IN_PIXELS,i,height)) ok=false;
      ChartRedraw(0);
   }
   // Leave the price window flexible so it receives the remaining height.
   ChartSetInteger(0,CHART_HEIGHT_IN_PIXELS,0,-1);
   ChartRedraw(0);
   PrintFormat("CN_REPLAY_LAYOUT: windows=%d available=%d pane=%d main=%d ok=%s error=%d",windows,available,height,
               (int)ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS),ok?"true":"false",GetLastError());
}

bool AddPracticeIndicator(const int handle,const bool separate,const string label)
{
   if(handle==INVALID_HANDLE) { Print("CN_REPLAY_INDICATOR_FAILED: ",label," create error=",GetLastError()); return false; }
   int window=separate?(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL):0;
   if(!ChartIndicatorAdd(0,window,handle))
   { Print("CN_REPLAY_INDICATOR_FAILED: ",label," add error=",GetLastError()); IndicatorRelease(handle); return false; }
   int n=ArraySize(extra_handles); ArrayResize(extra_handles,n+1); extra_handles[n]=handle;
   Print("CN_REPLAY_INDICATOR: ",label," window=",window);
   return true;
}

bool LoadPracticeIndicators()
{
   int available=0;
   for(int w=0;w<(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL);w++) available+=(int)ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS,w);
   // The launcher owns this tester layout; clear template indicators first.
   for(int w=(int)ChartGetInteger(0,CHART_WINDOWS_TOTAL)-1;w>=0;w--)
      for(int i=ChartIndicatorsTotal(0,w)-1;i>=0;i--) ChartIndicatorDelete(0,w,ChartIndicatorName(0,w,i));
   // Suppress the tester's automatic second copy; ChartIndicatorAdd below is explicit.
   TesterHideIndicators(true);
   if(ShowEMA && !AddPracticeIndicator(iCustom(_Symbol,_Period,"CN_Replay_3EMA",FastEMA,MiddleEMA,SlowEMA),false,"EMA")) return false;
   if(ShowBands && !AddPracticeIndicator(iBands(_Symbol,_Period,BandsPeriod,0,BandsDeviation,PRICE_CLOSE),false,"Bollinger")) return false;
   if(ShowRSI && !AddPracticeIndicator(iRSI(_Symbol,_Period,RSIPeriod,PRICE_CLOSE),true,"RSI")) return false;
   if(ShowMACD && !AddPracticeIndicator(iMACD(_Symbol,_Period,MACDFast,MACDSlow,MACDSignal,PRICE_CLOSE),true,"MACD")) return false;
   if(ShowATR && !AddPracticeIndicator(iATR(_Symbol,_Period,ATRPeriod),true,"ATR")) return false;
   if(ShowBIAS && !AddPracticeIndicator(iCustom(_Symbol,_Period,"CN_Replay_BIAS",BIASPeriod),true,"BIAS")) return false;
   if(UseCustom1 && CustomIndicator1!="" && !AddPracticeIndicator(iCustom(_Symbol,_Period,CustomIndicator1),CustomSeparate1,CustomIndicator1)) return false;
   if(UseCustom2 && CustomIndicator2!="" && !AddPracticeIndicator(iCustom(_Symbol,_Period,CustomIndicator2),CustomSeparate2,CustomIndicator2)) return false;
   ChartRedraw(0);
   return true;
}

CTrade trade;
string prefix="CN_REPLAY_";
string buttons[];
double panel_lots=0.01,sl_price=0.0,tp_price=0.0;
double csv_sizing_price=0,csv_sizing_volume=0;
int ema_handle=INVALID_HANDLE;
bool pumping=false;
ulong last_poll=0,last_render=0;
bool hedging_account=false;
bool slow_playback=true;
// Finish belongs to this tester boot. It never survives a new run or unlocks a sealed run.
bool finish_sealed=false,finish_end_requested=false,finish_stop_dispatched=false,finish_snapshot_ready=false;
datetime finish_history_time=0;
ulong finish_seal_seq=0;
long finish_chart_hwnd=0;
long finish_chart_id=0;
ulong finish_handle_query_after=0;
int finish_handle_error=0;
bool finish_handle_api_ok=false;
string finish_end_error="";
ulong selected_ticket=0,close_all_armed_at=0;
ulong close_all_snapshot[];
string panel_status="准备就绪，等待你点击买入或卖出";
color status_color=clrLightSteelBlue;
string editing="",edit_text="";
color bg=C'18,27,42',card=C'28,40,60',ink=C'226,232,240',muted=C'148,163,184';

// Read-only visual-tester recorder. Money fields retain MT5's signed fee convention.
struct CsvPosition
{
   ulong id;
   long opened,closed;
   int direction;
   double entered,exited,entry_value,exit_value,profit,commission,swap,fee,sl,tp;
   string reason,comment,source;
   bool unsupported;
};
string csv_dir="",csv_session="",csv_boot="",csv_error="",csv_action_rows[];
bool csv_ready=false,csv_final=false;
ulong csv_generation=0,csv_poll_at=0,csv_action_seq=0;
double csv_initial=0,csv_peak=0,csv_dd_amount=0,csv_dd_percent=0;
datetime csv_started=0;
CReplayEquityRecorder csv_equity;
string csv_pending_entry="";
string csv_pending_action="",csv_pending_source="",csv_pending_lots="",csv_pending_sl="",csv_pending_tp="";
ulong csv_pending_ticket=0,csv_pending_seq=0;

#include "CN_Replay_Playback.mqh"
#include "CN_Replay_Training.mqh"

string CsvCell(string text)
{
   StringReplace(text,"\"","\"\"");
   return "\""+text+"\"";
}
string CsvText(string text)
{
   string probe=text; StringTrimLeft(probe);
   if(StringLen(probe)>0 && StringFind("=+-@",StringSubstr(probe,0,1))>=0) text="'"+text;
   return CsvCell(text);
}
string CsvN(const double value,const int digits=2) { return CsvCell(DoubleToString(value,digits)); }
string CsvId(const ulong value) { return CsvCell((string)value); }
string CsvTime(const long milliseconds)
{
   if(milliseconds<=0) return CsvCell("");
   return CsvCell(TimeToString((datetime)(milliseconds/1000),TIME_DATE|TIME_SECONDS)+StringFormat(".%03d",(int)(milliseconds%1000)));
}
string CsvPrefix() { return CsvId(csv_generation)+","+CsvCell(csv_session)+","+CsvCell(csv_boot)+","+CsvText(_Symbol)+","+CsvCell(EnumToString(_Period))+","; }
string CsvMetaHeader() { return "快照号,回放ID,运行ID,品种,周期,"; }

string CsvActionName(const string action)
{
   if(action=="place_limit") return "图表限价挂单";
   if(action=="modify_limit") return "拖线修改挂单";
   if(action=="modify_position") return "拖线修改持仓止盈止损";
   if(action=="cancel_limit") return "撤销图表挂单";
   if(action=="cancel_all_limits") return "撤销全部挂单或确认";
   if(action=="buy") return "买入做多";
   if(action=="sell") return "卖出做空";
   if(action=="apply") return "应用止盈止损";
   if(action=="close") return "平仓所选单";
   if(action=="close_all") return "全部平仓或确认";
   if(action=="set") return "更新填写值";
   if(action=="read_stops") return "读取所选单止盈止损";
   if(action=="previous") return "选择上一单";
   if(action=="next") return "选择下一单";
   if(action=="play_slow") return "切换慢放";
   if(action=="play_fast") return "切换快放";
   if(action=="training_pause") return "暂停主动练习并保存";
   if(action=="training_passive") return "转入放置运行";
   if(action=="training_active") return "恢复主动练习";
   if(action=="training_expire") return "训练挂单到期";
   if(action=="finish_seal") return "确认收工并封存现场";
   if(action=="finish_end") return "保存复盘后正常结束测试";
   return action;
}

void CsvError(const string detail)
{
   if(csv_error!=detail) Print("CN_REPLAY_CSV_ERROR: ",detail);
   csv_error=detail;
}

bool CsvWrite(const string name,const string content)
{
   string temp=csv_dir+name+".tmp";
   int handle=FileOpen(temp,FILE_WRITE|FILE_BIN|FILE_COMMON);
   if(handle==INVALID_HANDLE) { CsvError("无法创建CSV："+name+"，代码 "+(string)GetLastError()); return false; }
   uchar data[];
   int count=StringToCharArray("\xFEFF"+content,data,0,WHOLE_ARRAY,CP_UTF8)-1;
   uint written=FileWriteArray(handle,data,0,count);
   FileFlush(handle); FileClose(handle);
   if(written!=(uint)count || !FileMove(temp,FILE_COMMON,csv_dir+name,FILE_COMMON|FILE_REWRITE))
   { CsvError("无法保存CSV："+name+"，代码 "+(string)GetLastError()); return false; }
   return true;
}

void CsvObserveEquity()
{
   if(!csv_ready) return;
   csv_equity.Observe(TimeCurrent(),AccountInfoDouble(ACCOUNT_BALANCE),AccountInfoDouble(ACCOUNT_EQUITY),PositionsTotal());
   csv_equity.ObservePositions(MagicNumber);
   csv_peak=csv_equity.Peak();
   csv_dd_amount=csv_equity.MaxAmount();
   csv_dd_percent=csv_equity.MaxPercent();
}

int CsvFind(CsvPosition &positions[],const ulong id)
{
   for(int i=0;i<ArraySize(positions);i++) if(positions[i].id==id) return i;
   return -1;
}

string CsvReason(const ulong deal)
{
   string comment=HistoryDealGetString(deal,DEAL_COMMENT),lower=comment; StringToLower(lower);
   if(StringFind(lower,"end of test")>=0) return "测试结束强平";
   ENUM_DEAL_REASON reason=(ENUM_DEAL_REASON)HistoryDealGetInteger(deal,DEAL_REASON);
   if(reason==DEAL_REASON_SL) return "止损触发";
   if(reason==DEAL_REASON_TP) return "止盈触发";
   if(reason==DEAL_REASON_SO) return "保证金强平";
   if(reason==DEAL_REASON_EXPERT) return "面板手动平仓";
   return EnumToString(reason);
}

// The fold is independent of account APIs and is exercised with partial-fill fixtures.
void CsvFold(CsvPosition &p,const ENUM_DEAL_ENTRY entry,const ENUM_DEAL_TYPE type,const long when,
             const double volume,const double price,const double profit,const double commission,
             const double swap,const double fee,const double sl,const double tp,const string reason,const string comment)
{
   if(entry==DEAL_ENTRY_IN)
   {
      if(p.opened==0 || when<p.opened) { p.opened=when; p.direction=type==DEAL_TYPE_BUY?1:-1; }
      p.entered+=volume; p.entry_value+=volume*price;
   }
   else if(entry==DEAL_ENTRY_OUT || entry==DEAL_ENTRY_OUT_BY)
   {
      p.exited+=volume; p.exit_value+=volume*price;
      if(when>=p.closed) { p.closed=when; p.reason=reason; }
   }
   else p.unsupported=true;
   p.profit+=profit; p.commission+=commission; p.swap+=swap; p.fee+=fee;
   p.sl=sl; p.tp=tp; p.comment=comment;
}

bool CsvLive(const ulong identifier,double &volume,double &floating,double &swap,double &sl,double &tp)
{
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(ticket==0 || PositionGetString(POSITION_SYMBOL)!=_Symbol ||
         (ulong)PositionGetInteger(POSITION_MAGIC)!=MagicNumber ||
         (ulong)PositionGetInteger(POSITION_IDENTIFIER)!=identifier) continue;
      volume=PositionGetDouble(POSITION_VOLUME); floating=PositionGetDouble(POSITION_PROFIT);
      swap=PositionGetDouble(POSITION_SWAP); sl=PositionGetDouble(POSITION_SL); tp=PositionGetDouble(POSITION_TP);
      return true;
   }
   return false;
}

bool CsvSnapshot(const bool force=false)
{
   if(!csv_ready || !TesterOnly()) return false;
   ulong now=GetTickCount64();
   if(!force && now-csv_poll_at<1500) return true;
   csv_poll_at=now; CsvObserveEquity();
   if(!HistorySelect(0,TimeCurrent()+86400)) { CsvError("无法读取测试成交历史"); return false; }
   CsvPosition positions[];
   // Discover ownership from entry deals, then include every exit for that identifier.
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong deal=HistoryDealGetTicket(i);
      ENUM_DEAL_TYPE type=(ENUM_DEAL_TYPE)HistoryDealGetInteger(deal,DEAL_TYPE);
      if((type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL) || HistoryDealGetString(deal,DEAL_SYMBOL)!=_Symbol ||
         (ulong)HistoryDealGetInteger(deal,DEAL_MAGIC)!=MagicNumber ||
         (ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal,DEAL_ENTRY)!=DEAL_ENTRY_IN) continue;
      ulong id=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
      if(id==0 || CsvFind(positions,id)>=0) continue;
      int n=ArraySize(positions); ArrayResize(positions,n+1); ZeroMemory(positions[n]); positions[n].id=id;
   }
   csv_generation++;
   string deals=CsvMetaHeader()+"成交编号,订单编号,持仓ID,历史成交时间,成交方向,进出场,手数,价格,毛盈亏,佣金,已结算隔夜费,其他手续费,净现金盈亏,止损价格,止盈价格,成交原因,备注,账户币种\r\n";
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong deal=HistoryDealGetTicket(i),id=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
      int index=CsvFind(positions,id); if(index<0) continue;
      ENUM_DEAL_TYPE type=(ENUM_DEAL_TYPE)HistoryDealGetInteger(deal,DEAL_TYPE);
      if(type!=DEAL_TYPE_BUY && type!=DEAL_TYPE_SELL) continue;
      ENUM_DEAL_ENTRY entry=(ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal,DEAL_ENTRY);
      long when=HistoryDealGetInteger(deal,DEAL_TIME_MSC);
      double volume=HistoryDealGetDouble(deal,DEAL_VOLUME),price=HistoryDealGetDouble(deal,DEAL_PRICE),
         profit=HistoryDealGetDouble(deal,DEAL_PROFIT),commission=HistoryDealGetDouble(deal,DEAL_COMMISSION),
         swap=HistoryDealGetDouble(deal,DEAL_SWAP),fee=HistoryDealGetDouble(deal,DEAL_FEE),
         sl=HistoryDealGetDouble(deal,DEAL_SL),tp=HistoryDealGetDouble(deal,DEAL_TP);
      string reason=entry==DEAL_ENTRY_IN?ChartEntrySource(deal):CsvReason(deal),comment=HistoryDealGetString(deal,DEAL_COMMENT);
      if(entry==DEAL_ENTRY_IN && positions[index].opened==0) positions[index].source=ChartEntrySource(deal);
      CsvFold(positions[index],entry,type,when,volume,price,profit,commission,swap,fee,sl,tp,reason,comment);
      deals+=CsvPrefix()+CsvId(deal)+","+CsvId((ulong)HistoryDealGetInteger(deal,DEAL_ORDER))+","+CsvId(id)+","+CsvTime(when)+","+
         CsvCell(type==DEAL_TYPE_BUY?"买入":"卖出")+","+CsvCell(entry==DEAL_ENTRY_IN?"开仓":entry==DEAL_ENTRY_OUT || entry==DEAL_ENTRY_OUT_BY?"平仓":"反向/需核对")+","+
         CsvN(volume,8)+","+CsvN(price,_Digits)+","+CsvN(profit,8)+","+CsvN(commission,8)+","+CsvN(swap,8)+","+CsvN(fee,8)+","+
         CsvN(profit+commission+swap+fee,8)+","+CsvN(sl,_Digits)+","+CsvN(tp,_Digits)+","+CsvText(reason)+","+CsvText(comment)+","+CsvText(AccountInfoString(ACCOUNT_CURRENCY))+"\r\n";
   }
   string results=CsvMetaHeader()+"持仓ID,方向,入场来源,开仓时间,最后平仓时间,开仓手数,已平手数,剩余手数,开仓均价,平仓均价,最近止损价格,最近止盈价格,已实现毛盈亏,佣金,已结算隔夜费,其他手续费,已实现净盈亏,未平仓浮动毛盈亏,未结算隔夜费,含浮动参考盈亏,持仓秒数,状态,退出原因,账户币种\r\n";
   int closed=0,wins=0,losses=0,breakeven=0,open=0; double net=0,gain=0,loss=0;
   for(int i=0;i<ArraySize(positions);i++)
   {
      CsvPosition p=positions[i]; double volume=0,floating=0,swap=0,sl=p.sl,tp=p.tp;
      bool live=CsvLive(p.id,volume,floating,swap,sl,tp);
      double remaining=MathMax(0,p.entered-p.exited),cash=p.profit+p.commission+p.swap+p.fee;
      bool fully_closed=!live && remaining<0.00000001 && p.entered>0 && !p.unsupported;
      string state=fully_closed?"已平仓":live?(p.exited>0?"部分平仓":"未平仓"):"需核对";
      if(fully_closed)
      {
         closed++;
         if(cash>0.00000001) { wins++; gain+=cash; }
         else if(cash< -0.00000001) { losses++; loss-=cash; }
         else breakeven++;
      }
      else open++;
      net+=cash;
      long seconds=(fully_closed?p.closed/1000:(long)TimeCurrent())-p.opened/1000;
      results+=CsvPrefix()+CsvId(p.id)+","+CsvCell(p.direction>0?"做多":"做空")+","+CsvCell(p.source)+","+CsvTime(p.opened)+","+CsvTime(p.closed)+","+
         CsvN(p.entered,8)+","+CsvN(p.exited,8)+","+CsvN(live?volume:remaining,8)+","+CsvN(p.entered>0?p.entry_value/p.entered:0,_Digits)+","+
         (p.exited>0?CsvN(p.exit_value/p.exited,_Digits):CsvCell(""))+","+CsvN(sl,_Digits)+","+CsvN(tp,_Digits)+","+
         CsvN(p.profit,8)+","+CsvN(p.commission,8)+","+CsvN(p.swap,8)+","+CsvN(p.fee,8)+","+CsvN(cash,8)+","+
         CsvN(floating,8)+","+CsvN(swap,8)+","+CsvN(cash+floating+swap,8)+","+CsvCell((string)MathMax(0,seconds))+","+
         CsvCell(state)+","+CsvText(p.reason)+","+CsvText(AccountInfoString(ACCOUNT_CURRENCY))+"\r\n";
   }
   double balance=AccountInfoDouble(ACCOUNT_BALANCE),equity=AccountInfoDouble(ACCOUNT_EQUITY);
   string summary=CsvMetaHeader()+"记录状态,回放开始日期,回放结束日期含当天,实际记录起点,最新历史时间,初始本金,余额,净值,已实现账户收益率百分比,含浮动账户收益率百分比,本面板已实现净盈亏,已平持仓数,盈利持仓数,亏损持仓数,持平持仓数,未全平持仓数,胜率百分比,净利润因子,最大净值回撤金额逐Tick,最大净值回撤百分比逐Tick,账户币种,口径说明,"+csv_equity.DrawdownHeader()+"\r\n";
   summary+=CsvPrefix()+CsvCell(csv_final?"记录已结束":"回放中快照")+","+CsvCell(TimeToString(ReplayStart,TIME_DATE))+","+
      CsvCell(ReplayEndExclusive>0?TimeToString(ReplayEndExclusive-1,TIME_DATE):"")+","+CsvCell(TimeToString(csv_started,TIME_DATE|TIME_SECONDS))+","+
      CsvCell(TimeToString(TimeCurrent(),TIME_DATE|TIME_SECONDS))+","+CsvN(csv_initial,8)+","+CsvN(balance,8)+","+CsvN(equity,8)+","+
      (csv_initial>0?CsvN((balance/csv_initial-1)*100,8):CsvCell(""))+","+(csv_initial>0?CsvN((equity/csv_initial-1)*100,8):CsvCell(""))+","+
      CsvN(net,8)+","+CsvCell((string)closed)+","+CsvCell((string)wins)+","+CsvCell((string)losses)+","+CsvCell((string)breakeven)+","+CsvCell((string)open)+","+
      (closed>0?CsvN((double)wins/closed*100,8):CsvCell(""))+","+(loss>0?CsvN(gain/loss,8):CsvCell(""))+","+
      CsvN(csv_dd_amount,8)+","+CsvN(csv_dd_percent,8)+","+CsvText(AccountInfoString(ACCOUNT_CURRENCY))+","+
      CsvCell("费用按MT5原有正负号相加；胜率和利润因子仅含已全平持仓；回撤逐Tick观察；净值过程按历史小时汇总；账户收支与持仓明细可存在单独记账费用差额；历史时间为测试数据时间")+","+csv_equity.DrawdownFields()+"\r\n";
   string actions=CsvMetaHeader()+"操作序号,命令序号,历史操作时间,来源,动作代码,动作,请求目标持仓票号,请求手数,请求止损,请求止盈,请求挂单价,操作后手数,操作后止损,操作后止盈,操作后所选票号,处理说明,金价匹配启用,基准手数,基准金价,自动手数上限,本次手数计算价,本次计算手数,训练计划ID,训练阶段,挂单有效截至,关联订单号,关联成交号,关联持仓ID,训练校验\r\n";
   for(int i=0;i<ArraySize(csv_action_rows);i++) actions+=CsvPrefix()+csv_action_rows[i];
   string equity_process=CsvMetaHeader()+csv_equity.HourHeader()+"\r\n"+csv_equity.HourRows(CsvPrefix(),csv_final);
   bool ok=CsvWrite("净值过程.csv",equity_process);
   ok=ChartPendingCsv() && ok;
   ok=CsvWrite("交易结果.csv",results) && ok;
   ok=CsvWrite("成交明细.csv",deals) && ok;
   ok=CsvWrite("操作记录.csv",actions) && ok;
   ok=CsvWrite("回放汇总.csv",summary) && ok;
   ok=ok && CsvWrite("snapshot.txt",(string)csv_generation+"\r\n");
   if(ok) csv_error="";
   return ok;
}

bool CsvInit(const string boot)
{
   if(!TesterOnly()) return false;
   csv_session=DesktopSession;
   if(csv_session=="") csv_session=StringFormat("%016I64x%016I64x",GetTickCount64(),(ulong)ChartID());
   csv_boot=boot;
   if(csv_boot=="") csv_boot=(string)GetMicrosecondCount()+"_"+(string)ChartID();
   csv_dir="CN_Manual_Replay\\"+csv_session+"\\records\\"+csv_boot+"\\";
   csv_initial=AccountInfoDouble(ACCOUNT_BALANCE); csv_peak=AccountInfoDouble(ACCOUNT_EQUITY); csv_started=TimeCurrent();
   csv_equity.Reset(csv_started,csv_peak);
   csv_ready=true; CsvSnapshot(true);
   Print("CN_REPLAY_CSV_READY: ",csv_dir);
   return csv_error=="";
}

void CsvBegin(const string action,const string source,const ulong seq,const ulong ticket,const string lots,const string sl,const string tp,const string entry="")
{
   TrainingBeginLog(action,ticket);
   csv_pending_entry=entry;csv_sizing_price=0;csv_sizing_volume=0;
   csv_pending_action=action; csv_pending_source=source; csv_pending_seq=seq; csv_pending_ticket=ticket;
   csv_pending_lots=lots; csv_pending_sl=sl; csv_pending_tp=tp;
}
void CsvEnd()
{
   if(!csv_ready || csv_pending_action=="") return;
   csv_action_seq++;
   int n=ArraySize(csv_action_rows); ArrayResize(csv_action_rows,n+1);
   csv_action_rows[n]=CsvId(csv_action_seq)+","+CsvId(csv_pending_seq)+","+CsvCell(TimeToString(TimeCurrent(),TIME_DATE|TIME_SECONDS))+","+
      CsvText(csv_pending_source)+","+CsvText(csv_pending_action)+","+CsvText(CsvActionName(csv_pending_action))+","+CsvId(csv_pending_ticket)+","+CsvText(csv_pending_lots)+","+
      CsvText(csv_pending_sl)+","+CsvText(csv_pending_tp)+","+CsvText(csv_pending_entry)+","+CsvN(panel_lots,8)+","+CsvN(sl_price,_Digits)+","+CsvN(tp_price,_Digits)+","+
      CsvId(selected_ticket)+","+CsvText(panel_status)+","+CsvCell(AutoPriceLots?"1":"0")+","+CsvN(PriceLotBase,8)+","+CsvN(PriceLotReference,8)+","+CsvN(0,8)+","+
      (csv_sizing_price>0?CsvN(csv_sizing_price,_Digits):CsvCell(""))+","+(csv_sizing_volume>0?CsvN(csv_sizing_volume,8):CsvCell(""))+","+TrainingCsvFields()+"\r\n";
   csv_pending_action=""; CsvSnapshot(true);
}
void CsvFinish() { if(csv_ready) { csv_final=true; CsvSnapshot(true); } }


bool TesterOnly()
{
   return MQLInfoInteger(MQL_TESTER) && MQLInfoInteger(MQL_VISUAL_MODE) && !MQLInfoInteger(MQL_OPTIMIZATION);
}

string N(const string key) { return prefix+key; }

void Say(const string message,const color tone=clrLightSteelBlue)
{
   panel_status=message; status_color=tone;
   Print("CN_REPLAY: ",message);
}

void Text(const string key,const string value)
{
   if(ObjectGetString(0,N(key),OBJPROP_TEXT)!=value) ObjectSetString(0,N(key),OBJPROP_TEXT,value);
}

bool Base(const string key,const ENUM_OBJECT type,const int x,const int y,const int w,const int h)
{
   string name=N(key);
   if(!ObjectCreate(0,name,type,0,0,0)) return false;
   ObjectSetInteger(0,name,OBJPROP_CORNER,CORNER_LEFT_UPPER);
   ObjectSetInteger(0,name,OBJPROP_XDISTANCE,x);
   ObjectSetInteger(0,name,OBJPROP_YDISTANCE,y);
   ObjectSetInteger(0,name,OBJPROP_XSIZE,w);
   ObjectSetInteger(0,name,OBJPROP_YSIZE,h);
   ObjectSetInteger(0,name,OBJPROP_SELECTABLE,false);
   ObjectSetInteger(0,name,OBJPROP_SELECTED,false);
   ObjectSetInteger(0,name,OBJPROP_HIDDEN,true);
   ObjectSetInteger(0,name,OBJPROP_BACK,false);
   return true;
}

bool Box(const string key,const int x,const int y,const int w,const int h,const color fill)
{
   if(!Base(key,OBJ_RECTANGLE_LABEL,x,y,w,h)) return false;
   ObjectSetInteger(0,N(key),OBJPROP_BGCOLOR,fill);
   ObjectSetInteger(0,N(key),OBJPROP_COLOR,C'55,65,81');
   ObjectSetInteger(0,N(key),OBJPROP_BORDER_TYPE,BORDER_FLAT);
   ObjectSetInteger(0,N(key),OBJPROP_ZORDER,1);
   return true;
}

bool Label(const string key,const string value,const int x,const int y,const color tone,const int size=10)
{
   if(!Base(key,OBJ_LABEL,x,y,0,0)) return false;
   ObjectSetInteger(0,N(key),OBJPROP_ANCHOR,ANCHOR_LEFT_UPPER);
   ObjectSetInteger(0,N(key),OBJPROP_COLOR,tone);
   ObjectSetInteger(0,N(key),OBJPROP_FONTSIZE,size);
   ObjectSetString(0,N(key),OBJPROP_FONT,"Microsoft YaHei");
   ObjectSetInteger(0,N(key),OBJPROP_ZORDER,2);
   Text(key,value);
   return true;
}

bool Button(const string key,const string value,const int x,const int y,const int w,const int h,const color fill,const int size=10)
{
   if(!Base(key,OBJ_BUTTON,x,y,w,h)) return false;
   ObjectSetInteger(0,N(key),OBJPROP_BGCOLOR,fill);
   ObjectSetInteger(0,N(key),OBJPROP_COLOR,ink);
   ObjectSetInteger(0,N(key),OBJPROP_BORDER_COLOR,C'71,85,105');
   ObjectSetInteger(0,N(key),OBJPROP_FONTSIZE,size);
   ObjectSetString(0,N(key),OBJPROP_FONT,"Microsoft YaHei");
   ObjectSetInteger(0,N(key),OBJPROP_STATE,false);
   ObjectSetInteger(0,N(key),OBJPROP_ZORDER,10);
   Text(key,value);
   int size_now=ArraySize(buttons); ArrayResize(buttons,size_now+1); buttons[size_now]=key;
   return true;
}

void RemoveKeypad()
{
   for(int i=ArraySize(buttons)-1;i>=0;i--)
      if(StringFind(buttons[i],"pad_")==0)
      {
         ObjectDelete(0,N(buttons[i]));
         for(int j=i;j<ArraySize(buttons)-1;j++) buttons[j]=buttons[j+1];
         ArrayResize(buttons,ArraySize(buttons)-1);
      }
   ObjectDelete(0,N("pad_bg")); ObjectDelete(0,N("pad_title"));
   ObjectDelete(0,N("pad_value")); ObjectDelete(0,N("pad_hint"));
   editing=""; edit_text="";
}

void Keypad(const string target)
{
   RemoveKeypad(); editing=target;
   // The button keypad does not depend on tester keyboard/OnChartEvent support.
   string title=(target=="lot")?"输入手数":((target=="sl")?"输入止损价格":"输入止盈价格");
   Box("pad_bg",456,36,280,364,bg);
   Label("pad_title",title+"（鼠标数字键盘）",470,49,ink,11);
   Label("pad_value","0",475,81,clrDeepSkyBlue,17);
   Label("pad_hint","0 = 不设置；输入后点击确认",470,115,muted,9);
   string digits[12]={"1","2","3","4","5","6","7","8","9",".","0","退格"};
   for(int i=0;i<12;i++) Button("pad_"+IntegerToString(i),digits[i],470+(i%3)*84,144+(i/3)*43,77,36,card,12);
   Button("pad_clear","清空",470,322,77,30,card);
   Button("pad_cancel","取消",554,322,77,30,card);
   Button("pad_ok","确认",638,322,77,30,C'15,118,110');
   ChartRedraw(0);
}

int VolumeDigits()
{
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   for(int i=0;i<8;i++) if(MathAbs(step-NormalizeDouble(step,i))<1e-9) return i;
   return 8;
}

bool ValidLots(const double value)
{
   double lo=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN),hi=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX);
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   return MathIsValidNumber(value) && step>0 && value>=lo-1e-9 && value<=hi+1e-9 &&
          MathAbs(value/step-MathRound(value/step))<1e-6;
}

double Price(const double value)
{
   if(value==0) return 0.0;
   double step=SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE);
   if(step<=0) step=_Point;
   return NormalizeDouble(MathRound(value/step)*step,_Digits);
}

bool SelectOwnPosition(const ulong ticket)
{
   return ticket>0 && PositionSelectByTicket(ticket) &&
          PositionGetString(POSITION_SYMBOL)==_Symbol &&
          (ulong)PositionGetInteger(POSITION_MAGIC)==MagicNumber;
}

int OwnTickets(ulong &tickets[])
{
   ArrayResize(tickets,0);
   for(int i=PositionsTotal()-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(!SelectOwnPosition(ticket)) continue;
      int count=ArraySize(tickets); ArrayResize(tickets,count+1); tickets[count]=ticket;
   }
   ArraySort(tickets);
   return ArraySize(tickets);
}

bool TradingAllowed()
{
   if(!TesterOnly()) return false;
   if(finish_sealed) { training_log_check="blocked_finished"; Say("本场已收工：只能复盘和退出，交易操作未执行",clrOrange); return false; }
   if(!hedging_account)
   { Say("测试器需切换 Hedging 多单模式后重新开始",clrOrange); return false; }
   return true;
}

void CancelCloseAll()
{
   close_all_armed_at=0; ArrayResize(close_all_snapshot,0);
   Text("close_all","全部平仓（本面板）");
}

void SelectPosition(const int direction)
{
   ulong tickets[];
   int count=OwnTickets(tickets),index=-1;
   if(count==0) { selected_ticket=0; Say("当前没有本面板持仓"); return; }
   for(int i=0;i<count;i++) if(tickets[i]==selected_ticket) { index=i; break; }
   if(index<0) index=(direction>0)?0:count-1;
   else index=(index+direction+count)%count;
   selected_ticket=tickets[index];
   // Selection never overwrites the staged SL/TP for the next new order.
   Say("已选择 #"+(string)selected_ticket+"；顶部填写值保持不变");
}

void ReadSelectedStops()
{
   if(!SelectOwnPosition(selected_ticket))
   { Say("请先选择一笔本面板持仓",clrOrange); return; }
   sl_price=PositionGetDouble(POSITION_SL); tp_price=PositionGetDouble(POSITION_TP);
   Say("已读取此单止损止盈；改好后点应用",clrLightGreen);
}

void SelectNewPosition(const ulong &before[])
{
   // Match the deal's stable position identifier, rather than assuming order=ticket.
   ulong deal=trade.ResultDeal(),identifier=0;
   if(deal>0 && HistoryDealSelect(deal)) identifier=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
   ulong after[]; OwnTickets(after);
   for(int i=ArraySize(after)-1;i>=0;i--)
   {
      if(!SelectOwnPosition(after[i])) continue;
      if(identifier>0 && (ulong)PositionGetInteger(POSITION_IDENTIFIER)==identifier)
      { selected_ticket=after[i]; return; }
   }
   for(int i=ArraySize(after)-1;i>=0;i--)
   {
      bool existed=false;
      for(int j=0;j<ArraySize(before);j++) if(after[i]==before[j]) { existed=true; break; }
      if(!existed) { selected_ticket=after[i]; return; }
   }
}

bool CheckStops(const bool buy,const bool modifying)
{
   if(!MathIsValidNumber(sl_price) || !MathIsValidNumber(tp_price) || sl_price<0 || tp_price<0)
   { Say("价格无效：请填写正数价格或0",clrOrange); return false; }
   MqlTick tick;
   if(!SymbolInfoTick(_Symbol,tick) || tick.ask<=0 || tick.bid<=0)
   { Say("正在等待有效历史报价",clrOrange); return false; }
   long points=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL);
   if(modifying) points=(long)MathMax(points,SymbolInfoInteger(_Symbol,SYMBOL_TRADE_FREEZE_LEVEL));
   double gap=points*_Point,reference=buy?tick.bid:tick.ask,eps=_Point*0.05;
   if(buy && ((sl_price>0 && (sl_price>=reference || reference-sl_price+eps<gap)) ||
              (tp_price>0 && (tp_price<=reference || tp_price-reference+eps<gap))))
   { Say("买单：止损需低于市价，止盈需高于市价",clrOrange); return false; }
   if(!buy && ((sl_price>0 && (sl_price<=reference || sl_price-reference+eps<gap)) ||
               (tp_price>0 && (tp_price>=reference || reference-tp_price+eps<gap))))
   { Say("卖单：止损需高于市价，止盈需低于市价",clrOrange); return false; }
   return true;
}

bool Done()
{
   uint code=trade.ResultRetcode();
   return code==TRADE_RETCODE_DONE || code==TRADE_RETCODE_DONE_PARTIAL;
}

void TradeError(const string action)
{
   if(TrainingEnabled) training_log_check="trade_rejected";
   Say(action+"失败，代码 "+IntegerToString((int)trade.ResultRetcode()),clrOrange);
   Print("CN_REPLAY_DETAIL: ",trade.ResultRetcodeDescription()," ",trade.ResultComment());
}

double AutoLotsAt(const double price)
{
   return ReplayPriceLots(PriceLotBase,PriceLotReference,price,SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN),
      SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX),SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP));
}
bool ResolveOrderLots(const double price,double &value)
{
   value=AutoPriceLots?AutoLotsAt(price):panel_lots;
   csv_sizing_price=price;csv_sizing_volume=value;
   if(!ValidLots(value)) { Say(AutoPriceLots?"自动计算手数不足最小值或参数不可用，本次未下单":"手数不符品种最小值或步长，请重新输入",clrOrange);return false; }
   return true;
}
string PriceLotTelemetry()
{
   MqlTick q={};SymbolInfoTick(_Symbol,q);
   return DesktopLine("auto_price_lots",AutoPriceLots?"1":"0")+DesktopLine("price_lot_base",DoubleToString(PriceLotBase,3))+
      DesktopLine("price_lot_reference",DoubleToString(PriceLotReference,3))+DesktopLine("price_lot_cap","0")+DesktopLine("price_lot_policy","inverse_price_uncapped_v2")+
      DesktopLine("auto_buy_lots",DoubleToString(AutoLotsAt(q.ask),VolumeDigits()))+DesktopLine("auto_sell_lots",DoubleToString(AutoLotsAt(q.bid),VolumeDigits()));
}

void OpenPosition(const bool buy)
{
   if(!TradingAllowed()) return;
   if(!TrainingMayOpen()) return;
   MqlTick quote={};if(!SymbolInfoTick(_Symbol,quote) || quote.ask<=0 || quote.bid<=0){Say("报价不可用，本次未下单",clrOrange);return;}
   double order_lots=0;if(!ResolveOrderLots(buy?quote.ask:quote.bid,order_lots))return;
   if(!CheckStops(buy,false)) return;
   ulong before[]; OwnTickets(before);
   // Zero is passed unchanged: there are no automatic exits or hidden stop rules.
   bool sent=buy?trade.Buy(order_lots,_Symbol,0,sl_price,tp_price,"CN manual replay"):
                 trade.Sell(order_lots,_Symbol,0,sl_price,tp_price,"CN manual replay");
   if(!sent || !Done()) { TradeError(buy?"买入":"卖出"); return; }
   TrainingCaptureResult(trade.ResultOrder(),trade.ResultDeal());
   SelectNewPosition(before);
   if(trade.ResultRetcode()==TRADE_RETCODE_DONE_PARTIAL)
      Say("开仓仅部分成交，请查看选中单实际手数",clrOrange);
   else Say((buy?"模拟买入已成交":"模拟卖出已成交")+"，已选中新单",clrLightGreen);
}

void ClosePosition()
{
   if(!TradingAllowed()) return;
   ulong ticket=selected_ticket;
   if(!SelectOwnPosition(ticket)) { Say("请先选择要平仓的持仓",clrOrange); return; }
   bool sent=trade.PositionClose(ticket);
   if(!sent || !Done()) { TradeError("平仓"); return; }
   TrainingCaptureResult(trade.ResultOrder(),trade.ResultDeal());
   if(SelectOwnPosition(ticket)) Say("所选单仅部分平仓，可再次点击单笔平仓",clrOrange);
   else { selected_ticket=0; Say("所选单已平仓；其余持仓保留",clrLightGreen); }
}

void CloseAllPositions()
{
   if(!TradingAllowed()) return;
   ulong now=GetTickCount64();
   if(close_all_armed_at==0 || now-close_all_armed_at>3000)
   {
      CancelCloseAll();
      int count=OwnTickets(close_all_snapshot);
      if(count==0) { Say("当前没有本面板持仓"); return; }
      close_all_armed_at=now;
      Text("close_all","再次点击确认全平");
      Say("3秒内再点全平：本面板当前品种 "+IntegerToString(count)+" 笔",clrOrange);
      return;
   }
   // Use only the confirmed snapshot. A new position is never silently added.
   ulong tickets[]; ArrayCopy(tickets,close_all_snapshot); CancelCloseAll();
   int closed=0,partial=0,failed=0,skipped=0;
   for(int i=0;i<ArraySize(tickets);i++)
   {
      ulong ticket=tickets[i];
      if(!SelectOwnPosition(ticket)) { skipped++; continue; }
      double old_volume=PositionGetDouble(POSITION_VOLUME);
      bool sent=trade.PositionClose(ticket);
      if(!sent || !Done())
      {
         failed++;
         Print("CN_REPLAY_CLOSE_ALL: #",ticket," failed ",trade.ResultRetcode()," ",trade.ResultRetcodeDescription());
         continue;
      }
      TrainingCaptureResult(trade.ResultOrder(),trade.ResultDeal());
      if(!SelectOwnPosition(ticket)) closed++;
      else if(PositionGetDouble(POSITION_VOLUME)<old_volume-1e-9) partial++;
      else failed++;
   }
   if(!SelectOwnPosition(selected_ticket)) selected_ticket=0;
   string outcome="全平结果：已平 "+IntegerToString(closed)+" / 部分 "+IntegerToString(partial)+" / 失败 "+IntegerToString(failed);
   if(skipped>0) outcome+=" / 已消失 "+IntegerToString(skipped);
   Say(outcome,(partial>0 || failed>0)?clrOrange:clrLightGreen);
}

void ApplyStops()
{
   if(!TradingAllowed()) return;
   ulong ticket=selected_ticket;
   if(!SelectOwnPosition(ticket)) { Say("请先选择要应用止损止盈的持仓",clrOrange); return; }
   bool buy=PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY;
   if(!CheckStops(buy,true)) return;
   double old_sl=PositionGetDouble(POSITION_SL),old_tp=PositionGetDouble(POSITION_TP);
   if(MathAbs(old_sl-sl_price)<_Point*0.1 && MathAbs(old_tp-tp_price)<_Point*0.1)
   { Say("止损止盈与现有设置相同"); return; }
   if(!SelectOwnPosition(ticket)) { Say("所选单已关闭，请重新选择",clrOrange); return; }
   bool sent=trade.PositionModify(ticket,sl_price,tp_price);
   if(!sent || !Done()) { TradeError("修改止损止盈"); return; }
   TrainingCaptureResult(0,0);
   Say((sl_price==0 && tp_price==0)?"已撤销所选单的止损止盈":"已更新所选单的止损止盈",clrLightGreen);
}

void BumpLot(const int direction)
{
   if(AutoPriceLots){Say("本场启用金价匹配；手动手数不生效");return;}
   double step=SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP);
   double next=NormalizeDouble(panel_lots+direction*step,VolumeDigits());
   if(ValidLots(next)) panel_lots=next;
   else Say("已达到允许的手数范围",clrOrange);
}

void PadAction(const string key)
{
   if(key=="pad_cancel") { RemoveKeypad(); return; }
   if(key=="pad_clear") edit_text="";
   else if(key=="pad_ok")
   {
      string raw=edit_text;
      if(raw=="" || raw==".") { Say("请先输入数值",clrOrange); return; }
      double value=StringToDouble(raw);
      if(!MathIsValidNumber(value) || value<0) { Say("输入无效",clrOrange); return; }
      if(editing=="lot")
      {
         if(!ValidLots(value)) { Say("手数不符最小值、最大值或步长",clrOrange); return; }
         panel_lots=NormalizeDouble(value,VolumeDigits());
      }
      else
      {
         double rounded=Price(value);
         if(value>0 && rounded<=0) { Say("价格小于最小报价单位，请重新填写",clrOrange); return; }
         if(editing=="sl") sl_price=rounded;
         else if(editing=="tp") tp_price=rounded;
      }
      RemoveKeypad(); Say("填写已保存；修改持仓请点“应用”"); return;
   }
   else
   {
      int index=(int)StringToInteger(StringSubstr(key,4));
      if(index==11) { if(StringLen(edit_text)>0) edit_text=StringSubstr(edit_text,0,StringLen(edit_text)-1); }
      else if(StringLen(edit_text)<15)
      {
         string digit=(index<9)?IntegerToString(index+1):((index==9)?".":"0");
         if(digit!="." || StringFind(edit_text,".")<0) edit_text+=digit;
      }
   }
   Text("pad_value",edit_text==""?"0":edit_text);
}

void BuildPanel()
{
   if(DesktopEnabled()) return;
   Box("background",12,16,420,550,bg);
   Label("title","手动历史回放 · 指标练习",24,25,ink,14);
   Label("symbol",_Symbol+"  "+EnumToString(_Period)+(hedging_account?"  Hedging 多单":"  Netting：交易禁用"),24,53,hedging_account?muted:clrOrange,9);
   Label("lot_title","手数",24,83,ink,9);
   Button("lot_minus","−",112,77,36,27,card,12);
   Button("lot","0.01",154,77,120,27,card,9);
   Button("lot_plus","+",280,77,36,27,card,12);
   Button("lot_reset","初始",322,77,94,27,card,9);
   Label("sl_title","止损价格",24,115,ink,9);
   Button("sl","0",154,109,162,27,card,9);
   Button("sl_zero","清零",322,109,94,27,card,9);
   Label("tp_title","止盈价格",24,147,ink,9);
   Button("tp","0",154,141,162,27,card,9);
   Button("tp_zero","清零",322,141,94,27,card,9);
   Button("sell","卖出 / 做空",24,177,190,32,C'153,27,27',11);
   Button("buy","买入 / 做多",226,177,190,32,C'4,120,87',11);
   Button("previous","上一单",24,217,90,27,card,9);
   Button("next","下一单",122,217,90,27,card,9);
   Button("read_stops","读取此单止损止盈",220,217,196,27,card,9);
   Label("ticket","未选择持仓",24,252,ink,9);
   Label("position","当前无持仓",24,273,ink,9);
   Label("actual","实际止损：无    止盈：无",24,294,muted,9);
   Button("apply","将顶部止损 / 止盈应用到所选单",24,319,392,28,C'30,64,110',9);
   Button("close","平仓所选单",24,355,190,28,card,10);
   Button("close_all","全部平仓（本面板）",226,355,190,28,C'112,45,45',9);
   Label("summary","本面板本品种：0 笔",24,391,ink,9);
   Label("pnl","余额 / 净值",24,412,muted,9);
   Label("status1",panel_status,24,433,status_color,9);
   Button("play_slow","观察 / 倍速",24,460,190,30,C'30,64,110',10);
   Button("play_fast","快放 / MT5",226,460,190,30,card,10);
   Label("play_mode","慢放",450,22,clrDeepSkyBlue,9);
   Label("progress_text","历史日期进度",24,503,muted,9);
   Box("progress_track",24,528,392,8,card);
   Box("progress_fill",24,528,1,8,C'56,189,248');
}

void Render()
{
   if(DesktopEnabled()) { PublishDesktopState("running"); return; }
   double progress=ReplayProgress();
   Text("progress_text",ReplayStart>0 && ReplayEndExclusive>ReplayStart ?
        TimeToString(TimeCurrent(),TIME_DATE|TIME_MINUTES)+"  "+DoubleToString(progress*100,1)+"%（只读）":"进度：请通过快速入口选择日期");
   ObjectSetInteger(0,N("progress_fill"),OBJPROP_XSIZE,(int)MathMax(1,392*progress));
   Text("lot",AutoPriceLots?"自动匹配":DoubleToString(panel_lots,VolumeDigits()));
   Text("sl",sl_price==0?"0（不设置）":DoubleToString(sl_price,_Digits));
   Text("tp",tp_price==0?"0（不设置）":DoubleToString(tp_price,_Digits));
   ulong tickets[];
   int count=OwnTickets(tickets),index=-1;
   double total_profit=0.0;
   for(int i=0;i<count;i++)
      if(SelectOwnPosition(tickets[i]))
      {
         total_profit+=PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
         if(tickets[i]==selected_ticket) index=i;
      }
   if(SelectOwnPosition(selected_ticket))
   {
      bool buy=PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY;
      Text("ticket","选中 #"+(string)selected_ticket+"   "+IntegerToString(index+1)+" / "+IntegerToString(count));
      Text("position",(buy?"持有买单  ":"持有卖单  ")+DoubleToString(PositionGetDouble(POSITION_VOLUME),VolumeDigits())+
           " 手   浮盈亏 "+DoubleToString(PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP),2));
      double sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
      Text("actual","实际止损："+(sl==0?"无":DoubleToString(sl,_Digits))+"    止盈："+(tp==0?"无":DoubleToString(tp,_Digits)));
   }
   else
   {
      // If a stop closed the selected ticket, do not silently retarget another one.
      selected_ticket=0;
      Text("ticket",count>0?"未选择持仓：请点上一单 / 下一单":"当前没有本面板持仓");
      Text("position",count>0?"选择不会改变顶部待填写的止损止盈":"可以连续买入或卖出，分别生成独立持仓");
      Text("actual","实际止损：—    实际止盈：—");
   }
   Text("summary","本面板本品种："+IntegerToString(count)+" 笔   总浮盈亏 "+DoubleToString(total_profit,2));
   Text("pnl","余额 "+DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE),2)+"   净值 "+DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY),2)+
        "  "+AccountInfoString(ACCOUNT_CURRENCY));
   Text("status1",panel_status);
   ObjectSetInteger(0,N("status1"),OBJPROP_COLOR,status_color);
   Text("play_mode",slow_playback?"当前：慢放观察":"当前：快速浏览");
   ObjectSetInteger(0,N("play_slow"),OBJPROP_BGCOLOR,slow_playback?C'30,64,110':card);
   ObjectSetInteger(0,N("play_fast"),OBJPROP_BGCOLOR,slow_playback?card:C'4,120,87');
   ChartRedraw(0);
}

// Local desktop panel transport. No DLLs, sockets, account credentials or chart events.
// A fresh launcher session and EA boot token bind commands to one visual test.
string desktop_dir="",desktop_boot="";
int desktop_lock=INVALID_HANDLE;
ulong desktop_seq=0,desktop_stamp=0,desktop_previous_stamp=0;
bool desktop_ready=false;

bool DesktopEnabled() { return desktop_ready; }

double ReplayProgress()
{
   if(ReplayStart<=0 || ReplayEndExclusive<=ReplayStart) return 0;
   return MathMax(0,MathMin(1,(double)(TimeCurrent()-ReplayStart)/(double)(ReplayEndExclusive-ReplayStart)));
}

bool InitDesktopBridge()
{
   if(DesktopSession=="") return true;
   if(StringLen(DesktopSession)!=32) return false;
   for(int i=0;i<32;i++)
      if(StringFind("0123456789abcdef",StringSubstr(DesktopSession,i,1))<0) return false;
   desktop_dir="CN_Manual_Replay\\"+DesktopSession+"\\";
   desktop_lock=FileOpen(desktop_dir+"owner.lock",FILE_WRITE|FILE_BIN|FILE_COMMON);
   if(desktop_lock==INVALID_HANDLE)
   { Print("CN_REPLAY_DESKTOP: another test owns this session, error=",GetLastError()); return false; }
   desktop_boot=(string)GetMicrosecondCount()+"_"+(string)ChartID();
   desktop_ready=true;
   Print("CN_REPLAY_DESKTOP_READY: ",DesktopSession," boot=",desktop_boot);
   return true;
}

string DesktopLine(const string key,string value)
{
   StringReplace(value,"\r"," "); StringReplace(value,"\n"," ");
   return key+"="+value+"\r\n";
}

// Native visual-tester pending orders. All requests still pass TesterOnly and TradingAllowed.
string chart_request_entry="",chart_request_side="",chart_request_kind="";
ulong chart_known_orders[];
double chart_initial_prices[];
ulong chart_cancel_armed=0,chart_cancel_snapshot[];

bool ChartOwnLimit(const ulong ticket)
{
   if(ticket==0 || !OrderSelect(ticket)) return false;
   ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE);
   return OrderGetString(ORDER_SYMBOL)==_Symbol && (ulong)OrderGetInteger(ORDER_MAGIC)==MagicNumber &&
      (type==ORDER_TYPE_BUY_LIMIT || type==ORDER_TYPE_SELL_LIMIT);
}
int ChartLimitTickets(ulong &tickets[])
{
   ArrayResize(tickets,0);
   for(int i=0;i<OrdersTotal();i++)
   {
      ulong ticket=OrderGetTicket(i); if(!ChartOwnLimit(ticket)) continue;
      int n=ArraySize(tickets); ArrayResize(tickets,n+1); tickets[n]=ticket;
   }
   ArraySort(tickets); return ArraySize(tickets);
}
void ChartRemember(const ulong ticket,const double price)
{
   for(int i=0;i<ArraySize(chart_known_orders);i++) if(chart_known_orders[i]==ticket) return;
   int n=ArraySize(chart_known_orders); ArrayResize(chart_known_orders,n+1); ArrayResize(chart_initial_prices,n+1);
   chart_known_orders[n]=ticket; chart_initial_prices[n]=price;
}
double ChartOriginalPrice(const ulong ticket,const double fallback)
{
   for(int i=0;i<ArraySize(chart_known_orders);i++) if(chart_known_orders[i]==ticket) return chart_initial_prices[i];
   return fallback;
}
bool ChartValidLimit(const bool buy,const double entry,const double sl,const double tp,const bool modifying)
{
   MqlTick q; if(!SymbolInfoTick(_Symbol,q) || q.ask<=0 || q.bid<=0) { Say("没有有效的历史报价",clrOrange); return false; }
   double gap=SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point;
   if(!MathIsValidNumber(entry) || !MathIsValidNumber(sl) || !MathIsValidNumber(tp) || entry<=0 || sl<0 || tp<0)
   { Say("挂单价格无效",clrOrange); return false; }
   // Direction follows the visible Bid line. Never silently convert a selected limit into another order type.
   if((buy && (entry>=q.bid || q.ask-entry+_Point*0.01<gap)) || (!buy && (entry<=q.bid || entry-q.bid+_Point*0.01<gap)))
   { Say("行情已变化或挂单距离不足，请重新选价",clrOrange); return false; }
   if((buy && ((sl>0 && (sl>=entry || entry-sl+_Point*0.01<gap)) || (tp>0 && (tp<=entry || tp-entry+_Point*0.01<gap)))) ||
      (!buy && ((sl>0 && (sl<=entry || sl-entry+_Point*0.01<gap)) || (tp>0 && (tp>=entry || entry-tp+_Point*0.01<gap)))))
   { Say("挂单止盈止损方向或距离不符合要求",clrOrange); return false; }
   return true;
}
bool ChartSend(MqlTradeRequest &request,const string action,const bool check=true)
{
   if(!TradingAllowed()) return false;
   MqlTradeCheckResult validation={}; MqlTradeResult result={};
   if(check && (!OrderCheck(request,validation) || (validation.retcode!=0 && validation.retcode!=TRADE_RETCODE_DONE)))
   { if(TrainingEnabled)training_log_check="request_check_rejected"; Say(action+"未通过MT5检查："+(string)validation.retcode+" "+validation.comment,clrOrange); return false; }
   bool sent=OrderSend(request,result);
   if(!sent || (result.retcode!=TRADE_RETCODE_DONE && result.retcode!=TRADE_RETCODE_PLACED && result.retcode!=TRADE_RETCODE_DONE_PARTIAL))
   { if(TrainingEnabled)training_log_check="trade_rejected"; Say(action+"失败："+(string)result.retcode+" "+result.comment,clrOrange); return false; }
   TrainingCaptureResult(result.order,result.deal);
   if(TrainingEnabled && !training_log_batch && request.action!=TRADE_ACTION_PENDING && request.order>0)training_link_order=request.order;
   if(request.action==TRADE_ACTION_PENDING && result.order>0) ChartRemember(result.order,request.price);
   if(request.action==TRADE_ACTION_PENDING && result.order>0)
      TrainingRememberOrder(result.order,training_log_plan,training_log_expiry,request.type_time==ORDER_TIME_SPECIFIED);
   Say(action+"已由测试器确认"+(result.order>0?" #"+(string)result.order:""),clrLightGreen);
   return true;
}
void ChartAction(const string action,const ulong ticket,const string kind,const string side,const string entry_text,
                 const string lots_text,const string sl_text,const string tp_text)
{
   if(!TradingAllowed()) return;
   if(action!="cancel_all_limits") { chart_cancel_armed=0; ArrayResize(chart_cancel_snapshot,0); }
   if(action=="cancel_all_limits")
   {
      ulong now=GetTickCount64();
      if(chart_cancel_armed==0 || now-chart_cancel_armed>3000)
      {
         int n=ChartLimitTickets(chart_cancel_snapshot);
         if(n==0) { Say("当前没有本面板挂单"); return; }
         chart_cancel_armed=now; Say("3秒内再次点击，确认撤销 "+(string)n+" 笔挂单",clrOrange); return;
      }
      ulong ids[]; ArrayCopy(ids,chart_cancel_snapshot); chart_cancel_armed=0; ArrayResize(chart_cancel_snapshot,0);
      int removed=0,changed=0;
      for(int i=0;i<ArraySize(ids);i++)
      {
         if(!ChartOwnLimit(ids[i])) { changed++; continue; }
         MqlTradeRequest req={}; req.action=TRADE_ACTION_REMOVE; req.order=ids[i];
         if(ChartSend(req,"撤销挂单",false)) removed++; else changed++;
      }
      Say("撤单结果：已撤 "+(string)removed+"；状态改变或失败 "+(string)changed); return;
   }
   if(action=="cancel_limit")
   {
      if(kind!="O" || !ChartOwnLimit(ticket)) { Say("挂单已成交或已撤销，请刷新查看；本次未平仓",clrOrange); return; }
      MqlTradeRequest req={}; req.action=TRADE_ACTION_REMOVE; req.order=ticket; ChartSend(req,"撤销挂单",false); return;
   }
   double entry=0,lots=0,sl=0,tp=0;
   if(!DesktopNumber(entry_text,entry) || !DesktopNumber(lots_text,lots) || !DesktopNumber(sl_text,sl) || !DesktopNumber(tp_text,tp))
   { Say("图表请求中有无效数值",clrOrange); return; }
   entry=Price(entry); sl=Price(sl); tp=Price(tp);
   if(action=="place_limit")
   {
      if(!TrainingMayOpen()) return;
      if((side!="buy" && side!="sell") || (!AutoPriceLots && !ValidLots(lots))) { Say("挂单方向或手数无效",clrOrange); return; }
      if(AutoPriceLots){if(!ResolveOrderLots(entry,lots))return;}
      else {csv_sizing_price=entry;csv_sizing_volume=lots;}
      bool buy=side=="buy";
      if(!ChartValidLimit(buy,entry,sl,tp,false)) return;
      if((SymbolInfoInteger(_Symbol,SYMBOL_ORDER_MODE)&SYMBOL_ORDER_LIMIT)==0) { Say("当前品种不允许限价挂单",clrOrange); return; }
      MqlTradeRequest req={}; req.action=TRADE_ACTION_PENDING; req.symbol=_Symbol; req.magic=MagicNumber;
      req.type=buy?ORDER_TYPE_BUY_LIMIT:ORDER_TYPE_SELL_LIMIT; req.volume=lots; req.price=entry; req.sl=sl; req.tp=tp;
      if(!TrainingConfigureExpiry(req)) return;
      req.type_filling=ORDER_FILLING_RETURN; req.comment="CN chart manual limit";
      ChartSend(req,"图表限价挂单"); return;
   }
   if(action=="modify_limit")
   {
      if(kind!="O" || !ChartOwnLimit(ticket)) { Say("挂单状态已改变，请重新选择；没有自动修改新持仓",clrOrange); return; }
      if(!TrainingCheckModification(ticket)) return;
      bool buy=(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE)==ORDER_TYPE_BUY_LIMIT;
      if((buy?"buy":"sell")!=side) { Say("挂单方向不匹配",clrOrange); return; }
      if(!ChartValidLimit(buy,entry,sl,tp,true)) return;
      MqlTradeRequest req={}; req.action=TRADE_ACTION_MODIFY; req.order=ticket; req.symbol=_Symbol;
      req.price=entry; req.sl=sl; req.tp=tp; req.type_time=(ENUM_ORDER_TYPE_TIME)OrderGetInteger(ORDER_TYPE_TIME);
      req.expiration=(datetime)OrderGetInteger(ORDER_TIME_EXPIRATION); ChartSend(req,"拖线修改挂单"); return;
   }
   if(action=="modify_position")
   {
      if(kind!="P" || !SelectOwnPosition(ticket)) { Say("持仓已关闭或不属于本面板",clrOrange); return; }
      bool buy=(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY;
      if((buy?"buy":"sell")!=side) { Say("所选持仓方向不匹配",clrOrange); return; }
      double saved_sl=sl_price,saved_tp=tp_price; sl_price=sl; tp_price=tp;
      bool valid=CheckStops(buy,true); sl_price=saved_sl; tp_price=saved_tp;
      if(!valid) return;
      MqlTradeRequest req={}; req.action=TRADE_ACTION_SLTP; req.position=ticket; req.symbol=_Symbol; req.sl=sl; req.tp=tp;
      ChartSend(req,"拖线修改持仓止盈止损"); return;
   }
}

string ChartItem(const string kind,const ulong ticket,const bool buy,const double volume,const double entry,const double sl,const double tp)
{
   return kind+","+(string)ticket+","+(buy?"buy":"sell")+","+DoubleToString(volume,8)+","+
      DoubleToString(entry,_Digits)+","+DoubleToString(sl,_Digits)+","+DoubleToString(tp,_Digits)+";";
}
string ChartTelemetry()
{
   MqlTick q; SymbolInfoTick(_Symbol,q);
   string items="",item_plans=""; ulong tickets[]; ChartLimitTickets(tickets);
   for(int i=0;i<ArraySize(tickets);i++)
      if(ChartOwnLimit(tickets[i]))
      {
         items+=ChartItem("O",tickets[i],(ENUM_ORDER_TYPE)OrderGetInteger(ORDER_TYPE)==ORDER_TYPE_BUY_LIMIT,
            OrderGetDouble(ORDER_VOLUME_CURRENT),OrderGetDouble(ORDER_PRICE_OPEN),OrderGetDouble(ORDER_SL),OrderGetDouble(ORDER_TP));
         int mapped=TrainingFindOrder(tickets[i]);
         item_plans+="O,"+(string)tickets[i]+","+(mapped>=0?training_orders[mapped].plan:"")+";";
      }
   ulong positions[]; OwnTickets(positions);
   for(int i=0;i<ArraySize(positions);i++)
      if(SelectOwnPosition(positions[i]))
      {
         items+=ChartItem("P",positions[i],(ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY,
            PositionGetDouble(POSITION_VOLUME),PositionGetDouble(POSITION_PRICE_OPEN),PositionGetDouble(POSITION_SL),PositionGetDouble(POSITION_TP));
         item_plans+="P,"+(string)positions[i]+","+TrainingFindPosition((ulong)PositionGetInteger(POSITION_IDENTIFIER))+";";
      }
   return DesktopLine("intraday_protocol","1")+DesktopLine("account_currency",AccountInfoString(ACCOUNT_CURRENCY))+DesktopLine("item_plans",item_plans)+
      DesktopLine("expert","CN_Manual_Replay_Chart_Preview")+DesktopLine("bid",DoubleToString(q.bid,_Digits))+
      DesktopLine("ask",DoubleToString(q.ask,_Digits))+DesktopLine("tick_size",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_SIZE),8))+
      DesktopLine("tick_value_loss",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE_LOSS),8))+
      DesktopLine("tick_value_profit",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_TRADE_TICK_VALUE_PROFIT),8))+
      DesktopLine("contract_size",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_TRADE_CONTRACT_SIZE),8))+
      DesktopLine("min_lots",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MIN),8))+
      DesktopLine("max_lots",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_MAX),8))+
      DesktopLine("lot_step",DoubleToString(SymbolInfoDouble(_Symbol,SYMBOL_VOLUME_STEP),8))+
      DesktopLine("stops_gap",DoubleToString(SymbolInfoInteger(_Symbol,SYMBOL_TRADE_STOPS_LEVEL)*_Point,8))+
      DesktopLine("plot_handle",(string)finish_chart_hwnd)+
      DesktopLine("plot_width",(string)ChartGetInteger(0,CHART_WIDTH_IN_PIXELS))+
      DesktopLine("plot_height",(string)ChartGetInteger(0,CHART_HEIGHT_IN_PIXELS,0))+
      DesktopLine("plot_min",DoubleToString(ChartGetDouble(0,CHART_PRICE_MIN,0),8))+
      DesktopLine("plot_max",DoubleToString(ChartGetDouble(0,CHART_PRICE_MAX,0),8))+
      DesktopLine("chart_items",items)+DesktopLine("pending_count",(string)ArraySize(tickets))+
      DesktopLine("pending_armed",chart_cancel_armed>0 && GetTickCount64()-chart_cancel_armed<3000?"1":"0");
}

string ChartEntrySource(const ulong deal)
{
   ulong order=(ulong)HistoryDealGetInteger(deal,DEAL_ORDER);
   ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)HistoryOrderGetInteger(order,ORDER_TYPE);
   return type==ORDER_TYPE_BUY_LIMIT || type==ORDER_TYPE_SELL_LIMIT?"限价挂单成交":"市价手动开仓";
}
string ChartOrderCsv(const ulong id,const bool active)
{
   ENUM_ORDER_TYPE type=(ENUM_ORDER_TYPE)(active?OrderGetInteger(ORDER_TYPE):HistoryOrderGetInteger(id,ORDER_TYPE));
   if(type!=ORDER_TYPE_BUY_LIMIT && type!=ORDER_TYPE_SELL_LIMIT) return "";
   string symbol=active?OrderGetString(ORDER_SYMBOL):HistoryOrderGetString(id,ORDER_SYMBOL);
   ulong magic=(ulong)(active?OrderGetInteger(ORDER_MAGIC):HistoryOrderGetInteger(id,ORDER_MAGIC));
   if(symbol!=_Symbol || magic!=MagicNumber) return "";
   double volume=active?OrderGetDouble(ORDER_VOLUME_INITIAL):HistoryOrderGetDouble(id,ORDER_VOLUME_INITIAL);
   double entry=active?OrderGetDouble(ORDER_PRICE_OPEN):HistoryOrderGetDouble(id,ORDER_PRICE_OPEN),filled=0;
   string position_ids="";
   for(int i=0;i<HistoryDealsTotal();i++)
   {
      ulong d=HistoryDealGetTicket(i);
      if((ulong)HistoryDealGetInteger(d,DEAL_ORDER)!=id) continue;
      filled+=HistoryDealGetDouble(d,DEAL_VOLUME);
      string pid=(string)HistoryDealGetInteger(d,DEAL_POSITION_ID);
      if(StringFind(";"+position_ids,";"+pid+";")<0) position_ids+=pid+";";
   }
   ENUM_ORDER_STATE state=(ENUM_ORDER_STATE)(active?OrderGetInteger(ORDER_STATE):HistoryOrderGetInteger(id,ORDER_STATE));
   string status=state==ORDER_STATE_PLACED?"已挂出":state==ORDER_STATE_FILLED?"全部成交":state==ORDER_STATE_PARTIAL?"部分成交":
      state==ORDER_STATE_CANCELED?"已撤销":state==ORDER_STATE_EXPIRED?"已到期":state==ORDER_STATE_REJECTED?"已拒绝":EnumToString(state);
   return CsvPrefix()+CsvId(id)+","+CsvCell(type==ORDER_TYPE_BUY_LIMIT?"限价买入":"限价卖出")+","+
      CsvTime(active?OrderGetInteger(ORDER_TIME_SETUP_MSC):HistoryOrderGetInteger(id,ORDER_TIME_SETUP_MSC))+","+
      CsvTime(active?0:HistoryOrderGetInteger(id,ORDER_TIME_DONE_MSC))+","+CsvN(ChartOriginalPrice(id,entry),_Digits)+","+CsvN(entry,_Digits)+","+
      CsvN(volume,8)+","+CsvN(filled,8)+","+CsvN(MathMax(0,volume-filled),8)+","+
      CsvN(active?OrderGetDouble(ORDER_SL):HistoryOrderGetDouble(id,ORDER_SL),_Digits)+","+
      CsvN(active?OrderGetDouble(ORDER_TP):HistoryOrderGetDouble(id,ORDER_TP),_Digits)+","+CsvCell(status)+","+CsvCell(position_ids)+","+TrainingOrderFields(id)+"\r\n";
}
bool ChartPendingCsv()
{
   string s=CsvMetaHeader()+"订单编号,类型,创建历史时间,结束历史时间,初始挂单价格,最新挂单价格,申请手数,已成交手数,未成交手数,止损价格,止盈价格,状态,关联持仓ID,训练计划ID,挂单有效截至,到期执行方式\r\n";
   for(int i=0;i<HistoryOrdersTotal();i++) { ulong id=HistoryOrderGetTicket(i); s+=ChartOrderCsv(id,false); }
   for(int i=0;i<OrdersTotal();i++) { ulong id=OrderGetTicket(i); s+=ChartOrderCsv(id,true); }
   return CsvWrite("挂单记录.csv",s);
}


void FinishRefreshChartHandle()
{
   if(!TesterOnly() || finish_chart_hwnd>0) return;
   ulong wall=GetTickCount64();
   if(finish_handle_query_after>wall) return;
   finish_handle_query_after=wall+1000; finish_chart_id=ChartID();
   long candidate=0; ResetLastError();
   finish_handle_api_ok=ChartGetInteger(finish_chart_id,CHART_WINDOW_HANDLE,0,candidate);
   finish_handle_error=GetLastError();
   if(finish_handle_api_ok && candidate>0) finish_chart_hwnd=candidate;
   // Visual tester charts can remain virtual and expose no HWND. Desktop ownership is the fallback.
}
void FinishReset()
{
   finish_sealed=false; finish_end_requested=false; finish_stop_dispatched=false; finish_snapshot_ready=false;
   finish_history_time=0; finish_seal_seq=0; finish_end_error="";
   finish_chart_hwnd=0;
   finish_chart_id=0; finish_handle_query_after=0; finish_handle_error=0; finish_handle_api_ok=false;
}
bool FinishReadOnlyAction(const string action)
{
   return action=="select_position" || action=="previous" || action=="next" || action=="read_stops";
}
bool FinishActionBlocked(const string action)
{
   if(!finish_sealed || action=="finish_seal" || action=="finish_end" || FinishReadOnlyAction(action)) return false;
   training_log_check="blocked_finished";
   Say("本场已收工：只能复盘和退出，操作未执行",clrOrange); return true;
}
#include "CN_Replay_Observation.mqh"

bool FinishControl(const string action)
{
   if(action!="finish_seal" && action!="finish_end") return false;
   if(!TesterOnly()) { training_log_check="blocked_not_tester"; return true; }
   if(action=="finish_seal")
   {
      if(!finish_sealed)
      {
         // Freeze the decision scene without cancelling or closing any order.
         finish_history_time=TimeCurrent(); finish_seal_seq=desktop_seq; finish_sealed=true;
         CancelCloseAll(); chart_cancel_armed=0; ArrayResize(chart_cancel_snapshot,0); RemoveKeypad();
      }
      PlaybackClockPause(true,GetMicrosecondCount()); training_mode=REPLAY_TRAIN_PAUSED;
      training_log_check="finish_sealed"; CsvObserveEquity();
      finish_snapshot_ready=ObservationExport(true);
      finish_snapshot_ready=CsvSnapshot(true) && finish_snapshot_ready;
      Say(finish_snapshot_ready?"本场已收工并暂停；交易已锁定，请保存现场后完成复盘":"本场已暂停并锁定；现场文件保存失败，请重试收工保存",finish_snapshot_ready?clrLightGreen:clrOrange);
      return true;
   }
   if(!finish_sealed)
   { training_log_check="blocked_finish_unsealed"; Say("请先确认收工并保存现场，再结束本场测试",clrOrange); return true; }
   finish_end_requested=true; finish_end_error=""; training_log_check="finish_end_requested";
   Say("复盘退出已确认，正在正常结束本场测试器",clrLightGreen); return true;
}
string FinishTelemetry()
{
   FinishRefreshChartHandle();
   return DesktopLine("finish_protocol","1")+DesktopLine("finish_sealed",finish_sealed?"1":"0")+
      DesktopLine("finish_history_time",(string)(long)finish_history_time)+DesktopLine("finish_seal_seq",(string)finish_seal_seq)+
      DesktopLine("finish_chart_hwnd",(string)finish_chart_hwnd)+
      DesktopLine("finish_chart_id",(string)finish_chart_id)+DesktopLine("finish_handle_api_ok",finish_handle_api_ok?"1":"0")+
      DesktopLine("finish_handle_error",(string)finish_handle_error)+
      DesktopLine("finish_handle_policy",finish_chart_hwnd>0?"chart_property_available":"desktop_ownership_required")+
      DesktopLine("finish_snapshot_ready",finish_snapshot_ready && csv_error==""?"1":"0")+
      DesktopLine("finish_end_requested",finish_end_requested?"1":"0")+
      DesktopLine("finish_stop_dispatched",finish_stop_dispatched?"1":"0")+DesktopLine("finish_end_error",finish_end_error);
}
bool PublishDesktopState(const string phase)
{
   if(!desktop_ready || !TesterOnly()) return false;
   FinishRefreshChartHandle();
   ulong stamp=GetTickCount64();
   string effective_phase=phase=="running" && finish_end_requested?"ending":phase;
   string s=DesktopLine("protocol","1")+DesktopLine("session",DesktopSession)+DesktopLine("boot",desktop_boot)+
      DesktopLine("mtf_protocol","1")+DesktopLine("mtf_seal_ready",observation_seal_ready?"1":"0")+ChartTelemetry()+PriceLotTelemetry()+PlaybackTelemetry()+TrainingTelemetry()+FinishTelemetry()+DesktopLine("record_dir",csv_dir)+DesktopLine("record_error",csv_error)+DesktopLine("phase",effective_phase)+DesktopLine("stamp",(string)stamp)+DesktopLine("ack",(string)desktop_seq)+
      DesktopLine("symbol",_Symbol)+DesktopLine("period",EnumToString(_Period))+
      DesktopLine("now",(string)(long)TimeCurrent())+DesktopLine("from",(string)(long)ReplayStart)+
      DesktopLine("to",(string)(long)ReplayEndExclusive)+DesktopLine("digits",(string)_Digits)+
      DesktopLine("lots",DoubleToString(panel_lots,8))+DesktopLine("sl",DoubleToString(sl_price,_Digits))+
      DesktopLine("tp",DoubleToString(tp_price,_Digits))+DesktopLine("slow",slow_playback?"1":"0")+
      DesktopLine("hedging",hedging_account?"1":"0")+DesktopLine("armed",close_all_armed_at>0?"1":"0")+
      DesktopLine("status",panel_status)+DesktopLine("selected",(string)selected_ticket);
   ulong tickets[]; int count=OwnTickets(tickets); double pnl=0;
   for(int i=0;i<count;i++)
      if(SelectOwnPosition(tickets[i])) pnl+=PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP);
   s+=DesktopLine("count",(string)count)+DesktopLine("profit",DoubleToString(pnl,2))+
      DesktopLine("balance",DoubleToString(AccountInfoDouble(ACCOUNT_BALANCE),2))+
      DesktopLine("equity",DoubleToString(AccountInfoDouble(ACCOUNT_EQUITY),2));
   string position="未选择持仓",actual="实际止损：—    止盈：—";
   if(SelectOwnPosition(selected_ticket))
   {
      position="选中 #"+(string)selected_ticket+"  "+(PositionGetInteger(POSITION_TYPE)==POSITION_TYPE_BUY?"买单 ":"卖单 ")+
         DoubleToString(PositionGetDouble(POSITION_VOLUME),VolumeDigits())+" 手  浮盈亏 "+
         DoubleToString(PositionGetDouble(POSITION_PROFIT)+PositionGetDouble(POSITION_SWAP),2);
      double sl=PositionGetDouble(POSITION_SL),tp=PositionGetDouble(POSITION_TP);
      actual="实际止损："+(sl==0?"无":DoubleToString(sl,_Digits))+"    止盈："+(tp==0?"无":DoubleToString(tp,_Digits));
   }
   else selected_ticket=0;
   s+=DesktopLine("position",position)+DesktopLine("actual",actual)+"complete=1\r\n";
   int file=FileOpen(desktop_dir+"state.tmp",FILE_WRITE|FILE_TXT|FILE_UNICODE|FILE_COMMON);
   if(file==INVALID_HANDLE) return false;
   uint written=FileWriteString(file,s); FileFlush(file); FileClose(file);
   if(written==0) return false;
   if(FileMove(desktop_dir+"state.tmp",FILE_COMMON,desktop_dir+"state.txt",FILE_COMMON|FILE_REWRITE))
   { desktop_previous_stamp=desktop_stamp; desktop_stamp=stamp; return true; }
   return false;
}

void FinishDispatchStop()
{
   if(!finish_end_requested || finish_stop_dispatched || !TesterOnly()) return;
   finish_end_error=""; finish_stop_dispatched=true;
   // MT5 stops only after this event returns. Persist the ACK before asking it to stop.
   if(!PublishDesktopState("ending"))
   {
      finish_stop_dispatched=false; finish_end_error="ending_state_publish_failed";
      Say("结束确认尚未写入成功；现场保持暂停，正在重试",clrOrange); return;
   }
   TesterStop();
}

bool DesktopNumber(const string text,double &value)
{
   if(text=="" || StringLen(text)>24) return false;
   int points=0;
   for(int i=0;i<StringLen(text);i++)
   {
      string c=StringSubstr(text,i,1);
      if(c==".") { points++; if(points>1) return false; }
      else if(StringFind("0123456789",c)<0) return false;
   }
   if(text==".") return false;
   value=StringToDouble(text);
   return MathIsValidNumber(value) && value>=0;
}

void PollDesktopCommand()
{
   if(!desktop_ready || !TesterOnly()) return;
   int file=FileOpen(desktop_dir+"command.txt",FILE_READ|FILE_TXT|FILE_UNICODE|FILE_COMMON|FILE_SHARE_READ);
   if(file==INVALID_HANDLE) return;
   string lines[]; int n=0;
   while(!FileIsEnding(file) && n<32) { ArrayResize(lines,n+1); lines[n++]=FileReadString(file); }
   FileClose(file);
   string session="",boot="",action="",lots="",sl="",tp="";
   chart_request_entry="0"; chart_request_side=""; chart_request_kind="";
   TrainingResetRequest();
   playback_request_speed="";
   ulong seq=0,stamp=0,ticket=0; bool complete=false;
   for(int i=0;i<n;i++)
   {
      int at=StringFind(lines[i],"="); if(at<1) continue;
      string key=StringSubstr(lines[i],0,at),value=StringSubstr(lines[i],at+1);
      if(key=="session") session=value;
      else if(key=="boot") boot=value;
      else if(key=="seq") seq=(ulong)StringToInteger(value);
      else if(key=="stamp") stamp=(ulong)StringToInteger(value);
      else if(key=="ticket") ticket=(ulong)StringToInteger(value);
      else if(key=="action") action=value;
      else if(key=="entry") chart_request_entry=value;
      else if(key=="side") chart_request_side=value;
      else if(key=="kind") chart_request_kind=value;
      else if(key=="lots") lots=value;
      else if(key=="sl") sl=value;
      else if(key=="tp") tp=value;
      else if(key=="playback_speed") playback_request_speed=value;
      else if(key=="training_plan") training_request_plan=value;
      else if(key=="training_expiry") training_request_expiry=value;
      else if(key=="training_mode") training_request_mode=value;
      else if(key=="complete") complete=(value=="1");
   }
   if(!complete || session!=DesktopSession || boot!=desktop_boot || seq<=desktop_seq) return;
   // Consume before dispatch. No command is retried or replayed across boots.
   desktop_seq=seq;
   CsvBegin(action,"桌面面板",seq,ticket,lots,sl,tp,chart_request_entry);
   ExecuteDesktopAction(action,stamp,ticket,lots,sl,tp);
   CsvEnd(); Render();
}

void ExecuteDesktopAction(const string action,const ulong stamp,const ulong ticket,const string lots,const string sl,const string tp)
{
   ulong now=GetTickCount64();
   if(stamp==0 || stamp>now || now-stamp>1500 || (stamp!=desktop_stamp && stamp!=desktop_previous_stamp))
   { if(TrainingEnabled)training_log_check="blocked_stale_command"; CancelCloseAll(); Say("操作已过期，未执行；请确认回放继续后重新点击",clrOrange); Render(); return; }
   if(action!="close_all") CancelCloseAll();
   if(FinishControl(action)) return;
   if(FinishActionBlocked(action)) return;
   if(PlaybackControl(action)) return;
   if(TrainingControl(action)) return;
   if(action=="select_position")
   {
      if(!TesterOnly() || !SelectOwnPosition(ticket)) { Say("该持仓已关闭或不属于本面板",clrOrange); return; }
      selected_ticket=ticket;
      Say("已选择 #"+(string)ticket+"；填写的止盈止损保持不变");
      return;
   }
   if(action=="place_limit" || action=="modify_limit" || action=="cancel_limit" || action=="cancel_all_limits" || action=="modify_position")
   {
      ChartAction(action,ticket,chart_request_kind,chart_request_side,chart_request_entry,lots,sl,tp); return;
   }
   if((action=="close" || action=="apply" || action=="read_stops") && (ticket==0 || ticket!=selected_ticket))
   { Say("所选单已改变，请重新选择后操作",clrOrange); Render(); return; }
   if(action=="set" || action=="buy" || action=="sell" || action=="apply")
   {
      double v=0,a=0,b=0;
      if(!DesktopNumber(lots,v) || (!AutoPriceLots && !ValidLots(v)) || !DesktopNumber(sl,a) || !DesktopNumber(tp,b))
      { Say("填写值无效，请检查手数和止损止盈价格",clrOrange); Render(); return; }
      panel_lots=v; sl_price=Price(a); tp_price=Price(b);
   }
   if(action=="set") Say("填写值已更新；现有持仓需点击应用");
   else if(action=="buy") OpenPosition(true);
   else if(action=="sell") OpenPosition(false);
   else if(action=="apply") ApplyStops();
   else if(action=="close") ClosePosition();
   else if(action=="close_all") CloseAllPositions();
   else if(action=="previous") SelectPosition(-1);
   else if(action=="next") SelectPosition(1);
   else if(action=="read_stops") ReadSelectedStops();
   else if(action=="play_slow" || action=="play_fast")
   { slow_playback=(action=="play_slow"); Say(slow_playback?"已切到慢放":"已切到快放",clrLightGreen); }
   else Say("未知操作，未执行",clrOrange);
   Render();
}


void CloseDesktopBridge()
{
   if(!desktop_ready) return;
   PublishDesktopState("ended");
   FileClose(desktop_lock); desktop_lock=INVALID_HANDLE; desktop_ready=false;
}


void Pump()
{
   if(!TesterOnly() || pumping || finish_stop_dispatched) return;
   ulong now=GetTickCount64();
   if(now-last_poll<20) return;
   last_poll=now; pumping=true;
   FitPracticeLayout();
   TrainingSyncPositionPlans();
   PollDesktopCommand();
   ObservationPump();
   FinishDispatchStop();
   if(finish_stop_dispatched) { pumping=false; return; }
   if(close_all_armed_at>0 && now-close_all_armed_at>3000) CancelCloseAll();
   // Capture and reset every button before handling a command, including keypad changes.
   string pressed[];
   for(int i=0;i<ArraySize(buttons);i++)
      if(ObjectGetInteger(0,N(buttons[i]),OBJPROP_STATE))
      {
         ObjectSetInteger(0,N(buttons[i]),OBJPROP_STATE,false);
         int k=ArraySize(pressed); ArrayResize(pressed,k+1); pressed[k]=buttons[i];
      }
   bool buy=false,sell=false;
   for(int i=0;i<ArraySize(pressed);i++) { if(pressed[i]=="buy") buy=true; if(pressed[i]=="sell") sell=true; }
   if(ArraySize(pressed)>0 && (ArraySize(pressed)!=1 || pressed[0]!="close_all")) CancelCloseAll();
   if(buy && sell) Say("同时检测到买入和卖出，本次均未执行",clrOrange);
   else if(ArraySize(pressed)>1) Say("请一次点击一个按钮，本次未执行",clrOrange);
   else if(ArraySize(pressed)==1)
   {
      string key=pressed[0];
      TrainingResetRequest();
      CsvBegin(key,"图内面板",0,selected_ticket,DoubleToString(panel_lots,8),DoubleToString(sl_price,_Digits),DoubleToString(tp_price,_Digits));
      if(FinishActionBlocked(key)) { /* A sealed scene accepts no tool chart mutations. */ }
      else if(key=="play_slow" || key=="play_fast")
      {
         PlaybackControl(key);
      }
      else if(editing!="")
      { if(StringFind(key,"pad_")==0) PadAction(key); else Say("请先确认或取消数字输入",clrOrange); }
      else if(AutoPriceLots && (key=="lot" || key=="lot_reset")) Say("本场启用金价匹配；手动手数不生效");
      else if(key=="lot" || key=="sl" || key=="tp") Keypad(key);
      else if(key=="lot_minus") BumpLot(-1);
      else if(key=="lot_plus") BumpLot(1);
      else if(key=="lot_reset") { panel_lots=InitialLots; Say("已恢复初始手数"); }
      else if(key=="sl_zero") { sl_price=0; Say("止损填写值已清零；持仓需点应用"); }
      else if(key=="tp_zero") { tp_price=0; Say("止盈填写值已清零；持仓需点应用"); }
      else if(key=="buy") OpenPosition(true);
      else if(key=="sell") OpenPosition(false);
      else if(key=="apply") ApplyStops();
      else if(key=="close") ClosePosition();
      else if(key=="previous") SelectPosition(-1);
      else if(key=="next") SelectPosition(1);
      else if(key=="read_stops") ReadSelectedStops();
      else if(key=="close_all") CloseAllPositions();
      CsvEnd();
   }
   if(ArraySize(pressed)>0 || now-last_render>=200) { Render(); last_render=now; }
   pumping=false;
}

int OnInit()
{
   // Hard guard before indicators, UI, timers, or any trade logic are initialized.
   if(!TesterOnly())
   {
      Print("CN_REPLAY_BLOCKED: 仅限策略测试器可视化模式，普通实盘/模拟图表禁止运行。");
      return INIT_FAILED;
   }
   FinishReset();
   FinishRefreshChartHandle();
   TrainingResetSession();
   if(!MathIsValidNumber(ReplaySpeed) || ReplaySpeed<1 || ReplaySpeed>100)return INIT_PARAMETERS_INCORRECT;
   PlaybackClockReset(ReplaySpeed,GetMicrosecondCount());
   PlaybackClockMode(!UseObservationPacing,GetMicrosecondCount());
   if(AutoPriceLots && (StringFind(_Symbol,"XAUUSD")!=0 || !MathIsValidNumber(PriceLotBase) || !MathIsValidNumber(PriceLotReference) || PriceLotBase<=0 || PriceLotReference<=0))return INIT_PARAMETERS_INCORRECT;
   if(!ValidLots(InitialLots) || InitialSLPrice<0 || InitialTPPrice<0 ||
      !MathIsValidNumber(InitialSLPrice) || !MathIsValidNumber(InitialTPPrice) ||
      FastEMA<1 || MiddleEMA<1 || SlowEMA<1 ||
      BandsPeriod<1 || !MathIsValidNumber(BandsDeviation) || BandsDeviation<=0 || RSIPeriod<1 || ATRPeriod<1 || BIASPeriod<1 ||
      MACDFast<1 || MACDSlow<=MACDFast || MACDSignal<1 ||
      SlowTickDelayMs<1 || SlowTickDelayMs>250) return INIT_PARAMETERS_INCORRECT;
   // This property is read-only in MQL5. Configure Hedging in tester advanced
   // account settings; the connected demo account's name is not authoritative.
   hedging_account=AccountInfoInteger(ACCOUNT_MARGIN_MODE)==ACCOUNT_MARGIN_MODE_RETAIL_HEDGING;
   slow_playback=!playback_native;
   panel_lots=InitialLots; sl_price=Price(InitialSLPrice); tp_price=Price(InitialTPPrice);
   trade.SetExpertMagicNumber(MagicNumber);
   trade.SetAsyncMode(false);
   trade.SetDeviationInPoints(20);
   trade.SetTypeFillingBySymbol(_Symbol);
   trade.SetMarginMode();
   if(!LoadPracticeIndicators()) return INIT_FAILED;
   // Restore native interactions if a newer visual tester template disabled them.
   ChartSetInteger(0,CHART_MOUSE_SCROLL,true);
   ChartSetInteger(0,CHART_KEYBOARD_CONTROL,true);
   ChartSetInteger(0,CHART_AUTOSCROLL,true);
   ChartSetInteger(0,CHART_SHOW_PRICE_SCALE,true);
   ChartSetInteger(0,CHART_SHOW_DATE_SCALE,true);
   ChartSetInteger(0,CHART_MODE,CHART_CANDLES);
   ChartSetInteger(0,CHART_SHOW_GRID,false);
   ChartSetInteger(0,CHART_FOREGROUND,false);
   ChartSetInteger(0,CHART_COLOR_BACKGROUND,C'11,17,27');
   ChartSetInteger(0,CHART_COLOR_FOREGROUND,ink);
   ChartSetInteger(0,CHART_COLOR_CANDLE_BULL,C'34,197,94');
   ChartSetInteger(0,CHART_COLOR_CHART_UP,C'34,197,94');
   ChartSetInteger(0,CHART_COLOR_CANDLE_BEAR,C'239,68,68');
   ChartSetInteger(0,CHART_COLOR_CHART_DOWN,C'239,68,68');
   if(!InitDesktopBridge()) return INIT_FAILED;
   ObservationInitialize();
   CsvInit(desktop_boot);
   BuildPanel(); Render();
   layout_started=GetTickCount64();
   if(!hedging_account) Say("测试器需切换 Hedging 多单模式后重新开始",clrOrange);
   if(!EventSetTimer(1)) { Print("CN_REPLAY: 计时器启动失败"); return INIT_FAILED; }
   Print("CN_REPLAY_READY: tester+visual guards active; hedging=",hedging_account,"; default SL=",sl_price," TP=",tp_price,"; no automatic signals or trailing exits.");
   return INIT_SUCCEEDED;
}

void PaceHistoricalTime(const long history_ms)
{
   if(!TesterOnly() || finish_sealed || finish_stop_dispatched) return;
   if(!PlaybackClockBegin(history_ms,GetMicrosecondCount()))return;
   while(!IsStopped() && !finish_stop_dispatched && !PlaybackClockDue(GetMicrosecondCount()))
   {Pump();TrainingPauseWait();}
   if(!IsStopped() && !finish_sealed && !finish_stop_dispatched && playback_clock_ready && playback_event_ms>0)PlaybackClockRelease(GetMicrosecondCount());
}

void OnTick()
{
   if(finish_stop_dispatched) return;
   MqlTick replay_tick;SymbolInfoTick(_Symbol,replay_tick);
   ObservationTick(replay_tick);
   long history_ms=replay_tick.time_msc>0?replay_tick.time_msc:(long)TimeCurrent()*1000;
   CsvObserveEquity();
   TrainingExpirePending();
   Pump();
   TrainingPauseWait();
   if(finish_stop_dispatched || IsStopped()) return;
   CsvSnapshot();
   PaceHistoricalTime(history_ms);
   TrainingPauseWait();
}
void OnTimer()
{
   if(finish_stop_dispatched) return;
   Pump();TrainingPauseWait();
   if(finish_stop_dispatched || IsStopped()) return;
   if(playback_clock_ready)PaceHistoricalTime((long)TimeTradeServer()*1000);
}

void OnDeinit(const int reason)
{
   EventKillTimer();
   // A failed normal-chart attachment must not alter that chart.
   if(!MQLInfoInteger(MQL_TESTER)) return;
   CsvFinish();
   if(!finish_sealed)ObservationExport(true);
   CloseDesktopBridge();
   ObjectsDeleteAll(0,prefix);
   for(int i=0;i<ArraySize(extra_handles);i++) IndicatorRelease(extra_handles[i]);
}

double OnTester() { CsvFinish(); return 0; }
