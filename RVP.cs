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
	public class RVP : Strategy
    {
        // Initialization of variables
        private double lastPrice = 0;
		private double current = 0;
        private double currentBidPrice = 0;
        private double currentAskPrice = 0;
        private List<double> AskValues = new List<double>();
        private List<double> BidValues = new List<double>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "RVP";
                Calculate = Calculate.OnEachTick; // Changed to OnEachTick for tick-level processing
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
            }
            else if (State == State.Configure)
            {
                // Configure additional strategy settings here if needed
            }
        }

        protected override void OnBarUpdate()
        {
            // Currently empty. Implement if needed.
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Ensure sufficient bars are loaded before executing logic
            if (CurrentBar < BarsRequiredToTrade)
                return;

            if (e.MarketDataType != MarketDataType.Last && State == State.Realtime)
                return;

            double price = e.Price;
            double ask = e.Ask;
            double bid = e.Bid;
            bool lastAsk = false;
            bool lastBid = false;
            double askAverage = 0;
            double bidAverage = 0;
			
            // Initialize lastPrice if not set
            if (lastPrice == 0)
                lastPrice = price;

            // Determine if the trade occurred at the ask or bid
            if (price == ask)
            {
                current += e.Volume;
                lastAsk = true;
            }
            else if (price == bid)
            {
                current += e.Volume;
                lastBid = true;
            }

            // Reset current ask volume if the ask price changes
            if (ask != lastPrice && lastAsk)
            {
				Print(current);
				AskValues.Add(current);
                lastPrice = price;
				current = 0;
               
            }

            // Reset current bid volume if the bid price changes
            if (bid != lastPrice && lastBid)
            {
				Print(current);
				BidValues.Add(current);
                lastPrice = price;
				current = 0;
             
            }

            // Manage list sizes correctly
            if (AskValues.Count > 500)
            {
                AskValues.RemoveAt(0);
               
            }

            if (BidValues.Count > 500)
            {
                BidValues.RemoveAt(0); // Corrected to remove from BidValues
               
            }

            // Ensure there are enough data points to calculate averages
            if (!AskValues.Any() || !BidValues.Any())
                return;

            // Calculate averages
            askAverage = AskValues.Sum() / AskValues.Count();
            bidAverage = BidValues.Sum() / BidValues.Count();

            // Print averages for debugging
            Print($"Ask Average: {askAverage}");
            Print($"Bid Average: {bidAverage}");

            // Entry and Exit Logic

            // Exit Short if currently Short and askAverage > bidAverage
            if (Position.MarketPosition == MarketPosition.Short && askAverage > bidAverage)
            {
                ExitShort("short");
                Print("Exiting Short position.");
            }

            // Enter Long if askAverage > bidAverage and not already Long
            if (askAverage > bidAverage)
            {
                if (Position.MarketPosition != MarketPosition.Long)
                {
                    EnterLong("buy");
                    Print("Entering Long position.");
                }
            }

            // Exit Long if currently Long and askAverage < bidAverage
            if (Position.MarketPosition == MarketPosition.Long && askAverage < bidAverage)
            {
                ExitLong("buy");
                Print("Exiting Long position.");
            }

            // Enter Short if askAverage < bidAverage and not already Short
            if (askAverage < bidAverage)
            {
                if (Position.MarketPosition != MarketPosition.Short)
                {
                    EnterShort("short");
                    Print("Entering Short position.");
                }
            }
        }
    }
}
