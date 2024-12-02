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
		private Dictionary<int, double> deltaValues = new Dictionary<int, double>();
        private Dictionary<int, Dictionary<double, PriceLevelData>> priceLevelsPerBar;
        private int lastBarIndex = -1;
        private double currentPriceLevel = 0.0;
        private DateTime levelEntryTime = DateTime.MinValue;
        private double barLow = 0.0;
        private double aggregationSize = 0.25;
		private double delta;
		
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
		[Display(Name = "Importance Threshold Highside", Order = 2, GroupName = "Trade Logic")]
		public double HighThreshold
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name = "Importance Threshold Lowside", Order = 3, GroupName = "Trade Logic")]
		public double LowThreshold
		{ get; set; }
		
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
               	HighThreshold = 8.0;
				LowThreshold = -4.0;
				ATMStrategy = "NQ Hyperscalp";
				
				TickAggregation = 4;
            }
            else if (State == State.Configure)
            {
                priceLevels = new Dictionary<double, PriceLevelData>();
                priceLevelsPerBar = new Dictionary<int, Dictionary<double, PriceLevelData>>();
            }
			else if (State == State.Historical)
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

		bool tradeTaken = false;
		private double highestPriceLevel = double.MinValue;
		private double lowestPriceLevel = double.MaxValue;
		private int MaxPriceLevels = 10; // Adjust as needed
		private int currentStreak = 0;
       		protected override void OnMarketData(MarketDataEventArgs e)
		{
			
		    if (CurrentBar < 3) return;
		
		    int barIndex = CurrentBar;
		
		    // Check if a new bar has started
		    if (barIndex != lastBarIndex)
		    {
	
		        // Save data for the completed bar
		        if (priceLevels.Count > 0 && lastBarIndex >= 0)
		        {

		            priceLevelsPerBar[lastBarIndex] = new Dictionary<double, PriceLevelData>(priceLevels);
					deltaValues[lastBarIndex] = delta;
				
		        }
		
		        // Reset variables for the new bar
		        priceLevels.Clear();
		        currentPriceLevel = 0.0;
		        levelEntryTime = DateTime.MinValue;
		        lastBarIndex = barIndex;
		        levelEntryTime = e.Time;
				tradeTaken = false;
		    }
		
		    // Only process MarketDataType.Last for price and volume updates
		    if (e.MarketDataType != MarketDataType.Last) return;
		
		    double price = e.Price;
		    double volume = e.Volume;
		    double midPrice = (e.Bid + e.Ask) / 2;
		    DateTime eventTime = e.Time;
		
		    // Get the aggregated price level
		    double newPriceLevel = Math.Round(GetAggregatedPriceLevel(price), 2); // Round to 2 decimal places
		
		   if (Math.Abs(newPriceLevel - currentPriceLevel) >= TickSize)
			{
			    // Price level has changed
			    TimeSpan deltaTime = eventTime - levelEntryTime;
			    double deltaSeconds = deltaTime.TotalSeconds;
			
			    if (!priceLevels.ContainsKey(currentPriceLevel))
			    {
			        priceLevels[currentPriceLevel] = new PriceLevelData();
			    }
			    priceLevels[currentPriceLevel].TimeSpent += deltaSeconds;
			
			    // Update the global currentStreak variable
			    if (newPriceLevel > currentPriceLevel)
			    {
			        if (priceLevels[currentPriceLevel].wasLastUp == false)
			        {
			             priceLevels[currentPriceLevel].CurrentStreak = 0;
			        }
			         priceLevels[currentPriceLevel].CurrentStreak++;
			       priceLevels[currentPriceLevel].wasLastUp = true;
			    }
			    else if (newPriceLevel < currentPriceLevel)
			    {
			          if (priceLevels[currentPriceLevel].wasLastUp == true)
			        {
			            priceLevels[currentPriceLevel].CurrentStreak = 0;
			        }
			        priceLevels[currentPriceLevel].CurrentStreak--;
			        priceLevels[currentPriceLevel].wasLastUp = false;
			    }
	
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
				
				priceLevels[currentPriceLevel].LastTrade = "Long";
				if(e.Volume > 10){
					priceLevels[currentPriceLevel].LargeOrders+= e.Volume;
				}
		    }
		    else
		    {
		
		        // Trade occurred at bid side
		        priceLevels[currentPriceLevel].BidVolume += volume;
			
				priceLevels[currentPriceLevel].LastTrade = "Short";
				if(e.Volume > 10){
					priceLevels[currentPriceLevel].LargeOrders+= e.Volume;
				}
		    }
			
			
			
			double totalAsk = priceLevels.Values.Sum(pl => pl.AskVolume);
			double totalBid = priceLevels.Values.Sum(pl => pl.BidVolume);
			delta = totalAsk - totalBid;
			
			UpdateHistoricalData(priceLevels);
			
			
			CalculateImportanceScores(priceLevels);
			
		
		    // Check for trading opportunity at the current price level
		    if (priceLevels.TryGetValue(currentPriceLevel, out var currentLevelData) && !tradeTaken)
		    {

		        if (currentLevelData.CurrentStreak >= HighThreshold || currentLevelData.CurrentStreak <= LowThreshold)
		        {
				
					bool askImbalance = currentLevelData.AskVolume > currentLevelData.BidVolume ;
					bool bidImbalance = currentLevelData.BidVolume > currentLevelData.AskVolume ;
					
					 // Manage ATM strategies and orders
		            if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime || Position.MarketPosition != MarketPosition.Flat)
		                return;
		
					Print($"Trade Taken. Ask Volume: {currentLevelData.AskVolume}, Bid Volume: {currentLevelData.BidVolume}, Importance Score: {currentLevelData.ImportanceScore}, Time at level {currentLevelData.TimeSpent}, Ratio: ({Math.Max(currentLevelData.BidVolume,currentLevelData.AskVolume)} / {Math.Min(currentLevelData.BidVolume,currentLevelData.AskVolume)})");
				
			            if (isLongMode &&  currentLevelData.CurrentStreak > 0 && isTrendMode)
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
							//resetButtons();
							tradeTaken = true;
			                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
			            } 
      					if (isLongMode &&  askImbalance && isRegressionMode)
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
							//resetButtons();
							tradeTaken = true;
			                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
			            } 
			
			
						
					
			            if (isShortMode &&  currentLevelData.CurrentStreak < 0 && isTrendMode )
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
							//resetButtons();
								tradeTaken = true;
			                Print($"[{Time[0]}] Entering Short Position (Auto Arm)");
			            }
					
			            if (isShortMode && bidImbalance && isRegressionMode )
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
							//resetButtons();
								tradeTaken = true;
			                Print($"[{Time[0]}] Entering Short Position (Auto Arm)");
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
		
