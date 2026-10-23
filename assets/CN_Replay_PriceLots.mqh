// Pure volume arithmetic shared by telemetry, final orders and math-only tests.
double ReplayPriceLots(const double basis,const double reference,const double price,const double lo,const double hi,const double step)
{
   if(!MathIsValidNumber(basis) || !MathIsValidNumber(reference) || !MathIsValidNumber(price) ||
      !MathIsValidNumber(lo) || !MathIsValidNumber(hi) || !MathIsValidNumber(step) ||
      basis<=0 || reference<=0 || price<=0 || lo<=0 || hi<lo || step<=0) return 0;
   double raw=basis*reference/price;if(!MathIsValidNumber(raw) || raw<=0)return 0;
   double bounded=MathMin(raw,hi);
   double units=bounded/step,nearest=MathRound(units);
   // Correct representation noise only near an exact grid boundary.
   if(MathAbs(units-nearest)<1e-10)units=nearest;
   double value=NormalizeDouble(MathFloor(units)*step,8);
   return value>=lo-1e-9 && value<=hi+1e-9?value:0;
}
