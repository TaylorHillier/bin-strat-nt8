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
    public class PriceVelocityAndDeltaDetection : Strategy
    {
        [NinjaScriptProperty]
        [Range(5, int.MaxValue)]
        [Display(Name = "LookbackPeriod", Description = "Number of ticks to use for velocity calculation", Order = 1, GroupName = "Parameters")]
        public int LookbackPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "SlopeHistoryLength", Description = "Number of past slopes to store for acceleration calculation", Order = 2, GroupName = "Parameters")]
        public int SlopeHistoryLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name="ATMStrategy", Order=3, GroupName="Parameters")]
        public string ATMStrategy { get; set; }

        private List<double> recentPrices;
        private List<double> slopeHistory;

        private bool isRegressionMode = false;
        private bool isTrendMode = false;

        // GUI elements
        private System.Windows.Controls.Button modeButton;
        private System.Windows.Controls.Grid myGrid;

        // Trade tracking
        public string atmStrategyId = string.Empty;
        public string orderId = string.Empty;
        public bool isAtmStrategyCreated = false;

        // State tracking for arrows
        bool upArrowDrawn = false;
        bool downArrowDrawn = false;

        // Concavity tracking
        double previousA = 0; // track previous 'a'
        bool firstRun = true;

        // Flags for waiting after concavity change
        bool concavityChangePending = false;
        bool waitingForPositiveThreshold = false;
        bool waitingForNegativeThreshold = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PriceVelocityAndDeltaDetection";
                Description = "Example: price velocity (slope), acceleration, and quadratic concavity detection.";
                Calculate = Calculate.OnPriceChange;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
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
                ATMStrategy = "NQ Hyperscalp";

                LookbackPeriod = 20;
                SlopeHistoryLength = 10;
                isRegressionMode = false;
                isTrendMode = true;
            }
            else if (State == State.DataLoaded)
            {
                recentPrices = new List<double>();
                slopeHistory = new List<double>();
            }
            else if (State == State.Historical)
            {
                if (UserControlCollection.Contains(myGrid))
                    return;

                Dispatcher.InvokeAsync(() =>
                {
                    myGrid = new System.Windows.Controls.Grid
                    {
                        Name = "MyCustomGrid",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 60)
                    };

                    System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition();
                    myGrid.ColumnDefinitions.Add(column1);

                    modeButton = new System.Windows.Controls.Button
                    {
                        Name = "ModeButton",
                        Foreground = Brushes.White,
                        Background = isRegressionMode ? Brushes.Teal : Brushes.Purple,
                        Content = isRegressionMode ? "Regression" : "Trend",
                    };

                    modeButton.Click += OnButtonClick;

                    System.Windows.Controls.Grid.SetColumn(modeButton, 0);

                    myGrid.Children.Add(modeButton);

                    UserControlCollection.Add(myGrid);
                });
            }
            else if (State == State.Terminated)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (myGrid != null)
                    {
                        if (modeButton != null)
                        {
                            myGrid.Children.Remove(modeButton);
                            modeButton.Click -= OnButtonClick;
                            modeButton = null;
                        }
                    }
                });
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last)
                return;

            double price = e.Price;

            recentPrices.Add(price);
            while (recentPrices.Count > LookbackPeriod)
                recentPrices.RemoveAt(0);

            if (recentPrices.Count < LookbackPeriod)
                return;

            double slope = ComputeLinearRegressionSlope(recentPrices);
            slopeHistory.Add(slope);
            while (slopeHistory.Count > SlopeHistoryLength)
                slopeHistory.RemoveAt(0);

            if (slopeHistory.Count < 2)
                return;

            double prevSlope = slopeHistory[slopeHistory.Count - 2];
            double acceleration = slope - prevSlope;

            double a = 0, b = 0, c = 0;
            bool canFit = slopeHistory.Count >= 3;
            if (canFit)
                FitQuadratic(slopeHistory, out a, out b, out c);

            if (!firstRun && canFit)
            {
                double currentA = a;
                bool wasConcaveDown = previousA < 0;
                bool nowConcaveUp = currentA > 0;

                bool wasConcaveUp = previousA > 0;
                bool nowConcaveDown = currentA < 0;

                Print($"Time: {Time[0]} Price: {price} slope={slope:F4}, accel={acceleration:F4}, a={currentA:F10}, prevA={previousA:F10}");

                // Check for concavity changes
                if (wasConcaveDown && nowConcaveUp)
                {
                    // Sign changed from down to up
                    // No immediate trade, we wait for threshold
                    concavityChangePending = true;
                    waitingForPositiveThreshold = true;
                    waitingForNegativeThreshold = false;
                    Print("Concavity changed from down to up. Waiting for a > 1e-8 to place a long.");
                }
                else if (wasConcaveUp && nowConcaveDown)
                {
                    // Sign changed from up to down
                    concavityChangePending = true;
                    waitingForNegativeThreshold = true;
                    waitingForPositiveThreshold = false;
                    Print("Concavity changed from up to down. Waiting for a < -1e-8 to place a short.");
                }

                // If waiting for positive threshold (concavity up) and we've crossed it
                if (concavityChangePending && waitingForPositiveThreshold && currentA > 1e-8 && acceleration > 0)
                {
                    Print("Threshold reached for up concavity. Placing long trade.");
                    Draw.ArrowUp(this, $"buy{CurrentBar}", true, 0, Close[0], Brushes.Green);
                    PlaceATMOrder(OrderAction.Buy);

                    // Reset flags
                    concavityChangePending = false;
                    waitingForPositiveThreshold = false;
                }

                // If waiting for negative threshold (concavity down) and we've crossed it
                if (concavityChangePending && waitingForNegativeThreshold && currentA < -1e-8 && acceleration < 0)
                {
                    Print("Threshold reached for down concavity. Placing short trade.");
                    Draw.ArrowDown(this, $"sell{CurrentBar}", true, 0, Close[0], Brushes.Red);
                    PlaceATMOrder(OrderAction.Sell);

                    // Reset flags
                    concavityChangePending = false;
                    waitingForNegativeThreshold = false;
                }

                previousA = currentA;
                firstRun = false;
            }
            else if (canFit && firstRun)
            {
                // On the very first run after we can fit, just set prevA
                previousA = a;
                firstRun = false;
            }

            // ATM strategy state checks
            if (State == State.Realtime)
            {
                if (!isAtmStrategyCreated)
                    return;

                if (orderId.Length > 0)
                {
                    string[] status = GetAtmStrategyEntryOrderStatus(orderId);
                    if (status.GetLength(0) > 0)
                    {
                        if (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected")
                            orderId = string.Empty;
                    }
                }
                else if (atmStrategyId.Length > 0 && atmStrategyId != string.Empty && GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat)
                    atmStrategyId = string.Empty;
            }
        }

        private void PlaceATMOrder(OrderAction action)
        {
            if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
            {
                Print("Not placing trade due to invalid state or existing order.");
                return;
            }

            Print($"Placing ATM order: {action}");
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
                        Print("ATM Strategy Created Successfully.");
                        isAtmStrategyCreated = true;
                    }
                    else
                    {
                        Print($"ATM Strategy Creation Failed: {atmCallbackErrorCode}");
                    }
                });
        }

        private double ComputeLinearRegressionSlope(List<double> prices)
        {
            int n = prices.Count;
            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumX2 = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = prices[i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumX2 += x * x;
            }

            double denom = (n * sumX2 - sumX * sumX);
            if (denom == 0)
                return 0;

            double slope = (n * sumXY - sumX * sumY) / denom;
            return slope;
        }

        private void FitQuadratic(List<double> data, out double a, out double b, out double c)
        {
            int n = data.Count;
            double sumY = 0;
            double sumX = 0;
            double sumX2 = 0;
            double sumX3 = 0;
            double sumX4 = 0;
            double sumXY = 0;
            double sumX2Y = 0;

            for (int i = 0; i < n; i++)
            {
                double x = i;
                double y = data[i];
                sumY += y;
                sumX += x;
                sumX2 += x * x;
                sumX3 += x * x * x;
                sumX4 += x * x * x * x;
                sumXY += x * y;
                sumX2Y += x * x * y;
            }

            double[,] A = {
                { n,    sumX,   sumX2  },
                { sumX, sumX2,  sumX3  },
                { sumX2,sumX3,  sumX4  }
            };
            double[] R = { sumY, sumXY, sumX2Y };

            double[] solution = Solve3x3(A, R);
            c = solution[0];
            b = solution[1];
            a = solution[2];
        }

        private double[] Solve3x3(double[,] A, double[] R)
        {
            double d = Det3x3(A);
            if (Math.Abs(d) < 1e-14)
                return new double[] {0,0,0};

            double[,] A_c = (double[,])A.Clone();
            A_c[0, 0] = R[0]; A_c[1, 0] = R[1]; A_c[2, 0] = R[2];
            double dx = Det3x3(A_c);

            A_c = (double[,])A.Clone();
            A_c[0, 1] = R[0]; A_c[1, 1] = R[1]; A_c[2, 1] = R[2];
            double dy = Det3x3(A_c);

            A_c = (double[,])A.Clone();
            A_c[0, 2] = R[0]; A_c[1, 2] = R[1]; A_c[2, 2] = R[2];
            double dz = Det3x3(A_c);

            double c = dx / d;
            double b = dy / d;
            double a = dz / d;
            return new double[]{c,b,a};
        }

        private double Det3x3(double[,] M)
        {
            return M[0,0]*(M[1,1]*M[2,2]-M[1,2]*M[2,1]) -
                   M[0,1]*(M[1,0]*M[2,2]-M[1,2]*M[2,0]) +
                   M[0,2]*(M[1,0]*M[2,1]-M[1,1]*M[2,0]);
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
            string buttonText = button.Content.ToString();
            string buttonName = button.Name;

            if (buttonText == "Trend" && buttonName == "ModeButton" && button == modeButton)
            {
                isRegressionMode = true;
                isTrendMode = false;
                modeButton.Content = "Regression";
                modeButton.Background = Brushes.Teal;
                Print("Regression mode activated");
            }
            else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
            {
                isRegressionMode = false;
                isTrendMode = true;
                modeButton.Content = "Trend";
                modeButton.Background = Brushes.Purple;
                Print("Trend mode activated");
            }
        }
    }
}
