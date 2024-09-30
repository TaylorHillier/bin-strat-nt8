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

namespace NinjaTrader.NinjaScript.Strategies
{
    public class WaveRider : Strategy
    {
        // ** Property Definitions **
        [NinjaScriptProperty]
        [Display(Name = "Anomalous Bid Count", Order = 1, GroupName = "Parameters")]
        public int AnomalousBidCount { get; set; } = 20; // Number of consecutive bids at the same price to consider anomalous

        [NinjaScriptProperty]
        [Display(Name = "Anomalous Ask Count", Order = 2, GroupName = "Parameters")]
        public int AnomalousAskCount { get; set; } = 20; // Number of consecutive asks at the same price to consider anomalous

        [NinjaScriptProperty]
        [Display(Name = "Breakout Threshold", Order = 3, GroupName = "Parameters")]
        public double BreakoutThreshold { get; set; } = 0.5; // Price movement required to trigger a breakout

        // ** Queues to Store the Last 10 Bid and Ask Events **
        private Queue<(double Price, long Volume)> last10BidEvents = new Queue<(double, long)>();
        private Queue<(double Price, long Volume)> last10AskEvents = new Queue<(double, long)>();

        // ** Lists to Store Multiple Anomalous Bid and Ask Price Levels **
        private List<double> anomalousBidPrices = new List<double>();
        private List<double> anomalousAskPrices = new List<double>();

		private int MaxEventCount = 50;
        // ** Trade Action Enum **
        public enum TradeAction
        {
            None,
            Buy,
            Sell
        }

        // ** ATM Strategy Variables (Existing) **
        public string atmStrategyId = string.Empty;
        public string orderId = string.Empty;
        public bool isAtmStrategyCreated = false;

        // ** Mode Control Variables (Existing) **
        private bool isLongMode = false;
        private bool isShortMode = false;
        private bool isRegressionMode = false;
        private bool isTrendMode = false;

        // ** UI Elements (Existing) **
        private System.Windows.Controls.Button longButton;
        private System.Windows.Controls.Button shortButton;
        private System.Windows.Controls.Grid myGrid;
        private System.Windows.Controls.Button modeButton;

