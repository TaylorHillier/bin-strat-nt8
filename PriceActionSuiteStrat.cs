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
	public class PriceActionSuiteStrat : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "PriceActionSuiteStrat";
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
				if(AllowOtherSeries){
				AddDataSeries(barType, period);
				}
			}
		}

		int anchorBar = 0;
		bool anchorBarSet = false;
		double anchorBarHigh = 0;
		double anchorBarLow = 0;
		int count = 0;
		

		  private bool up1 = false;
        private bool down1 = false;
		
		protected override void OnBarUpdate()
		{
	
			
			
			if(CurrentBars[0]  >= 50 && BarsInProgress == 0){
			
				bool down = false;
				bool up = false;
			
				if(showReversals){
					if(Close[0] > Open[0] && Low[0] < Low[1] && High[0] < High[1] ){
						Draw.ArrowUp(this, $"{CurrentBar}upreversal", false, 0, Low[0], Brushes.Green);
						up = true;
					}
					
					if(Close[0] < Open[0] && Low[0] > Low[1] && High[0] > High[1] ){
						Draw.ArrowDown(this, $"{CurrentBar}downreversal", false, 0, High[0], Brushes.Red);
						down = true;
					}
				}
				
				if(showEngulfing){
					if(Low[0] < Low[1] && High[0] >= High[1] && Close[0] > Open[0]){
						Draw.Diamond(this, $"{CurrentBar}upengulfing", false, 0, Low[0] - 1, Brushes.Green);
						up = true;
					}
					
					if(High[0] > High[1] && Low[0] <= Low[1] && Close[0] < Open[0]){
						Draw.Diamond(this, $"{CurrentBar}downengulfing", false, 0, High[0] + 1, Brushes.Red);
						down = true;
					}
					
				}
				
				if(showHogies){
					if(High[0] < High[1] && Low[0] > Low[1] && !anchorBarSet){
						anchorBar = CurrentBar - 1;
						anchorBarHigh = High[1];
						anchorBarLow = Low[1];
						anchorBarSet = true;
	
					}
					
					if(anchorBarHigh == 0 || anchorBarLow == 0 || anchorBar ==0 || anchorBarSet == false) return;
					
					if(Close[0] <= anchorBarHigh && Close[0] >= anchorBarLow){
						count++;
					} else {
						anchorBarSet = false;
					    anchorBarHigh = anchorBarLow = 0;
					    anchorBar   = count = 0;
					    return;
					}
					
					if(count >= 3) {
					
						Draw.Rectangle(this, $"Rectangle{anchorBar}", false, CurrentBar - anchorBar, anchorBarLow, 0, anchorBarHigh, Brushes.Transparent, Brushes.CornflowerBlue, 20);
					}
					
						
				}
				
				if(up && up1 && !(down1 && down)){
					EnterLong("long");
					SetStopLoss(CalculationMode.Ticks, 20);
					SetProfitTarget(CalculationMode.Ticks, 20);
					
				}
				
				if(!(up && up1) && down1 && down){
					EnterShort("short");
					SetStopLoss(CalculationMode.Ticks, 20);
					SetProfitTarget(CalculationMode.Ticks, 20);
					
				}
				
				up1 = false;
				down1 = false;
			}
			
			if(BarsInProgress == 1){
				
				if(CurrentBars[1] < 50) return;
				
				if(Lows[1][0] < Lows[1][1] && Highs[1][0] >= Highs[1][1] && Closes[1][0] > Opens[1][0]){
					Draw.Diamond(this, $"{CurrentBars[1]}upengulfing2", false, 0, Lows[1][0] - 1, Brushes.Blue);
					up1 = true;
				}
				
				if(Highs[1][0] > Highs[1][1] && Lows[1][0] <= Lows[1][1] && Closes[1][0] < Opens[1][0]){
					Draw.Diamond(this, $"{CurrentBars[1]}downengulfing2", false, 0, Highs[1][0] + 1, Brushes.Purple);
					down1 = true;
				}
				
				if(Closes[1][0] > Opens[1][0] && Lows[1][0] < Lows[1][1] && Highs[1][0] < Highs[1][1] ){
					Draw.ArrowUp(this, $"{CurrentBar}upreversal", false, 0, Lows[1][0], Brushes.Blue);
					up1 = true;
				}
				
				if(Closes[1][0] < Opens[1][0] && Lows[1][0] > Lows[1][1] && Highs[1][0] > Highs[1][1] ){
					Draw.ArrowDown(this, $"{CurrentBar}downreversal", false, 0, Highs[1][0], Brushes.Purple);
					down1 = true;
				}
				
			}

			
	
		}
		
			[NinjaScriptProperty]
		[Display( Name = "Show Reversal Candles?", GroupName = "Options", Order = 0)]
		public bool showReversals
		{ get; set; } 
		
			[NinjaScriptProperty]
		[Display( Name = "Show Engulfing Candles?", GroupName = "Options", Order = 0)]
		public bool showEngulfing
		{ get; set; } 
		
			[NinjaScriptProperty]
		[Display( Name = "Show Mini Ranges (hogies)?", GroupName = "Options", Order = 0)]
		public bool showHogies
		{ get; set; } 
		
			[NinjaScriptProperty]
		[Display( Name = "Allow another data series for stronger signals?", GroupName = "Advanced", Order = 0)]
		public bool AllowOtherSeries
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "BarType to use for second series", GroupName = "Advanced", Order = 0)]
		public BarsPeriodType barType
		{ get; set; } 
		
			[NinjaScriptProperty]
		[Display( Name = "Period to use for second series", GroupName = "Advanced", Order = 0)]
		public int period
		{ get; set; } 
	}
}
