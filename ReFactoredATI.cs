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
	
	public class NewHttpClientWrapper
	{
		private static readonly HttpClient client = new HttpClient();
		private const string BaseUrl = "http://192.168.1.116:5000"; // Your server address
		private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

		public static Dictionary<string, object> Get(string endpoint)
		{
			HttpResponseMessage response = client.GetAsync($"{BaseUrl}/{endpoint}").Result;
			response.EnsureSuccessStatusCode();
			string responseBody = response.Content.ReadAsStringAsync().Result;
			return serializer.Deserialize<Dictionary<string, object>>(responseBody);
		}

		public static Dictionary<string, object> Post(string endpoint, object data)
		{
			string json = serializer.Serialize(data);
			HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
			HttpResponseMessage response = client.PostAsync($"{BaseUrl}/{endpoint}", content).Result;
			response.EnsureSuccessStatusCode();
			string responseBody = response.Content.ReadAsStringAsync().Result;
			return serializer.Deserialize<Dictionary<string, object>>(responseBody);
		}
	}
		
	public class NewSimTrade
	{
		public double ImbVol { get; set; }
		public double AdvDetection { get; set; }
		public double Ratio  { get; set; }
		public double Streak { get; set; }
		public double EntryPrice { get; set; }
		public string Direction { get; set; }
		public string Status { get; set; }
		public double WinRate { get; set; }
		public string ImbalanceType { get; set; }
		public int WindowId { get; set; }

		public double VolumeSpeed { get; set; }
		public double PriceSpeed { get; set; }
		public double BolDif { get; set; }
		public double MADif { get; set; }
		public double TOD { get; set; }
		public double StdDev { get; set; }
		public string TradingMode { get; set; }
		
		public int WinCount { get; set; } = 0;
		public int LossCount { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;

		public double PF { get; set; }
		
		public NewSimTrade(double imbVol, double advDetection, double ratio, double entryPrice, string direction, double volumeSpeed, double maDif, double bolDif, double stdDeviation, double tod, string tradingMode )
		{
		    ImbVol = imbVol;
		    AdvDetection = advDetection;
			Ratio = ratio;
		    EntryPrice = entryPrice;
			Direction = direction;
			VolumeSpeed = volumeSpeed;
			MADif = maDif;
			TOD = tod;
			BolDif = bolDif;
			StdDev = stdDeviation;
			TradingMode = tradingMode;
		}
		
	}

	public enum NewTradeType{
		Regress,
		Trend
	}
	
	public class NewTradeParameters
	{
	    public double ImbVolThreshold { get; }
	    public double AdvDetectionThreshold { get; }
	    public double RatioThreshold { get; }
	    public string Direction { get; }
	    public DateTime TradeWindowEndTime { get; } // Add this property
	    public List<NewSimTrade> Trades { get; } = new List<NewSimTrade>();
	    public bool IsActive { get; set; } = true;
		public bool allowInTrade { get; set; } = true;
		public TradeType TradeType  { get; set; }
		
	    public NewTradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, DateTime tradeWindowEndTime, TradeType tradeType)
	    {
	        ImbVolThreshold = imbVolThreshold;
	        AdvDetectionThreshold = advDetectionThreshold;
	        RatioThreshold = ratioThreshold;
	        TradeWindowEndTime = tradeWindowEndTime; // Initialize the property
			TradeType = tradeType;
	    }
	}
	
	public class ReFactoredATI : Strategy
	{
		#region Controls
		
		private bool longMode = false;
		private bool shortMode = false;
		private bool bothArmed = false;
		
		//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button armButton;
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		
		public TradingMode currentMode;
		public enum TradingMode
		{
		   Regression,
		   Trend
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
			    shortMode =  true;
				shortButton.Content = "Armed Short";
				shortButton.Background = Brushes.Red;
					
		    }
			
			if (button == shortButton && buttonText == "Armed Short" && buttonName == "ShortButton" || (Position.MarketPosition != MarketPosition.Flat))
		    {
					// Switch to short-only mode
			    shortMode =  false;
				shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
					
		    }
			
		  	if (button == armButton && buttonName == "ArmButton" && buttonText == "Both Armed")
		    {
		        // Switch to ranged mode
		        bothArmed = false;
				longButton.Content = "Arm Long";
				shortButton.Content = "Arm Short";
				longMode = false;
				shortMode = false;
				armButton.Content = "Arm Both";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
				armButton.Background = Brushes.Gray;
				Print($"Auto Arm deactivated: Long mode = {longMode}, Short mode = {shortMode}, Auto Arm = {bothArmed}");

		    }
			
			if (button == armButton && buttonName == "ArmButton" && buttonText == "Arm Both")
		    {
		        // Switch to ranged mode
		        bothArmed = true;
				longButton.Content = "Armed Long";
				shortButton.Content = "Armed Short";
				longMode = true;
				shortMode = true;
				armButton.Content = "Both Armed";
				shortButton.Background = Brushes.Red;
				longButton.Background = Brushes.Green;
				armButton.Background = Brushes.Blue;
				Print($"Auto Arm activated: Long mode = {longMode}, Short mode = {shortMode}, Auto Arm = {bothArmed}");
			
		    }
			
		    if (button == longButton && buttonText == "Arm Long" && buttonName == "LongButton")
		    {
				// Switch to short-only mode
		        longMode = true;
				longButton.Content = "Armed Long";
				longButton.Background = Brushes.Green;
		    }
			
			if (button == longButton && buttonText == "Armed Long" && buttonName == "LongButton")
		    {
				// Switch to short-only mode
		        longMode = false;
				longButton.Content = "Arm Long";
				longButton.Background = Brushes.Gray;
					
		    }
			
			if (buttonText == "Trend" && buttonName == "ModeButton" && button == modeButton)
		    {
				currentMode = TradingMode.Regression;
				modeButton.Content = "Regression";
				modeButton.Background = Brushes.Teal;
				Print("regression - " + currentMode);
    			Print($"Mode changed: Regression mode activated, Trend mode deactivated");
		    }
			
		    else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
		    {
		   		currentMode = TradingMode.Trend;
				modeButton.Content = "Trend";
				modeButton.Background = Brushes.Purple;
   			 	Print($"Mode changed: Trend mode activated, Regression mode deactivated");

		    }
		
		    // Update the button content or perform any other necessary actions
		}
		
		private void resetButtons()
		{
		    Dispatcher.Invoke(() =>
	        {
	            longMode = false;
	            shortMode = false;
				bothArmed = false;
	            shortButton.Content = "Arm Short";
	            longButton.Content = "Arm Long";
				armButton.Content = "Arm Both";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
				armButton.Background = Brushes.Gray;
			});
		}
		
		#endregion;
		
		#region Trading Variables
		
		int activeBar = -1;
		bool tradeTaken = false;
		
		double price = 0;
		VolumeData volData;
		public class VolumeData
		{
		    public double AskVolume { get; set; }
		    public double BidVolume { get; set; }
		    
		    public VolumeData(double askVol, double bidVol)
		    {
		        AskVolume = askVol;
		        BidVolume = bidVol;
		    }
		}
		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
		private Dictionary<double, VolumeData> priceVolumeMap = new Dictionary<double, VolumeData>();
		#endregion;
		
		#region Machine Learning Variables
		
		private DateTime StrategyStartTime;
		private List<NewTradeParameters> completedTradeParamsBuffer = new List<NewTradeParameters>();
		bool pastTime = false;
		
		DateTime currentTime;
		double lowOfBar = 999999999;
		double highOfBar = 0;
			
		private TradeParameters? currentTradeParameters = null;
		private List<NewTradeParameters> tradeParamsList = new List<NewTradeParameters>();
		private Dictionary<double, VolumeData> aggregatedVolumes;
	    private const int MaxHistoricalBars = 1; // Adjust this value as needed
		
		private List<NewSimTrade> simTrades = new List<NewSimTrade>();
	    private DateTime lastSampleTime = DateTime.MinValue;
		private DateTime lastDay = DateTime.MinValue;
		private DateTime predValueWrite = DateTime.MinValue;

		// Flag to indicate whether the trades window period has ended
		private bool tradesWindowEnded = false;
		double winRate = 0;
		
		int currentWindowId = 0;
		#endregion
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "ReFactoredATI";
				Calculate									= Calculate.OnBarClose;
				EntriesPerDirection							= 1;
				EntryHandling								= EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy				= true;
				ExitOnSessionCloseSeconds					= 30;
				IsFillLimitOnTouch							= false;
				MaximumBarsLookBack							= MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution							= OrderFillResolution.Standard;
				Slippage									= 0;
				StartBehavior								= StartBehavior.WaitUntilFlat;
				TimeInForce									= TimeInForce.Gtc;
				TraceOrders									= false;
				RealtimeErrorHandling						= RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling							= StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade							= 20;
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
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
				        Margin = new Thickness(0, 0, 0, 60) // Adjust bottom margin as needed
				    };
				
				    // Define 3 rows
				    System.Windows.Controls.RowDefinition row1 = new System.Windows.Controls.RowDefinition(); // Row 0
				    System.Windows.Controls.RowDefinition row2 = new System.Windows.Controls.RowDefinition(); // Row 1
				    System.Windows.Controls.RowDefinition row3 = new System.Windows.Controls.RowDefinition(); // Row 2
				
				    // Define 4 columns
				    System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition(); // Col 0
				    System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition(); // Col 1
				    System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition(); // Col 2
				    System.Windows.Controls.ColumnDefinition column4 = new System.Windows.Controls.ColumnDefinition(); // Col 3
				
				    myGrid.RowDefinitions.Add(row1);
				    myGrid.RowDefinitions.Add(row2);
				    myGrid.RowDefinitions.Add(row3);
				
				    myGrid.ColumnDefinitions.Add(column1);
				    myGrid.ColumnDefinitions.Add(column2);
				    myGrid.ColumnDefinitions.Add(column3);
				    myGrid.ColumnDefinitions.Add(column4);
				
				    // BUTTON DEFINITIONS
				    longButton = new System.Windows.Controls.Button
				    {
				        Name = "LongButton",
				        Content = longMode ? "Armed Long" : "Arm Long",
				        Foreground = Brushes.White,
				        Background = longMode ? Brushes.Green : Brushes.Gray,
				    };
				
				    shortButton = new System.Windows.Controls.Button
				    {
				        Name = "ShortButton",
				        Content = shortMode ? "Armed Short" : "Arm Short",
				        Foreground = Brushes.White,
				        Background = shortMode ? Brushes.Red : Brushes.Gray,
				    };
				
				    armButton = new System.Windows.Controls.Button
				    {
				        Name = "ArmButton",
				        Content = bothArmed ? "Both Armed" : "Arm Both",
				        Foreground = Brushes.White,
				        Background = Brushes.Blue,
				    };
				
				    modeButton = new System.Windows.Controls.Button
				    {
				        Name = "ModeButton",
				        Foreground = Brushes.White,
				        Background = currentMode == TradingMode.Regression ? Brushes.Teal : Brushes.Purple,
				        Content = currentMode == TradingMode.Regression ? "Regression" : "Trend",
				    };
				
				    // Assign the same Click event handler to all buttons
				    longButton.Click += OnButtonClick;
				    shortButton.Click += OnButtonClick;
				    armButton.Click += OnButtonClick;
				    modeButton.Click += OnButtonClick;
				
				    //
				    // BUTTON LAYOUT:
				    //
				    //  - Mode (Trend/Regression) in Row 0
				    //  - Long & Short in Row 1
				    //  - Auto Arm in Row 2
				    //
				
				    // Mode button in Row 0, Column 0 (top row)
				    System.Windows.Controls.Grid.SetRow(modeButton, 0);
				    System.Windows.Controls.Grid.SetColumn(modeButton, 0);
				 	System.Windows.Controls.Grid.SetColumnSpan(modeButton, 2);
					
				    // Long button in Row 1, Column 0
				    System.Windows.Controls.Grid.SetRow(longButton, 1);
				    System.Windows.Controls.Grid.SetColumn(longButton, 0);
				
				    // Short button in Row 1, Column 1
				    System.Windows.Controls.Grid.SetRow(shortButton, 1);
				    System.Windows.Controls.Grid.SetColumn(shortButton, 1);
				
				    // Auto Arm in Row 2, Column 0 (bottom row)
				    System.Windows.Controls.Grid.SetRow(armButton, 2);
				    System.Windows.Controls.Grid.SetColumn(armButton, 0);
					System.Windows.Controls.Grid.SetColumnSpan(armButton, 2);
				
				    // Add them to the grid
				    myGrid.Children.Add(modeButton);
				    myGrid.Children.Add(longButton);
				    myGrid.Children.Add(shortButton);
				    myGrid.Children.Add(armButton);
				
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

		protected override void OnBarUpdate()
		{
			if(CurrentBar > activeBar){
				activeBar = CurrentBar;
				
				priceVolumeMap.Clear();
				tradeTaken = false;
				
				highOfBar = 0;
				lowOfBar = 99999999;
				
		
				if(State==State.Realtime && Optimise && MLOn)
				{
					
				  WriteCurrentPredictiveValuesToServer();
				  ReadOptimizedParamsFromServer();
					
				}
			}
			
		}
		
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if(e.MarketDataType != MarketDataType.Last || CurrentBar < 2)
				return;
					
			double volume = e.Volume;
			price = e.Price;
			currentTime = e.Time;
			
			#region Mapping Ask and Bid Volumes
			
			double askPrice = e.Ask;
			double bidPrice = e.Bid;
			double midpoint = (askPrice + bidPrice) / 2;
			
			bool isAskSide = price > midpoint;
			bool isBidSide = price < midpoint;
			
			// Retrieve or create a VolumeData object for this price
		
			if (!priceVolumeMap.TryGetValue(price, out volData))
			{
			    // If price not in dictionary, create a new entry
			    volData = new VolumeData(0, 0);
			    priceVolumeMap[price] = volData;
			}
			
			// Accumulate volumes
			if (isAskSide)
			    volData.AskVolume += volume;
			else if (isBidSide)
			    volData.BidVolume += volume;
			
			#endregion;
			
			HandleEntryConditions();
			UpdateSimTrades(ProfitTarget, StopLoss, e.Price);
			
			if (e.Price > highOfBar && e.Volume != 0)
			{
				highOfBar = e.Price;
			
			
			}
			if (e.Price < lowOfBar && e.Volume != 0)
			{
				lowOfBar = e.Price;
			
			}
			
			if (e.MarketDataType == MarketDataType.Last)
			{
				if (CurrentBar < 1)
				{
					return;
				}
	
				if (incTrain || trainModel)
				{
					aggregatedVolumes = AggregateVolumesIntoGroups(priceVolumeMap, lowOfBar,highOfBar);
					
					ProcessTradeParams(e);
				
					pastTime = e.Time - lastSampleTime > TimeSpan.FromSeconds(sampleInterval);
								
					if(pastTime)
					{
						lastSampleTime = e.Time;
					
						InitializeTradeParams();
							
					}
				}
			
				if (trainModel || incTrain)
				{
					if (CurrentBar < 3) return;
					
					if (incTrain && State != State.Realtime)
						return;
					
					if (e.Time - lastDay > TimeSpan.FromHours(1))
					{
						Print("Current Date:" + Time[0]);
						lastDay = Time[0];
					}
					
					double closePrice = e.Price;
				
					// Now it accepts Dictionary<double, VolumeData>:
					double FindClosestKey(Dictionary<double, VolumeData> dict, double targetPrice)
					{
					    return dict.Keys
					               .OrderBy(key => Math.Abs(key - targetPrice))
					               .FirstOrDefault();
					};

					double closestPrice = FindClosestKey(aggregatedVolumes, closePrice);
					
					if(aggregatedVolumes.TryGetValue(closestPrice, out VolumeData segmentVol))
					{
						double buyVolume = segmentVol.AskVolume;
						double sellVolume = segmentVol.BidVolume;
						
						double imbVol = Math.Abs(buyVolume - sellVolume);
						double advDetection = Math.Min(buyVolume, sellVolume);
						double tradeRatio = 0;
						
						string trendDirection = "";
						string regressDirection = "";
						string direction = "";
						// Example snippet inside your logic block:

						if (buyVolume > sellVolume)
						{
						    tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
							
						    // The "Trend" direction is "Long", the "Regression" direction is "Short"
						    trendDirection   = "Long";
						    regressDirection = "Short";
						}
						else if (sellVolume > buyVolume)
						{
						    tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
						
						    // The "Trend" direction is "Short", the "Regression" direction is "Long"
						    trendDirection   = "Short";
						    regressDirection = "Long";
						}
						
						var activeTrades = tradeParamsList.Where(tp => tp.IsActive && tp.TradeWindowEndTime > currentTime);
						
						bool moreThan;
						bool lessThan;
						
						foreach (var tradeParams in activeTrades)
						{
						    if (advDetection >= tradeParams.AdvDetectionThreshold 
						        && imbVol >= tradeParams.ImbVolThreshold 
						        && tradeRatio >= tradeParams.RatioThreshold 
						        && tradeParams.allowInTrade == true)
						    {
						        if (incTrain && !trainModel)
						        {
						            // 1) Always open one "Trend" trade (with the trendDirection you computed)
						             if (tradeParams.TradeType == TradeType.Trend)
						            {
						                // e.g. 'trendDirection' is direction with imbalance
						                SimulateTrade(tradeParams, trendDirection, closePrice, "Trend");
						            }
						            else // TradeType.Regress
						            {
						                // e.g. 'regressDirection' is the opposite direction
						                SimulateTrade(tradeParams, regressDirection, closePrice, "Regression");
						            }
						        }
						        else
						        {
						            // Original logic: If you’re in a "Trend" mode => use trendDirection
						            // If you’re in "Regression" mode => use regressDirection
						            if ((modelToTrain == TradingMode.Trend && trainModel) 
						                || (currentMode == TradingMode.Trend && incTrain))
						            {
						                // Trend trade
						                SimulateTrade(tradeParams, trendDirection, closePrice, "Trend");
						            }
						            else
						            {
						                // Regression trade
						                SimulateTrade(tradeParams, regressDirection, closePrice, "Regression");
						            }
						        }
						    }
						}
					}
					
				}
			}
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
		
		private void HandleEntryConditions()
		{
		    // If an ATM strategy is already open, do nothing.
		    if (orderId.Length > 0 || atmStrategyId.Length > 0 || tradeTaken)
		        return;
		
		    // 1) Gather “ask” volumes below current price
		    double totalAskBelow = 0;
		    double totalBidBelow = 0;
		    for (int i = 0; i < tickStacking; i++)
		    {
		        double checkPrice = price - i * TickSize;
		        if (priceVolumeMap.TryGetValue(checkPrice, out volData))
		        {
		            totalAskBelow += volData.AskVolume;
		            totalBidBelow += volData.BidVolume;
		        }
		    }
		    
		    // 2) Gather “bid” volumes above current price
		    double totalAskAbove = 0;
		    double totalBidAbove = 0;
		    for (int i = 0; i < tickStacking; i++)
		    {
		        double checkPrice = price + i * TickSize;
		        if (priceVolumeMap.TryGetValue(checkPrice, out volData))
		        {
		            totalAskAbove += volData.AskVolume;
		            totalBidAbove += volData.BidVolume;
		        }
		    }
		
		    bool askSignal = false;
		    bool bidSignal = false;
		
		    if (totalBidBelow > 0
		        && (totalAskBelow / totalBidBelow > minRatio)
		        && (totalAskBelow - totalBidBelow > minVolume)
		        && (totalBidBelow > minDetection))
		    {
		        askSignal = true;
		    }
		
		    if (totalAskAbove > 0
		        && (totalBidAbove / totalAskAbove > minRatio)
		        && (totalBidAbove - totalAskAbove > minVolume)
		        && (totalAskAbove > minDetection))
		    {
		        bidSignal = true;
		    }
			
		
		    if (askSignal && longMode)
		    {

		        if (currentMode == TradingMode.Trend)
		            SubmitAtmStrategy(OrderAction.Buy);
		        else if (currentMode == TradingMode.Regression)
		            SubmitAtmStrategy(OrderAction.Sell);
		    }
		
		    if (bidSignal && shortMode)
		    {
				
		        if (currentMode == TradingMode.Trend)
		            SubmitAtmStrategy(OrderAction.Sell);
		        else if (currentMode == TradingMode.Regression)
		            SubmitAtmStrategy(OrderAction.Buy);
		    }
		}

		private (bool askSignal, bool bidSignal) CheckConsecutiveChunks(
		    double basePrice,
		    int offsetSign,       // +1 => above, -1 => below
		    int ticksPerChunk,    // tickStacking
		    int numberOfChunks,   // imbalancesToTrade
		    double minRatio,
		    double minVolume,
		    double minDetection)
		{
		    bool allChunksAsk = true; 
		    bool allChunksBid = true;
		
		    for (int chunkIndex = 0; chunkIndex < numberOfChunks; chunkIndex++)
		    {
		        double chunkAsk = 0;
		        double chunkBid = 0;
		
		        // Sum volumes for "ticksPerChunk" ticks
		        for (int tickIndex = 0; tickIndex < ticksPerChunk; tickIndex++)
		        {
		            double offsetTicks = (chunkIndex * ticksPerChunk + tickIndex) * TickSize;
		            double checkPrice   = basePrice + offsetSign * offsetTicks;
		
		            if (priceVolumeMap.TryGetValue(checkPrice, out volData))
		            {
		                chunkAsk += volData.AskVolume;
		                chunkBid += volData.BidVolume;
		            }
		        }
		
		        // Decide if this chunk is "ask" or "bid"
		        bool chunkIsAsk = false;
		        bool chunkIsBid = false;
		
		        // Same ratio checks you used previously
		        if (chunkBid > 0 &&
		            (chunkAsk / chunkBid > minRatio) &&
		            (chunkAsk - chunkBid > minVolume) &&
		            (chunkBid > minDetection))
		        {
		            chunkIsAsk = true;
		        }
		
		        if (chunkAsk > 0 &&
		            (chunkBid / chunkAsk > minRatio) &&
		            (chunkBid - chunkAsk > minVolume) &&
		            (chunkAsk > minDetection))
		        {
		            chunkIsBid = true;
		        }
		
		        // If this chunk isn't ask, we can't have allChunksAsk
		        if (!chunkIsAsk)
		            allChunksAsk = false;
		        // If this chunk isn't bid, we can't have allChunksBid
		        if (!chunkIsBid)
		            allChunksBid = false;
		
		        // If both are already false, no need to check further
		        if (!allChunksAsk && !allChunksBid)
		            break;
		    }
		
		    return (allChunksAsk, allChunksBid);
		}
		
		private void SubmitAtmStrategy(OrderAction action)
		{
			if(State != State.Realtime)
				return;
			
		    isAtmStrategyCreated = false;
		    orderId       = GetAtmStrategyUniqueId();
		    atmStrategyId = GetAtmStrategyUniqueId();
		
		    AtmStrategyCreate(
		        action,
		        OrderType.Market,
		        0,
		        0,
		        TimeInForce.Gtc,
		        orderId,
		        ATMStrategy,
		        atmStrategyId,
		        (atmCallbackErrorCode, atmCallBackId) =>
		        {
		            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                isAtmStrategyCreated = true;
		        });
			resetButtons();
			tradeTaken = true;
		}
		
		private void InitializeTradeParams()
		{
		    DateTime tradeWindowEndTime = currentTime.AddSeconds(tradesWindowMinutes);
	
	        foreach (var kvp in aggregatedVolumes)
	        {
	            double price       = kvp.Key;
	            double buyVolume   = kvp.Value.AskVolume;
	            double sellVolume  = kvp.Value.BidVolume;
	            double tradeRatio  = 0;

	            // Determine direction logic (like before)
	            if (buyVolume > sellVolume)
	            {
	                tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
	            }
	            else if (sellVolume > buyVolume)
	            {
	                tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
	       
	            }
	
	            // Check thresholds
	            if (Math.Min(buyVolume, sellVolume) > 2)
	            {
	                // If incTrain is ON (and optionally trainModel is OFF),
	                // create both a Trend AND a Regression trade parameter
	                if (incTrain && !trainModel)
	                {
	                    // 1) Trend trade parameters
	                    NewTradeParameters trendParams = new NewTradeParameters(
	                        Math.Abs(buyVolume - sellVolume),
	                        Math.Min(buyVolume, sellVolume),
	                        tradeRatio,
	                        tradeWindowEndTime,
	                        TradeType.Trend
	                    );
	                    tradeParamsList.Add(trendParams);
	
	                    // 2) Regression trade parameters
	                    NewTradeParameters regressParams = new NewTradeParameters(
	                        Math.Abs(buyVolume - sellVolume),
	                        Math.Min(buyVolume, sellVolume),
	                        tradeRatio,
	                        tradeWindowEndTime,
	                        TradeType.Regress
	                    );
	                    tradeParamsList.Add(regressParams);
	                }
	                else
	                {
	                    // Existing logic: pick a single trade type (Trend or Regress)
	                    TradeType tradeType = TradeType.Trend;
	                    
	                    if (currentMode == TradingMode.Trend)
	                        tradeType = TradeType.Trend;
	                    else if (currentMode == TradingMode.Regression)
	                        tradeType = TradeType.Regress;
	
	                    NewTradeParameters tradeParams = new NewTradeParameters(
	                        Math.Abs(buyVolume - sellVolume),
	                        Math.Min(buyVolume, sellVolume),
	                        tradeRatio,
	                        tradeWindowEndTime,
	                        tradeType
	                    );
						
													// Example: only add if there's no *active* tradeParams with the same thresholds
						bool alreadyExists = tradeParamsList.Any(tp => 
						    tp.IsActive 
						    && tp.ImbVolThreshold == tradeParams.ImbVolThreshold
						    && tp.AdvDetectionThreshold == tradeParams.AdvDetectionThreshold
						    && tp.RatioThreshold == tradeParams.RatioThreshold
						 
						);
						
						if (!alreadyExists)
						{
						    tradeParamsList.Add(tradeParams);
						}
	                }
	            }
    		}

		}

		private Dictionary<double, VolumeData> AggregateVolumesIntoGroups(
		    Dictionary<double, VolumeData> volumes,
		    double barLow,
		    double barHigh)
		{
		    Dictionary<double, VolumeData> aggregatedVolumes = new Dictionary<double, VolumeData>();
		
		    int ticksPerSegment = 4;            // Number of ticks in each segment
		    double tickSize = TickSize;         // Tick size of the instrument
		    double segmentSize = ticksPerSegment * tickSize; // Size of each segment in price terms
		    
		    // Calculate total ticks within [barLow, barHigh], inclusive
		    int totalTicks = (int)Math.Round((barHigh - barLow) / tickSize) + 1;
		    // Calculate number of full segments
		    int fullSegments = (totalTicks + ticksPerSegment - 1) / ticksPerSegment;
		
		    const double epsilon = 1e-6; // Helps account for floating-point rounding
		
		    foreach (var kvp in volumes)
		    {
		        double price = kvp.Key;
		        VolumeData volData = kvp.Value; // Contains AskVolume, BidVolume
		
		        // Only consider prices within this bar range
		        if (price >= barLow - epsilon && price <= barHigh + epsilon)
		        {
		            // Determine which segment the price belongs to
		            int segmentIndex = (int)Math.Floor((price - barLow) / segmentSize);
		            // Clamp to avoid going beyond total segments
		            segmentIndex = Math.Min(segmentIndex, fullSegments - 1);
		
		            // The segment's "key" in the dictionary (start price of that segment)
		            double pointKey = barLow + segmentIndex * segmentSize;
		
		            // If we don't have a VolumeData record for this segment yet, create one
		            if (!aggregatedVolumes.TryGetValue(pointKey, out VolumeData aggData))
		            {
		                aggData = new VolumeData(0, 0);
		                aggregatedVolumes[pointKey] = aggData;
		            }
		
		            // Accumulate ask and bid volumes
		            aggData.AskVolume += volData.AskVolume;
		            aggData.BidVolume += volData.BidVolume;
		        }
		    }
		
		    return aggregatedVolumes;
		}

		private double GetAggregatedPriceLevel(double price)
		{
		    // Ensure TickAggregation is at least 1
		    int tickAggregation = Math.Max(1, tickStacking);
		
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
		
		private void SimulateTrade(NewTradeParameters tradeParams, string direction, double price, string tradingMode)
		{
		    double entryPrice = price;
		    
		    NewSimTrade newTrade = new NewSimTrade(
		        tradeParams.ImbVolThreshold,
		        tradeParams.AdvDetectionThreshold,
		        tradeParams.RatioThreshold,
		        price,
				direction,
				PATIMachineLearningInputsV2().VolumeSpeedPerSecond[0],
				PATIMachineLearningInputsV2().MovingAvgDiff[0],
				PATIMachineLearningInputsV2().BollingerDiff[0],
				PATIMachineLearningInputsV2().StdDevBB[0],
				PATIMachineLearningInputsV2().TimeOfDay[0],
				tradingMode
		    );
		
		    tradeParams.Trades.Add(newTrade);
		    tradeParams.allowInTrade = false;
		    simTrades.Add(newTrade);
		}

		private void UpdateSimTrades(double target, double stopLoss, double price)
		{
		    foreach (NewSimTrade trade in simTrades.Where(t => t.Status == null))
		    {
		        double entryPrice = trade.EntryPrice;
		        double currentPrice = price;
		        string positionType = trade.Direction;  // "Long" or "Short"
				
		        if (positionType == "Long")
		        {
		            if (currentPrice >= entryPrice + (target * TickSize))
		                UpdateTradeStatus(trade, "Target Hit");
		            else if (currentPrice <= entryPrice - (stopLoss * TickSize))
		                UpdateTradeStatus(trade, "Stop Loss Hit");
					
		        }
		        else if (positionType == "Short")
		        {
		            if (currentPrice <= entryPrice - (target * TickSize))
		                UpdateTradeStatus(trade, "Target Hit");
		            else if (currentPrice >= entryPrice + (stopLoss * TickSize))
		                UpdateTradeStatus(trade, "Stop Loss Hit");
					
		        }
		    }
		
		    // Remove all completed from active list
		    simTrades.RemoveAll(tr => tr.IsCompleted);
		}

		private void UpdateTradeStatus(NewSimTrade trade, string status)
		{
		    trade.Status = status;         // "Target Hit" or "Stop Loss Hit"
		    trade.IsCompleted = true;
		
		    // If you still use TradeParameters for other logic:
		    var tradeParams = tradeParamsList.FirstOrDefault(tp => tp.Trades.Contains(trade));
		    if (tradeParams != null)
		    {
		        tradeParams.allowInTrade = true;
		    }
		}
		
		private void UpdateWinRateForTradeParams(NewTradeParameters tradeParams, double target, double stopLoss)
		{
		    int winCount = tradeParams.Trades.Count(trade => trade.Status == "Target Hit");
		    double totalTrades = tradeParams.Trades.Count();
		    double winRate = totalTrades > 0 ? (double)winCount / totalTrades : 0;

		    foreach (var trade in tradeParams.Trades)
		    {
		        trade.WinRate = winRate;	
		    }
		}
		
		private void ProcessTradeParams(MarketDataEventArgs e)
		{
		    var justCompleted = new List<NewTradeParameters>();
		
		    for (int i = tradeParamsList.Count - 1; i >= 0; i--)
		    {
		        var tradeParams = tradeParamsList[i];
		        if (e.Time > tradeParams.TradeWindowEndTime)
		        {
		            UpdateWinRateForTradeParams(tradeParams, ProfitTarget, StopLoss);
		            justCompleted.Add(tradeParams);
		        }
		    }
		
		    // Remove from active list
		    tradeParamsList.RemoveAll(trp => e.Time > trp.TradeWindowEndTime);
		
		    // Add newly completed trades to our class-level buffer
		    completedTradeParamsBuffer.AddRange(justCompleted);
		
		    // Now decide when to actually write them to CSV
		    // For example, if we have at least 500 trades in historical or any new trades in real-time
		    if (trainModel && completedTradeParamsBuffer.Count >= 500 && State == State.Historical)
		    {
		        WriteTradesToCsv(completedTradeParamsBuffer);
		        completedTradeParamsBuffer.Clear(); // Clear once written
		    }
		    else if (justCompleted.Any() && State == State.Realtime && trainModel || justCompleted.Any() && incTrain)
		    {
		        // If this is real-time data, write them immediately
		        WriteTradesToCsv(completedTradeParamsBuffer);
		        completedTradeParamsBuffer.Clear();
		    }
		}
				
		private void WriteTradesToCsv(List<NewTradeParameters> completedTradeParams)
		{
		    // If we have no reason to write (both incTrain and trainModel false), just return
		    if (!incTrain && !trainModel) 
		        return;
		
		    // If there's nothing to write, just return
		    if (completedTradeParams == null || completedTradeParams.Count == 0)
		        return;
		
		    // Paths for incremental or main
		    string regressionIncrementalPath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\inctrain_regression.csv";
		    string trendIncrementalPath      = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\inctrain_trend.csv";
		    string mainTrainPath             = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\train_model.csv";
		
		    try
		    {

		        if (trainModel)
		        {

		            WriteTradesToSingleFile(
		                completedTradeParams,
		                mainTrainPath
		            );
		            
		            // Clear after writing
		            completedTradeParams.Clear();
		            return;
		        }
		
		        List<NewTradeParameters> regressList = completedTradeParams
		            .Where(tp => tp.TradeType == TradeType.Regress)
		            .ToList();
		
		        List<NewTradeParameters> trendList = completedTradeParams
		            .Where(tp => tp.TradeType == TradeType.Trend)
		            .ToList();

		        if (regressList.Count > 0)
		            WriteTradesToSingleFile(regressList, regressionIncrementalPath);

		        if (trendList.Count > 0)
		            WriteTradesToSingleFile(trendList, trendIncrementalPath);

		        completedTradeParams.Clear();
				
		    }
		    catch (Exception ex)
		    {
		        Print($"Error writing to CSV: {ex.Message}");
		    }
		}
		
		
		/// <summary>
		/// Helper function to write ANY list of TradeParameters to a single CSV file,
		/// ensuring the correct header and appending completed trades.
		/// </summary>
		private void WriteTradesToSingleFile(List<NewTradeParameters> tradeParamsList, string filePath)
		{
		    if (tradeParamsList == null || tradeParamsList.Count == 0) 
		        return;
		
		    // Ensure directory
		    string directory = Path.GetDirectoryName(filePath);
		    if (!Directory.Exists(directory))
		        Directory.CreateDirectory(directory);
		
		    bool fileExists   = File.Exists(filePath);
		    bool headerExists = false;
		
		    if (fileExists)
		    {
		        string firstLine = File.ReadLines(filePath).FirstOrDefault();
		        headerExists = firstLine != null 
		                       && firstLine.StartsWith("WinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD");
		    }
		
		    using (StreamWriter writer = new StreamWriter(filePath, append: true))
		    {
		        // If no header yet, write it once
		        if (!headerExists)
		        {
		            writer.WriteLine("WinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD");
		            headerExists = true;
		        }
		
		        // Build CSV lines for each completed trade in each TradeParameters
		        StringBuilder sb = new StringBuilder();
		        foreach (var tradeParams in tradeParamsList)
		        {
					                 // Only write the *last* completed trade in each TradeParameters
		            var lastCompletedTrade = tradeParams.Trades
		                .FirstOrDefault(t => t.IsCompleted);
		
		            if (lastCompletedTrade != null)
		            {
		                sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8}\n",
		                    Math.Round(lastCompletedTrade.WinRate, 2),
		                    lastCompletedTrade.ImbVol,
		                    Math.Round(lastCompletedTrade.Ratio, 2),
		                    lastCompletedTrade.AdvDetection,
		                    Math.Round(lastCompletedTrade.VolumeSpeed, 2),
		                    Math.Round(lastCompletedTrade.MADif, 2),
		                    Math.Round(lastCompletedTrade.BolDif, 2),
		                    Math.Round(lastCompletedTrade.StdDev, 2),
		                    Math.Round(lastCompletedTrade.TOD, 2)
		                );
		            }
	 
		        }
		
		        writer.Write(sb.ToString());
		    }
		}
				
		private void WriteCurrentPredictiveValuesToServer()
		{
			
			var predictiveValues = new
			{
				WinRate = targetWinRate,
				VolumeSpeed = PATIMachineLearningInputsV2().VolumeSpeedPerSecond[0],
				MADif = PATIMachineLearningInputsV2().MovingAvgDiff[0],
				BolDif = PATIMachineLearningInputsV2().BollingerDiff[0],
				StdDev = PATIMachineLearningInputsV2().StdDevBB[0],
				TOD = PATIMachineLearningInputsV2().TimeOfDay[0],
				CurrentModel = (currentMode == TradingMode.Regression) ? "Regression" : "Trend"
			};
			
			try
			{
				HttpClientWrapper.Post("current_predictive_values", predictiveValues);
			}
			catch (Exception ex)
			{
				Print($"Error updating current predictive values: {ex.Message}");
			}
		}

		double prevratio;
		int prevvol;
		int prevdet;
		private void ReadOptimizedParamsFromServer()
		{
		    try
		    {
		        // Assuming HttpClientWrapper.Get returns Dictionary<string, object>
		        var optimizedParams = HttpClientWrapper.Get("optimized_params");
		
		        if (optimizedParams == null)
		        {
		            Print("Failed to retrieve response from server.");
		            return;
		        }
		
		        // Extract parameters
		        int newMinVolume = Convert.ToInt32(optimizedParams["ImbVol"]);
		        double newRatio = Convert.ToDouble(optimizedParams["ImbRatio"]);
		        int newDetectionValue = Convert.ToInt32(optimizedParams["AdversaryDetection"]);
		
		        // Proceed with your logic
		        if (newMinVolume != prevvol || newRatio != prevratio || newDetectionValue != prevdet)
		        {
		            minVolume = newMinVolume;
		            minRatio = newRatio;
		            minDetection = newDetectionValue;
		
		            Print($"MinVol: {minVolume}, Ratio: {minRatio}, AdvDet: {minDetection}");
		
		            prevvol = newMinVolume;
		            prevratio = newRatio;
		            prevdet = newDetectionValue;
		        }
		        else
		        {
		            Print("No changes in optimized parameters.");
		        }
		    }
		    catch (HttpRequestException ex)
		    {
		        Print($"HTTP error reading optimized parameters from server: {ex.Message}");
		    }
		    catch (Exception ex)
		    {
		        Print($"Error reading optimized parameters from server: {ex.Message}");
		    }
		}

		#region Properties
		
		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Minimum Imbalance Volume", Order=2, GroupName="Imbalances")]
		public int minVolume
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Minimum Ratio", Order=3, GroupName="Imbalances")]
		public double minRatio
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Adversary Detection ", Order=4, GroupName="Imbalances")]
		public int minDetection
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="tick levels to sum for one imbalance", Order=5, GroupName="Imbalances")]
		public int tickStacking { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Number of imbalances to classify a trade", Order=6, GroupName="Imbalances")]
		public int imbalancesToTrade { get; set; }
		
		[NinjaScriptProperty]
		[Display( Name = "SL for ML", GroupName = "Machine Learning Targets", Order = 0)]
		public int StopLoss
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display( Name = "PT for ML", GroupName = "Machine Learning Targets", Order = 0)]
		public int ProfitTarget
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display( Name = "Target WinRate", GroupName = "Machine Learning Targets", Order = 0)]
		public double targetWinRate
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Use Machine Learning?", GroupName = "Machine Learning", Order = 0)]
		public bool MLOn
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Use Parameter Optmization?? (ML needs to be on)", GroupName = "Machine Learning", Order = 0)]
		public bool Optimise
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Train the model", GroupName = "Machine Learning Training", Order = 0)]
		public bool trainModel
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Regression or Trend", GroupName = "Machine Learning Training", Order = 0)]
		public TradingMode modelToTrain
		{ get; set; } 
		
		
		[NinjaScriptProperty]
		[Display( Name = "Allow incremental learning", GroupName = "Machine Learning Incremental Training", Order = 0)]
		public bool incTrain
		{ get; set; } 
		
		
		[NinjaScriptProperty]
		[Display( Name = "Sample Interval (seconds)", GroupName = "Machine Learning", Order = 0)]
		public double sampleInterval
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Trade window (seconds)", GroupName = "Machine Learning", Order = 0)]
		public int tradesWindowMinutes
		{ get; set; } 

		#endregion
	}
}
