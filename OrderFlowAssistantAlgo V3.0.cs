#region Using declarations
using System;
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
	#region Classes
	
		
	public class SimTrade
	{
		public double ImbVol { get; set; }
		public double AdvDetection { get; set; }
		public double Ratio  { get; set; }
		public double EntryPrice { get; set; }
		public string Direction { get; set; }
		public string Status { get; set; }
		public double WinRate { get; set; }
		
		public int WindowId { get; set; }

		public double VolumeSpeed { get; set; }
		public double BolDif { get; set; }
		public double MADif { get; set; }
		public double TOD { get; set; }
		public double StdDev { get; set; }
	
	
		
		public int TradeCount { get; set; } = 1;
		public int WinCount { get; set; } = 0;
		public int LossCount { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;
		
		
		public SimTrade(double imbVol, double advDetection, double ratio, double entryPrice, string direction,double volumeSpeed, double bolDif, double maDif, double tod, double stdDev)
		{
		    ImbVol = imbVol;
		    AdvDetection = advDetection;
			Ratio = ratio;
		    EntryPrice = entryPrice;
		    Direction = direction;
			VolumeSpeed = volumeSpeed;
			BolDif = bolDif;
			MADif = maDif;
			TOD = tod;
			StdDev = stdDev;
			
		}
		
		public void UpdateWinRate()
		{
		    int totalTrades = WinCount + LossCount;
		    WinRate = totalTrades > 0 ? (double)WinCount / totalTrades : 0;
		}
	}

	public class TradeParameters
	{
	    public double ImbVolThreshold { get; }
	    public double AdvDetectionThreshold { get; }
	    public double RatioThreshold { get; }
	    public string Direction { get; }
	    public DateTime TradeWindowEndTime { get; } // Add this property
	    public List<SimTrade> Trades { get; } = new List<SimTrade>();
	    public bool IsActive { get; set; } = true;
	
	    public TradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, string direction, DateTime tradeWindowEndTime)
	    {
	        ImbVolThreshold = imbVolThreshold;
	        AdvDetectionThreshold = advDetectionThreshold;
	        RatioThreshold = ratioThreshold;
	        Direction = direction;
	        TradeWindowEndTime = tradeWindowEndTime; // Initialize the property
	    }
	}


	#endregion
		
	public class FootPrintStrat : Strategy
	{
		
		#region Trading Modes and Buttons
		
		double trainingMode;
		private bool isLongMode = false;
		private bool isShortMode = false;
		
		private bool isRegressionMode = false;
		private bool isTrendMode = false;
		private bool isAutoArm = false;
			//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button armButton;
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		
		public enum TradingMode
		{
		   Regression,
		   Trend
		}
		
		public enum TradeDirection
		{
			Long,
			Short
		}
		
		public enum CalculationMethod
		{
			ES,
			NQ
		}
		
		#endregion
		
		#region Machine Learning Variables
		
		private DateTime StrategyStartTime;
		
		string initialLetters;
			
		private TradeParameters? currentTradeParameters = null;
		private List<TradeParameters> tradeParamsList = new List<TradeParameters>();
		double Delta;
		double prevDelta = 0;
		double averageAsk;
		double averageBid;
		double highBid;
		double highAsk;
	
		private List<double> deltaValues = new List<double>() { 0, 0, 0 };  // Initialize with three zeroes
		private List<SimTrade> simTrades = new List<SimTrade>();
	    private DateTime lastSampleTime = DateTime.MinValue;
		private List<SimTrade> initialSimTrades = new List<SimTrade>();
		
		private DateTime predValueWrite = DateTime.MinValue;
		
		int askDetection = 0;
		int bidDetection = 0;

		// Flag to indicate whether the trades window period has ended
		private bool tradesWindowEnded = false;
		
		double winRate = 0;
		int currentWindowId = 0;
		
		double fillPrice = 0;
		bool winner;
		bool tradeOutcomeEvaluated = false;
		string lastPosition = string.Empty;
		double lastFillPrice = 0;
		double lastLongTarget = 0;
		double lastShortTarget = 0;
		double lastLongSL = 0;
		double lastShortSL = 0;
		int losers = 0;
		int winners = 0;
		
		#endregion
		
		#region Live Trade Variables
		private Dictionary<double, double> buysAtBar = new Dictionary<double, double>();
		private Dictionary<double, double> sellsAtBar = new Dictionary<double, double>();
		private Dictionary<double, double> totalBuysAndSells = new Dictionary<double, double>();
		Dictionary<double, double> aggregatedBuys;
		Dictionary<double, double> aggregatedSells;
		Dictionary<double, double> laggedaggregatedBuys;
		Dictionary<double, double> laggedaggregatedSells;
		
		string mode;
		
		private int activeBar = -1;
		private double lastPrice = 0;
		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
	    private double recentHighPrice = double.MinValue;
		private double recentLowPrice = double.MaxValue;
		
		bool tradeTaken = false;
		
		bool lowSpread = false;
		#endregion
		
		#region Core NT Strategy Functions
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "FootPrintStrat";
				Calculate									= Calculate.OnEachTick;
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
			
				minVolume = 85;
				ATMStrategy = "Scalp";
				
				
			
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
		
		bool firstStart = true;
		
		protected override void OnBarUpdate()
		{
			Delta = buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
			
			if(isTrendMode && !isRegressionMode){
				trainingMode = 0;
			} 
			if (isRegressionMode && !isTrendMode){
				trainingMode = 1;
			}
			double barLow = Low[0]; // Assuming [0] is the index of the current bar
			double barHigh = High[0];
			double barRange = barHigh - barLow;
			
			if(MLOn && Optimise && State == State.Realtime){
			ReadOptimizedParamsFromCSV();
			}
			
			if(StrategyStartTime == null){
				StrategyStartTime = Time[0];
			}
			
			aggregatedBuys = AggregateVolumesIntoGroups(buysAtBar, barLow, barHigh);
			aggregatedSells = AggregateVolumesIntoGroups(sellsAtBar, barLow, barHigh);

			if (trainModel || incTrain)
			{
				if(!incTrain)
				{
					if(regressForTrain){
						isRegressionMode = true;
						isTrendMode = false;
						trainingMode = 1;
					} else {
						isTrendMode = true;
						isRegressionMode = false;
						trainingMode = 0;
					}
				}
			
			 if (CurrentBar < 2) return;
			
			 if(incTrain && State != State.Realtime)
				 return;
			 
				string symbol = Instrument.FullName;

				// Find the index of the first non-letter character
				int index = 0;
				while (index < symbol.Length && char.IsLetter(symbol[index]))
				{
				    index++;
				}
				
				// Extract the initial letters
				initialLetters = symbol.Substring(0, index);
			 	
				if (Time[0] - lastSampleTime > TimeSpan.FromSeconds(sampleInterval))
		        {
		            lastSampleTime = Time[0];
		            InitializeTradeParams();
		        }
				
				 DateTime currentTime = Time[0];
			 	
				if(initialLetters == "NQ"){
			   
						
				        foreach (var tradeParams in tradeParamsList.Where(tp => tp.IsActive && tp.TradeWindowEndTime > currentTime))
				        {
				            foreach (var kvp in aggregatedBuys)
				            {
				                double price = kvp.Key;
				                double buyVolume = (int)kvp.Value;
				                double sellVolume = aggregatedSells.ContainsKey(price) ? (int)aggregatedSells[price] : 0;
							
				                double imbVol = Math.Abs(buyVolume - sellVolume);
				                double advDetection = Math.Min(buyVolume, sellVolume);
								double tradeRatio = 0;
							
								string direction = "";
								
									if(buyVolume > sellVolume)
									{
										 tradeRatio = sellVolume > 0 ? buyVolume/sellVolume : buyVolume;
										 if(isTrendMode){
										direction = "Long";
										} else if (isRegressionMode){
											direction = "Short";
										}
									} else if(sellVolume > buyVolume) {
										tradeRatio = buyVolume > 0 ? sellVolume/buyVolume : sellVolume;
										if(isTrendMode){
										direction = "Short";
										} else if (isRegressionMode){
											direction = "Long";
										}
									}
									
									//Print($"Price: {price}, BuyVolume: {buyVolume}, SellVolume: {sellVolume}, ImbVol: {imbVol}, AdvDetection: {advDetection}, TradeRatio: {tradeRatio}");
				                if (imbVol > tradeParams.ImbVolThreshold && advDetection > tradeParams.AdvDetectionThreshold && tradeRatio > tradeParams.RatioThreshold)
				                {
				                    if (!simTrades.Any(t => t.WindowId == currentWindowId && t.ImbVol == tradeParams.ImbVolThreshold && t.AdvDetection == tradeParams.AdvDetectionThreshold && t.Ratio == tradeParams.RatioThreshold && t.Status == null))
				                    {
										
				                        SimulateTrade(tradeParams, direction);
										 sampledLevels.Add(price);
										
				                    }
				                }
				            }
				        }
					
			    }
				 else if (initialLetters == "ES")
			    {
			       
			            double price = Close[0]; // Current price
			
			             foreach (var tradeParams in tradeParamsList.Where(tp => tp.IsActive && tp.TradeWindowEndTime > currentTime))
    {
			                bool isLong = false;
			                bool isShort = false;
			
			                // Check for long trades starting at the current price and going down
			                int validLongLevels = 0;
			                for (int i = 0; i < levelstotrade; i++)
			                {
			                    double priceToCheck = price - i * TickSize;
			                    if (buysAtBar.TryGetValue(priceToCheck, out double askVolume) && sellsAtBar.TryGetValue(priceToCheck - TickSize, out double bidVolume))
			                    {
			                        if (askVolume - bidVolume >= tradeParams.ImbVolThreshold && askVolume / bidVolume > tradeParams.RatioThreshold && bidVolume >= tradeParams.AdvDetectionThreshold)
			                        {
			                            validLongLevels++;
			                        }
			                    }
			                }
			                if (validLongLevels == levelstotrade)
			                {
			                    isLong = true;
			                }
			
			                // Check for short trades starting at the current price and going up
			                int validShortLevels = 0;
			                for (int i = 0; i < levelstotrade; i++)
			                {
			                    double priceToCheck = price + i * TickSize;
			                    if (sellsAtBar.TryGetValue(priceToCheck, out double bidVolume) && buysAtBar.TryGetValue(priceToCheck + TickSize, out double askVolume))
			                    {
			                        if (bidVolume - askVolume >= tradeParams.ImbVolThreshold && bidVolume / askVolume > tradeParams.RatioThreshold && askVolume >= tradeParams.AdvDetectionThreshold)
			                        {
			                            validShortLevels++;
			                        }
			                    }
			                }
			                if (validShortLevels == levelstotrade)
			                {
			                    isShort = true;
			                }
			
			                string direction = "";
			                if (isLong)
			                {
			                    direction = "Long";
			                }
			                else if (isShort)
			                {
			                    direction = "Short";
			                }
			
			                if (!string.IsNullOrEmpty(direction) && !simTrades.Any(t => t.WindowId == currentWindowId && t.ImbVol == tradeParams.ImbVolThreshold && t.AdvDetection == tradeParams.AdvDetectionThreshold && t.Ratio == tradeParams.RatioThreshold && t.Status == null))
			                {
			                    SimulateTrade(tradeParams, direction);
			                    sampledLevels.Add(price);
			                }
			            }
			        
				}
			
			    // Update simulated trades and check for target or stop loss
			    UpdateSimTrades(ProfitTarget, StopLoss);
			
			    // If the trade window period has passed, write to CSV and initialize new trade parameters
			
				
				//Print("hello");
//				if (Time[0] - StrategyStartTime > TimeSpan.FromMinutes(tradesWindowMinutes))
//			    {
//				        //Print("Trades window period reached. Writing to CSV...");
//				    UpdateWinRateForCurrentWindow();
//				    WriteTradesToCsv();
//				    currentWindowId++;
//				    InitializeTradeParams();
//				    StrategyStartTime = Time[0];  // Reset the strategy start time
			
//				}
				
				 var completedTradeParams = new List<TradeParameters>();

			    for (int i = tradeParamsList.Count - 1; i >= 0; i--)
			    {
			        var tradeParams = tradeParamsList[i];
			        if (Time[0] > tradeParams.TradeWindowEndTime)
			        {
			            UpdateWinRateForTradeParams(tradeParams);
			            completedTradeParams.Add(tradeParams);
			            tradeParamsList.RemoveAt(i);
			        }
			    }
			
			    if (completedTradeParams.Any())
			    {
			        WriteTradesToCsv(completedTradeParams);
			    }
								
					
			    

			}
	
		    if (CurrentBar != activeBar)
		    {
				UpdateDeltaValues(Delta);
		
				if(State==State.Realtime)
				{
				 WriteCurrentPredictiveValuesToCsv();
				}
				
				prevDelta = Delta;

				UpdateHighsAndLows();
		        activeBar = CurrentBar;
					
				tradeTaken =false;
				
		        buysAtBar.Clear();
		        sellsAtBar.Clear();
		    }
			
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
		
				//Print(atmStrategyId + " empty string");
		
				
//				if (atmStrategyId.Length > 0 )
//				{
				    
//				    if (GetAtmStrategyMarketPosition(atmStrategyId) != Cbi.MarketPosition.Flat)
//				    {
//				         //Disable all trading modes after a successful trade
//				        Dispatcher.Invoke(() =>
//				        {
//				            isLongMode = false;
//				            isShortMode = false;
//							isAutoArm = false;
//				            shortButton.Content = "Arm Short";
//				            longButton.Content = "Arm Long";
//							armButton.Content = "Auto Arm Off";
////							Print("The current ATM Strategy market position is: " + GetAtmStrategyMarketPosition(atmStrategyId));
////							Print("The current ATM Strategy position quantity is: " + GetAtmStrategyPositionQuantity(atmStrategyId));
////							Print("The current ATM Strategy average price is: " + GetAtmStrategyPositionAveragePrice(atmStrategyId));
////							Print("The current ATM Strategy Unrealized PnL is: " + GetAtmStrategyUnrealizedProfitLoss(atmStrategyId));
////							 Print("PnL is " + GetAtmStrategyRealizedProfitLoss(atmStrategyId));
//						});
						

//				    }
//				}
				
			
		}
		
		int upBid;
		int downAsk;
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			
			if(State == State.Historical && e.MarketDataType == MarketDataType.Last)
			{
				  double price = e.Price;
		        double volume = e.Volume;
				
				 if (price > (e.Ask + e.Bid) / 2)
		        {
		            RecordTrade(buysAtBar, price, volume, e);
		            RecordTrade(sellsAtBar, price, 0, e);
		        }
				
		        else if (price < (e.Ask + e.Bid) / 2)
		        {
		            RecordTrade(sellsAtBar, price, volume, e);
		            RecordTrade(buysAtBar, price, 0, e);
		        }
				
			}
			
		    if (State == State.Realtime && e.MarketDataType == MarketDataType.Last)
		    {
		        double price = e.Price;
		        double volume = e.Volume;
				lowSpread = false;
				
				if(e.Ask - e.Bid == 0.25){
					lowSpread = true;
				}
		      
		        if (price > (e.Ask + e.Bid) / 2)
		        {
		            RecordTrade(buysAtBar, price, volume, e);
		            RecordTrade(sellsAtBar, price, 0, e);
		        }
		        else if (price < (e.Ask + e.Bid) / 2)
		        {
		            RecordTrade(sellsAtBar, price, volume, e);
		            RecordTrade(buysAtBar, price, 0, e);
		        }
				
				
		       
		        UpdateTotalBuysAndSells(Close[0]);
		    }
		}
		
		#endregion

		#region Machine Learning Functions
			int curwindowid = 0;
			HashSet<double> sampledLevels = new HashSet<double>(); // HashSet to track sampled levels
			
			private void InitializeTradeParams()
			{
			    DateTime currentTime = Time[0];
			    DateTime tradeWindowEndTime = currentTime.AddMinutes(tradesWindowMinutes);
			
			    if (initialLetters == "NQ")
			    {
			        foreach (var kvp in aggregatedBuys)
			        {
			            double price = kvp.Key;
			
			            if (sampledLevels.Contains(price))
			            {
			                continue;
			            }
			
			            double buyVolume = kvp.Value;
			            double sellVolume = aggregatedSells.ContainsKey(price) ? aggregatedSells[price] : 0;
			            double tradeRatio;
			
			            if (buyVolume > sellVolume)
			            {
			                tradeRatio = sellVolume > 0 ? (double)buyVolume / sellVolume : buyVolume;
			            }
			            else
			            {
			                tradeRatio = buyVolume > 0 ? (double)sellVolume / buyVolume : sellVolume;
			            }
			
			            if (incTrain ? (Math.Abs(buyVolume - sellVolume) > incVol && Math.Min(buyVolume, sellVolume) > incDet && tradeRatio > incRatio) : (Math.Abs(buyVolume - sellVolume) > minVolume && Math.Min(buyVolume, sellVolume) > detectionValue && tradeRatio > ratio))
			            {
			                string direction = buyVolume > sellVolume ? (isTrendMode ? "Long" : "Short") : (isTrendMode ? "Short" : "Long");
			                TradeParameters tradeParams = new TradeParameters(Math.Abs(buyVolume - sellVolume), Math.Min(buyVolume, sellVolume), tradeRatio, direction, tradeWindowEndTime);
			                tradeParamsList.Add(tradeParams);
			                if (!incTrain)
			                {
			                    Print($"Initialized trade parameters: ImbVol: {Math.Abs(buyVolume - sellVolume)}, AdvDetection: {Math.Min(buyVolume, sellVolume)}, Ratio: {tradeRatio}, Direction: {direction}");
			                }
			                sampledLevels.Add(price);
			            }
			        }
			    }
			    else if (initialLetters == "ES")
			    {
			        foreach (var kvp in buysAtBar)
			        {
			            double price = kvp.Key;
			
			            if (sampledLevels.Contains(price))
			            {
			                continue;
			            }
			
			            double buyVolume = kvp.Value;
			            double sellVolume = sellsAtBar.ContainsKey(price - TickSize) ? sellsAtBar[price - TickSize] : 0;
			            double tradeRatio = buyVolume > sellVolume ? (sellVolume > 0 ? buyVolume / sellVolume : buyVolume) : (buyVolume > 0 ? sellVolume / buyVolume : sellVolume);
			
			            if (incTrain ? (Math.Abs(buyVolume - sellVolume) > incVol && Math.Min(buyVolume, sellVolume) > incDet && tradeRatio > incRatio) : (Math.Abs(buyVolume - sellVolume) > minVolume && Math.Min(buyVolume, sellVolume) > detectionValue && tradeRatio > ratio))
			            {
			                string direction = buyVolume > sellVolume ? (isTrendMode ? "Long" : "Short") : (isTrendMode ? "Short" : "Long");
			                TradeParameters tradeParams = new TradeParameters((int)Math.Ceiling((double)Math.Abs(buyVolume - sellVolume) / levelstotrade), (int)Math.Ceiling((double)Math.Min(buyVolume, sellVolume) / levelstotrade), tradeRatio, direction, tradeWindowEndTime);
			                tradeParamsList.Add(tradeParams);
			                if (!incTrain)
			                {
			                    Print($"Initialized trade parameters: ImbVol: {Math.Abs(buyVolume - sellVolume)}, AdvDetection: {Math.Min(buyVolume, sellVolume)}, Ratio: {tradeRatio}");
			                }
			                sampledLevels.Add(price);
			
			                // Stop after finding the first valid imbalance
			                break;
			            }
			        }
			    }
			}

			
			private Dictionary<double, double> AggregateVolumesIntoGroups(Dictionary<double, double> volumes, double barLow, double barHigh)
			{
			    Dictionary<double, double> aggregatedVolumes = new Dictionary<double, double>();
			
			    double segmentSize = levelstotrade *  TickSize; // Assuming you want to aggregate into segments of 4 ticks
			
			    foreach (var kvp in volumes)
			    {
			        double price = kvp.Key;
			        double volume = kvp.Value;
			
			        // Ensure that the price is within the bar's range
			        if (price >= barLow && price <= barHigh)
			        {
			            int segmentIndex = (int)((price - barLow) / segmentSize);
			            double pointKey = barLow + segmentIndex * segmentSize;
			
			            // Update the aggregated volume for the corresponding segment
			            if (aggregatedVolumes.ContainsKey(pointKey))
			            {
			                aggregatedVolumes[pointKey] += volume;
			            }
			            else
			            {
			                aggregatedVolumes.Add(pointKey, volume);
			            }
			        }
			    }
			
			    return aggregatedVolumes;
			}
			
			private void UpdateDeltaValues(double newDelta)
			{
			    // Shift values in the list
			    deltaValues[2] = deltaValues[1];
			    deltaValues[1] = deltaValues[0];
			    deltaValues[0] = newDelta;
			}

			private void SimulateTrade(TradeParameters tradeParams, string direction)
			{
			    double entryPrice = Close[0];  // Use the current close price as the entry price
				if(!incTrain)
				{
			    Print($"Simulating trade with parameters: ImbVol: {tradeParams.ImbVolThreshold}, AdvDetection: {tradeParams.AdvDetectionThreshold}, Ratio: {tradeParams.RatioThreshold}, Direction: {direction}");
				}
			    double delta = buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
			    
			    // Update the delta values
			    UpdateDeltaValues(delta);
			
			    SimTrade newTrade = new SimTrade(
			        tradeParams.ImbVolThreshold,
			        tradeParams.AdvDetectionThreshold,
			        tradeParams.RatioThreshold,
			        entryPrice,
			        direction,
					PATIMachineLearningInputsV2().VolumeSpeedPerSecond[1],
					PATIMachineLearningInputsV2().BollingerDiff[1],
					PATIMachineLearningInputsV2().MovingAvgDiff[1],
					PATIMachineLearningInputsV2().TimeOfDay[1],
					PATIMachineLearningInputsV2().StdDevBB[1]
					
				
			    )
			    {
			        WindowId = currentWindowId
			    };
			
			    tradeParams.Trades.Add(newTrade);
			    simTrades.Add(newTrade);
			
//			    Print($"Simulated trade created. Direction: {newTrade.Direction}, ImbVol: {newTrade.ImbVol}, AdvDetection: {newTrade.AdvDetection}, Ratio: {newTrade.Ratio}, EntryPrice: {newTrade.EntryPrice}, Delta: {newTrade.Delta}");
			}

	
			private void UpdateSimTrades(double target, double stopLoss)
			{
				
			    //Print("Updating simulated trades...");
			    foreach (SimTrade trade in simTrades)
			    {
			        if (trade.WindowId != currentWindowId || trade.Status != null) continue;
			
			        double entryPrice = trade.EntryPrice;
			        double currentPrice = Close[0];
			        string positionType = trade.Direction;
			        bool tradeUpdated = false;
			
			        if (positionType == "Long")
			        {
			            if (currentPrice >= entryPrice + (target * TickSize))
			            {
			                trade.Status = "Target Hit";
			                trade.WinCount++;
			                tradeUpdated = true;
			                trade.IsCompleted = true;
							if(!incTrain)
								{
			                Print($"Trade target hit. EntryPrice: {entryPrice}, CurrentPrice: {currentPrice}");
								}
			            }
			            else if (currentPrice <= entryPrice - (stopLoss * TickSize))
			            {
			                trade.Status = "Stop Loss Hit";
			                trade.LossCount++;
			                tradeUpdated = true;
			                trade.IsCompleted = true;
							if(!incTrain)
								{
			                Print($"Trade stop loss hit. EntryPrice: {entryPrice}, CurrentPrice: {currentPrice}");
								}
			            }
			        }
			        else if (positionType == "Short")
			        {
			            if (currentPrice <= entryPrice - (target * TickSize))
			            {
			                trade.Status = "Target Hit";
			                trade.WinCount++;
			                tradeUpdated = true;
			                trade.IsCompleted = true;
							if(!incTrain)
								{
			                Print($"Trade target hit. EntryPrice: {entryPrice}, CurrentPrice: {currentPrice}");
								}
			            }
			            else if (currentPrice >= entryPrice + (stopLoss * TickSize))
			            {
			                trade.Status = "Stop Loss Hit";
			                trade.LossCount++;
			                tradeUpdated = true;
			                trade.IsCompleted = true;
							if(!incTrain)
								{
			                Print($"Trade stop loss hit. EntryPrice: {entryPrice}, CurrentPrice: {currentPrice}");
								}
			            }
			        }
			
			        if (tradeUpdated)
			        {
			            trade.TradeCount++;
			            trade.UpdateWinRate();
						if(!incTrain)
								{
			            Print($"Trade updated. Direction: {trade.Direction}, Status: {trade.Status}, WinRate: {trade.WinRate}");
								}
			        }
			    }
			
			   // Print("Simulated trades update complete.");
			}
	
			private void UpdateWinRateForTradeParams(TradeParameters tradeParams)
			{
			    int winCount = tradeParams.Trades.Count(trade => trade.Status == "Target Hit");
			    int lossCount = tradeParams.Trades.Count(trade => trade.Status == "Stop Loss Hit");
			    int totalTrades = winCount + lossCount;
			
			    double winRate = totalTrades > 0 ? (double)winCount / totalTrades : 0;
			
			    foreach (var trade in tradeParams.Trades)
			    {
			        trade.WinRate = winRate;
			    }
			
			    Print($"Win Rate for trade type (ImbVol: {tradeParams.ImbVolThreshold}, AdvDetection: {tradeParams.AdvDetectionThreshold}): {winRate:P2}");
			}

			
			private void WriteTradesToCsv(List<TradeParameters> completedTradeParams)
			{
			    string filePath = "";
			    if (incTrain)
			    {
			        filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\inctrain.csv";
			    }
			    else if (trainModel)
			    {
			        filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\train_model.csv";
			    }
			
			    try
			    {
			        string directory = Path.GetDirectoryName(filePath);
			        if (!Directory.Exists(directory))
			        {
			            Directory.CreateDirectory(directory);
			        }
			
			        using (StreamWriter writer = new StreamWriter(filePath, append: true))
			        {
			            if (new FileInfo(filePath).Length == 0)
			            {
			                writer.WriteLine("ImbalanceVolume,AdversaryDetection,Ratio,WinRate,VolumeSpeed,BollingerDiff,MovingAvgDiff,TimeOfDay,StdDevBB,Direction,Status");
			            }
			
			            foreach (var tradeParams in completedTradeParams)
			            {
			                foreach (var trade in tradeParams.Trades.Where(t => t.IsCompleted))
			                {
			                    writer.WriteLine($"{trade.ImbVol},{trade.AdvDetection},{trade.Ratio},{trade.WinRate},{trade.VolumeSpeed},{trade.BolDif},{trade.MADif},{trade.TOD},{trade.StdDev},{trade.Direction},{trade.Status}");
			                }
			            }
			        }
			    }
			    catch (Exception ex)
			    {
			        Print($"Error writing to CSV: {ex.Message}");
			    }
			}

	
			private void WriteCurrentPredictiveValuesToCsv()
			{
			    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\current_predictive_values.csv";
			
			    // Open or create the CSV file and overwrite any existing content
			    using (StreamWriter writer = new StreamWriter(filePath, false)) // false to overwrite existing content
			    {
			        // Write headers
			        writer.WriteLine("VolumeSpeed,BollingerDiff,MovingAvgDiff,TimeOfDay,StdDevBB");
			
			        // Get the current delta values
			        double currentDelta = deltaValues[0];

					double volVel = PATIMachineLearningInputsV2().VolumeSpeedPerSecond[1];
					double bd = PATIMachineLearningInputsV2().BollingerDiff[1];
					double mad = PATIMachineLearningInputsV2().MovingAvgDiff[1];
					double tod = PATIMachineLearningInputsV2().TimeOfDay[1];
					double std = PATIMachineLearningInputsV2().StdDevBB[1];
					
				
			        // Write the current values to the CSV file
			        writer.WriteLine($"{volVel},{bd},{mad},{tod},{std}");
			    }
			
			
			    //Print("Current predictive values written to CSV.");
			}
				int prevvol = 0;
				double prevratio = 0;
				int prevdet = 0;
			private void ReadOptimizedParamsFromCSV()
			{
			    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\optimized_params.csv"; // Update with the actual path to your CSV file
			
				
				
			    if (File.Exists(filePath))
			    {
			        try
			        {
			            using (StreamReader reader = new StreamReader(filePath))
			            {
			                string headerLine = reader.ReadLine(); // Read and ignore the header line
			                string line = reader.ReadLine(); // Read the second line with the actual values
			                if (line != null)
			                {
			                    string[] values = line.Split(',');
			
			                    // Assign each parameter based on its order in the CSV
			                 
			                    minVolume = int.Parse(values[0]);
			                	ratio = double.Parse(values[1]);
								detectionValue = int.Parse(values[2]);
							
								
//								if (minVolume < 25)
//								{
//								    minVolume = 25;
//								}
								
//								if(ratio < 1.2){
//									ratio = 1.2;
//								}
	
								
//								if(askDetection < 5){
//									askDetection = 5;
//								}
								
//								if(bidDetection < 5){
//									bidDetection = 5;
//								}
			                    // Print or use these variables as needed
								if(minVolume != prevvol || prevratio != ratio || prevdet != detectionValue) {
									
			                   // Print("Optimized parameters loaded from CSV:");
			                    Print($"MinVolume: {minVolume}, Ratio: {ratio}, AdvDet:  {detectionValue}");
								prevvol = minVolume;
								prevratio = ratio;
								prevdet = detectionValue;
								}
			                    // Here you can use these parameters as needed, for example:
			                    // SetOptimizedParams(depth, detection_value, iterations, learning_rate, minbvelocity, minsvelocity, minvolume, normal_ratio);
			                }
			            }
			        }
			        catch (Exception ex)
			        {
			            Print("Error reading optimized parameters from CSV: " + ex.Message);
			        }
			    }
			    else
			    {
			        Print("Optimized parameters CSV file not found.");
			    }
			}

		#endregion
		
		#region Trading Functions
			
		private void RecordTrade(Dictionary<double, double> priceDictionary, double price, double volume, MarketDataEventArgs e)
		{
		    if (priceDictionary.ContainsKey(price))
		    {
		        priceDictionary[price] += volume;
		    }
		    else
		    {
		        priceDictionary.Add(price, volume);
		    }
		
		    //Print($"Recorded trade at price {price}. Total volume at this price: {priceDictionary[price]}");
		}
		
		private void UpdateTotalBuysAndSells(double price)
		{
			if(SelectedCalculationMethod == CalculationMethod.NQ)
			{
		    CheckAndEnterLong(price);  // Check upwards
		    CheckAndEnterShort(price); // Check downwards
			}
			
			if(SelectedCalculationMethod == CalculationMethod.ES)
			{
		    CheckAndEnterLongES(price);  // Check upwards
		    CheckAndEnterShortES(price); // Check downwards
			}
			
		}
		
		private void CheckAndEnterLong(double price)
		{
		    double cumulativeBuys = 0;
		    double cumulativeSells = 0;
		    int validPriceLevels = 0;
		
		    // Define the range of levels to check
		    int levelsToCheck = levelstotrade > 1 ? levelstotrade / 2 : 1;
		    int startLevel = -levelsToCheck;
		    int endLevel = levelsToCheck;
		
		    // Access and sum the values within the range
		    for (int i = startLevel; i < endLevel; i++)
		    {
		        double priceToCheck = price + i * TickSize;
		        
		        // Check if the price level exists in both dictionaries
		        if (buysAtBar.TryGetValue(priceToCheck, out double buys) && sellsAtBar.TryGetValue(priceToCheck, out double sells))
		        {
		            cumulativeBuys += buys;
		            cumulativeSells += sells;
		            validPriceLevels++;
		        }
		    }
		
		    // Ensure we have exactly the required number of levels
		    if (validPriceLevels != levelstotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
		
		    double buyRatio = cumulativeBuys / cumulativeSells;
			
		    if (isLongMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && cumulativeBuys - cumulativeSells >= minVolume && buyRatio > ratio && cumulativeSells >= (MLOn ? detectionValue : detectionValue) && lowSpread)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Sell, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		            else if (isTrendMode && cumulativeBuys - cumulativeSells >= minVolume && buyRatio > ratio && cumulativeSells >= (MLOn ? detectionValue : detectionValue) && lowSpread)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		        }
		    }
		}

		private void CheckAndEnterShort(double price)
		{
		    double cumulativeBuys = 0;
		    double cumulativeSells = 0;
		    int validPriceLevels = 0;
		
		    // Define the range of levels to check
		    int levelsToCheck = levelstotrade > 1 ? levelstotrade / 2 : 1;
		    int startLevel = -levelsToCheck;
		    int endLevel = levelsToCheck;
		
		    // Access and sum the values within the range
		    for (int i = startLevel; i < endLevel; i++)
		    {
		        double priceToCheck = price + i * TickSize;
		        
		        // Check if the price level exists in both dictionaries
		        if (buysAtBar.TryGetValue(priceToCheck, out double buys) && sellsAtBar.TryGetValue(priceToCheck, out double sells))
		        {
		            cumulativeBuys += buys;
		            cumulativeSells += sells;
		            validPriceLevels++;
		        }
		    }
		
		    // Ensure we have exactly the required number of levels
		    if (validPriceLevels != levelstotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
		
		    double sellRatio = cumulativeSells / cumulativeBuys;
		
		    if (isShortMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && cumulativeSells - cumulativeBuys >= minVolume && sellRatio > ratio && cumulativeBuys >= (MLOn ? detectionValue : detectionValue) && lowSpread)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		            else if (isTrendMode && cumulativeSells - cumulativeBuys >= minVolume && sellRatio > ratio && cumulativeBuys >= (MLOn ? detectionValue : detectionValue) && lowSpread)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Sell, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		        }
		    }
		}
		
		private void CheckAndEnterLongES(double price)
		{
		    int validPriceLevels = 0;
		
		    // Define the range of levels to check
		    int levelsToCheck = levelstotrade;
		
		    // Access and sum the values within the range
		    for (int i = 0; i < levelsToCheck; i++)
		    {
		        double priceToCheck = price - i * TickSize;
		        
		        // Check if the price level exists in both dictionaries
		        if (buysAtBar.TryGetValue(priceToCheck, out double buys) && sellsAtBar.TryGetValue(priceToCheck - TickSize, out double sells))
		        {
					if (buys - sells >= minVolume && buys/sells > ratio && sells >= (MLOn ? detectionValue : detectionValue))
					{
		            validPriceLevels++;
					}
		        }
		    }
		
		    // Ensure we have exactly the required number of levels
		    if (validPriceLevels != levelstotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
			
		    if (isLongMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && validPriceLevels == levelstotrade)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Sell, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		            else if (isTrendMode && validPriceLevels == levelstotrade)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		        }
		    }
		}
		
		private void CheckAndEnterShortES(double price)
		{
		    int validPriceLevels = 0;
		
		    // Define the range of levels to check
		    int levelsToCheck = levelstotrade;
		
		    // Access and sum the values within the range
		    for (int i = 0; i < levelsToCheck; i++)
		    {
		        double priceToCheck = price + i * TickSize;
		        
		        // Check if the price level exists in both dictionaries
		        if (buysAtBar.TryGetValue(priceToCheck  + TickSize, out double buys) && sellsAtBar.TryGetValue(priceToCheck, out double sells))
		        {
					if(sells - buys >= minVolume && sells/buys > ratio && buys >= (MLOn ? detectionValue : detectionValue)){
			
		            validPriceLevels++;
						
					}
		        }
		    }
		
		    // Ensure we have exactly the required number of levels
		    if (validPriceLevels != levelstotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
		
		    if (isShortMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && validPriceLevels == levelstotrade)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		            else if (isTrendMode && validPriceLevels == levelstotrade)
		            {
		                tradeTaken = true;
		
		                #region ATMStrat
		
		                isAtmStrategyCreated = false;  // reset atm strategy created check to false
		                atmStrategyId = GetAtmStrategyUniqueId();
		                orderId = GetAtmStrategyUniqueId();
		                AtmStrategyCreate(OrderAction.Sell, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
		                {
		                    // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
		                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
		                    {
		                        isAtmStrategyCreated = true;
		                    }
		                });
		
		                #endregion
						resetButtons();
		            }
		        }
		    }
		}
		
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
//							Print("The current ATM Strategy market position is: " + GetAtmStrategyMarketPosition(atmStrategyId));
//							Print("The current ATM Strategy position quantity is: " + GetAtmStrategyPositionQuantity(atmStrategyId));
//							Print("The current ATM Strategy average price is: " + GetAtmStrategyPositionAveragePrice(atmStrategyId));
//							Print("The current ATM Strategy Unrealized PnL is: " + GetAtmStrategyUnrealizedProfitLoss(atmStrategyId));
//							 Print("PnL is " + GetAtmStrategyRealizedProfitLoss(atmStrategyId));
			});
		}

		
		private void UpdateHighsAndLows()
		{
		    // Detect Highs
		    double high = Swing(strength).SwingHigh[0]; // Swing indicator with a sensitivity of 3 and offset of 2 ticks
			int highBar = Swing(strength).SwingHighBar(0, 1, 50);
		    Draw.Line(this, "HighLine", highBar, high - offset * TickSize, -100, high - offset  * TickSize,  Brushes.Red); // Offset of 2 ticks
		
		    // Save the recent high price
		    recentHighPrice = high;
		
			
		    // Detect Lows
		    double low = Swing(strength).SwingLow[0]; // Swing indicator with a sensitivity of 3 and offset of 2 tick
			int lowBar = Swing(strength).SwingLowBar(0, 1, 50);
		    Draw.Line(this, "LowLine", lowBar, low + offset * TickSize, -100, low + offset  * TickSize, Brushes.Green); // Offset of 2 ticks
		
		    // Save the recent low price
		    recentLowPrice = low;
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
			
				Print(isLongMode);
				Print(isShortMode);
				Print(isAutoArm);
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
				Print(isLongMode);
				Print(isShortMode);
				Print(isAutoArm);
			
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
				Print("trend - " + isTrendMode);
				
		    }
		    else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
		    {
		   		isRegressionMode = false;
				isTrendMode = true;
				modeButton.Content = "Trend";
				modeButton.Background = Brushes.Purple;
					Print("regression - " + isRegressionMode);
				Print("trend - " + isTrendMode);
		    }
		
		    // Update the button content or perform any other necessary actions
			
		    
		}
			
		#endregion

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
		public double ratio
		{ get; set; }
		
		
		[NinjaScriptProperty]
		[Display(Name="Adversary Detection ", Order=4, GroupName="Imbalances")]
		public int detectionValue
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Calculation Method", Order=5, GroupName="Imbalances")]
		public CalculationMethod SelectedCalculationMethod { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="tick levels to trade", Order=5, GroupName="Imbalances")]
		public int levelstotrade { get; set; }
		
		
		[NinjaScriptProperty]
		[Display(Name="Strength", Order=8, GroupName="Swings")]
		public int strength
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Offset", Order=9, GroupName="Swings")]
		public int offset
		{ get; set; }
		
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
		[Display( Name = "Allow incremental learning", GroupName = "Machine Learning Incremental Training", Order = 0)]
		public bool incTrain
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Min Volume for inc training", GroupName = "Machine Learning Incremental Training", Order = 0)]
		public double incVol
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Min Ratio for inc training", GroupName = "Machine Learning Incremental Training", Order = 0)]
		public double incRatio
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Min Detection for inc training", GroupName = "Machine Learning Incremental Training", Order = 0)]
		public double incDet
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Sample Interval (seconds)", GroupName = "Machine Learning", Order = 0)]
		public double sampleInterval
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Trade window (minutes)", GroupName = "Machine Learning", Order = 0)]
		public int tradesWindowMinutes
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Use regression training data", GroupName = "Machine Learning", Order = 0)]
		public bool regressForTrain
		{ get; set; } 
		
		
		
		
		
	
		#endregion

	}
}
