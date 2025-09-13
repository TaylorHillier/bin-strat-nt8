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
	public class DailyExtremeReversal : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "DailyExtremeReversal";
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
				
				NumBars = 50;
				MinVolume = 2;
			}
			else if (State == State.Configure)
			{
			}
		}

		int lowCount = 0;
		int highCount = 0;
		protected override void OnBarUpdate()
		{
			if(CurrentBar < 2) return;
			
			double DailyHigh = CurrentDayOHL().CurrentHigh[1];
			double DailyLow = CurrentDayOHL().CurrentLow[1];
			
		
			if(highCount > NumBars && High[0] > DailyHigh && Volume[0] > ATR(Volumes[0], 14)[0] * MinVolume){
				EnterShort(2, "short");
				SetProfitTarget(CalculationMode.Ticks, 60/TickSize);
				SetStopLoss(CalculationMode.Ticks, 20/TickSize);
			}
			
			if(lowCount > NumBars && Low[0] < DailyLow && Volume[0] > ATR(Volumes[0], 14)[0] * MinVolume){
				EnterLong(2, "long");
				SetProfitTarget(CalculationMode.Ticks, 60/TickSize);
				SetStopLoss(CalculationMode.Ticks, 20/TickSize);
			}
			
			if(Close[0] < DailyLow){
				lowCount = 0;
			}
			
			if(Close[0] > DailyHigh){
				highCount = 0;
			}
			
			lowCount++;
			highCount++;
			
		}
		
		#region Properties

        [NinjaScriptProperty]
        public int NumBars { get; set; }

		[NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Min FMA Length", Order = 3, GroupName = "Parameters")]
        public int MinVolume { get; set; }
		
		#endregion
	}
}
