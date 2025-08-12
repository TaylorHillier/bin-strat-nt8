#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using System.Net.Http;
using System.Web.Script.Serialization;
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
using System.IO;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class OFImbalance : Strategy
    {
        // Existing parameters.
        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "Imbalance Threshold", Order = 1, GroupName = "Parameters")]
        public double ImbalanceThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Additional Entries", Order = 2, GroupName = "Parameters")]
        public bool AllowAdditionalEntries { get; set; }
		
        [NinjaScriptProperty]
        [Display(Name = "ATMStrategy", Order = 3, GroupName = "Parameters")]
        public string ATMStrategy { get; set; }
		
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Aggregation Ticks", Order = 4, GroupName = "Parameters")]
        public int AggregationTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Depth Levels to Sum", Order = 5, GroupName = "Parameters")]
        public int DepthLevelsToSum { get; set; }


        // Internal variables for handling order management (existing logic).
        public string atmStrategyId = string.Empty;
        public string orderId = string.Empty;
        public bool isAtmStrategyCreated = false;
		
        // Dictionaries to store aggregated bid and ask volumes.
        private Dictionary<double, long> aggregatedBids = new Dictionary<double, long>();
        private Dictionary<double, long> aggregatedAsks = new Dictionary<double, long>();

        // Tick size used for bucketing prices.
        private double tickSize;
        // Stores the most recent tradeable price.
        private double price = 0;
		
        // Enum to track current imbalance side.
        private enum ImbalanceSide { None, Long, Short }
        private ImbalanceSide currentImbalanceSide = ImbalanceSide.None;
        private int imbalanceUpdateCount = 0;
        // These variables store values to be displayed in OnRender.
        private long currentTotalBidVolume = 0;
        private long currentTotalAskVolume = 0;
        private double currentImbalance = 0.0;


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Trades based on market depth imbalance and a Bayesian microprobability forecast, combining a one-tick metric with Bayesian updates.";
                Name = "OFImbalance";
                Calculate = Calculate.OnEachTick; // For near real-time execution.
                ImbalanceThreshold = 0.5;
                AllowAdditionalEntries = false;
                AggregationTicks = 4;
                DepthLevelsToSum = 20;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                BarsRequiredToTrade = 1;
            }
            else if (State == State.Configure)
            {
                tickSize = Instrument.MasterInstrument.TickSize;
            }
        }
		
		double lastPrice = 0;


		// Per-bar counters for current bar
		double totalUp = 0;
		double totalDown = 0;
		double withUp = 0;
		double withDown = 0;
		double correlationUp = 0;
		double correlationDown = 0;
		double activeBar = -1;
		
		// Rolling lists to store the per-bar totals (up to the last 500 bars)
		List<double> rollingTotalUp = new List<double>();
		List<double> rollingWithUp = new List<double>();
		List<double> rollingTotalDown = new List<double>();
		List<double> rollingWithDown = new List<double>();
		
		protected override void OnMarketData(MarketDataEventArgs e)
		{

	 
	
	        // Enforce a maximum of 500 entries per list.
	        if (rollingTotalUp.Count > 500)
	        {
	            rollingTotalUp.RemoveAt(0);
	            rollingWithUp.RemoveAt(0);
	        }
	        if (rollingTotalDown.Count > 500)
	        {
	            rollingTotalDown.RemoveAt(0);
	            rollingWithDown.RemoveAt(0);
	        }

		    
		
			if(State != State.Realtime)
				return;
			
		    // Process only last-price market data events.
		    if (e.MarketDataType == MarketDataType.Last)
		    {
		        price = e.Price;
		
		        // Evaluate price changes versus the last tick.
		        if (price > lastPrice)
		        {
		           rollingTotalUp.Add(1);
		            if (currentImbalance > 0)
		            {

					   rollingWithUp.Add(1);

		            }
		            else if (currentImbalance < 0)
		            {
		               rollingWithDown.Add(-1);
		            }
		        }
		        else if (price < lastPrice)
		        {
		              rollingTotalDown.Add(1);
		            if (currentImbalance < 0)
		            {
		              rollingWithDown.Add(1);
		            }
		            else if (currentImbalance > 0)
		            {
		                rollingWithUp.Add(-1);
		            }
		        }
		
		        // Compute the per-bar correlations (if totals are nonzero).
		        correlationUp  = rollingTotalUp.Sum() != 0 ? rollingWithUp.Sum() / rollingTotalUp.Sum() : 0;
		        correlationDown  = rollingTotalDown.Sum() != 0 ? rollingWithDown.Sum() / rollingTotalDown.Sum() : 0;

		        lastPrice = price;
		    }
		}


        protected override void OnMarketDepth(MarketDepthEventArgs e)
        {

            if (tickSize <= 0)
                return;

            // Calculate the bucketing size using AggregationTicks.
            double bucketSize = tickSize * AggregationTicks;
            double aggPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Floor(e.Price / bucketSize) * bucketSize);

            // Determine if the update is for bids or asks.
            bool isBid = e.MarketDataType == MarketDataType.Bid;
            Dictionary<double, long> targetDict = isBid ? aggregatedBids : aggregatedAsks;

            // Update the aggregated volume for this bucket.
            if (e.Operation == Operation.Remove || (e.Operation == Operation.Update && e.Volume == 0))
            {
                targetDict.Remove(aggPrice);
            }
            else if (e.Operation == Operation.Add || e.Operation == Operation.Update)
            {
                targetDict[aggPrice] = e.Volume;
            }
			
            // Get the current best bid/ask from the exchange.
            double currentBestBid = GetCurrentBid();
            double currentBestAsk = GetCurrentAsk();
			
            // Define boundaries for summing volumes.
            double bidLowerBound = currentBestBid - (DepthLevelsToSum - 1) * bucketSize;
            double askUpperBound = currentBestAsk + (DepthLevelsToSum - 1) * bucketSize;
			
            long sumBidVolume = 0;
            long sumAskVolume = 0;
			
            foreach (var kvp in aggregatedBids)
            {
                if (kvp.Key <= currentBestBid && kvp.Key >= bidLowerBound)
                    sumBidVolume += kvp.Value;
            }
            foreach (var kvp in aggregatedAsks)
            {
                if (kvp.Key >= currentBestAsk && kvp.Key <= askUpperBound)
                    sumAskVolume += kvp.Value;
            }
			
            // Calculate the imbalance from the aggregated volumes.
            double imbalance = 0.0;
            if (sumBidVolume + sumAskVolume > 0)
                imbalance = (sumBidVolume - sumAskVolume) / (double)(sumBidVolume + sumAskVolume);
			
            // --- Persistence Filter Logic ---
            // Update the current imbalance side and the count of consecutive updates.
            if (imbalance > ImbalanceThreshold)
            {
               currentImbalanceSide = ImbalanceSide.Long;   
            }
            else if (imbalance < -ImbalanceThreshold)
            {
               currentImbalanceSide = ImbalanceSide.Short;
            }
            else
            {
                currentImbalanceSide = ImbalanceSide.None;
                imbalanceUpdateCount = 0;
            }
			
            // Print debug info.
            //Print($"MD Event: SumBid={sumBidVolume}, SumAsk={sumAskVolume}, Imbalance={imbalance:0.00}, PersistenceCount={imbalanceUpdateCount}");
			
            // Save values for displaying on the chart.
            currentTotalBidVolume = sumBidVolume;
            currentTotalAskVolume = sumAskVolume;
            currentImbalance = imbalance;
          
            // For this example, we set a threshold for triggering a trade.
            double triggerThreshold = 0.70;

            if (currentImbalanceSide == ImbalanceSide.Long &&  correlationDown < correlationUp)
            {
				            // Avoid duplicate orders.
	            if (orderId.Length > 0 || atmStrategyId.Length > 0)
	                return;
				
				 isAtmStrategyCreated = false;
           		 orderId = GetAtmStrategyUniqueId();
           		 atmStrategyId = GetAtmStrategyUniqueId();
				
                AtmStrategyCreate(
                    OrderAction.Buy,
                    OrderType.Limit,
                    price,
                    0,
                    TimeInForce.Gtc,
                    orderId,
                    ATMStrategy,
                    atmStrategyId,
                    (atmCallbackErrorCode, atmCallBackId) =>
                    {
                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                            isAtmStrategyCreated = true;
                    }
                );
            }
            else if (currentImbalanceSide == ImbalanceSide.Short &&  correlationDown > correlationUp)
            {
				
				 if (orderId.Length > 0 || atmStrategyId.Length > 0)
	                return;
				
				 isAtmStrategyCreated = false;
           		 orderId = GetAtmStrategyUniqueId();
           		 atmStrategyId = GetAtmStrategyUniqueId();
				 
                AtmStrategyCreate(
                    OrderAction.Sell,
                    OrderType.Limit,
                    price,
                    0,
                    TimeInForce.Gtc,
                    orderId,
                    ATMStrategy,
                    atmStrategyId,
                    (atmCallbackErrorCode, atmCallBackId) =>
                    {
                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                            isAtmStrategyCreated = true;
                    }
                );
            }
			
            // In realtime, check pending orders and reset IDs when necessary.
            if (State == State.Realtime)
            {
                if (!isAtmStrategyCreated)
                    return;

                if (orderId.Length > 0)
                {
                    string[] status = GetAtmStrategyEntryOrderStatus(orderId);
                    if (status.Length > 0 && (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected"))
                        orderId = string.Empty;
                }
                else if (atmStrategyId.Length > 0 && GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat)
                {
                    atmStrategyId = string.Empty;
                }
            }
        }

        /// <summary>
        /// Override OnRender to display current total bid, total ask, imbalance,
        /// and the current long and short microprobabilities on the chart.
        /// </summary>
        /// <param name="chartControl">ChartControl object</param>
        /// <param name="chartScale">ChartScale object</param>
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Call the base method first.
            base.OnRender(chartControl, chartScale);

            // Compose the display text.
            string displayText = $"Total Bid: {currentTotalBidVolume}\n" +
                                 $"Total Ask: {currentTotalAskVolume}\n" +
                                 $"Imbalance: {currentImbalance:0.00}\n" +
				 				 $"CorrelationUp: {correlationUp:0.00}\n" +
								 $"CorrelationDown: {correlationDown:0.00}\n";

            // Use Draw.TextFixed to display the text at the top-left of the chart.
            // This built-in helper automatically refreshes the overlay each render.
            Draw.TextFixed(this, "OverlayInfo", displayText, TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 12), Brushes.Transparent, Brushes.Transparent, 0);
        }
    }
}
