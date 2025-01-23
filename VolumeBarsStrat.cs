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
	public class VolumeBarsStrat : Strategy
	{
		[Range(2, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Bars Ago", Description = "Number of bars to evaluate", Order = 1, GroupName = "Parameters")]
		public int BarsAgo { get; set; } = 4;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Enter the description for your new custom Strategy here.";
				Name = "VolumeBarsStrat";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration = true;
			}
			else if (State == State.Configure)
			{
			}
		}

		protected override void OnBarUpdate()
		{
			if (State == State.Historical || State == State.Realtime)
			{
				if (CurrentBar < BarsAgo)
					return;

				// Determine volume comparison
				bool moreVolume = true;

				// Determine direction
				bool up = true;
				bool down = true;
				
				// Determine smaller candle conditions dynamically
				bool smallerCandleLong = true;
				bool smallerCandleShort = true;

				for (int i = 0; i < BarsAgo - 1; i++)
				{
					double currentBarSize = Math.Abs(Open[i] - Close[i]);
					double nextBarSize = Math.Abs(Open[i + 1] - Close[i + 1]);

					// Check long conditions
					if (!(currentBarSize < nextBarSize && Close[i] > Open[i] && Close[i + 1] > Open[i + 1]))
					{
						smallerCandleLong = false;
					}

					// Check short conditions
					if (!(currentBarSize < nextBarSize && Close[i] < Open[i] && Close[i + 1] < Open[i + 1]))
					{
						smallerCandleShort = false;
					}
					
					if(!(Volume[i] > Volume[i+1])){
						moreVolume = false;
					};
					
					if(!(Close[i] > Close[i+1])){
						up = false;
					}
					
					if(!(Close[i] < Close[i+1])){
						down = false;
					}

					// Early exit for optimization
//					if (!smallerCandleLong && !smallerCandleShort)
//						break;
				}

				

				// Entry logic
				if (moreVolume  && down && Position.MarketPosition == MarketPosition.Flat)
				{
					EnterLong("Short");
					SetProfitTarget(CalculationMode.Ticks, 20);
					SetStopLoss(CalculationMode.Ticks, 40);
				}

				if (moreVolume && up && Position.MarketPosition == MarketPosition.Flat)
				{
					EnterShort("Long");
					SetProfitTarget(CalculationMode.Ticks, 20);
					SetStopLoss(CalculationMode.Ticks, 40);
				}
			}
		}
	}
}
