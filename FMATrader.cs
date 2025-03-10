#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class FMATrader : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "FMATrader";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
			}
			else if (State == State.Configure)
			{
			}
		}
       
      	bool wasAbove = false;
		bool wasBelow = false;
		
		int belowBar = -1;
		int aboveBar = -1;
		protected override void OnBarUpdate()
		{
			if(CurrentBar < 2)
				return;
			
			double FMA =  TaylorFMA(MovingAverageType.EMA, 8,0,0)[0];
			double ATRvalue = ATR(14)[0];
			RSI RSIplot = RSI(FibFisher(17,0,0), 5,2);
			double upperRSI = 80;
			double lowerRSI = 20;
			double RSIsmooth = RSIplot.Avg[0];
	
			if( Low[0] < FMA - (1.5 * ATRvalue)){
				wasBelow = true;
				belowBar = CurrentBar;
			}
			
			if(High[0] > FMA + (1.5 * ATRvalue)){
				wasAbove = true;
				aboveBar = CurrentBar;
			}
			
			if(wasBelow && CurrentBar >= belowBar + 5){
				wasBelow = false;
			}
			
			if(wasAbove && CurrentBar >= aboveBar + 5){
				wasAbove = false;
			}
			
					
			if(wasBelow && CrossAbove(RSIplot, lowerRSI, 1) && RSIplot[0] > RSIplot[1]){
				   //EnterLong("Long");
		        Draw.ArrowUp(this, $"upArrow{CurrentBar}", false, 0, Low[0] - 1, Brushes.Green);
//		        SetProfitTarget(CalculationMode.Ticks, 6 * (Close[0] - Low[0]) / TickSize);
//		        SetStopLoss(CalculationMode.Ticks, 2 * (Close[0] - Low[0]) / TickSize);
		        //Print("Long PT/SL Ticks: " + ((Close[0] - Low[0]) / TickSize));
			}
			
			if(wasAbove && CrossBelow(RSIplot, upperRSI, 1) && RSIplot[0] < RSIplot[1]){
				   //EnterShort("Short");
		        Draw.ArrowDown(this, $"downArrow{CurrentBar}", false, 0, High[0] + 1, Brushes.Red);
//		        SetProfitTarget(CalculationMode.Ticks, 6 * (High[0] - Close[0]) / TickSize);
//		        SetStopLoss(CalculationMode.Ticks, 2* (High[0] - Close[0]) / TickSize);
		       // Print("Short PT/SL Ticks: " + ((High[0] - Close[0]) / TickSize));
			}
			
			if(Close[0] > FMA  && RSIplot[0]> RSIsmooth  && CrossAbove(RSIplot, lowerRSI, 1) && RSIplot[0] > RSIplot[1]){
				   //EnterLong("Long");
		        Draw.ArrowUp(this, $"upArrow{CurrentBar}", false, 0, Low[0] - 1, Brushes.Turquoise);
//		        SetProfitTarget(CalculationMode.Ticks, 6 * (Close[0] - Low[0]) / TickSize);
//		        SetStopLoss(CalculationMode.Ticks, 2 * (Close[0] - Low[0]) / TickSize);
		       // Print("Long PT/SL Ticks: " + ((Close[0] - Low[0]) / TickSize));
			}
			
			if(Close[0] < FMA  && RSIplot[0] < RSIsmooth  && CrossBelow(RSIplot, upperRSI, 1) && RSIplot[0] < RSIplot[1]){
				   //EnterShort("Short");
		        Draw.ArrowDown(this, $"downArrow{CurrentBar}", false, 0, High[0] + 1, Brushes.Orange);
//		        SetProfitTarget(CalculationMode.Ticks, 6 * (High[0] - Close[0]) / TickSize);
//		        SetStopLoss(CalculationMode.Ticks, 2* (High[0] - Close[0]) / TickSize);
		        //Print("Short PT/SL Ticks: " + ((High[0] - Close[0]) / TickSize));
			}

			
		}
	}
}