        // ** Pattern Detection Variables **
        private bool tradeTaken = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Strategy based on pattern detection of bid and ask events for enhanced trading signals.";
                Name = "WaveRider";
                Calculate = Calculate.OnEachTick;
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
                isTrendMode = true;
                isRegressionMode = false;
                IsOverlay = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Volume, 1);
            }
            else if (State == State.Historical)
            {
                if (UserControlCollection.Contains(myGrid))
                    return;

                Dispatcher.InvokeAsync((() =>
                {
                    myGrid = new System.Windows.Controls.Grid
                    {
                        Name = "MyCustomGrid",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 60)
                    };

                    System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition();
                    System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition();
                    System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition();

                    myGrid.ColumnDefinitions.Add(column1);
                    myGrid.ColumnDefinitions.Add(column2);
                    myGrid.ColumnDefinitions.Add(column3);

                    longButton = new System.Windows.Controls.Button
                    {
                        Name = "LongButton",
                        Content = isLongMode ? "Armed Long" : "Arm Long",
                        Foreground = Brushes.White,
                        Background = isLongMode ? Brushes.Green : Brushes.Gray,
                    };

                    shortButton = new System.Windows.Controls.Button
                    {
                        Name = "ShortButton",
                        Content = isShortMode ? "Armed Short" : "Arm Short",
                        Foreground = Brushes.White,
                        Background = isShortMode ? Brushes.Red : Brushes.Gray,
                    };

                    modeButton = new System.Windows.Controls.Button
                    {
                        Name = "ModeButton",
                        Foreground = Brushes.White,
                        Background = isRegressionMode ? Brushes.Teal : Brushes.Purple,
                        Content = isRegressionMode ? "Regression" : "Trend",
                    };

                    longButton.Click += OnButtonClick;
                    shortButton.Click += OnButtonClick;
                    modeButton.Click += OnButtonClick;

                    System.Windows.Controls.Grid.SetColumn(longButton, 1);
                    System.Windows.Controls.Grid.SetColumn(shortButton, 2);
                    System.Windows.Controls.Grid.SetColumn(modeButton, 0);

                    myGrid.Children.Add(longButton);
                    myGrid.Children.Add(shortButton);
                    myGrid.Children.Add(modeButton);

                    UserControlCollection.Add(myGrid);
                }));
            }
            else if (State == State.Terminated)
            {
                Dispatcher.InvokeAsync((() =>
                {
                    if (myGrid != null)
                    {
                        if (longButton != null)
                        {
                            myGrid.Children.Remove(longButton);
                            longButton.Click -= OnButtonClick;
                            longButton = null;
                        }
                        if (shortButton != null)
                        {
                            myGrid.Children.Remove(shortButton);
                            shortButton.Click -= OnButtonClick;
                            shortButton = null;
                        }
                        if (modeButton != null)
                        {
                            myGrid.Children.Remove(modeButton);
                            modeButton.Click -= OnButtonClick;
                            modeButton = null;
                        }
                    }
                }));
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 1 )
                return;

            // Implement any necessary logic related to bars here
            // For this pattern-based strategy, bar updates might be less critical
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Handle only Real-time Market Data
            if (State != State.Realtime)
                return;

            // Handle Last Trade Events
            if (e.MarketDataType == MarketDataType.Last)
            {
                double currentPrice = e.Price;
                bool isAskSide = currentPrice > (e.Ask + e.Bid) / 2;

                // Print Bid or Ask Series
                if (isAskSide)
                {
                    Print($"Ask Series: {currentPrice} price : {e.Volume}");
                }
                else
                {
                    Print($"Bid Series: {currentPrice} price : {e.Volume}");
                }

                // ** Pattern Detection Logic **
                DetectPatternAndTrade(isAskSide, currentPrice, e.Volume);
            }

            // ** Handle Bid Price Updates **
            else if (e.MarketDataType == MarketDataType.Bid)
            {
                double bidPrice = e.Price;
                long bidVolume = e.Volume; // Changed from int to long

                // Enqueue the new bid event
                EnqueueMarketEvent(last10BidEvents, (bidPrice, bidVolume));

                // Print the last 10 bid events
                PrintBidEvents();
            }

            // ** Handle Ask Price Updates **
            else if (e.MarketDataType == MarketDataType.Ask)
            {
                double askPrice = e.Price;
                long askVolume = e.Volume; // Changed from int to long

                // Enqueue the new ask event
                EnqueueMarketEvent(last10AskEvents, (askPrice, askVolume));

                // Print the last 10 ask events
                PrintAskEvents();
            }

            // ** Existing ATM strategy handling code **
            HandleAtmStrategy(e);
        }

        /// <summary>
        /// Enqueues a new market event into the specified queue.
        /// If the queue exceeds the maximum allowed count, dequeues the oldest event.
        /// </summary>
        /// <param name="eventQueue">The queue to enqueue the event into.</param>
        /// <param name="marketEvent">The market event to enqueue.</param>
        private void EnqueueMarketEvent(Queue<(double Price, long Volume)> eventQueue, (double Price, long Volume) marketEvent)
        {
            eventQueue.Enqueue(marketEvent);
            if (eventQueue.Count > MaxEventCount)
            {
                eventQueue.Dequeue();
            }
        }

        /// <summary>
        /// Prints the last 10 bid events, including price and volume.
        /// </summary>
        private void PrintBidEvents()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Last 10 Bid Events:");
            int count = 1;
            foreach (var bid in last10BidEvents)
            {
                sb.AppendLine($"{count}: Price = {bid.Price}, Volume = {bid.Volume}");
                count++;
            }
            Print(sb.ToString());
        }

        /// <summary>
        /// Prints the last 10 ask events, including price and volume.
        /// </summary>
        private void PrintAskEvents()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Last 10 Ask Events:");
            int count = 1;
            foreach (var ask in last10AskEvents)
            {
                sb.AppendLine($"{count}: Price = {ask.Price}, Volume = {ask.Volume}");
                count++;
            }
            Print(sb.ToString());
        }

        /// <summary>
        /// Detects patterns based on bid/ask events and triggers trades accordingly.
        /// </summary>
        /// <param name="isAskSide">Indicates if the current event is an ask.</param>
        /// <param name="currentPrice">The price of the current event.</param>
        /// <param name="currentVolume">The volume of the current event.</param>
        private void DetectPatternAndTrade(bool isAskSide, double currentPrice, long currentVolume)
        {
            if (isAskSide)
            {
                // ** Detect Anomalous Ask Pattern **
                if (IsAnomalousPattern(last10AskEvents, out double detectedAskPrice))
                {
                    if (!anomalousAskPrices.Contains(detectedAskPrice))
                    {
                        anomalousAskPrices.Add(detectedAskPrice);
                        Print($"Anomalous Ask Detected at Price: {detectedAskPrice}");
                    }
                }

                // ** Check for Breakout Above All Anomalous Ask Prices **
                if (anomalousAskPrices.Count > 0)
                {
                    double highestAnomalousAsk = anomalousAskPrices.Max();
                    if (currentPrice > highestAnomalousAsk + BreakoutThreshold )
                    {
                        Print($"Ask Breakout Above Highest Anomalous Price: {currentPrice}");
                        if (isLongMode && !tradeTaken && isTrendMode || isShortMode && !tradeTaken && isRegressionMode)
                        {
                            CreateAtmStrategy(OrderAction.Buy);
                            tradeTaken = true;
                            anomalousAskPrices.Clear(); // Reset after trade
                        }
                    }
                }
            }
            else
            {
                // ** Detect Anomalous Bid Pattern **
                if (IsAnomalousPattern(last10BidEvents, out double detectedBidPrice))
                {
                    if (!anomalousBidPrices.Contains(detectedBidPrice))
                    {
                        anomalousBidPrices.Add(detectedBidPrice);
                        Print($"Anomalous Bid Detected at Price: {detectedBidPrice}");
                    }
                }

                // ** Check for Breakout Below All Anomalous Bid Prices **
                if (anomalousBidPrices.Count > 0)
                {
                    double lowestAnomalousBid = anomalousBidPrices.Min();
                    if (currentPrice < lowestAnomalousBid - BreakoutThreshold)
                    {
                        Print($"Bid Breakout Below Lowest Anomalous Price: {currentPrice}");
                        if (isShortMode && !tradeTaken && isTrendMode || isLongMode && !tradeTaken && isRegressionMode)
                        {
                            CreateAtmStrategy(OrderAction.Sell);
                            tradeTaken = true;
                            anomalousBidPrices.Clear(); // Reset after trade
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines if the last events form an anomalous pattern based on consecutive counts.
        /// </summary>
        /// <param name="eventQueue">The queue containing the last events.</param>
        /// <param name="detectedPrice">Outputs the detected anomalous price if a pattern is found.</param>
        /// <returns>True if an anomalous pattern is detected; otherwise, false.</returns>
        private bool IsAnomalousPattern(Queue<(double Price, long Volume)> eventQueue, out double detectedPrice)
        {
            detectedPrice = 0.0;

            int requiredCount = isAskMode() ? AnomalousAskCount : AnomalousBidCount;

            if (eventQueue.Count < requiredCount)
                return false;

            // Group events by price
            var grouped = eventQueue.GroupBy(e => e.Price)
                                    .Select(g => new { Price = g.Key, Count = g.Count(), TotalVolume = g.Sum(e => e.Volume) })
                                    .OrderByDescending(g => g.Count)
                                    .ThenByDescending(g => g.TotalVolume)
                                    .ToList();

            if (grouped.Count == 0)
                return false;

            var topGroup = grouped.First();

            // Check if the top group meets the anomalous count
            if (topGroup.Count >= requiredCount)
            {
                detectedPrice = topGroup.Price;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if the current mode is Ask or Bid based on the anomalous price lists.
        /// </summary>
        /// <returns>True if in Ask mode; otherwise, false.</returns>
        private bool isAskMode()
        {
            // Determines the requiredCount based on whether there are anomalous asks
            return anomalousAskPrices.Count > 0;
        }

        /// <summary>
        /// Handles the creation and management of ATM strategies.
        /// </summary>
        /// <param name="e">Market data event arguments.</param>
        private void HandleAtmStrategy(MarketDataEventArgs e)
        {
            if (atmStrategyId.Length > 0)
            {
                if (GetAtmStrategyMarketPosition(atmStrategyId) == MarketPosition.Flat)
                {
                    atmStrategyId = string.Empty;
                    isAtmStrategyCreated = false;
                }
            }

            // Reset tradeTaken flag when no ATM strategy is active
            if (atmStrategyId.Length == 0)
            {
                tradeTaken = false;
            }
        }

        /// <summary>
        /// Creates an ATM strategy based on the specified action.
        /// </summary>
        /// <param name="action">The order action (Buy/Sell).</param>
        private void CreateAtmStrategy(OrderAction action)
        {
            isAtmStrategyCreated = false;
            orderId = GetAtmStrategyUniqueId();
            atmStrategyId = GetAtmStrategyUniqueId();

            AtmStrategyCreate(
                action,
                OrderType.Market, 0, 0, TimeInForce.Gtc,
                orderId, ATMStrategy, atmStrategyId,
               (atmCallbackErrorCode, atmCallBackId) =>
                {
                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                    {
                        isAtmStrategyCreated = true;
                        Print($"ATM strategy '{ATMStrategy}' created successfully with ID: {atmStrategyId}");
                    }
                    else
                    {
                        Print($"ATM Strategy Creation Failed: {atmCallbackErrorCode}");
                    }
                });
        }

        [NinjaScriptProperty]
        [Display(Name = "ATM Strategy Name", Order = 2, GroupName = "Parameters")]
        public string ATMStrategy { get; set; }

        /// <summary>
        /// Handles button click events for mode and trade controls.
        /// </summary>
        /// <param name="sender">The button that was clicked.</param>
        /// <param name="e">Event arguments.</param>
        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;

            string buttonText = button.Content.ToString();
            string buttonName = button.Name;

            if (button == shortButton && buttonText == "Arm Short" && buttonName == "ShortButton")
            {
                isShortMode = true;
                shortButton.Content = "Armed Short";
                shortButton.Background = Brushes.Red;
            }
            else if ((button == shortButton && buttonText == "Armed Short" && buttonName == "ShortButton") ||
                     (Position.MarketPosition != MarketPosition.Flat))
            {
                isShortMode = false;
                shortButton.Content = "Arm Short";
                shortButton.Background = Brushes.Gray;
            }
            else if (button == longButton && buttonText == "Arm Long" && buttonName == "LongButton")
            {
                isLongMode = true;
                longButton.Content = "Armed Long";
                longButton.Background = Brushes.Green;
            }
            else if (button == longButton && buttonText == "Armed Long" && buttonName == "LongButton")
            {
                isLongMode = false;
                longButton.Content = "Arm Long";
                longButton.Background = Brushes.Gray;
            }
            else if (buttonText == "Trend" && buttonName == "ModeButton" && button == modeButton)
            {
                isRegressionMode = true;
                isTrendMode = false;
                modeButton.Content = "Regression";
                modeButton.Background = Brushes.Teal;
                Print("Mode changed: Regression mode activated, Trend mode deactivated");
            }
            else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
            {
                isRegressionMode = false;
                isTrendMode = true;
                modeButton.Content = "Trend";
                modeButton.Background = Brushes.Purple;
                Print("Mode changed: Trend mode activated, Regression mode deactivated");
            }
        }
    }
}
