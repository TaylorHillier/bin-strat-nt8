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
	public class HMAFMACross : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "HMAFMACross";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= false;
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

		double high = 0;
				double low  = 0;
		bool wasAbove10 = true;
			bool wasBelow10 = true;
		protected override void OnBarUpdate()
		{
			bool isAbove10 = Close[0] - TaylorFMA(MovingAverageType.EMA, 22, 0, 0)[0] > 30;
			bool isBelow10 = Close[0] - TaylorFMA(MovingAverageType.EMA, 22, 0, 0)[0] < -30;
		
			
			bool drawdown = false;
			
						
			
			if(Position.MarketPosition == MarketPosition.Flat){
				high = 0;
				low  = 0;
			}
			
			if(Position.MarketPosition == MarketPosition.Long){
				if(high == 0)
					high = Close[0];
				
				if(low == 0){
					low = Close[0];
				}
				if(Close[0] > high){
					high = Close[0];
					low = high;
				}
				
				if(Close[0] < low)
					low = Close[0];
				
				if(high - low > 60)
					drawdown = true;
			}
			
			if(Position.MarketPosition == MarketPosition.Short){
				if(high == 0)
					high = Close[0];
				
				if(low == 0){
					low = Close[0];
				}
				if(Close[0] < low){
					low = Close[0];
					high = low;
				}
				
				if(Close[0] > high)
					high = Close[0];
				
				if(high - low > 50)
					drawdown = true;
			}
			
			if(isAbove10 && wasBelow10){
				EnterLong("long");
				wasAbove10 = true;
				wasBelow10 = false;
			} else if(isBelow10 && wasAbove10){
				EnterShort("short");
				wasBelow10 = true;
				wasAbove10 = false;
			}
			
			if(wasAbove10 && Close[0] - TaylorFMA(MovingAverageType.EMA, 22, 0, 0)[0] < 30)
				wasBelow10 = true;
			
			if(wasBelow10 && Close[0] - TaylorFMA(MovingAverageType.EMA, 22, 0, 0)[0] > -30)
				wasAbove10 = true;
				
		
			
			if(Position.MarketPosition == MarketPosition.Long && drawdown){
				ExitLong("long");
				high = 0;
				low = 0;
			}
			
			if(Position.MarketPosition == MarketPosition.Short && drawdown){
				ExitShort("short");
				high = 0;
				low = 0;
			}
		}
	}
}