//		        if (atmStrategyId.Length > 0 && UseLimit)
//		        {
//		            if (GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat &&
//		                (e.Price > midRange + 20 * TickSize || e.Price < midRange - 20 * TickSize))
//		            {
//		                AtmStrategyClose(atmStrategyId);
//		            }
//		        }
		    }
		}
		
		private List<HistoricalData> historicalRatios = new List<HistoricalData>();
	private void CalculateImportanceScores(Dictionary<double, PriceLevelData> priceLevels)
		{
		    // Check for sufficient data
		    if (historicalRatios.Count < 2)
		    {
		        Print("Not enough historical data to calculate importance scores.");
		        return;
		    }
		
		    // Extract imbalance ratios
		    List<double> imbalanceRatios = historicalRatios.Select(hr => hr.ImbalanceRatio).ToList();
		
		    double meanRatio = imbalanceRatios.Average();
		    double stdDevRatio = CalculateStandardDeviation(imbalanceRatios, meanRatio);
		
		    if (stdDevRatio == 0) stdDevRatio = 0.0001;
		
			double averageStreak = priceLevels.Values.Average(pl=>Math.Abs(pl.CurrentStreak));
			double averageTime =  priceLevels.Values.Average(pl => pl.TimeSpent);
			double totalAsk =  priceLevels.Values.Sum(pl => pl.AskVolume);
			double totalBid =  priceLevels.Values.Sum(pl => pl.BidVolume);
			double totalVolumeBar = totalAsk + totalBid;
			double totalImbalance = Math.Abs(totalAsk - totalBid);
			
		    foreach (var pl in priceLevels)
		    {
		           double priceLevel = pl.Key;
		        double bidVolume = pl.Value.BidVolume;
		        double askVolume = pl.Value.AskVolume;
		        double timeSpent = pl.Value.TimeSpent;
				double largeOrders = pl.Value.LargeOrders > 1 ? pl.Value.LargeOrders : 1;
				double priceDif = pl.Value.CurrentStreak;
		        double imbalanceVolume = Math.Abs(bidVolume - askVolume);
				
		        // Calculate imbalance ratio
		        double totalVolume = bidVolume + askVolume;
		        double imbalanceRatio = 0;
		
		          if (totalVolume > 0 && averageStreak > 0 && averageTime > 0)
		         {
		          imbalanceRatio = imbalanceVolume / totalVolume + totalVolume / totalVolumeBar + timeSpent / averageTime + priceDif / averageStreak;
		        }
		        else
		        {
		            // Skip this price level as there's no volume
		            continue;
		        }
				
				double zRatio = (imbalanceRatio - meanRatio) / stdDevRatio;

		        pl.Value.ImportanceScore = zRatio;
				
		        // Debugging
		       // Print($"Price Level: {pl.Key}, ImbalanceRatio: {imbalanceRatio}, zRatio: {zRatio}, ImportanceScore: {pl.Value.ImportanceScore}, Volume Streak: {volumeStreak}, Large Orders: {largeOrders}");
		    }
		}
			
		private double CalculateStandardDeviation(List<double> values, double mean)
		{
		    double variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;
		    return Math.Sqrt(variance);
		}
				
		private void UpdateHistoricalData(Dictionary<double, PriceLevelData> priceLevels)
		{
		  	double averageStreak = priceLevels.Values.Average(pl=>Math.Abs(pl.CurrentStreak));
			double averageTime =  priceLevels.Values.Average(pl => pl.TimeSpent);
			double totalAsk =  priceLevels.Values.Sum(pl => pl.AskVolume);
			double totalBid =  priceLevels.Values.Sum(pl => pl.BidVolume);
			double totalVolumeBar = totalAsk + totalBid;
			double totalImbalance = Math.Abs(totalAsk - totalBid);
			
		    foreach (var pl in priceLevels)
		    {
		           double priceLevel = pl.Key;
		        double bidVolume = pl.Value.BidVolume;
		        double askVolume = pl.Value.AskVolume;
		        double timeSpent = pl.Value.TimeSpent;
				double largeOrders = pl.Value.LargeOrders > 1 ? pl.Value.LargeOrders : 1;
				double priceDif = pl.Value.CurrentStreak;
		        double imbalanceVolume = Math.Abs(bidVolume - askVolume);
				
		        // Calculate imbalance ratio
		        double totalVolume = bidVolume + askVolume;
		        double imbalanceRatio = 0;
		
		          if (totalVolume > 0 && averageStreak > 0 && averageTime > 0)
		         {
		          imbalanceRatio = imbalanceVolume / totalVolume + totalVolume / totalVolumeBar + timeSpent / averageTime + priceDif / averageStreak;
		        }
		        else
		        {
		            // Skip this price level as there's no volume
		            continue;
		        }
		
		        // Check if the price level exists in historicalRatios
		        int existingIndex = historicalRatios.FindIndex(hr => hr.PriceLevel == priceLevel);
		
		        if (existingIndex != -1)
		        {
		            // Update the existing entry for the price level
		            historicalRatios[existingIndex].ImbalanceRatio = imbalanceRatio;
		        }
		        else
		        {
		            // Add a new entry for this price level
		            historicalRatios.Add(new HistoricalData
		            {
		                PriceLevel = priceLevel,
		                ImbalanceRatio = imbalanceRatio
		            });
		        }
		    }
		
		    // Optionally limit the size of historical data
		    int maxHistorySize = 100; // Adjust as needed
		    if (historicalRatios.Count > maxHistorySize)
		    {
		        int removeCount = historicalRatios.Count - maxHistorySize;
		        historicalRatios.RemoveRange(0, removeCount);
		    }
		}
		
		private class HistoricalData
		{
		    public double PriceLevel { get; set; }
		    public double ImbalanceRatio { get; set; }
		}

			
		private double GetAggregatedPriceLevel(double price)
		{
		    // Ensure TickAggregation is at least 1
		    int tickAggregation = Math.Max(1, TickAggregation);
		
		    // Adjust price slightly to avoid floating-point precision issues
		    double adjustedPrice = price + TickSize * 1e-6;
		
		    // Convert price to integer ticks using Math.Floor
		    int priceInTicks = (int)Math.Floor(adjustedPrice / TickSize);
		
		    // Calculate zone index
		    int zoneIndex = priceInTicks / tickAggregation;
		
		    // Calculate the aggregated price in ticks
		    int aggregatedPriceInTicks = zoneIndex * tickAggregation;
		
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
			public double LargeOrders {get; set; }
			public double LongestStreak {get; set; }
			public double CurrentStreak {get; set; }
			public string LastTrade  {get; set; }
			public bool wasLastUp  {get; set; }
            public PriceLevelData()
            {
                BidVolume = 0;
                AskVolume = 0;
                TimeSpent = 0;
                ImportanceScore = 0;
				LargeOrders = 0;
				LongestStreak = 0;
				CurrentStreak = 0;
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
				isLongMode = false;
				shortButton.Content = "Armed Short";
				longButton.Content = "Arm Long";
				longButton.Background = Brushes.Gray;
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
					isShortMode = false;
					longButton.Content = "Armed Long";
					longButton.Background = Brushes.Green;
					shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
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
