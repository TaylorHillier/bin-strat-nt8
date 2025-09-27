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
    public class Trade {
        public double Price { get; }
        public double Quantity { get; }
        public int BarIndex { get; }

        public Trade(double price, double quantity, int barIndex)
        {
            Price = price;
            Quantity = quantity;
            BarIndex = barIndex;
        }
    }

    public class Accumulator
    {
        private readonly Dictionary<double, Queue<Trade>> tradesByPrice =
            new Dictionary<double, Queue<Trade>>();

        private Dictionary<double, double> qtyByPrice = new Dictionary<double, double>();

        public void AddTrade(double price, double quantity, int barIndex, bool debug = false)
        {
            if (!tradesByPrice.ContainsKey(price))
            {
                tradesByPrice[price] = new Queue<Trade>();
                qtyByPrice[price] = 0;
                if (debug)
                    System.Diagnostics.Debug.WriteLine($"[Accumulator:AddTrade] Created bucket @ {price:F5}");
            }

            tradesByPrice[price].Enqueue(new Trade(price, quantity, barIndex));
            qtyByPrice[price] += quantity;

            if (debug)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Accumulator:AddTrade] +Enqueue Trade(price={price:F5}, qty={quantity}, bar={barIndex}) " +
                    $"=> Totals: qtyByPrice[{price:F5}]={qtyByPrice[price]}, queueCount={tradesByPrice[price].Count}");
            }
        }

        public void RemoveExpiredTrades(int currentBarIndex, int barExpiry, bool debug = false)
        {
            if (debug)
                System.Diagnostics.Debug.WriteLine($"[Accumulator:RemoveExpiredTrades] START currentBar={currentBarIndex} window={barExpiry}");

            foreach (var price in tradesByPrice.Keys.ToList())
            {
                var queue = tradesByPrice[price];

                if (debug)
                    System.Diagnostics.Debug.WriteLine(
                        $"[Accumulator:RemoveExpiredTrades] Checking @ {price:F5} | queueCount={queue.Count}, totalQty={qtyByPrice[price]}");

                while (queue.Count > 0 && currentBarIndex - queue.Peek().BarIndex >= barExpiry)
                {
                    var head = queue.Peek();
                    if (debug)
                        System.Diagnostics.Debug.WriteLine(
                            $"[Accumulator:RemoveExpiredTrades] Expiring head trade @ {price:F5}: (qty={head.Quantity}, bar={head.BarIndex}), age={currentBarIndex - head.BarIndex}");

                    var expiredTrade = queue.Dequeue();
                    qtyByPrice[price] -= expiredTrade.Quantity;

                    if (debug)
                        System.Diagnostics.Debug.WriteLine(
                            $"[Accumulator:RemoveExpiredTrades] -Dequeue qty={expiredTrade.Quantity} -> qtyByPrice[{price:F5}]={qtyByPrice[price]}, queueCountNow={queue.Count}");

                    if (qtyByPrice[price] < 0)
                    {
                        if (debug)
                            System.Diagnostics.Debug.WriteLine(
                                $"[Accumulator:RemoveExpiredTrades][ERROR] Negative qty at {price:F5}! qtyByPrice={qtyByPrice[price]}");
                        throw new InvalidOperationException("It is impossible to have a negative quantity at a price level, check code logic");
                    }
                }

                if (qtyByPrice[price] == 0 && tradesByPrice[price].Count == 0)
                {
                    if (debug)
                        System.Diagnostics.Debug.WriteLine($"[Accumulator:RemoveExpiredTrades] Removing empty bucket @ {price:F5}");
                    tradesByPrice.Remove(price);
                    qtyByPrice.Remove(price);
                }
                else if (debug)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Accumulator:RemoveExpiredTrades] After cleanup @ {price:F5}: queueCount={(tradesByPrice.ContainsKey(price) ? tradesByPrice[price].Count : 0)}, totalQty={(qtyByPrice.ContainsKey(price) ? qtyByPrice[price] : 0)}");
                }
            }

            if (debug)
                System.Diagnostics.Debug.WriteLine($"[Accumulator:RemoveExpiredTrades] END currentBar={currentBarIndex}");
        }

        public double GetQtyAtPrice(double price, bool debug = false)
        {
            var found = qtyByPrice.TryGetValue(price, out double qty);
            if (debug)
                System.Diagnostics.Debug.WriteLine($"[Accumulator:GetQtyAtPrice] Lookup @ {price:F5} -> found={found}, qty={qty}");
            return found ? qty : 0;
        }

        public Dictionary<double, double> GetAllQuantities()
        {
            return new Dictionary<double, double>(qtyByPrice);
        }
    }

    public class TrappedParticipants : Strategy
    {
        #region Variables
        [NinjaScriptProperty]
        [Range(5, int.MaxValue)]
        [Display(Name = "Period", Description = "Period to reference for accumulator", Order = 1, GroupName = "Parameters")]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Range(5, int.MaxValue)]
        [Display(Name = "Threshold", Description = "Threshold for impactful level", Order = 2, GroupName = "Parameters")]
        public int Threshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DebugMode", Description = "Enable extra debug logging", Order = 3, GroupName = "Parameters")]
        public bool DebugMode { get; set; }

        private double currentPrice;
        private readonly Accumulator accumulator = new Accumulator();
        #endregion;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "TrappedParticipants";
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

                Period = 20;
                Threshold = 40;
                DebugMode = false;

                if (DebugMode)
                    Print($"[State.SetDefaults] Strategy={Name}, Default Period={Period}");
            }
            else if (State == State.Configure)
            {
                if (DebugMode) Print("[State.Configure] Configure step");
            }
            else if (State == State.DataLoaded)
            {
                if (DebugMode) Print("[State.DataLoaded] Data loaded; accumulator ready");
            }
        }

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
			{
				if (DebugMode)
					Print($"[OnBarUpdate] Skipping until BarsRequiredToTrade. CurrentBar={CurrentBar}, Required={BarsRequiredToTrade}");
				return;
			}

			if (DebugMode)
				Print($"[OnBarUpdate] Bar={CurrentBar} | Calling RemoveExpiredTrades(window={Period})");

			accumulator.RemoveExpiredTrades(CurrentBar, Period, DebugMode);

			if (DebugMode)
			{
				var filtered = accumulator.GetAllQuantities()
					.Where(kv => kv.Value > Threshold)
					.OrderByDescending(kv => kv.Key)
					.Select(kv => $"{kv.Key:F5}:{kv.Value}\n");

				if (filtered.Any())
					Print(filtered.Aggregate((a, b) => a + ", " + b));
				else
					Print("No price levels above threshold.");
			}

			// always-on summary print
			double aboveCurPrice = 0;
			double belowCurPrice = 0;

			double avgPriceAbove = 0;
			double avgPriceBelow = 0;

			double keysAbove = 0;
			double keysBelow = 0;

			foreach (var kv in accumulator.GetAllQuantities())
			{
				if (kv.Value >= Threshold)
				{
					if (kv.Key > Close[0])
					{
						aboveCurPrice += kv.Value;
						avgPriceAbove += kv.Key * kv.Value;

					}
					else if (kv.Key < Close[0])
					{
						belowCurPrice += kv.Value;
						avgPriceBelow += kv.Key * kv.Value;
					}

				}
			}

			avgPriceAbove = aboveCurPrice > 0 ? avgPriceAbove / aboveCurPrice : 0;
			avgPriceBelow = belowCurPrice > 0 ? avgPriceBelow / belowCurPrice : 0;
			Print($"[OnBarUpdate] CurrentPrice={currentPrice:F5}, Close={Close[0]:F5}, aboveCurPrice={aboveCurPrice}, belowCurPrice={belowCurPrice}");
			Print($"[OnBarUpdate] AvgPriceAbove={avgPriceAbove:F5} AvgPriceBelow={avgPriceBelow:F5}");

			Draw.HorizontalLine(this, $"above", avgPriceAbove, Brushes.Green);
			Draw.HorizontalLine(this, $"below", avgPriceBelow, Brushes.Red);
			Draw.Text(this, $"aboveText", $"{aboveCurPrice}", 0, Low[0] - 1);
			Draw.Text(this, $"belowText", $"{belowCurPrice}", 0, Low[0] - 2);
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last) return;

            currentPrice = e.Price;

            accumulator.AddTrade(e.Price, e.Volume, CurrentBar, DebugMode);
        }
    }
}