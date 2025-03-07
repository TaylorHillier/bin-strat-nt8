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
        #region Parameters

        [Range(2, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Bars Ago", Description = "Lookback period (number of bars) for trend and volume averaging", Order = 1, GroupName = "Parameters")]
        public int BarsAgo { get; set; } = 4;

        // (Optional) Volume multiplier: current bar volume must exceed avg volume * multiplier
        [Range(1.0, 5.0), NinjaScriptProperty]
        [Display(Name = "Volume Multiplier", Description = "Multiplier above average volume to trigger a pullback signal", Order = 2, GroupName = "Parameters")]
        public double VolumeMultiplier { get; set; } = 1.5;

        // Profit target in ticks.
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "Parameters")]
        public int ProfitTargetTicks { get; set; } = 20;

        // Stop loss in ticks.
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop Loss (ticks)", Order = 4, GroupName = "Parameters")]
        public int StopLossTicks { get; set; } = 40;

        #endregion

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Strategy that looks for increased volume on pullbacks and enters in the direction of the prevailing trend.";
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
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                // Nothing additional to configure.
            }
        }
        #endregion

        #region Helper: Compute Average Volume
        private double GetAverageVolume(int period)
        {
            double sum = 0;
            for (int i = 0; i < period; i++)
            {
                sum += Volume[i];
            }
            return sum / period;
        }
        #endregion

        #region OnBarUpdate: Improved Logic
        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars
            if (CurrentBar < BarsAgo)
                return;

            // Compute average volume over BarsAgo bars (including the current bar or previous bars)
            double avgVolume = GetAverageVolume(BarsAgo);

            // Check if current bar's volume exceeds the threshold multiplier.
            bool highVolume = Volume[0] > avgVolume * VolumeMultiplier;

            // Determine the prevailing trend over the lookback period.
            // (For simplicity, we compare the current close with the close of the bar BarsAgo-1 bars ago.)
            bool uptrend = Close[0] > Close[BarsAgo - 1];
            bool downtrend = Close[0] < Close[BarsAgo - 1];

            // Determine pullback conditions:
            // In an uptrend, a pullback is a bar that is bearish (i.e. Close < Open).
            // In a downtrend, a pullback is a bar that is bullish (i.e. Close > Open).
            bool pullback = false;
            if (uptrend && (Close[0] < Open[0]))
                pullback = true;
            else if (downtrend && (Close[0] > Open[0]))
                pullback = true;

            // Additionally, require that the current bar's body is smaller than the previous bar's body.
            bool smallerBody = Math.Abs(Close[0] - Open[0]) < Math.Abs(Close[1] - Open[1]);

            // Advanced logic: only trigger the reversal if volume is high, a pullback is present, and the bar is relatively small.
            if (highVolume && pullback && smallerBody && Position.MarketPosition == MarketPosition.Flat)
            {
                // In an uptrend (prices rising), a pullback (a down candle) can be seen as a temporary dip.
                // You might then enter long in anticipation of a rebound.
                if (uptrend)
                {
                    EnterLong("ReversalLong");
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                    SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                }
                // In a downtrend (prices falling), a pullback (an up candle) can be seen as a temporary rally.
                // You might then enter short in anticipation of continuation of the downtrend.
                else if (downtrend)
                {
                    EnterShort("ReversalShort");
                    SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
                    SetStopLoss(CalculationMode.Ticks, StopLossTicks);
                }
            }
        }
        #endregion
    }
}
