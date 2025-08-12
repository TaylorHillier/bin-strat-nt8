#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class VolumeProfileShapeStrategy : Strategy
    {
        #region User Parameters
        [NinjaScriptProperty]
        [Display(Name = "Aggregation Ticks", Order = 1, GroupName = "Parameters")]
        public int AggregationTicks { get; set; }
        
        // This threshold is defined as a percentage of the bar's range for the initial offset check.
        // (It’s not used in the grouping logic below but can serve as a minimum filter if desired.)
        [NinjaScriptProperty]
        [Display(Name = "Volume Profile Offset Threshold (%)", Order = 2, GroupName = "Parameters")]
        public double VolumeProfileOffsetThreshold { get; set; }
        #endregion

        // Dictionary to store volume profile for each bar.
        // Key: Bar index. Value: Dictionary keyed by bucket price.
        private Dictionary<int, Dictionary<double, VolumeProfileData>> barProfiles = new Dictionary<int, Dictionary<double, VolumeProfileData>>();
        // For each bar, we store its anchor price – here, the bar’s low.
        private Dictionary<int, double> barLows = new Dictionary<int, double>();

        // Helper class to hold aggregated volume data for a price bucket.
        public class VolumeProfileData
        {
            public double Price { get; set; }
            public double BidVolume { get; set; }
            public double AskVolume { get; set; }
            public double TotalVolume { get { return BidVolume + AskVolume; } }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A strategy that detects specific volume profile shapes. It groups buckets around the max volume level. If the group (with buckets within 70% of the max) is isolated and lies below the bar's midpoint (B shape) a long is triggered, and if above (P shape) a short is triggered.";
                Name = "VolumeProfileShapeStrategy";
                Calculate = Calculate.OnBarClose; // Act on the completed bar.
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                BarsRequiredToTrade = 20;
                // Default parameter values.
                AggregationTicks = 4;
                VolumeProfileOffsetThreshold = 10.0; // Not used directly in grouping but kept as a parameter.
            }
            else if (State == State.Configure)
            {
                // Optionally, add a tick data series if available.
                // AddDataSeries(Data.BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize dictionaries.
                barProfiles = new Dictionary<int, Dictionary<double, VolumeProfileData>>();
                barLows = new Dictionary<int, double>();
            }
        }

        /// <summary>
        /// OnMarketData aggregates tick-by-tick volume into buckets for the bar that is forming.
        /// We assign data to the current bar index so that each bar’s profile is built.
        /// </summary>
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Process only Ask and Bid events.
            if (e.MarketDataType != MarketDataType.Ask && e.MarketDataType != MarketDataType.Bid)
                return;

            // Use the current bar index.
            int barIndex = CurrentBar;

            if (!barProfiles.ContainsKey(barIndex))
            {
                barProfiles[barIndex] = new Dictionary<double, VolumeProfileData>();
                // Use the current bar's low as the anchor.
                barLows[barIndex] = Low[0];
            }

            double baseLow = barLows[barIndex];
            double bucketSize = AggregationTicks * Instrument.MasterInstrument.TickSize;
            double bucket = baseLow + Math.Floor((e.Price - baseLow) / bucketSize) * bucketSize;

            var dict = barProfiles[barIndex];
            VolumeProfileData vpData;
            if (!dict.TryGetValue(bucket, out vpData))
            {
                vpData = new VolumeProfileData { Price = bucket, BidVolume = 0, AskVolume = 0 };
                dict[bucket] = vpData;
            }

            // Add tick volume to the bucket.
            if (e.MarketDataType == MarketDataType.Ask)
                vpData.AskVolume += e.Volume;
            else if (e.MarketDataType == MarketDataType.Bid)
                vpData.BidVolume += e.Volume;
        }

        /// <summary>
        /// OnBarUpdate is called on bar close. It processes the volume profile for the just-closed bar,
        /// groups buckets around the max volume bucket if they are within 70% of its value, verifies that buckets
        /// outside this group are significantly lower, and then compares the group's average price to the bar's midpoint.
        /// </summary>
        protected override void OnBarUpdate()
        {
            // Ensure we have at least one completed bar.
            if (CurrentBar < 1)
                return;

            // We process the bar that just closed: use the previous bar index.
            int closedBarIndex = CurrentBar - 1;
            if (!barProfiles.ContainsKey(closedBarIndex))
                return;

            double barHigh = High[1];
            double barLow = Low[1];
            double barRange = barHigh - barLow;
            if (barRange <= 0)
                return;

            // Retrieve and sort the buckets by price.
            var buckets = barProfiles[closedBarIndex].OrderBy(kvp => kvp.Key).ToList();
            if (buckets.Count == 0)
                return;

            // Step 1: Find the bucket with the maximum volume.
            double maxVolume = 0;
            int maxIndex = -1;
            for (int i = 0; i < buckets.Count; i++)
            {
                double vol = buckets[i].Value.TotalVolume;
                if (vol > maxVolume)
                {
                    maxVolume = vol;
                    maxIndex = i;
                }
            }
            if (maxIndex == -1)
                return;

            // Define the grouping threshold as 70% of maxVolume.
            double groupThreshold = 0.50 * maxVolume;

            // Step 2: Expand the group to include contiguous buckets that are at or above the groupThreshold.
            int groupStart = maxIndex;
            int groupEnd = maxIndex;

            // Expand to the left.
            for (int i = maxIndex - 1; i >= 0; i--)
            {
                if (buckets[i].Value.TotalVolume >= groupThreshold)
                    groupStart = i;
                else
                    break;
            }

            // Expand to the right.
            for (int i = maxIndex + 1; i < buckets.Count; i++)
            {
                if (buckets[i].Value.TotalVolume >= groupThreshold)
                    groupEnd = i;
                else
                    break;
            }

            // Step 3: Check that buckets outside the group are lower than the group threshold.
            bool validGroup = true;
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i < groupStart || i > groupEnd)
                {
                    if (buckets[i].Value.TotalVolume >= groupThreshold)
                    {
                        validGroup = false;
                        break;
                    }
                }
            }
            if (!validGroup)
            {
                // The profile does not have a distinct peak.
                return;
            }

            // Step 4: Compute the weighted average price of the group.
            double groupWeightedSum = 0;
            double groupVolumeSum = 0;
            for (int i = groupStart; i <= groupEnd; i++)
            {
                double bucketPrice = buckets[i].Key;
                double bucketVol = buckets[i].Value.TotalVolume;
                groupWeightedSum += bucketPrice * bucketVol;
                groupVolumeSum += bucketVol;
            }
            if (groupVolumeSum == 0)
                return;
            double groupAvgPrice = groupWeightedSum / groupVolumeSum;

            double barMid = (barHigh + barLow) / 2;
            double offset = groupAvgPrice - barMid;
            double offsetPercent = (offset / barRange) * 100.0;

            // Debug prints.
            Print($"Bar {closedBarIndex}: MaxVolume={maxVolume:F0}, Group from index {groupStart} to {groupEnd}, GroupAvgPrice={groupAvgPrice:F2}, BarMid={barMid:F2}, OffsetPercent={offsetPercent:F2}%");

            // Step 5: Define our shape conditions.
            // For a "B shape" (long candidate): the group is below the midpoint and the bar is bullish.
            if (offsetPercent < 0 && Close[1] > Open[1])
            {
                EnterLong("LongBShape");
                SetProfitTarget(CalculationMode.Ticks, 40);
                SetStopLoss(CalculationMode.Ticks, 40);
                Print($"Long trade triggered on bar {closedBarIndex} (B shape detected).");
            }
            // For a "P shape" (short candidate): the group is above the midpoint and the bar is bearish.
            else if (offsetPercent > 0 && Close[1] < Open[1])
            {
                EnterShort("ShortPShape");
                SetProfitTarget(CalculationMode.Ticks, 40);
                SetStopLoss(CalculationMode.Ticks, 40);
                Print($"Short trade triggered on bar {closedBarIndex} (P shape detected).");
            }
        }
    }
}
