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
	#region Classes
	
	public class HttpClientWrapper
	{
		private static readonly HttpClient client = new HttpClient();
		private const string BaseUrl = "http://192.168.1.124:5000"; // Your server address
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
		
	public class SimTrade
	{
		public double ImbVol { get; set; }
		public double AdvDetection { get; set; }
		public double Ratio  { get; set; }
		public double EntryPrice { get; set; }
		public string Direction { get; set; }
		public string Status { get; set; }
		public double WinRate { get; set; }
		public double TradeCount;
		public double TradeExpectancy;
		public string ImbalanceType { get; set; }
		public int WindowId { get; set; }

		public double VolumeSpeed { get; set; }
		public double PriceSpeed { get; set; }
		public double BolDif { get; set; }
		public double MADif { get; set; }
		public double TOD { get; set; }
		public double StdDev { get; set; }
		public double PriceDistance { get; set; }
		public double PriceDifference { get; set; }
		public double AverageBarVol { get; set; }
		
		
		public int WinCount { get; set; } = 0;
		public int LossCount { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;

		public double PF { get; set; }
		
		
		
		
		public SimTrade(double imbVol, double advDetection, double ratio, double entryPrice, string direction, double volumeSpeed, double maDif, double bolDif, double stdDeviation, double tod, double priceDistance, double averageBarVol )
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
			PriceDistance =priceDistance;
			AverageBarVol = averageBarVol;
		}
		
	}

	public enum TradeType{
		Regress,
		Trend
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
		public bool allowInTrade { get; set; } = true;
		public TradeType TradeType  { get; set; }
		
	    public TradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, DateTime tradeWindowEndTime, TradeType tradeType)
	    {
	        ImbVolThreshold = imbVolThreshold;
	        AdvDetectionThreshold = advDetectionThreshold;
	        RatioThreshold = ratioThreshold;
	        TradeWindowEndTime = tradeWindowEndTime; // Initialize the property
			TradeType = tradeType;
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
		double priceSpeed;
		double priceDistance;
		double priceDistance1Lag;
		double priceDistanceRatio;
		double priceUp;
		double priceDown;
		double R2;
		
	    double deltaAbove = 0;
		double deltaBelow = 0;
		double deltaAtPrice = 0;
		double totalDelta = 0;
		double thePrice = 0;
		double delta = 0;
		
		double positionOffHigh = 0;
		double positionOffLow = 0;
		double deltaOffHigh = 0;
		double deltaOffLow = 0;

	    private const int MaxHistoricalBars = 1; // Adjust this value as needed
		
		private List<SimTrade> simTrades = new List<SimTrade>();
	    private DateTime lastSampleTime = DateTime.MinValue;
		private List<SimTrade> initialSimTrades = new List<SimTrade>();
		private DateTime lastDay = DateTime.MinValue;
		
		private DateTime predValueWrite = DateTime.MinValue;

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

		double lastClose = 0;
		double dailyBars = 0;
		double avgBarVolume = 0;
		protected override void OnBarUpdate()
		{ 

			if(StrategyStartTime == null){
				StrategyStartTime = Time[0];
			}

		    if (CurrentBar != activeBar)
		    {
				activeBar = CurrentBar;
				
				prevDelta=buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
				
				UpdateHighsAndLows();
				
				dailyBars++;
				
				buysAtBar.Clear();
		        sellsAtBar.Clear();
				tradeTaken =false;
				highOfBar = 0;
				lowOfBar = 99999999;
				priceUp = 0;
				priceDown = 0;
				priceDistance = 0;
				priceDifference = 0;
				priceDistanceRatio = 0;
				priceSpeed = 0;
		    }
			
				// If we're at the first bar or currentDay hasn't been set yet, initialize it
		    if (currentDay == DateTime.MinValue)
		    {
		        currentDay = Time[0].Date;  // set currentDay to the date of the first bar
		        dailyVolume = 0;            // start with zero volume for the day
				dailyBars = 0;
		    }
		
		    // Check if a new day has started
		    if (Time[0].Date > currentDay)
		    {
		        // New day detected: reset dailyVolume and update currentDay
		        dailyVolume = 0;
		        currentDay = Time[0].Date;
		    }
		
		    // Check if current bar time is after startOfDay and same day
		    if (ToTime(Time[0].ToUniversalTime()) > startOfDay)
		    {
		        // Add this bar's volume to dailyVolume
		        dailyVolume += Volume[0];
		    }
				
			avgBarVolume = dailyVolume / dailyBars;
			// Update simulated trades and check for target or stop loss
			
		}
						
		double lowOfBar = 999999999;
		double highOfBar = 0;
		double priceDifference;
		
		private void ProcessTradeParams(MarketDataEventArgs e)
	    {
	       var completedTradeParams = new List<TradeParameters>();
						
			for (int i = tradeParamsList.Count - 1; i >= 0; i--)
			{
				var tradeParams = tradeParamsList[i];
				
				if (e.Time > tradeParams.TradeWindowEndTime)
				{
					UpdateWinRateForTradeParams(tradeParams, ProfitTarget, StopLoss);
					completedTradeParams.Add(tradeParams);
				}
			}
			
			tradeParamsList.RemoveAll(tradeParams => e.Time > tradeParams.TradeWindowEndTime);
			
			if (completedTradeParams.Any())
			{
				WriteTradesToCsv(completedTradeParams);
			}
	    }
		
		double startOfDay = 143000;

		// Variables declared at class level
		private double dailyVolume = 0;
		private DateTime currentDay = DateTime.MinValue; // Will store the current day's date
		bool pastTime = false;
		DateTime currentTime;
		double price = 0;
		double lastSampleBar = 0;

		protected override void OnMarketData(MarketDataEventArgs e)
		{
//			if ((State == State.Historical && trainModel) || (State == State.Realtime))
			{
			
				if (e.MarketDataType == MarketDataType.Last)
				{
					currentTime = e.Time;
					price = e.Price;
					double volume = e.Volume;
					
					ProcessTradeParams(e);
				
					pastTime = e.Time - lastSampleTime > TimeSpan.FromSeconds(sampleInterval);
								
					if(pastTime)
					{
						lastSampleTime = e.Time;
						
						if(CurrentBar > lastSampleBar){
							InitializeTradeParams();
							lastSampleBar = CurrentBar;
						}
						
						if(State==State.Realtime && Optimise && MLOn)
						{
							
						  WriteCurrentPredictiveValuesToServer();
						  ReadOptimizedParamsFromServer();
							
						}
							
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
					
					if(Math.Abs(e.Ask - e.Bid) <= 0.5){
						lowSpread = true;
					}
					
					delta = buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
				

					if(price > lastPrice){
						priceUp++;
					}
					if(price < lastPrice){
						priceDown++;
					}
					lastPrice = price;
					
					priceDistance = priceUp + priceDown;
					priceDifference = priceUp - priceDown;
					
					if(CurrentBar >= 2){
					TimeSpan t = Time[1]- Time[2];
					priceSpeed = (Math.Abs(Close[1] - Close[2]) * 4 * TickSize) / (t.TotalSeconds > 0 ? t.TotalSeconds : 1);
						
					} else{
						priceSpeed = 0;
					}
		
					UpdateTotalBuysAndSells(e.Price);
					
					UpdateSimTrades(ProfitTarget, StopLoss, e.Price);
					
					
				}
				
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
		
					
					if (incTrain || aggregate || trainModel)
					{
						aggregatedBuys = AggregateVolumesIntoGroups(buysAtBar, lowOfBar, highOfBar);
						aggregatedSells = AggregateVolumesIntoGroups(sellsAtBar, lowOfBar, highOfBar);
						
					}
					
					if (trainModel || incTrain)
					{
						if (CurrentBar < 3) return;
						
						if (incTrain && State != State.Realtime)
							return;
						
						#region string reader
						string symbol = Instrument.FullName;
						
						int index = 0;
						while (index < symbol.Length && char.IsLetter(symbol[index]))
						{
							index++;
						}
						
						initialLetters = symbol.Substring(0, index);
						
						DateTime currentTime = e.Time;
						
						if (e.Time - lastDay > TimeSpan.FromHours(1))
						{
							Print("Current Date:" + Time[0]);
							lastDay = Time[0];
						}
						#endregion
						
						double closePrice = e.Price;
					
						if (initialLetters == "NQ")
						{
							double FindClosestKey(Dictionary<double, double> dict, double targetPrice)
							{
								return dict.Keys.OrderBy(key => Math.Abs(key - targetPrice)).FirstOrDefault();
							};
							
							double closestPrice = FindClosestKey(aggregatedBuys, closePrice);
							
							double buyVolume = aggregatedBuys.ContainsKey(closestPrice) ? aggregatedBuys[closestPrice] : 0;
							double sellVolume = aggregatedSells.ContainsKey(closestPrice) ? aggregatedSells[closestPrice] : 0;
							
							
							double imbVol = Math.Abs(buyVolume - sellVolume);
							double advDetection = Math.Min(buyVolume, sellVolume);
							double tradeRatio = 0;
							
							string direction = "";
							
							if (buyVolume > sellVolume)
							{
							    tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
							    
							    // If EITHER condition is true => "Long", otherwise => "Short"
							    direction = ((modelToTrain == TradingMode.Trend && trainModel) 
							                 || (isTrendMode && incTrain))
							                ? "Long" 
							                : "Short";
							}
							else if (sellVolume > buyVolume)
							{
							    tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
							    
							    // If EITHER condition is true => "Short", otherwise => "Long"
							    direction = ((modelToTrain == TradingMode.Trend && trainModel) 
							                 || (isTrendMode && incTrain))
							                ? "Short" 
							                : "Long";
							}
							
							var activeTrades = tradeParamsList.Where(tp => tp.IsActive && tp.TradeWindowEndTime > currentTime);
							
							foreach (var tradeParams in activeTrades)
							{
								if (advDetection > tradeParams.AdvDetectionThreshold && imbVol > tradeParams.ImbVolThreshold && tradeRatio > tradeParams.RatioThreshold && tradeParams.allowInTrade == true)
								{
									SimulateTrade(tradeParams, direction, closePrice);
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
		}
		
		private const int RectangleWidth = 50; // Width of the volume profile visualization
    	private const double MaxDeltaPercentage = 0.05; // Maximum delta as a percentage of chart height
    	private const int RightPadding = 60; // Padding from the right edge of the chart

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
		    base.OnRender(chartControl, chartScale);
		
		    // Create brushes
		    SharpDX.Direct2D1.SolidColorBrush positiveBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DarkTurquoise);
		    SharpDX.Direct2D1.SolidColorBrush negativeBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.DarkOrange);
		    SharpDX.Direct2D1.SolidColorBrush textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
		    //SharpDX.Direct2D1.SolidColorBrush centerLineBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Gray);
		
		    var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 12)
		    {
		        TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
		        ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
		    };
		
		    var metricsFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 12)
		    {
		        TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
		        ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near
		    };
		
		    // Render delta metrics
		    float metricsX = 10;
		    float metricsY = 10;
		
			
			 RenderTarget.DrawText($"Min Volume: {minVolume:F0}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY, 200, 20), 
		        textBrush);
		
	         RenderTarget.DrawText($"Min Ratio: {ratio:F2}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 20, 200, 20), 
		        textBrush);
		
		    RenderTarget.DrawText($"Adversary Detection: {detectionValue:F0}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 40, 200, 20), 
		        textBrush);
		
		    // Calculate the maximum width of the rectangles
		    float maxWidth = (float)(chartControl.ActualWidth * MaxDeltaPercentage);
		
		    // Calculate the starting X position for the rectangles
		    float rightEdge = (float)chartControl.ActualWidth;
		    float startX = rightEdge - RightPadding;
		    float centerX = startX - maxWidth / 2;
		
		
		    // Dispose of resources
		    positiveBrush.Dispose();
		    negativeBrush.Dispose();
		    textBrush.Dispose();
		    //centerLineBrush.Dispose();
		    textFormat.Dispose();
		    metricsFormat.Dispose();
		}
		#endregion

		#region Machine Learning Functions
			int curwindowid = 0;
			HashSet<double> sampledLevels = new HashSet<double>(); // HashSet to track sampled levels
			
			private void InitializeTradeParams()
			{
			    DateTime tradeWindowEndTime = currentTime.AddSeconds(tradesWindowMinutes);
			
			    if (initialLetters == "NQ")
				{
					
				    foreach (var kvp in aggregatedBuys)
				    {
						
				        double price = kvp.Key;
				        double buyVolume = kvp.Value;
				        double sellVolume = aggregatedSells.ContainsKey(price) ? aggregatedSells[price] : 0;
				        double tradeRatio = 0;
				        string direction = "";
				        
				        // Debug prints for volumes and ratio
				       
				
				        if (buyVolume > sellVolume)
						{
						    tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
						    
						    // If EITHER condition is true => "Long", otherwise => "Short"
						    direction = ((modelToTrain == TradingMode.Trend && trainModel) 
						                 || (isTrendMode && incTrain))
						                ? "Long" 
						                : "Short";
						}
						else if (sellVolume > buyVolume)
						{
						    tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
						    
						    // If EITHER condition is true => "Short", otherwise => "Long"
						    direction = ((modelToTrain == TradingMode.Trend && trainModel) 
						                 || (isTrendMode && incTrain))
						                ? "Short" 
						                : "Long";
						}
				
				        if (Math.Min(buyVolume, sellVolume) > detectionValueForML &&
				            Math.Abs(buyVolume - sellVolume) > imbalanceValueForML &&
				            tradeRatio > ratioValueForML)
				        {
							TradeType tradeType = TradeType.Trend;
							
							if(isTrendMode){
								tradeType = TradeType.Trend;
							}else if(isRegressionMode){
								tradeType = TradeType.Regress;
							}
				          
				            TradeParameters tradeParams = new TradeParameters(
				                Math.Abs(buyVolume - sellVolume),
				                Math.Min(buyVolume, sellVolume),
				                tradeRatio,
				                tradeWindowEndTime,
								tradeType
				            );
							
							Print( Math.Abs(buyVolume - sellVolume));
							Print( Math.Min(buyVolume, sellVolume));
							Print(tradeRatio);
							Print(tradeWindowEndTime);
							Print(currentTime);
				            tradeParamsList.Add(tradeParams);
				        }
				
				    }
				}
			    else if (initialLetters == "ES")
			    {
			        foreach (var kvp in buysAtBar)
			        {
			            double price = kvp.Key;
			            double buyVolume = kvp.Value;
			            double sellVolume = sellsAtBar.ContainsKey(price - TickSize) ? sellsAtBar[price - TickSize] : 0;
			            double tradeRatio = buyVolume > sellVolume ? (sellVolume > 0 ? buyVolume / sellVolume : buyVolume) : (buyVolume > 0 ? sellVolume / buyVolume : sellVolume);
					
			            if (Math.Min(buyVolume, sellVolume) > detectionValueForML)
			            {
			                TradeParameters tradeParams = new TradeParameters(Math.Abs(buyVolume - sellVolume), Math.Min(buyVolume, sellVolume), tradeRatio, tradeWindowEndTime, isRegressionMode ? TradeType.Regress : TradeType.Trend);
			                tradeParamsList.Add(tradeParams);
			                break;
			            }
			        }
			    }
			}

		private Dictionary<double, double> AggregateVolumesIntoGroups(Dictionary<double, double> volumes, double barLow, double barHigh)
		{
		    Dictionary<double, double> aggregatedVolumes = new Dictionary<double, double>();
		    int ticksPerSegment = 4; // Number of ticks in each segment
		    double tickSize = TickSize; // Tick size of the instrument
		    double segmentSize = ticksPerSegment * tickSize; // Size of each segment in price terms
		    
		    // Calculate the total number of ticks in the bar range, including both ends
		    int totalTicks = (int)Math.Round((barHigh - barLow) / tickSize) + 1;
		    
		    // Calculate the number of full segments
		    int fullSegments = (totalTicks + ticksPerSegment - 1) / ticksPerSegment;
		    
		    const double epsilon = 1e-6; // Small value to adjust for floating-point precision
		    
		    foreach (var kvp in volumes)
		    {
		        double price = kvp.Key;
		        double volume = kvp.Value;
		        
		        // Only consider prices within the current bar range, adjusted with epsilon
		        if (price >= barLow - epsilon && price <= barHigh + epsilon)
		        {
		            // Determine which segment the price belongs to
		            int segmentIndex = (int)Math.Floor((price - barLow) / segmentSize);
		            
		            // Ensure we don't exceed the number of segments
		            segmentIndex = Math.Min(segmentIndex, fullSegments - 1);
		            
		            double pointKey = barLow + segmentIndex * segmentSize;
		            if (aggregatedVolumes.ContainsKey(pointKey))
		                aggregatedVolumes[pointKey] += volume;
		            else
		                aggregatedVolumes[pointKey] = volume;
		        }
		    }
		    
		    return aggregatedVolumes;
		}
		
			private void SimulateTrade(TradeParameters tradeParams, string direction, double price)
			{
			    double entryPrice = price;
			    
			    SimTrade newTrade = new SimTrade(
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
					priceDistance,
					avgBarVolume
			    );
			
			    tradeParams.Trades.Add(newTrade);
			    tradeParams.allowInTrade = false;
			    simTrades.Add(newTrade);
			}

			private void UpdateSimTrades(double target, double stopLoss, double price)
			{
			    foreach (SimTrade trade in simTrades.Where(t => t.Status == null))
			    {
			        double entryPrice = trade.EntryPrice;
			        double currentPrice = price;
			        string positionType = trade.Direction;
			
			        if (positionType == "Long")
			        {
			            if (currentPrice >= entryPrice + (target * TickSize))
			            {
			                UpdateTradeStatus(trade, "Target Hit");
			            }
			            else if (currentPrice <= entryPrice - (stopLoss * TickSize))
			            {
			                UpdateTradeStatus(trade, "Stop Loss Hit");
			            }
			        }
			        else if (positionType == "Short")
			        {
			            if (currentPrice <= entryPrice - (target * TickSize))
			            {
			                UpdateTradeStatus(trade, "Target Hit");
			            }
			            else if (currentPrice >= entryPrice + (stopLoss * TickSize))
			            {
			                UpdateTradeStatus(trade, "Stop Loss Hit");
			            }
			        }
			    }
			
			    simTrades.RemoveAll(trade => trade.IsCompleted);
			}
			

			private void UpdateTradeStatus(SimTrade trade, string status)
			{
			    trade.Status = status;
			    trade.WinCount += (status == "Target Hit") ? 1 : 0;
			    trade.LossCount += (status == "Stop Loss Hit") ? 1 : 0;
				
			    trade.IsCompleted = true;
				
			    var tradeParams = tradeParamsList.FirstOrDefault(tp => tp.Trades.Contains(trade));
			    if (tradeParams != null)
			    {
			        tradeParams.allowInTrade = true;
			    }
			}

			private void UpdateWinRateForTradeParams(TradeParameters tradeParams, double target, double stopLoss)
			{
			    int winCount = tradeParams.Trades.Count(trade => trade.Status == "Target Hit");
			    int lossCount = tradeParams.Trades.Count(trade => trade.Status == "Stop Loss Hit");
			    double totalTrades = winCount + lossCount;
			    double winRate = totalTrades > 0 ? (double)winCount / totalTrades : 0;

			    foreach (var trade in tradeParams.Trades)
			    {
			        trade.WinRate = winRate;
			        trade.TradeCount = totalTrades;
		
			    }
			}
			
			private void WriteTradesToCsv(List<TradeParameters> completedTradeParams)
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
			        // ---------------------------------------------------
			        // 1) If trainModel == true, EVERYTHING goes to mainTrainPath
			        // ---------------------------------------------------
			        if (trainModel)
			        {
			            // We only need one file (mainTrainPath).
			            // We'll gather all completed trades (any TradeType) together.
			            WriteTradesToSingleFile(
			                completedTradeParams,
			                mainTrainPath
			            );
			            
			            // Clear after writing
			            completedTradeParams.Clear();
			            return;
			        }
			
			        // ---------------------------------------------------
			        // 2) If we reach here, then trainModel == false, but incTrain == true
			        //    => separate files for Regress vs. Trend
			        // ---------------------------------------------------
			        List<TradeParameters> regressList = completedTradeParams
			            .Where(tp => tp.TradeType == TradeType.Regress)
			            .ToList();
			
			        List<TradeParameters> trendList = completedTradeParams
			            .Where(tp => tp.TradeType == TradeType.Trend)
			            .ToList();
			
			        // Write Regress trades to inctrain_regression.csv
			        if (regressList.Count > 0)
			            WriteTradesToSingleFile(regressList, regressionIncrementalPath);
			
			        // Write Trend trades to inctrain_trend.csv
			        if (trendList.Count > 0)
			            WriteTradesToSingleFile(trendList, trendIncrementalPath);
			
			        // Clear after writing
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
			private void WriteTradesToSingleFile(List<TradeParameters> tradeParamsList, string filePath)
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
			                       && firstLine.StartsWith("WinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD,PriceDistance,AverageBarVol");
			    }
			
			    using (StreamWriter writer = new StreamWriter(filePath, append: true))
			    {
			        // If no header yet, write it once
			        if (!headerExists)
			        {
			            writer.WriteLine("WinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD,PriceDistance,AverageBarVol");
			            headerExists = true;
			        }
			
			        // Build CSV lines for each completed trade in each TradeParameters
			        StringBuilder sb = new StringBuilder();
			        foreach (var tradeParams in tradeParamsList)
			        {
						                 // Only write the *last* completed trade in each TradeParameters
			            var lastCompletedTrade = tradeParams.Trades
			                .LastOrDefault(t => t.IsCompleted);
			
			            if (lastCompletedTrade != null)
			            {
			                sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\n",
			                    Math.Round(lastCompletedTrade.WinRate, 2),
			                    lastCompletedTrade.ImbVol,
			                    Math.Round(lastCompletedTrade.Ratio, 2),
			                    lastCompletedTrade.AdvDetection,
			                    Math.Round(lastCompletedTrade.VolumeSpeed, 2),
			                    Math.Round(lastCompletedTrade.MADif, 2),
			                    Math.Round(lastCompletedTrade.BolDif, 2),
			                    Math.Round(lastCompletedTrade.StdDev, 2),
			                    Math.Round(lastCompletedTrade.TOD, 2),
			                    Math.Round(lastCompletedTrade.PriceDistance, 2),
			                    lastCompletedTrade.AverageBarVol
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
					PriceDistance = priceDistance,
					AverageBarVol = avgBarVolume,
					CurrentModel = (isRegressionMode && !isTrendMode) ? "Regression" : "Trend"
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
		            ratio = newRatio;
		            detectionValue = newDetectionValue;
		
		
		            Print($"MinVol: {minVolume}, Ratio: {ratio}, AdvDet: {detectionValue}");
		
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
		    // For Long trades
			CheckAndEnterTrade(price);

			}
			
			if(SelectedCalculationMethod == CalculationMethod.ES)
			{
		    CheckAndEnterLongES(price);  // Check upwards
		    CheckAndEnterShortES(price); // Check downwards
			}
			
		}
		double cumulativeBuys = 0, cumulativeSells = 0;
		private void CheckAndEnterTrade(double price)
		{
		
		   
		    int validPriceLevels = 0, validImb = 0;
		    bool normalImbalanceDetected = false;
		    bool significantZeroVolumeDetected = false;
			cumulativeBuys = 0 ;
			cumulativeSells = 0;
		    double startLevel = -levelstotrade / 2;
		    double endLevel = levelstotrade / 2;
		
		    for (int numImb = imbalancestotrade; numImb > 0 && validImb < imbalancestotrade; numImb--)
		    {
		        cumulativeBuys = cumulativeSells = 0;
		        validPriceLevels = 0;
		
		        if (aroundPrice)
		        {
		            for (double j = startLevel; j < endLevel; j++)
		            {
		                double priceToCheck = price + j * TickSize;
		
		                if (buysAtBar.TryGetValue(priceToCheck, out double buys) && sellsAtBar.TryGetValue(priceToCheck, out double sells))
		                {
		                    cumulativeBuys += buys;
		                    cumulativeSells += sells;
		                    validPriceLevels++;
		                }
		            }
		        }
		        if (aggregate)
		        {
		            double adjustedPrice = FindClosestKey(aggregatedBuys.Keys, price);
		
		            if (aggregatedBuys.TryGetValue(adjustedPrice, out double buys) && aggregatedSells.TryGetValue(adjustedPrice, out double sells))
		            {
		                cumulativeBuys = buys;
		                cumulativeSells = sells;
		                validPriceLevels++;
		            }
		        }
		
		        // Significant zero volume detection
		        if ((cumulativeBuys == 0 && cumulativeSells >= detectionValue) || (cumulativeSells == 0 && cumulativeBuys >= detectionValue))
		        {
		            significantZeroVolumeDetected = true;
		        }
		
		        bool isValidImbalance = false;
		        double ratio = 0;
		
		        if (cumulativeBuys > 0 && cumulativeSells > 0)
		        {
		            double rawRatio = cumulativeBuys / cumulativeSells;
		            ratio = rawRatio < 1 ? 1 / rawRatio : rawRatio;
		        }
		        else
		        {
		            ratio = double.PositiveInfinity;
		        }
		
		        if (isLongMode && cumulativeBuys > cumulativeSells)
		        {
		            isValidImbalance = true;
		        }
		        else if (isShortMode && cumulativeSells > cumulativeBuys)
		        {
		            isValidImbalance = true;
		        }
		
		        if (isValidImbalance &&
		            Math.Abs(cumulativeBuys - cumulativeSells) >= minVolume &&
		            Math.Min(cumulativeBuys, cumulativeSells) >= detectionValue &&
		            ratio >= this.ratio &&
		            (aggregate ? validPriceLevels == 1 : validPriceLevels == 4) )
		        {
		            validImb++;
		            normalImbalanceDetected = true;
		        }
		    }
			
			  if (orderId.Length > 0 || atmStrategyId.Length > 0 || tradeTaken || (!isLongMode && !isShortMode))
		        return;
			  
		    if (State == State.Realtime )
		    {
		   // VolumeProfileAnalysis vpAnalysis = AnalyzeVolumeProfile();
		
			bool isBuyImbalance = false;
		    bool isSellImbalance = false;
		
		    // Determine if there's a buy or sell imbalance (existing logic)
		    if (normalImbalanceDetected)
		    {
		        isBuyImbalance = cumulativeBuys > cumulativeSells;
		        isSellImbalance = cumulativeSells > cumulativeBuys;
		    }

		        if (isLongMode  && isBuyImbalance && isTrendMode  )
		        {
		              
		        tradeTaken = true;
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
		
		            resetButtons();
		         //Print($"Trend mode: Long signal detected with probability {trendLongProb:F2}");
		            
		        }
		        if (isShortMode  && isTrendMode  && isSellImbalance  )
		        {
		           
		               
		        tradeTaken = true;
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
		
		            resetButtons();
		                //Print($"Trend mode: Short signal detected with probability {trendShortProb:F2}");
		            
		        }
		        if (isLongMode && isBuyImbalance && isRegressionMode   )
		        {
		           
		               
		             tradeTaken = true;
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
		
		            resetButtons();
		             //Print($"Regression mode: Counter-trend Sell signal detected with probability {reverseLongProb:F2}");
		            
		        }
		      	if (isShortMode && isSellImbalance  && isRegressionMode   )
		        {
		              
		         	 tradeTaken = true;
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
		
		           	 resetButtons();
		             //Print($"Regression mode: Counter-trend Buy signal detected with probability {reverseShortProb:F2}");
		            
		        }
		  
		    }
		}
				
			// Helper method to find the closest key to a given price
		private double FindClosestKey(IEnumerable<double> keys, double price)
				{
				    // If the keys collection is empty, return the original price
				    if (!keys.Any())
				        return price;
				
				    // Find the key with the minimum absolute difference from the price
				    double closestKey = keys.OrderBy(k => Math.Abs(k - price)).First();
				
				    return closestKey;
				}

	
		private void CheckAndEnterLongES(double price)
		{
		    int validPriceLevels = 0;
		
		    // Define the range of levels to check
		    int levelsToCheck = imbalancestotrade;
		
		    // Access and sum the values within the range
		    for (int i = 0; i < levelsToCheck + (allowTickGap ? 1 : 0); i++)
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
		    if (validPriceLevels != imbalancestotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
			
		    if (isLongMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && validPriceLevels ==  imbalancestotrade)
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
		            else if (isTrendMode && validPriceLevels ==  imbalancestotrade)
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
		    int levelsToCheck = imbalancestotrade;
		
		    // Access and sum the values within the range
		    for (int i = 0; i < levelsToCheck + (allowTickGap ? 1 : 0); i++)
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
		    if (validPriceLevels != imbalancestotrade)
		    {
		        return; // Exit if not exactly the required levels
		    }
		
		    if (isShortMode && orderId.Length == 0 && atmStrategyId.Length == 0 && !tradeTaken)
		    {
		        if (State == State.Realtime)
		        {
		            if (isRegressionMode && validPriceLevels ==  imbalancestotrade)
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
		            else if (isTrendMode && validPriceLevels ==  imbalancestotrade)
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
		[Display(Name="Allow Gap for ES", Order=6, GroupName="Imbalances")]
		public bool allowTickGap
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Calculation Method", Order=7, GroupName="Imbalances")]
		public CalculationMethod SelectedCalculationMethod { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="tick levels to sum for one imbalance", Order=8, GroupName="Imbalances")]
		public int levelstotrade { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="number of imbalances to classify a trade", Order=9, GroupName="Imbalances")]
		public int imbalancestotrade { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="use aggregation for trades w tick stacking", Order=10, GroupName="Imbalances")]
		public bool aggregate { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="use points around price for trades w tick stacking", Order=11, GroupName="Imbalances")]
		public bool aroundPrice { get; set; }
		
		
		
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
		[Display(Name="Adversary Detection for training data", Order=5, GroupName="Machine Learning Training")]
		public int detectionValueForML
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Imbalance value for training data", Order=5, GroupName="Machine Learning Training")]
		public int imbalanceValueForML
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Ratio for training data", Order=5, GroupName="Machine Learning Training")]
		public double ratioValueForML
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display( Name = "Regression or Trend", GroupName = "Machine Learning Training Mode", Order = 0)]
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
		
		[NinjaScriptProperty]
		[Display( Name = "Use regression training data", GroupName = "Machine Learning", Order = 0)]
		public bool regressForTrain
		{ get; set; } 
		
		
		
		
		
	
		#endregion

	}
}