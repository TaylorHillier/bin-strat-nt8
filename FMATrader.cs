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
       
protected override void OnBarUpdate()
{
    // Get the current FMA value
    double fmaValue = TaylorFMA(MovingAverageType.TMA, 8, 0, 2)[0];
    
    // Check for long entry conditions
    if (wasBelow && IsRising(TaylorFMA(MovingAverageType.TMA, 8, 0, 2)) && Close[0] > fmaValue) {
        EnterLong("Long");
        Draw.ArrowUp(this, $"upArrow{CurrentBar}", false, 0, Low[0] - 1, Brushes.Green);
        SetProfitTarget(CalculationMode.Ticks, (Close[0] - Low[0]) / TickSize);
        SetStopLoss(CalculationMode.Ticks, (Close[0] - Low[0]) / TickSize);
        Print("Long PT/SL Ticks: " + ((Close[0] - Low[0]) / TickSize));
    }
    
    // Check for short entry conditions
    if (wasAbove && IsFalling(TaylorFMA(MovingAverageType.TMA, 8, 0, 2)) && Close[0] < fmaValue) {
        EnterShort("Short");
        Draw.ArrowDown(this, $"downArrow{CurrentBar}", false, 0, High[0] + 1, Brushes.Red);
        SetProfitTarget(CalculationMode.Ticks, (High[0] - Close[0]) / TickSize);
        SetStopLoss(CalculationMode.Ticks, (High[0] - Close[0]) / TickSize);
        Print("Short PT/SL Ticks: " + ((High[0] - Close[0]) / TickSize));
    }
    
    // Update the state variables AFTER trade logic to prepare for next bar
    if (Close[0] > fmaValue) {
        wasAbove = true;
        wasBelow = false;
    } else if (Close[0] < fmaValue) {
        wasAbove = false;
        wasBelow = true;
    } else {
        // Price exactly equals FMA (rare but possible)
        wasAbove = false;
        wasBelow = false;
    }
}
	}
}
