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
    public class MarketWaveStrategy : Strategy
    {
        #region Variables
        private Dictionary<double, PriceLevelData> priceLevels;
        private Dictionary<int, Dictionary<double, PriceLevelData>> priceLevelsPerBar;
        private int lastBarIndex = -1;
        private double currentPriceLevel = 0.0;
        private DateTime levelEntryTime = DateTime.MinValue;
        private double barLow = 0.0;
        private double aggregationSize = 0.25;
		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;

		private bool isRegressionMode = false;
		private bool isTrendMode = false;
		
		private bool isLongMode = false;
		private bool isShortMode = false;
		private bool isAutoArm = false;
		
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button armButton;
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Tick Aggregation", Order = 1, GroupName = "Trade Logic")]
        public int TickAggregation { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Importance Threshold", Order = 2, GroupName = "Trade Logic")]
        public double ImportanceThreshold { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="ATM Strategy")]
		public string ATMStrategy
		{ get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "A strategy using importance scores to trade based on market data.";
                Name = "MarketWaveStrategy";
                Calculate = Calculate.OnEachTick;
                TickAggregation = 4;
                ImportanceThreshold = 1.0;
                IsOverlay = false;
				ATMStrategy = "NQ Hyperscalp";
				ImportanceThreshold = 50;
				TickAggregation = 4;
            }
            else if (State == State.Configure)
            {
                priceLevels = new Dictionary<double, PriceLevelData>();
                priceLevelsPerBar = new Dictionary<int, Dictionary<double, PriceLevelData>>();
            }else if (State == State.Historical)
			{
			if (UserControlCollection.Contains(myGrid))
					return;
				
				Dispatcher.InvokeAsync((() =>
				{
					myGrid = new System.Windows.Controls.Grid
					{
						Name = "MyCustomGrid", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,    Margin = new Thickness(0, 0, 0, 60) // Adjust the bottom margin as needed
					};
					
					System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column4 = new System.Windows.Controls.ColumnDefinition();
					
					myGrid.ColumnDefinitions.Add(column1);
					myGrid.ColumnDefinitions.Add(column2);
					myGrid.ColumnDefinitions.Add(column3);
					myGrid.ColumnDefinitions.Add(column4);
					
					
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
					
					armButton = new System.Windows.Controls.Button
					{
					    Name = "ArmButton",
					    Content = isAutoArm ? "Auto Arm On" : "Auto Arm Off",
					    Foreground = Brushes.White,
					    Background = Brushes.Blue,
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
					armButton.Click += OnButtonClick;
					modeButton.Click += OnButtonClick;
					
					System.Windows.Controls.Grid.SetColumn(longButton, 1);
					System.Windows.Controls.Grid.SetColumn(shortButton, 2);
					System.Windows.Controls.Grid.SetColumn(armButton, 3);
					System.Windows.Controls.Grid.SetColumn(modeButton, 0);
					
					myGrid.Children.Add(longButton);
					myGrid.Children.Add(shortButton);
					myGrid.Children.Add(armButton);
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
						if (armButton != null)
						{
							myGrid.Children.Remove(armButton);
							armButton.Click -= OnButtonClick;
							armButton = null;
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

       	protected override void OnMarketData(MarketDataEventArgs e)
		{
		    if (CurrentBar < 2) return;
		
		    int barIndex = CurrentBar;
		
		    // Check if a new bar has started
		    if (barIndex != lastBarIndex)
		    {
		        // Handle remaining time at the last price level
		        if (currentPriceLevel != 0.0 && levelEntryTime != DateTime.MinValue)
		        {
		            // Use the time of the last market data event
		            DateTime lastEventTime = e.Time;
		
		            // Calculate time spent at the last price level
		            TimeSpan deltaTime = lastEventTime - levelEntryTime;
		            double deltaSeconds = deltaTime.TotalSeconds;
		
		            if (!priceLevels.ContainsKey(currentPriceLevel))
		            {
		                priceLevels[currentPriceLevel] = new PriceLevelData();
		            }
		            priceLevels[currentPriceLevel].TimeSpent += deltaSeconds;
		        }
		
		        // Save data for the completed bar
		        if (priceLevels.Count > 0 && lastBarIndex >= 0)
		        {
		            priceLevelsPerBar[lastBarIndex] = new Dictionary<double, PriceLevelData>(priceLevels);
		        }
		
		        // Reset variables for the new bar
		        priceLevels.Clear();
		        currentPriceLevel = 0.0;
		        levelEntryTime = DateTime.MinValue;
		        lastBarIndex = barIndex;
		        levelEntryTime = e.Time;
		    }
		
		    // Only process MarketDataType.Last for price and volume updates
		    if (e.MarketDataType != MarketDataType.Last) return;
		
		    double price = e.Price;
		    double volume = e.Volume;
		    double midPrice = (e.Bid + e.Ask) / 2;
		    DateTime eventTime = e.Time;
		
		    // Get the aggregated price level
		    double newPriceLevel = GetAggregatedPriceLevel(price);
		
		    if (currentPriceLevel == 0.0)
		    {
		        // First time setting currentPriceLevel
		        currentPriceLevel = newPriceLevel;
		        levelEntryTime = eventTime;
		    }
		    else if (newPriceLevel != currentPriceLevel)
		    {
		        // Price level has changed
		        // Calculate time spent at the previous level
		        TimeSpan deltaTime = eventTime - levelEntryTime;
		        double deltaSeconds = deltaTime.TotalSeconds;
		
		        if (!priceLevels.ContainsKey(currentPriceLevel))
		        {
		            priceLevels[currentPriceLevel] = new PriceLevelData();
		        }
		        priceLevels[currentPriceLevel].TimeSpent += deltaSeconds;
		
		        // Update currentPriceLevel and levelEntryTime
		        currentPriceLevel = newPriceLevel;
		        levelEntryTime = eventTime;
		    }
		
		    // Update volume at the current price level
		    if (!priceLevels.ContainsKey(currentPriceLevel))
		    {
		        priceLevels[currentPriceLevel] = new PriceLevelData();
		    }
		
		    if (price >= midPrice)
		    {
		        // Trade occurred at ask side
		        priceLevels[currentPriceLevel].AskVolume += volume;
		    }
		    else
		    {
		        // Trade occurred at bid side
		        priceLevels[currentPriceLevel].BidVolume += volume;
		    }
			
			CalculateImportanceScores(priceLevels);
		
		    // Check for trading opportunity at the current price level
		    if (priceLevels.TryGetValue(currentPriceLevel, out var currentLevelData))
		    {

		        if (currentLevelData.ImportanceScore >= ImportanceThreshold)
		        {
					
					 // Manage ATM strategies and orders
		            if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime || Position.MarketPosition != MarketPosition.Flat)
		                return;
		
					if (currentLevelData.AskVolume > currentLevelData.BidVolume)
		            {
			            if (isTrendMode && isLongMode)
			            {
			               
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
			                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
			            } 
						else if (isRegressionMode && isLongMode)
			            {
			               
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
			                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
			            }
					}
						
						
					 if (currentLevelData.BidVolume > currentLevelData.AskVolume)
		             {
			            if (isTrendMode && isShortMode)
			            {
			               
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
			                Print($"[{Time[0]}] Entering Short Position (Auto Arm)");
			            }
					
					
		
			            if (isRegressionMode && isShortMode)
			            {
			               
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
			                Print($"[{Time[0]}] Entering Short Position (Auto Arm)");
			            }
					 }
		        }
		    }
		}
		

       private void CalculateImportanceScores(Dictionary<double, PriceLevelData> priceLevels)
		{
		    double maxTotalVolume = priceLevels.Values.Max(pl => pl.BidVolume + pl.AskVolume);
		    double totalTimeSpent = priceLevels.Values.Sum(pl => pl.TimeSpent);
		
		    foreach (var pl in priceLevels)
		    {
		        double totalVolume = pl.Value.BidVolume + pl.Value.AskVolume;
		
		        // Avoid division by zero
		        if (totalVolume == 0 || totalTimeSpent == 0 || maxTotalVolume == 0)
		        {
		            pl.Value.ImportanceScore = 0;
		            continue;
		        }
		
		        // Calculate Imbalance Ratio
		        double imbalance = Math.Abs(pl.Value.AskVolume - pl.Value.BidVolume);
		        double imbalanceRatio = imbalance / totalVolume;
		
		        // Normalize total volume
		        double normalizedVolume = totalVolume / maxTotalVolume;
		
		        // Normalize time spent and apply reverse bell curve
		        double normalizedTime = pl.Value.TimeSpent / totalTimeSpent;
		        double timeImpact = (1 - Math.Pow(1 - normalizedTime, 2)) * (1 - Math.Pow(normalizedTime, 2));
		
		        // Calculate level importance score
		        pl.Value.ImportanceScore = normalizedVolume * imbalanceRatio * timeImpact * 100; // Scale by 100 for readability
		        Print($"Level {pl.Key}: Score {pl.Value.ImportanceScore}");
		    }
		}

        private double GetAggregatedPriceLevel(double price)
        {
            // Ensure TickAggregation is at least 1
            int tickAggregation = Math.Max(1, TickAggregation);

            // Convert price to integer ticks
            int priceInTicks = (int)Math.Round(price / TickSize);

            // Calculate the aggregated price in ticks
            int aggregatedPriceInTicks = (priceInTicks / tickAggregation) * tickAggregation;

            // Convert back to price
            double aggregatedPrice = aggregatedPriceInTicks * TickSize;

            return aggregatedPrice;
        }

        #region PriceLevelData Class
        private class PriceLevelData
        {
            public double BidVolume { get; set; }
            public double AskVolume { get; set; }
            public double TimeSpent { get; set; }
            public double ImportanceScore { get; set; }

            public PriceLevelData()
            {
                BidVolume = 0;
                AskVolume = 0;
                TimeSpent = 0;
                ImportanceScore = 0;
            }
        }
        #endregion
		
		#region Button Controls
		private void resetButtons()
		{
		    Dispatcher.Invoke(() =>
	        {
	            isLongMode = false;
	            isShortMode = false;
				isAutoArm = false;
	            shortButton.Content = "Arm Short";
	            longButton.Content = "Arm Long";
				armButton.Content = "Auto Arm Off";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
			});
		}
		
		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
		    // Handle the button click event here
		    // You can implement the logic to switch between long-only, short-only, or ranged mode
		    // For example:
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
		
			string buttonText = button.Content.ToString();
   			 string buttonName = button.Name;
			
		 if (button == shortButton && buttonText == "Arm Short" && buttonName == "ShortButton")
		    {
					// Switch to short-only mode
			    isShortMode =  true;
				shortButton.Content = "Armed Short";
				shortButton.Background = Brushes.Red;
					
		    }
			 if (button == shortButton && buttonText == "Armed Short" && buttonName == "ShortButton" || (Position.MarketPosition != MarketPosition.Flat))
		    {
					// Switch to short-only mode
			    isShortMode =  false;
				shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
					
		    }
		   if (button == armButton && buttonName == "ArmButton" && buttonText == "Auto Arm On")
		    {
		        // Switch to ranged mode
		        isAutoArm = false;
				longButton.Content = "Arm Long";
				shortButton.Content = "Arm Short";
				 isLongMode = false;
				 isShortMode = false;
				armButton.Content = "Auto Arm Off";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
			
				    Print($"Auto Arm deactivated: Long mode = {isLongMode}, Short mode = {isShortMode}, Auto Arm = {isAutoArm}");

		    }
			  if (button == armButton && buttonName == "ArmButton" && buttonText == "Auto Arm Off")
		    {
		        // Switch to ranged mode
		         isAutoArm = true;
				longButton.Content = "Armed Long";
				shortButton.Content = "Armed Short";
				 isLongMode = true;
				  isShortMode = true;
				armButton.Content = "Auto Arm On";
				shortButton.Background = Brushes.Red;
				longButton.Background = Brushes.Green;
				    Print($"Auto Arm activated: Long mode = {isLongMode}, Short mode = {isShortMode}, Auto Arm = {isAutoArm}");

			
		    }
		    if (button == longButton && buttonText == "Arm Long" && buttonName == "LongButton")
		    {
					// Switch to short-only mode
			        isLongMode = true;
					longButton.Content = "Armed Long";
					longButton.Background = Brushes.Green;
		    }
			 if (button == longButton && buttonText == "Armed Long" && buttonName == "LongButton")
		    {
					// Switch to short-only mode
			        isLongMode = false;
					longButton.Content = "Arm Long";
					longButton.Background = Brushes.Gray;
					
		    }
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
		    
		}
		#endregion
    }
}
