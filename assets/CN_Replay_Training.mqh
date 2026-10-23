#ifndef CN_REPLAY_TRAINING_MQH
#define CN_REPLAY_TRAINING_MQH

// This protocol describes training intent. It never sets volume, SL or TP.
enum EReplayTrainingMode { REPLAY_TRAIN_ACTIVE=0, REPLAY_TRAIN_PASSIVE=1, REPLAY_TRAIN_PAUSED=2 };
EReplayTrainingMode training_mode=REPLAY_TRAIN_ACTIVE;
string training_request_plan="",training_request_expiry="",training_request_mode="";
string training_log_plan="",training_log_check="";
bool training_log_batch=false;
datetime training_log_expiry=0;
ulong training_link_order=0,training_link_deal=0,training_link_position=0;

struct ReplayTrainingOrder
{
   ulong ticket;
   string plan;
   datetime expiry;
   bool native_expiry,expiry_logged;
   ulong retry_after;
   datetime retry_after_history;
};
ReplayTrainingOrder training_orders[];
ulong training_position_ids[];
string training_position_plans[];
datetime training_next_expiry=0;
ulong training_position_scan_after=0;
int training_position_count=-1;

bool TrainingGuid(const string value)
{
   if(StringLen(value)!=32) return false;
   for(int i=0;i<32;i++)
      if(StringFind("0123456789abcdef",StringSubstr(value,i,1))<0) return false;
   return true;
}
bool TrainingDeadline(const string value,datetime &deadline)
{
   deadline=0;
   if(value=="") return true; // Optional metadata for non-entry commands.
   if(StringLen(value)>11) return false;
   for(int i=0;i<StringLen(value);i++)
      if(StringFind("0123456789",StringSubstr(value,i,1))<0) return false;
   long parsed=StringToInteger(value);
   if(parsed<0 || parsed>32535215999) return false;
   deadline=(datetime)parsed;
   return true;
}
string TrainingModeName()
{
   return training_mode==REPLAY_TRAIN_PAUSED?"paused":training_mode==REPLAY_TRAIN_PASSIVE?"passive":"active";
}
void TrainingResetRequest()
{
   training_request_plan=""; training_request_expiry=""; training_request_mode="";
}
void TrainingResetSession()
{
   training_mode=REPLAY_TRAIN_ACTIVE;
   TrainingResetRequest();
   training_log_plan=""; training_log_check=""; training_log_expiry=0; training_log_batch=false;
   training_link_order=0; training_link_deal=0; training_link_position=0;
   ArrayResize(training_orders,0); ArrayResize(training_position_ids,0); ArrayResize(training_position_plans,0);
   training_next_expiry=0; training_position_scan_after=0; training_position_count=-1;
}
int TrainingFindOrder(const ulong ticket)
{
   for(int i=0;i<ArraySize(training_orders);i++) if(training_orders[i].ticket==ticket) return i;
   return -1;
}
string TrainingFindPosition(const ulong identifier)
{
   for(int i=0;i<ArraySize(training_position_ids);i++)
      if(training_position_ids[i]==identifier) return training_position_plans[i];
   return "";
}
bool TrainingHasPosition(const ulong identifier)
{
   for(int i=0;i<ArraySize(training_position_ids);i++) if(training_position_ids[i]==identifier) return true;
   return false;
}
void TrainingRememberPosition(const ulong identifier,const string plan)
{
   if(identifier==0 || (plan!="" && !TrainingGuid(plan))) return;
   for(int i=0;i<ArraySize(training_position_ids);i++)
      if(training_position_ids[i]==identifier) return;
   int n=ArraySize(training_position_ids);
   ArrayResize(training_position_ids,n+1); ArrayResize(training_position_plans,n+1);
   training_position_ids[n]=identifier; training_position_plans[n]=plan;
}
void TrainingRememberOrder(const ulong ticket,const string plan,const datetime expiry,const bool native_expiry)
{
   if(!TrainingEnabled || ticket==0 || TrainingFindOrder(ticket)>=0) return;
   int n=ArraySize(training_orders); ArrayResize(training_orders,n+1);
   training_orders[n].ticket=ticket; training_orders[n].plan=TrainingGuid(plan)?plan:"";
   training_orders[n].expiry=expiry; training_orders[n].native_expiry=native_expiry;
   training_orders[n].expiry_logged=false; training_orders[n].retry_after=0; training_orders[n].retry_after_history=0;
   if(expiry>0 && (training_next_expiry==0 || expiry<training_next_expiry)) training_next_expiry=expiry;
}
void TrainingBeginLog(const string action,const ulong ticket)
{
   training_log_plan=""; training_log_check=TrainingEnabled?"not_checked":"disabled";
   training_log_batch=TrainingEnabled && (action=="close_all" || action=="cancel_all_limits");
   training_log_expiry=0; training_link_order=0; training_link_deal=0; training_link_position=0;
   if(!TrainingEnabled) return;
   // A batch can cover different ideas. Its summary must not combine the selected
   // position's plan with the final order/deal returned by the loop.
   if(training_log_batch) { training_log_check="batch_summary"; return; }
   if(action=="buy" || action=="sell" || action=="place_limit")
   {
      if(TrainingGuid(training_request_plan)) training_log_plan=training_request_plan;
      TrainingDeadline(training_request_expiry,training_log_expiry);
      return;
   }
   int mapped=TrainingFindOrder(ticket);
   if(mapped>=0)
   {
      training_log_plan=training_orders[mapped].plan; training_log_expiry=training_orders[mapped].expiry;
      training_link_order=ticket;
   }
   else if(ticket>0 && SelectOwnPosition(ticket))
   {
      training_link_position=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
      training_log_plan=TrainingFindPosition(training_link_position);
   }
}
bool TrainingMayOpen()
{
   if(finish_sealed) { training_log_check="blocked_finished"; Say("本场已收工，不能新增交易",clrOrange); return false; }
   if(!TrainingEnabled) return true;
   if(training_mode!=REPLAY_TRAIN_ACTIVE)
   {
      training_log_check=training_mode==REPLAY_TRAIN_PAUSED?"blocked_paused":"blocked_passive";
      Say(training_mode==REPLAY_TRAIN_PAUSED?"训练已暂停：恢复主动练习后才能开新单":"当前为放置运行：请恢复主动练习后开新单",clrOrange);
      return false;
   }
   if((RequireTrainingPlan || training_request_plan!="") && !TrainingGuid(training_request_plan))
   { training_log_check="blocked_missing_plan"; Say("缺少有效事前计划：请先在训练计划中完成选择题",clrOrange); return false; }
   datetime deadline=0;
   if(!TrainingDeadline(training_request_expiry,deadline))
   { training_log_check="blocked_invalid_expiry"; Say("训练挂单有效期格式无效，本次未开单",clrOrange); return false; }
   if(deadline>0 && deadline<=TimeCurrent())
   { training_log_check="blocked_expired_plan"; Say("训练计划有效期已结束，请重新确认计划后开单",clrOrange); return false; }
   training_log_plan=training_request_plan; training_log_expiry=deadline;
   training_log_check=TrainingGuid(training_request_plan)?"plan_checked":"unplanned_exploration";
   return true;
}
void TrainingCaptureResult(const ulong order,const ulong deal)
{
   if(!TrainingEnabled) return;
   if(training_log_batch) { training_log_check="batch_summary"; return; }
   if(order>0) training_link_order=order;
   if(deal>0)
   {
      training_link_deal=deal;
      if(HistoryDealSelect(deal))
      {
         training_link_position=(ulong)HistoryDealGetInteger(deal,DEAL_POSITION_ID);
         TrainingRememberPosition(training_link_position,training_log_plan);
      }
   }
   training_log_check="accepted";
}
bool TrainingConfigureExpiry(MqlTradeRequest &request)
{
   long supported=SymbolInfoInteger(_Symbol,SYMBOL_EXPIRATION_MODE);
   if(TrainingEnabled && training_log_expiry>0 && (supported&SYMBOL_EXPIRATION_SPECIFIED)!=0)
   { request.type_time=ORDER_TIME_SPECIFIED; request.expiration=training_log_expiry; return true; }
   if((supported&SYMBOL_EXPIRATION_GTC)==0)
   { training_log_check="blocked_expiry_support"; Say("品种不支持本次挂单有效方式，未创建挂单",clrOrange); return false; }
   request.type_time=ORDER_TIME_GTC; request.expiration=0;
   return true;
}
bool TrainingCheckModification(const ulong ticket)
{
   if(!TrainingEnabled) return true;
   int mapped=TrainingFindOrder(ticket);
   if(mapped<0) return true; // Unknown older order: do not invent a new plan or renew expiry.
   training_log_plan=training_orders[mapped].plan; training_log_expiry=training_orders[mapped].expiry;
   if(training_log_expiry>0 && TimeCurrent()>=training_log_expiry)
   { training_log_check="blocked_expired_modification"; Say("挂单已超过原有效期，不能通过拖线续期",clrOrange); return false; }
   return true;
}
bool TrainingControl(const string action)
{
   if(action!="training_pause" && action!="training_passive" && action!="training_active") return false;
   if(finish_sealed) { training_log_check="blocked_finished"; Say("本场已收工，不能恢复交易或推进回放",clrOrange); return true; }
   if(!TrainingEnabled || !TesterOnly())
   { training_log_check="blocked_training_disabled"; Say("本场未启用训练协议；下一场使用新版设置后生效",clrOrange); return true; }
   PlaybackClockPause(action=="training_pause",GetMicrosecondCount());
   if(action=="training_pause")
   { training_mode=REPLAY_TRAIN_PAUSED; Say("主动练习已暂停；已请求记录当前快照，仍可平仓、撤单或修改止盈止损",clrLightGreen); }
   else if(action=="training_passive")
   { training_mode=REPLAY_TRAIN_PASSIVE; Say("已标记放置运行；已有挂单和止盈止损继续，新开单停用",clrOrange); }
   else
   { training_mode=REPLAY_TRAIN_ACTIVE; Say("已恢复主动练习；新开单继续核对事前计划",clrLightGreen); }
   training_log_check="stage_confirmed";
   CsvObserveEquity();
   return true;
}
string TrainingTelemetry()
{
   // Sealing is available even when plan enforcement was disabled for this run.
   if(!TrainingEnabled && !finish_sealed) return "";
   return DesktopLine("training_protocol","1")+DesktopLine("training_mode",TrainingModeName())+
      DesktopLine("training_require_plan",RequireTrainingPlan?"1":"0")+
      DesktopLine("training_pause_policy","hold_tick_one_core")+
      DesktopLine("training_expiry_policy","native_or_first_following_tick");
}
string TrainingCsvFields()
{
   if(training_log_batch)
      return CsvCell("")+","+CsvCell(TrainingModeName())+","+CsvCell("")+","+
         CsvCell("")+","+CsvCell("")+","+CsvCell("")+","+CsvCell("batch_summary");
   return CsvCell(training_log_plan)+","+CsvCell(TrainingEnabled?TrainingModeName():"")+","+
      CsvCell(TrainingEnabled?(string)(long)training_log_expiry:"")+","+CsvId(training_link_order)+","+
      CsvId(training_link_deal)+","+CsvId(training_link_position)+","+CsvCell(training_log_check);
}
string TrainingOrderFields(const ulong ticket)
{
   int at=TrainingEnabled?TrainingFindOrder(ticket):-1;
   if(at<0) return CsvCell("")+","+CsvCell("")+","+CsvCell("");
   ReplayTrainingOrder item=training_orders[at];
   return CsvCell(item.plan)+","+CsvCell((string)(long)item.expiry)+","+
      CsvCell(item.expiry<=0?"none":item.native_expiry?"native":"tick_managed");
}
void TrainingSyncPositionPlans()
{
   if(!TrainingEnabled) return;
   int count=PositionsTotal(); ulong wall=GetTickCount64();
   if(count==training_position_count && wall<training_position_scan_after) return;
   training_position_count=count; training_position_scan_after=wall+500;
   for(int i=count-1;i>=0;i--)
   {
      ulong ticket=PositionGetTicket(i);
      if(!SelectOwnPosition(ticket)) continue;
      ulong identifier=(ulong)PositionGetInteger(POSITION_IDENTIFIER);
      if(TrainingHasPosition(identifier) || !HistorySelectByPosition(identifier)) continue;
      bool entry_found=false;
      for(int j=0;j<HistoryDealsTotal();j++)
      {
         ulong deal=HistoryDealGetTicket(j);
         if((ENUM_DEAL_ENTRY)HistoryDealGetInteger(deal,DEAL_ENTRY)!=DEAL_ENTRY_IN) continue;
         entry_found=true;
         int at=TrainingFindOrder((ulong)HistoryDealGetInteger(deal,DEAL_ORDER));
         if(at>=0) { TrainingRememberPosition(identifier,training_orders[at].plan); break; }
      }
      if(entry_found && !TrainingHasPosition(identifier)) TrainingRememberPosition(identifier,"");
   }
}
void TrainingRefreshNextExpiry()
{
   training_next_expiry=0;
   for(int i=0;i<ArraySize(training_orders);i++)
      if(!training_orders[i].expiry_logged && training_orders[i].expiry>0 &&
         (training_next_expiry==0 || training_orders[i].expiry<training_next_expiry))
         training_next_expiry=training_orders[i].expiry;
}
void TrainingExpirePending()
{
   if(!TrainingEnabled || !TesterOnly() || finish_sealed) return;
   datetime now=TimeCurrent();
   if(training_next_expiry==0 || now<training_next_expiry) return;
   ulong wall=GetTickCount64();
   for(int i=0;i<ArraySize(training_orders);i++)
   {
      ReplayTrainingOrder item=training_orders[i];
      if(item.expiry<=0 || item.expiry_logged || now<item.expiry) continue;
      if(!ChartOwnLimit(item.ticket))
      {
         if(HistoryOrderSelect(item.ticket))
         {
            ENUM_ORDER_STATE state=(ENUM_ORDER_STATE)HistoryOrderGetInteger(item.ticket,ORDER_STATE);
            if(state==ORDER_STATE_EXPIRED)
            {
               CsvBegin("training_expire","测试器原生到期",0,item.ticket,"","","","");
               training_log_check="expired_native";
               Say("训练挂单 #"+(string)item.ticket+" 已由测试器按原有效期到期");
               CsvEnd();
            }
            if(state!=ORDER_STATE_PLACED && state!=ORDER_STATE_PARTIAL) training_orders[i].expiry_logged=true;
         }
         continue;
      }
      // Native expiry is exact where supported. GTC fallback can only cancel on a following tick;
      // the tester matches orders before OnTick, so this is deliberately not an exact-time guarantee.
      // Fast playback can advance hours inside a few wall-clock seconds. Retry
      // when either clock reaches its deadline, while suppressing same-tick loops.
      if(wall<item.retry_after && now<item.retry_after_history) continue;
      training_orders[i].retry_after=wall+5000;
      training_orders[i].retry_after_history=now+5;
      CsvBegin("training_expire","历史时间到期撤单",0,item.ticket,"","","","");
      MqlTradeRequest request={}; request.action=TRADE_ACTION_REMOVE; request.order=item.ticket;
      bool removed=ChartSend(request,"训练挂单到期撤销",false);
      training_log_check=removed?"expired_cancelled_tick":"expiry_cancel_failed";
      training_link_order=item.ticket;
      if(removed) training_orders[i].expiry_logged=true;
      CsvEnd();
   }
   TrainingRefreshNextExpiry();
}
void TrainingPauseWait()
{
   // Sleep() advances simulated time in MT5's tester. Holding this event preserves all ticks.
   // Polling is throttled in Pump(), but this explicit pause occupies one CPU core.
   while((finish_sealed || (TrainingEnabled && training_mode==REPLAY_TRAIN_PAUSED)) && !finish_stop_dispatched && !IsStopped()) Pump();
}

#endif
