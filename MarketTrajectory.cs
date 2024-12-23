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

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class MarketTrajectory : Strategy
    {
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Entry Conditions")]
		public string ATMStrategy
		{ get; set; }
		
        // Define trajectory lengths
        private readonly int[] trajectoryLengths = { 2, 5, 10, 20, 50, 100, 200, 500, 1000, 1500, 2000 };

        // TrajectoryLine class
        private class TrajectoryLine
        {
            public int StartBar { get; set; }
            public int EndBar { get; set; }
            public double Price { get; set; }
            public DateTime Time { get; set; }
			public double Slope  { get; set; }
            public TrajectoryLine(int startBar, int endBar, double price, DateTime time, double slope)
            {
                StartBar = startBar;
                EndBar = endBar;
                Price = price;
                Time = time;
				Slope = slope;
            }
        }

        // Dictionary to hold one trajectory line per length
        private Dictionary<int, TrajectoryLine> trajectoryLines;

        // Colors for different trajectory lengths (optional enhancement)
        private readonly Dictionary<int, Brush> trajectoryColors = new Dictionary<int, Brush>
        {
            {2, Brushes.Blue},
            {5, Brushes.Green},
            {10, Brushes.Orange},
            {20, Brushes.Purple},
            {50, Brushes.Teal},
            {100, Brushes.Brown},
            {200, Brushes.Magenta},
            {500, Brushes.Cyan},
            {1000, Brushes.Yellow},
			{1500, Brushes.Red},
            {2000, Brushes.Black}
        };

        // Method to get color based on trajectory length
        private Brush GetColorForLength(int length)
        {
            return trajectoryColors.ContainsKey(length) ? trajectoryColors[length] : Brushes.Red;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "MarketTrajectory";
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
                BarsRequiredToTrade = 2000; // Set to the maximum trajectory length
                // Disable this property for performance gains in Strategy Analyzer optimizations
                // See the Help Guide for additional information
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.DataLoaded)
            {
            
            }
            else if (State == State.Terminated)
            {
                // Clean up: Remove all trajectory lines from the chart
                foreach (var length in trajectoryLengths)
                {
                    string tag = $"Trajectory_{length}";
                    RemoveDrawObject(tag);
                }
            }
        }
		
		bool below = false;
		bool above = false;
		
        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Ensure historical or real-time state
            if (State != State.Historical && State != State.Realtime)
                return;

			    // Initialize the trajectoryLines dictionary
                trajectoryLines = new Dictionary<int, TrajectoryLine>();

			if(CurrentBar > 2500){
                // Initialize Draw.Line objects for each length with default positions
                foreach (var length in trajectoryLengths)
                {
                    string tag = $"Trajectory_{length}";

                    // Initialize with the Close price at 'length' bars ago
                    double initialPrice = Close[length];

                    // Draw the initial line
                    Draw.Line(this, tag, false, length, initialPrice, 0, Close[0], 
                              GetColorForLength(length), 
                              DashStyleHelper.Solid, 1);
                }
			}
			
			double totalSlope = 0;
				
            foreach(var length in trajectoryLengths)
            {
                if(CurrentBar >= length)
                {
                    int startBar = CurrentBar - length;
                    int endBar = CurrentBar;
                    double price = Close[length];
                    DateTime time = Time[length];
					double slope = (Close[0] - price) / (endBar - startBar)/*/ ((double)ToTime(Time[0].ToUniversalTime()) - (double)ToTime(time.ToUniversalTime()))*/;
                    // Update or create the TrajectoryLine for this length
                    if(trajectoryLines.ContainsKey(length))
                    {
                        // Update existing TrajectoryLine
                        TrajectoryLine trajLine = trajectoryLines[length];
                        trajLine.StartBar = startBar;
                        trajLine.EndBar = endBar;
                        trajLine.Price = price;
                        trajLine.Time = time;
						trajLine.Slope = slope;
                    }
                    else
                    {
                        // Create new TrajectoryLine
                        TrajectoryLine trajLine = new TrajectoryLine(startBar, endBar, price, time, slope);
                        trajectoryLines.Add(length, trajLine);
                    }

                    // Draw or update the line on the chart
                    string tag = $"Trajectory_{length}";

                    // Ensure the trajectoryColors dictionary has a color for this length
                    Brush lineColor = GetColorForLength(length);

                    // Update the line position using Draw.Line
                    // Y1 and Y2 are the same since it's a horizontal line at the specified price
                    Draw.Line(this, tag, false, length, price, 0, Close[0], 
                              lineColor, DashStyleHelper.Solid, 1);

					totalSlope += slope;
                    // Optional: Print debug information
                    Print($"[TRAJECTORY] Length: {length}, StartBar: {startBar}, EndBar: {endBar}, Price: {price}, Time: {time}, Slope: {slope}");
                }
                else
                {
                    // Optional: Log insufficient bars for this length
                    Print($"[TRAJECTORY] Length: {length} - Not enough bars. CurrentBar: {CurrentBar}");
                }
            }
			
			Print(totalSlope);
			
			if (orderId.Length == 0 && atmStrategyId.Length == 0){
		               
				if(totalSlope > 20){
					 
					if(State == State.Realtime){
						isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Buy,
		                    OrderType.Market, 0, 0, TimeInForce.Gtc,
		                    orderId, ATMStrategy, atmStrategyId,
		                    (atmCallbackErrorCode, atmCallBackId) =>
		                    {
		                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                        {
		                            isAtmStrategyCreated = true;
		                        }
		                    });
						
						above = true;
						below = false;
					}
					
					if(State == State.Historical){
						
						if(Position.MarketPosition == MarketPosition.Short){
							ExitShort();
						}
						EnterLong();
					}
				} else if (totalSlope < -20){
				 
					if(State == State.Realtime){
						isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Sell,
		                    OrderType.Market, 0, 0, TimeInForce.Gtc,
		                    orderId, ATMStrategy, atmStrategyId,
		                    (atmCallbackErrorCode, atmCallBackId) =>
		                    {
		                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                        {
		                            isAtmStrategyCreated = true;
		                        }
		                    });
						
						below = true;
						above = false;
					}
					
					if(State == State.Historical){
						
						if(Position.MarketPosition == MarketPosition.Long){
							ExitLong();
						}
						EnterShort();
					}
				}
			}
			
			  // Manage ATM Strategies and Orders
		    if (State == State.Realtime)
		    {
		        if (!isAtmStrategyCreated)
		            return;
		
		        // Check for a pending entry order
		        if (orderId.Length > 0)
		        {
		            string[] status = GetAtmStrategyEntryOrderStatus(orderId);
		
		            // If the order state is terminal, reset the order id value
		            if (status.GetLength(0) > 0 && (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected"))
		                orderId = string.Empty;
		        }
		        // If the strategy has terminated, reset the strategy id
		        else if (atmStrategyId.Length > 0 && atmStrategyId != string.Empty && GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat)
		            atmStrategyId = string.Empty;
				
				if (atmStrategyId.Length > 0)
				{
					
					if (GetAtmStrategyMarketPosition(atmStrategyId) != MarketPosition.Flat){
						if(above && totalSlope < -7){
							 AtmStrategyClose(atmStrategyId);
						} 
						
						if( below && totalSlope > 7){
							 AtmStrategyClose(atmStrategyId);
						}
					}
				}
		
		    }


            // No need to limit trajectoryLines since we're maintaining only one per length
        }

        // Optional: Override OnRender for custom rendering (not necessary with Draw.Line)
        /*
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (trajectoryLines == null || trajectoryLines.Count == 0)
                return; // Nothing to draw

            foreach (var traj in trajectoryLines.Values)
            {
                // Calculate the X (horizontal) positions based on bar index
                // Convert bar index to pixel position
                int barsAgoStart = CurrentBar - traj.StartBar;
                int barsAgoEnd = CurrentBar - traj.EndBar;

                // Ensure barsAgoStart and barsAgoEnd are within chart bounds
                if (barsAgoStart < 0 || barsAgoEnd < 0)
                    continue;

                // Get the screen coordinates for start and end bars
                double x1 = chartControl.GetXByBarIdx(ChartBars, traj.StartBar);
                double x2 = chartControl.GetXByBarIdx(ChartBars, traj.EndBar);

                // Get the Y (vertical) positions based on price
                double y1 = chartScale.GetYByValue(traj.Price);
                double y2 = chartScale.GetYByValue(traj.Price);

                // Define the line properties
                System.Windows.Media.Pen pen = new System.Windows.Media.Pen(GetColorForLength(traj.EndBar), 1);
                pen.Freeze(); // Improve performance by freezing the pen

                // Draw the line
                chartControl.ChartPanel.RenderTransform = new System.Windows.Media.TranslateTransform(0, 0); // Ensure no transform
                chartControl.ChartPanel.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = pen.Brush,
                    StrokeThickness = pen.Thickness
                });
            }
        }
        */
    }
}
