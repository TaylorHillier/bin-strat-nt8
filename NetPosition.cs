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
        // Parameters
        [NinjaScriptProperty]
        [Range(5, int.MaxValue)]
        [Display(Name = "LookbackPeriod", Description = "Number of ticks to use for velocity calculation", Order = 1, GroupName = "Parameters")]
        public int LookbackPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "SlopeHistoryLength", Description = "Number of past slopes to store for acceleration calculation", Order = 2, GroupName = "Parameters")]
        public int SlopeHistoryLength { get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }

        private List<double> recentPrices;
        private List<double> slopeHistory;
		
			private bool isRegressionMode = false;
		private bool isTrendMode = false;
	
			//buttons/grid
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PriceVelocityAndDeltaDetection";
                Description = "Demonstration of calculating price velocity using linear regression on recent ticks and measuring acceleration.";
                Calculate = Calculate.OnPriceChange; // or OnBarClose, but we'll rely on OnMarketData
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
            }if(State == State.Historical){
				if (UserControlCollection.Contains(myGrid))
					return;
				
				Dispatcher.InvokeAsync((() =>
				{
					myGrid = new System.Windows.Controls.Grid
					{
						Name = "MyCustomGrid", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,    Margin = new Thickness(0, 0, 0, 60) // Adjust the bottom margin as needed
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
				}));
				
				
			}else if (State == State.Terminated)
			{
				Dispatcher.InvokeAsync((() =>
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
				}));
			}
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // We're only interested in last trade prices
            if (e.MarketDataType != MarketDataType.Last)
                return;

            double price = e.Price;
            
            // Add the newest price to the list
            recentPrices.Add(price);
            // Ensure we only keep the last N prices
            while (recentPrices.Count > LookbackPeriod)
                recentPrices.RemoveAt(0);

            // We need at least N prices to compute a slope
            if (recentPrices.Count < LookbackPeriod)
                return;

            // Compute slope via linear regression
            double slope = ComputeLinearRegressionSlope(recentPrices);

            // Add slope to slopeHistory
            slopeHistory.Add(slope);
            while (slopeHistory.Count > SlopeHistoryLength)
                slopeHistory.RemoveAt(0);

            // We can only compute acceleration if we have at least 2 slopes
            if (slopeHistory.Count < 2)
            {
                Print($"[DEBUG] CurrentSlope={slope:F4}, Insufficient history for acceleration.");
                return;
            }

            double prevSlope = slopeHistory[slopeHistory.Count - 2];
            double acceleration = slope - prevSlope; // change in slope

            // Interpret velocity changes:
            // Positive slope = price trending up, Negative slope = trending down
            // Positive acceleration = slope is increasing (velocity increasing)
            // Negative acceleration = slope is decreasing (velocity slowing down)
            // This is a simplistic interpretation.

            string velocityState = InterpretVelocity(slope, acceleration);

           // Print($"Time: {Time[0]} Price: {price} Slope={slope:F4} Acceleration={acceleration} State={velocityState}");
			
			if(State == State.Realtime){
			if (!isAtmStrategyCreated )
				return;
		
			
				// Check for a pending entry order
				if (orderId.Length > 0)
				{
					string[] status = GetAtmStrategyEntryOrderStatus(orderId);
				
					// If the status call can't find the order specified, the return array length will be zero otherwise it will hold elements
					if (status.GetLength(0) > 0)
					{
					
						// If the order state is terminal, reset the order id value
						if (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected")
							orderId = string.Empty;
					}
				} // If the strategy has terminated reset the strategy id
				else if (atmStrategyId.Length > 0 && atmStrategyId != string.Empty && GetAtmStrategyMarketPosition(atmStrategyId)  == Cbi.MarketPosition.Flat) 
					atmStrategyId = string.Empty;
			}
        }

        private double ComputeLinearRegressionSlope(List<double> prices)
        {
            int n = prices.Count;
            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumX2 = 0;

            // x will be just the index: 0,1,... n-1
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

            // slope = (n*sumXY - sumX*sumY) / (n*sumX2 - sumX^2)
            double slope = (n * sumXY - sumX * sumY) / denom;
            return slope;
        }

		bool upArrowDrawn = false;
		bool downArrowDrawn =  false;
		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
        private string InterpretVelocity(double slope, double acceleration)
        {
            // Very rough interpretation for demonstration:
            // If slope > 0 and acceleration > 0: price velocity increasing upward
            // If slope > 0 and acceleration < 0: price still up but velocity slowing
            // If slope < 0 and acceleration < 0: price velocity increasing downward
            // If slope < 0 and acceleration > 0: price down but decelerating downward velocity

            if (slope > 0)
            {
                if (acceleration > 0 && isTrendMode){
					if(!upArrowDrawn){
					Draw.ArrowUp(this,$"buy{CurrentBar}",true,0,Close[0],Brushes.Green);
					upArrowDrawn = true;
						downArrowDrawn = false;
						
						
		
					}
					
					 if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
		        		return "In Trade";
					
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
					
				
                    return "Up Velocity Increasing";
				}
               	else if(acceleration < 0 && isRegressionMode){
					if(!downArrowDrawn){
					Draw.ArrowDown(this,$"sell{CurrentBar}",true,0, Close[0],Brushes.Red);
					downArrowDrawn = true;
						upArrowDrawn = false;
						
						
					}
					
					 if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
		        		return "In Trade";
					 
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
                    return "Up Velocity Slowing";
				} else {
					return "NA";
				}
            }
            else if (slope < 0)
            {
                if (acceleration < 0 && isTrendMode){
					if(!downArrowDrawn){
					Draw.ArrowDown(this,$"sell{CurrentBar}",true,0, Close[0],Brushes.Red);
					downArrowDrawn = true;
						upArrowDrawn = false;
	
					}
					
										
						 if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
		        		return "In Trade";
					 
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
					
                    return "Down Velocity Increasing";
				}
                else if(acceleration > 0 && isRegressionMode){
					
						if(!upArrowDrawn){
					Draw.ArrowUp(this,$"buy{CurrentBar}",true,0,Close[0],Brushes.Green);
					upArrowDrawn = true;
						downArrowDrawn = false;
					}
						
						 if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
		        		return "In Trade";
					
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
					//Draw.ArrowUp(this,$"buy{CurrentBar}",true,0, Low[0],Brushes.Red);
                    return "Down Velocity Slowing";
				} else {
					return "NA";
				}
            }
            else
            {
                // slope ~ 0 means price is flat
                if (acceleration > 0)
                    return "Flat but acceleration up (potential turn up)";
                else if (acceleration < 0)
                    return "Flat but acceleration down (potential turn down)";
                else
                    return "Flat/No Change";
            }
        }
		
		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
		    // Handle the button click event here
		    // You can implement the logic to switch between long-only, short-only, or ranged mode
		    // For example:
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
		
			string buttonText = button.Content.ToString();
   			 string buttonName = button.Name;
			
			if (buttonText == "Trend" && buttonName == "ModeButton" && button == modeButton)
		    {
		     
				isRegressionMode = true;
				isTrendMode = false;
				modeButton.Content = "Regression";
				modeButton.Background = Brushes.Teal;
				Print("regression - " + isRegressionMode);
   				 Print($"Mode changed: Regression mode activated, Trend mode deactivated");
				
		    }
		    else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
		    {
		   		isRegressionMode = false;
				isTrendMode = true;
				modeButton.Content = "Trend";
				modeButton.Background = Brushes.Purple;
    			Print($"Mode changed: Trend mode activated, Regression mode deactivated");

		    }
		
		    // Update the button content or perform any other necessary actions
			
		    
		}
    }
}
