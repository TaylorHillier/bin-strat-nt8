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
	public class HighATRVol : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "HighATRVol";
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

		protected override void OnBarUpdate()
		{
			bool highATR = Math.Abs(Close[0] - Open[0]) > ATR(14)[0] * 2;
			bool highVol = Volume[0] > Bollinger(Volumes[0], 2, 10).Upper[0];
			
			if(Close[0] > Open[0] && highATR && highVol){
				EnterShortLimit(1, GetCurrentAsk());
				SetProfitTarget(CalculationMode.Ticks,  ((High[0] - Low[0]) / TickSize)* 4 );
				SetStopLoss(CalculationMode.Ticks,  ((High[0] - Low[0]) / TickSize) / 2);
			}
			
			if(Close[0] < Open[0] && highVol  && highATR){
				EnterLongLimit(1, GetCurrentBid());
				SetProfitTarget(CalculationMode.Ticks,  ((High[0] - Low[0]) / TickSize) * 4);
				SetStopLoss(CalculationMode.Ticks,  ((High[0] - Low[0]) / TickSize) / 2);
			}
		}
	}
}
