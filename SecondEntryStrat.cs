#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml.Serialization;
using NinjaTrader.Cbi;

using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SecondEntryStrategy : Strategy
    {
        [NinjaScriptProperty]
        [Display(Name = "ATMStrategy", Order = 1, GroupName = "ATM Strategy")]
        public string ATMStrategy { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Swing Strength", Order = 2, GroupName = "Trade Logic")]
        public int swingStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum Pullback Points", Order = 3, GroupName = "Trade Logic")]
        public double MinPullbackPoints { get; set; }

        private string atmStrategyId = string.Empty;
        private string orderId = string.Empty;
        private bool isAtmStrategyCreated = false;

        // State machines for long and short setups
        private EntryState longState = EntryState.WaitingForPivot;
        private EntryState shortState = EntryState.WaitingForPivot;

        // Variables for long setup
        private int lastLongSwingBar = 0;
        private double lastLongSwingPrice = 0.0;
        private double recentHigh = double.NaN;
        private double longRetracementLow = double.MaxValue;
        private double longFirstEntryPrice = double.NaN;

        // Variables for short setup
        private int lastShortSwingBar = 0;
        private double lastShortSwingPrice = 0.0;
        private double recentLow = double.NaN;
        private double shortRetracementHigh = double.MinValue;
        private double shortFirstEntryPrice = double.NaN;

        private enum EntryState
        {
            WaitingForPivot,
            WaitingForFirstEntry,
            WaitingForRetracement,
            WaitingForSecondEntry
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "SecondEntryStrategy";
                Calculate = Calculate.OnBarClose;
                swingStrength = 2;
                MinPullbackPoints = 10.0; // Default minimum pullback magnitude
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < swingStrength + 1)
                return;

            int swingIndex = swingStrength;

            // Get the current confirmed swing high and low
            double swingHigh = Swing(swingStrength).SwingHigh[swingIndex];
            double swingLow = Swing(swingStrength).SwingLow[swingIndex];

            // Detect new confirmed swing highs and lows
            bool newSwingHigh = !double.IsNaN(swingHigh) && swingHigh != recentHigh;
            bool newSwingLow = !double.IsNaN(swingLow) && swingLow != recentLow;

            // Update recentHigh and handle long setup
            if (newSwingHigh)
            {
                recentHigh = swingHigh;
                lastLongSwingBar = CurrentBar - swingIndex;
                lastLongSwingPrice = recentHigh;
                longState = EntryState.WaitingForFirstEntry;
                longFirstEntryPrice = double.NaN; // Reset
                longRetracementLow = double.MaxValue; // Reset retracement low
                Print($"New Swing High at bar {lastLongSwingBar}, price {lastLongSwingPrice}");
            }

            // Update recentLow and handle short setup
            if (newSwingLow)
            {
                recentLow = swingLow;
                lastShortSwingBar = CurrentBar - swingIndex;
                lastShortSwingPrice = recentLow;
                shortState = EntryState.WaitingForFirstEntry;
                shortFirstEntryPrice = double.NaN; // Reset
                shortRetracementHigh = double.MinValue; // Reset retracement high
                Print($"New Swing Low at bar {lastShortSwingBar}, price {lastShortSwingPrice}");
            }

            // **Pivot Invalidation Check**

            // For long setup: If price drops below recent swing low, invalidate pivot
            if (longState != EntryState.WaitingForPivot && !double.IsNaN(recentLow) && High[0] >= lastLongSwingPrice)
            {
                Print($"Long pivot invalidated at bar {CurrentBar}");
                longState = EntryState.WaitingForPivot;
                longFirstEntryPrice = double.NaN;
            }

            // For short setup: If price rises above recent swing high, invalidate pivot
            if (shortState != EntryState.WaitingForPivot && !double.IsNaN(recentHigh) && Low[0] <= lastShortSwingPrice)
            {
                Print($"Short pivot invalidated at bar {CurrentBar}");
                shortState = EntryState.WaitingForPivot;
                shortFirstEntryPrice = double.NaN;
            }

            // Process long setup state machine
            if (longState != EntryState.WaitingForPivot)
            {
                switch (longState)
                {
                    case EntryState.WaitingForFirstEntry:
                        if (High[0] > High[1])
                        {
                            Print($"First Entry Long at bar {CurrentBar}");
                            longFirstEntryPrice = High[0];
                            longState = EntryState.WaitingForRetracement;
                            longRetracementLow = double.MaxValue; // Reset retracement low
                        }
                        break;

                    case EntryState.WaitingForRetracement:
                        // Update retracement low
                        if (Low[0] < longRetracementLow)
                            longRetracementLow = Low[0];

                        double longPullbackPoints = longFirstEntryPrice - longRetracementLow;

                        // Check if pullback magnitude is sufficient
                        if (longPullbackPoints >= MinPullbackPoints)
                        {
                            longState = EntryState.WaitingForSecondEntry;
                            Print($"Valid Retracement detected at bar {CurrentBar} for Long setup, pullback: {longPullbackPoints} points");
                        }
                        break;

                    case EntryState.WaitingForSecondEntry:
                        if (High[0] > High[1])
                        {
                            Print($"Second Entry Long at bar {CurrentBar}");
                            PlaceAtmOrder(OrderAction.Buy);
                            longState = EntryState.WaitingForPivot;
                            longFirstEntryPrice = double.NaN; // Reset
                        }
                        break;
                }
            }

            // Process short setup state machine
            if (shortState != EntryState.WaitingForPivot)
            {
                switch (shortState)
                {
                    case EntryState.WaitingForFirstEntry:
                        if (Low[0] < Low[1])
                        {
                            Print($"First Entry Short at bar {CurrentBar}");
                            shortFirstEntryPrice = Low[0];
                            shortState = EntryState.WaitingForRetracement;
                            shortRetracementHigh = double.MinValue; // Reset retracement high
                        }
                        break;

                    case EntryState.WaitingForRetracement:
                        // Update retracement high
                        if (High[0] > shortRetracementHigh)
                            shortRetracementHigh = High[0];

                        double shortPullbackPoints = shortRetracementHigh - shortFirstEntryPrice;

                        // Check if pullback magnitude is sufficient
                        if (shortPullbackPoints >= MinPullbackPoints)
                        {
                            shortState = EntryState.WaitingForSecondEntry;
                            Print($"Valid Retracement detected at bar {CurrentBar} for Short setup, pullback: {shortPullbackPoints} points");
                        }
                        break;

                    case EntryState.WaitingForSecondEntry:
                        if (Low[0] < Low[1])
                        {
                            Print($"Second Entry Short at bar {CurrentBar}");
                            PlaceAtmOrder(OrderAction.Sell);
                            shortState = EntryState.WaitingForPivot;
                            shortFirstEntryPrice = double.NaN; // Reset
                        }
                        break;
                }
            }

            // Update ATM strategy status
            ManageAtmStrategy();
        }

        private void PlaceAtmOrder(OrderAction orderAction)
        {
            if (orderId.Length == 0 && atmStrategyId.Length == 0 && State == State.Realtime)
            {
                isAtmStrategyCreated = false;
                orderId = GetAtmStrategyUniqueId();
                atmStrategyId = GetAtmStrategyUniqueId();

                AtmStrategyCreate(
                    orderAction,
                    OrderType.Market, 0, 0, TimeInForce.Gtc,
                    orderId, ATMStrategy, atmStrategyId,
                    (atmCallbackErrorCode, atmCallBackId) =>
                    {
                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        {
                            isAtmStrategyCreated = true;
                        }
                    });
            }
			
			if(State==State.Historical){
				if(orderAction == OrderAction.Sell){
					EnterShort();
					SetProfitTarget(CalculationMode.Ticks,80);
					SetStopLoss(CalculationMode.Ticks,40);
				}else if(orderAction == OrderAction.Buy){
					EnterLong();
					SetProfitTarget(CalculationMode.Ticks,80);
					SetStopLoss(CalculationMode.Ticks,40);
				}
			}
        }

        private void ManageAtmStrategy()
        {
            if (State == State.Realtime)
            {
                if (!isAtmStrategyCreated)
                    return;

                // Check for a pending entry order
                if (orderId.Length > 0)
                {
                    string[] status = GetAtmStrategyEntryOrderStatus(orderId);

                    // If the order state is terminal, reset the order id value
                    if (status.Length > 0 && (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected"))
                        orderId = string.Empty;
                }
                // If the strategy has terminated, reset the strategy id
                else if (atmStrategyId.Length > 0 && atmStrategyId != string.Empty && GetAtmStrategyMarketPosition(atmStrategyId) == MarketPosition.Flat)
                    atmStrategyId = string.Empty;
            }
        }
    }
}