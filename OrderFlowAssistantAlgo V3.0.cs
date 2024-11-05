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
		public TradeType TradeType  { get; set; }
		public int WindowId { get; set; }

		public double VolumeSpeed { get; set; }
		public double BolDif { get; set; }
		public double MADif { get; set; }
		public double TOD { get; set; }
		public double StdDev { get; set; }
		public double PriceDistance { get; set; }
		public double PriceDifference { get; set; }
		
		public double OpenToLow { get; set; }
		public double OpenToHigh { get; set; }
		
		public double MomentumChange { get; set; }
		public double ADXChange {get; set;}
		
		public double R2 {get; set;}
		public double ChangeR2 {get; set;}
		
		public int WinCount { get; set; } = 0;
		public int LossCount { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;

		public double PF { get; set; }
		
		
		
		
		public SimTrade(double imbVol, double advDetection, double ratio, double entryPrice, string direction,  double priceDistance, double priceDifference, double adxChange, double momentumChange, double r2, double openToLow, double openToHigh)
		{
		    ImbVol = imbVol;
		    AdvDetection = advDetection;
			Ratio = ratio;
		    EntryPrice = entryPrice;
			Direction = direction;
			PriceDistance = priceDistance;
			PriceDifference = priceDifference;
			ADXChange = adxChange;
			MomentumChange = momentumChange;
			R2 = r2;
			OpenToLow = openToLow;
			OpenToHigh = openToHigh;
		}
		
		public void UpdateWinRate()
		{
		    int totalTrades = WinCount + LossCount;
			TradeCount = totalTrades;
		    WinRate = totalTrades > 0 ? (double)WinCount / totalTrades : 0;
		}
	}

	public enum TradeType{
		Regress,
		Trend
//		ReverseLong,
//		TrendLong,
//		ReverseShort,
//		TrendShort
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
	
	    public TradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, DateTime tradeWindowEndTime)
	    {
	        ImbVolThreshold = imbVolThreshold;
	        AdvDetectionThreshold = advDetectionThreshold;
	        RatioThreshold = ratioThreshold;
	        TradeWindowEndTime = tradeWindowEndTime; // Initialize the property
	    }
	}

	   public struct VolumeProfileLevel
		{
		    public double BuyVolume;
		    public double SellVolume;
		    public double Delta => BuyVolume - SellVolume;
		
		    public void AddBuyVolume(double volume)
		    {
		        BuyVolume += Math.Abs(volume);
		    }
		
		    public void AddSellVolume(double volume)
		    {
		        SellVolume += Math.Abs(volume);
		    }
		}

		 public struct VolumeProfileAnalysis
		{
		    public double TotalDelta;
		    public double MaxVolume;
		    public double PriceWithMaxVolume;
		    public Dictionary<double, VolumeProfileLevel> Profile;
		    public bool IsValid;
		    public double DeltaAbove; // Added property
		    public double DeltaBelow; // Added property
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
		
		  private Dictionary<double, VolumeProfileLevel> accumulatedVolumeProfile;
	    private Queue<Dictionary<double, VolumeProfileLevel>> historicalProfiles;

		private Dictionary<double, VolumeProfileLevel> currentBarVolumeProfile;

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
			Dictionary<double, double> aggregatedBuysPB;
			Dictionary<double, double> aggregatedSellsPB;
		
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
				accumulatedVolumeProfile = new Dictionary<double, VolumeProfileLevel>();
            	historicalProfiles = new Queue<Dictionary<double, VolumeProfileLevel>>();
		
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
		double entropyValue = 0;

		// With these lists
		private List<double> historicalIncreases = new List<double>();
		private List<double> historicalDecreases = new List<double>();
		
        // Bayesian parameters
        double priorPriceIncrease = 0.5; // P(H=1)
        double priorPriceDecrease = 0.5; // P(H=0)

        // Statistical parameters for likelihoods
        private double muIncrease = 0;
        private double sigmaIncrease = 1;
        private double muDecrease = 0;
        private double sigmaDecrease = 1;
		
		  private double muAskIncrease;
        private double sigmaAskIncrease;
        private double muAskDecrease;
        private double sigmaAskDecrease;
		
		  private double muBidIncrease;
        private double sigmaBidIncrease;
        private double muBidDecrease;
        private double sigmaBidDecrease;
		
		private double muRsiIncrease;
		private double sigmaRsiIncrease;
		private double muRsiDecrease;
		private double sigmaRsiDecrease;
		
		private double muAdxIncrease;
		private double sigmaAdxIncrease;
		private double muAdxDecrease;
		private double sigmaAdxDecrease;
		
		private double muMacdIncrease;
		private double sigmaMacdIncrease;
		private double muMacdDecrease;
		private double sigmaMacdDecrease;
		
		double lastClose = 0;
		
		double p_h1_d;
		double p_h0_d;
		
		protected override void OnBarUpdate()
		{ 

			if(StrategyStartTime == null){
				StrategyStartTime = Time[0];
			}

		    if (CurrentBar != activeBar )
		    {
				
				prevDelta=buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
				if(State==State.Realtime && Optimise && MLOn)
				{
				  WriteCurrentPredictiveValuesToServer();
				   ReadOptimizedParamsFromServer();
				}
				
				UpdateHighsAndLows();
		        activeBar = CurrentBar;

				
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
			
			if (Time[0] - lastSampleTime > TimeSpan.FromSeconds(sampleInterval))
	        {
	            lastSampleTime = Time[0];
	            InitializeTradeParams();
				
	        }
			
			bool priceIncreased = Close[0] >= lastClose + ( ProfitTarget* TickSize);
			
			if (Close[0] >= (lastClose + (ProfitTarget * TickSize)) || (Close[0] <= (lastClose  - (ProfitTarget * TickSize))))
            {
                // Update historical data
                UpdateHistoricalData(priceIncreased);
				lastClose = Close[0];
            }
						
                // Compute posterior probabilities
                double itotal = ComputeImbalance();
               var posteriors = CalculatePosteriors();

                p_h1_d = posteriors.Item1;
                p_h0_d = posteriors.Item2;
			
		
			// Update simulated trades and check for target or stop loss
			ProcessTradeParams();

		}
						
		double lowOfBar = 999999999;
		double highOfBar = 0;
		
		private Dictionary<double, double> currentBuys = new Dictionary<double, double>();
		private Dictionary<double, double> currentSells = new Dictionary<double, double>();
		
		private Dictionary<double, double> buysOffLow = new Dictionary<double, double>();
		private Dictionary<double, double> sellsOffLow = new Dictionary<double, double>();
		
		private Dictionary<double, double> buysOffHigh = new Dictionary<double, double>();
		private Dictionary<double, double> sellsOffHigh = new Dictionary<double, double>();
		
		double priceDifference;

		private void ResetBuysAndSells()
		{
		    currentBuys.Clear();
		    currentSells.Clear();
		   
		}
		
		private void ProcessTradeParams()
	    {
	        var completedTradeParams = new List<TradeParameters>();
	
	        foreach (var tradeParams in tradeParamsList.ToList())
	        {
	            if (Time[0] > tradeParams.TradeWindowEndTime)
	            {
	                UpdateWinRateForTradeParams(tradeParams, ProfitTarget, StopLoss);
	                completedTradeParams.Add(tradeParams);
	                tradeParamsList.Remove(tradeParams);
	            }
	        }
	
	        if (completedTradeParams.Any())
	        {
	            WriteTradesToCsv(completedTradeParams);
	        }
	    }


		protected override void OnMarketData(MarketDataEventArgs e)
		{
//			if ((State == State.Historical && trainModel) || (State == State.Realtime))
			{
				if (e.MarketDataType == MarketDataType.Last)
				{
					double price = e.Price;
					double volume = e.Volume;
					
					if (price > (e.Ask + e.Bid) / 2)
					{
						RecordTrade(buysAtBar, price, volume, e);
						RecordTrade(sellsAtBar, price, 0, e);
						if (State == State.Realtime)
						{
							RecordTrade(currentBuys, price, volume, e);
							RecordTrade(currentSells, price, 0, e);
						}
						
						
					}
					else if (price < (e.Ask + e.Bid) / 2)
					{
						RecordTrade(sellsAtBar, price, volume, e);
						RecordTrade(buysAtBar, price, 0, e);
						if (State == State.Realtime)
						{
							RecordTrade(currentBuys, price, 0, e);
							RecordTrade(currentSells, price, volume, e);
						}
					
						
					}
					
					if(Math.Abs(e.Ask - e.Bid) <= 0.5){
						lowSpread = true;
					}
					
					delta = buysAtBar.Values.Sum() - sellsAtBar.Values.Sum();
				
				    deltaAbove = 0;
					deltaBelow = 0;
					deltaAtPrice = 0;
					totalDelta = 0;
					
					if(accumulatedVolumeProfile.Any()){
				    foreach (var kvp in accumulatedVolumeProfile)
				    {
				        if (kvp.Key > e.Price)
				            deltaAbove += kvp.Value.Delta;
				        else if (kvp.Key < e.Price)
				            deltaBelow += kvp.Value.Delta;
				        else
				            deltaAtPrice += kvp.Value.Delta;
				    }
					}
					
				    totalDelta = deltaAbove + deltaBelow + deltaAtPrice;
						
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
					
					//if (State == State.Realtime)
					{
						UpdateTotalBuysAndSells(e.Price);
					}
					
					UpdateSimTrades(ProfitTarget, StopLoss, e.Price);
					
					if ((incTrain && !trainModel) || State == State.Historical)
					{
						var completedTradeParams = new List<TradeParameters>();
						
						for (int i = tradeParamsList.Count - 1; i >= 0; i--)
						{
							var tradeParams = tradeParamsList[i];
							
							if (Time[0] > tradeParams.TradeWindowEndTime)
							{
								UpdateWinRateForTradeParams(tradeParams, ProfitTarget, StopLoss);
								completedTradeParams.Add(tradeParams);
							}
						}
						
						tradeParamsList.RemoveAll(tradeParams => Time[0] > tradeParams.TradeWindowEndTime);
						
						if (completedTradeParams.Any())
						{
							WriteTradesToCsv(completedTradeParams);
						}
					}
				}
				
				if (e.Price > highOfBar && e.Volume != 0)
				{
					highOfBar = e.Price;
					ResetBuysAndSells();
					buysOffHigh.Clear();
					sellsOffHigh.Clear();
				}
				if (e.Price < lowOfBar && e.Volume != 0)
				{
					lowOfBar = e.Price;
					ResetBuysAndSells();
					buysOffLow.Clear();
					sellsOffLow.Clear();
				}
			
				
			
				if (e.MarketDataType == MarketDataType.Last || e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask)
				{
					if (CurrentBar < 1)
					{
						return;
					}
		
					
					if (incTrain || aggregate || trainModel)
					{
						aggregatedBuys = AggregateVolumesIntoGroups(buysAtBar, lowOfBar, highOfBar);
						aggregatedSells = AggregateVolumesIntoGroups(sellsAtBar, lowOfBar, highOfBar);
						aggregatedBuysPB = AggregateVolumesIntoGroups(currentBuys, lowOfBar, highOfBar);
						aggregatedSellsPB = AggregateVolumesIntoGroups(currentSells, lowOfBar, highOfBar);
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
						
						DateTime currentTime = Time[0];
						
						if (Time[0] - lastDay > TimeSpan.FromHours(1))
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
								direction = "Long";
							}
							else if (sellVolume > buyVolume)
							{
								tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
								direction = "Short";
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
		
//		    RenderTarget.DrawText($"Delta Above: {deltaAbove:F0}", metricsFormat, 
//		        new SharpDX.RectangleF(metricsX, metricsY, 200, 20), 
//		        deltaAbove >= 0 ? positiveBrush : negativeBrush);
		
//		    RenderTarget.DrawText($"Delta Below: {deltaBelow:F0}", metricsFormat, 
//		        new SharpDX.RectangleF(metricsX, metricsY + 20, 200, 20), 
//		        deltaBelow >= 0 ? positiveBrush : negativeBrush);
		
//		    RenderTarget.DrawText($"Total Delta: {Math.Max(Math.Abs(deltaAbove/deltaBelow),Math.Abs(deltaBelow/deltaAbove)):F2}", metricsFormat, 
//		        new SharpDX.RectangleF(metricsX, metricsY + 40, 200, 20), 
//		        totalDelta >= 0 ? positiveBrush : negativeBrush);
			
			 RenderTarget.DrawText($"Min Volume: {minVolume:F0}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY, 200, 20), 
		        textBrush);
		
	         RenderTarget.DrawText($"Min Ratio: {ratio:F2}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 20, 200, 20), 
		        textBrush);
		
		    RenderTarget.DrawText($"Adversary Detection: {detectionValue:F0}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 40, 200, 20), 
		        textBrush);

			 RenderTarget.DrawText($"Long Probability: {p_h1_d:F3}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 60, 200, 20), 
		        textBrush);
			
			
			 RenderTarget.DrawText($"Short Probability: {p_h0_d:F3}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 80, 200, 20), 
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
			    DateTime currentTime = Time[0];
			    DateTime tradeWindowEndTime = currentTime.AddSeconds(tradesWindowMinutes);
	
			    if (initialLetters == "NQ")
			    {
			        foreach (var kvp in aggregatedBuys)
			        {
			            double price = kvp.Key;
			            double buyVolume = kvp.Value;
			            double sellVolume = aggregatedSells.ContainsKey(price) ? aggregatedSells[price] : 0;
			            double tradeRatio;
			            string direction = "";
						
			            if (buyVolume > sellVolume)
			            {
			                tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
			                direction = "Long";
			            }
			            else
			            {
			                tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
			                direction = "Short";
			            }
					
			            if (Math.Min(buyVolume, sellVolume) > detectionValueForML && Math.Abs(buyVolume - sellVolume) > 0)
			            {
			                TradeParameters tradeParams = new TradeParameters(Math.Abs(buyVolume - sellVolume), Math.Min(buyVolume, sellVolume), tradeRatio, tradeWindowEndTime);
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
			                TradeParameters tradeParams = new TradeParameters(Math.Abs(buyVolume - sellVolume), Math.Min(buyVolume, sellVolume), tradeRatio, tradeWindowEndTime);
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
			    double entryPrice = Close[0];
			    
			    SimTrade newTrade = new SimTrade(
			        tradeParams.ImbVolThreshold,
			        tradeParams.AdvDetectionThreshold,
			        tradeParams.RatioThreshold,
			        price,
					direction,
					priceDistance,
					priceDifference,
					ADX(5)[0] - ADX(5)[1],
					Momentum(5)[0] - Momentum(5)[1],
					RSquared(5)[0],
					Open[1] - Low[1],
					High[1] - Open[1]
					
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
			    string filePath = incTrain ? @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\inctrain.csv" :
			                      trainModel ? @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\train_model.csv" : "";
			
			    if (string.IsNullOrEmpty(filePath)) return;
			
			    try
			    {
			        string directory = Path.GetDirectoryName(filePath);
			        if (!Directory.Exists(directory))
			        {
			            Directory.CreateDirectory(directory);
			        }
			
			        bool fileExists = File.Exists(filePath);
			        bool headerExists = false;
			
			        if (fileExists)
			        {
			            // Check if the header exists
			            string firstLine = File.ReadLines(filePath).FirstOrDefault();
			            headerExists = firstLine != null && firstLine.StartsWith("WinRate,ImbVol,ImbRatio,AdversaryDetection,PriceDistance,PriceDifference,ADXChange,MomentumChange,R2,OpenToLow,OpenToHigh");
			        }
			
			        using (StreamWriter writer = new StreamWriter(filePath, append: true))
			        {
			            if (!headerExists)
			            {
			                writer.WriteLine("WinRate,ImbVol,ImbRatio,AdversaryDetection,PriceDistance,PriceDifference,ADXChange,MomentumChange,R2,OpenToLow,OpenToHigh");
			            }
			
			            StringBuilder sb = new StringBuilder();
			            foreach (var tradeParams in completedTradeParams)
			            {
			                var lastCompletedTrade = tradeParams.Trades.FirstOrDefault(t => t.IsCompleted);
							
							//foreach(var trade in tradeParams.Trades){
			                if (lastCompletedTrade != null)
			                {
			                    sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\n",
			                        Math.Round(lastCompletedTrade.WinRate, 2),
			                        lastCompletedTrade.ImbVol,
			                        Math.Round(lastCompletedTrade.Ratio, 2),
			                        lastCompletedTrade.AdvDetection,
			                        Math.Round(lastCompletedTrade.PriceDistance, 2),
								  	Math.Round(lastCompletedTrade.PriceDifference, 2),
									Math.Round(lastCompletedTrade.ADXChange, 2),
									Math.Round(lastCompletedTrade.MomentumChange, 2),
									Math.Round(lastCompletedTrade.R2, 2),
 									Math.Round(lastCompletedTrade.OpenToLow, 2),
									Math.Round(lastCompletedTrade.OpenToHigh, 2)
			                    );
			                }
							//}
			            }
			            writer.Write(sb.ToString());
			        }
			        completedTradeParams.Clear();
			    }
			    catch (Exception ex)
			    {
			        Print($"Error writing to CSV: {ex.Message}");
			    }
			}

	
			private void WriteCurrentPredictiveValuesToServer()
			{
				var predictiveValues = new
				{
					WinRate = 0.65,
					PriceDistance = priceDistance,
					PriceDifference = priceDifference,
					ADXChange = ADX(14)[0] - ADX(14)[1],
					MomentumChange = Momentum(5)[0] - Momentum(5)[1],
					R2 = RSquared(5)[0],
					OpenToLow = Open[1] - Low[1],
					OpenToHigh = High[1] - Open[1]
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

		        if (isLongMode  && isBuyImbalance && isTrendMode && p_h1_d > 0.70)
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
		        if (isShortMode  && isTrendMode  && isSellImbalance && p_h0_d > 0.70 )
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
		        if (isLongMode && isBuyImbalance && isRegressionMode && p_h0_d > 0.70 )
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
		      	if (isShortMode && isSellImbalance  && isRegressionMode && p_h1_d > 0.70 )
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
		[Display(Name="Adversary Detection for training data", Order=5, GroupName="Imbalances")]
		public int detectionValueForML
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
		[Display( Name = "Trade window (seconds)", GroupName = "Machine Learning", Order = 0)]
		public int tradesWindowMinutes
		{ get; set; } 
		
		[NinjaScriptProperty]
		[Display( Name = "Use regression training data", GroupName = "Machine Learning", Order = 0)]
		public bool regressForTrain
		{ get; set; } 
		
		
		
		
		
	
		#endregion

		private List<int> observationSequence = new List<int>();
		private int maxSequenceLength = 100; // Adjust as needed
		
		private List<double> historicalAskVolumes = new List<double>();  // List to store historical ask volumes
		private List<double> historicalBidVolumes = new List<double>();  // List to store historical bid volumes
		
		private List<double> historicalAskIncreases = new List<double>();  // List to store historical ask volumes
		private List<double> historicalBidIncreases = new List<double>();  // List to store historical bid volumes
		
		private List<double> historicalAskDecreases = new List<double>();  // List to store historical ask volumes
		private List<double> historicalBidDecreases = new List<double>();  // List to store historical bid volumes
	
		private List<double> historicalRsiIncreases = new List<double>();
		private List<double> historicalRsiDecreases = new List<double>();
		
		private List<double> historicalAdxIncreases = new List<double>();
		private List<double> historicalAdxDecreases = new List<double>();
		
		private List<double> historicalMacdIncreases = new List<double>();
		private List<double> historicalMacdDecreases = new List<double>();

		private List<(int Observation, double AskVolume, double BidVolume, double Rsi, double Adx, double Macd)> observationData = new List<(int, double, double, double, double, double)>();

		private void UpdateHistoricalData(bool priceIncreased)
		{
	
		    // Calculate technical indicators
		    double rsi = RSI(14, 3)[0];
		    double adx = ADX(14)[0];
		    double macd = MACD(12, 26, 9).Diff[0];
		
		    // Calculate volume imbalance
		    double totalBidVolume = cumulativeBuys;
		    double totalAskVolume = cumulativeSells;

		    // Add the observation, ask, bid volumes, and technical indicators to observationData
		    int observation = priceIncreased ? 1 : 0;
		    observationData.Add((observation, totalAskVolume, totalBidVolume, rsi, adx, macd));
		
		    // Update historical data based on price movement
		    if (priceIncreased)
		    {
			
		        historicalAskIncreases.Add(totalAskVolume);
				historicalBidIncreases.Add(totalBidVolume);
		        historicalRsiIncreases.Add(rsi);
		        historicalAdxIncreases.Add(adx);
		        historicalMacdIncreases.Add(macd);
		    }
		    else
		    {
		        historicalAskDecreases.Add(totalAskVolume);
				historicalBidDecreases.Add(totalBidVolume);
		        historicalRsiDecreases.Add(rsi);
		        historicalAdxDecreases.Add(adx);
		        historicalMacdDecreases.Add(macd);
		    }
		
		    // Maintain the maximum sequence length
		    if (observationData.Count > maxSequenceLength)
		        observationData.RemoveAt(0);
		
		    if (historicalAskDecreases.Count > maxSequenceLength)
		        historicalAskDecreases.RemoveAt(0);
		    if (historicalAskIncreases.Count > maxSequenceLength)
		        historicalAskIncreases.RemoveAt(0);
			
			if (historicalBidIncreases.Count > maxSequenceLength)
		        historicalBidIncreases.RemoveAt(0);
		    if (historicalBidDecreases.Count > maxSequenceLength)
		        historicalBidDecreases.RemoveAt(0);
		
		    if (historicalRsiIncreases.Count > maxSequenceLength)
		        historicalRsiIncreases.RemoveAt(0);
		    if (historicalRsiDecreases.Count > maxSequenceLength)
		        historicalRsiDecreases.RemoveAt(0);
		
		    if (historicalAdxIncreases.Count > maxSequenceLength)
		        historicalAdxIncreases.RemoveAt(0);
		    if (historicalAdxDecreases.Count > maxSequenceLength)
		        historicalAdxDecreases.RemoveAt(0);
		
		    if (historicalMacdIncreases.Count > maxSequenceLength)
		        historicalMacdIncreases.RemoveAt(0);
		    if (historicalMacdDecreases.Count > maxSequenceLength)
		        historicalMacdDecreases.RemoveAt(0);
		
		
		    // Recalculate statistical parameters and Bayesian priors
		    RecalculateStatistics();
		}

		private void RecalculateStatistics()
		{
		    // Recalculate for price increases
		    if (historicalAskIncreases.Count > 0)
		    {
		        muAskIncrease = historicalAskIncreases.Average();
		        sigmaAskIncrease = Math.Sqrt(historicalAskIncreases.Average(askvolume => Math.Pow(askvolume - muAskIncrease, 2)));
				
				muBidIncrease = historicalBidIncreases.Average();
		        sigmaBidIncrease = Math.Sqrt(historicalBidIncreases.Average(bidvolume => Math.Pow(bidvolume - muBidIncrease, 2)));
		
		        muRsiIncrease = historicalRsiIncreases.Average();
		        sigmaRsiIncrease = Math.Sqrt(historicalRsiIncreases.Average(rsi => Math.Pow(rsi - muRsiIncrease, 2)));
		
		        muAdxIncrease = historicalAdxIncreases.Average();
		        sigmaAdxIncrease = Math.Sqrt(historicalAdxIncreases.Average(adx => Math.Pow(adx - muAdxIncrease, 2)));
		
		        muMacdIncrease = historicalMacdIncreases.Average();
		        sigmaMacdIncrease = Math.Sqrt(historicalMacdIncreases.Average(macd => Math.Pow(macd - muMacdIncrease, 2)));
		    }
		    else
		    {
		        muAskIncrease = 0;
		        sigmaAskIncrease = 1;
				 muBidIncrease = 0;
		        sigmaBidIncrease = 1;
		        muRsiIncrease = 0;
		        sigmaRsiIncrease = 1;
		        muAdxIncrease = 0;
		        sigmaAdxIncrease = 1;
		        muMacdIncrease = 0;
		        sigmaMacdIncrease = 1;
		    }
		
		    // Recalculate for price decreases
		    if (historicalAskDecreases.Count > 0)
		    {
		         muAskDecrease = historicalAskDecreases.Average();
		        sigmaAskDecrease= Math.Sqrt(historicalAskDecreases.Average(askvolume => Math.Pow(askvolume - muAskDecrease, 2)));
				
				muBidDecrease = historicalBidDecreases.Average();
		        sigmaBidDecrease = Math.Sqrt(historicalBidDecreases.Average(bidvolume => Math.Pow(bidvolume - muBidDecrease, 2)));
		
		        muRsiDecrease = historicalRsiDecreases.Average();
		        sigmaRsiDecrease = Math.Sqrt(historicalRsiDecreases.Average(rsi => Math.Pow(rsi - muRsiDecrease, 2)));
		
		        muAdxDecrease = historicalAdxDecreases.Average();
		        sigmaAdxDecrease = Math.Sqrt(historicalAdxDecreases.Average(adx => Math.Pow(adx - muAdxDecrease, 2)));
		
		        muMacdDecrease = historicalMacdDecreases.Average();
		        sigmaMacdDecrease = Math.Sqrt(historicalMacdDecreases.Average(macd => Math.Pow(macd - muMacdDecrease, 2)));
		    }
		    else
		    {
		        muAskDecrease = 0;
		        sigmaAskDecrease = 1;
				 muBidDecrease = 0;
		        sigmaBidDecrease= 1;
		        muRsiDecrease = 0;
		        sigmaRsiDecrease = 1;
		        muAdxDecrease = 0;
		        sigmaAdxDecrease = 1;
		        muMacdDecrease = 0;
		        sigmaMacdDecrease = 1;
		    }
		
		    // Calculate Bayesian priors based on historical data
		    CalculateBayesianPriors();

		}

		/// <summary>
		/// Calculates Bayesian priors P(H=1) and P(H=0) based on historical data.
		/// </summary>
		private void CalculateBayesianPriors()
		{
		    double totalEvents = historicalAskIncreases.Count + historicalAskDecreases.Count;
		
		    if (totalEvents == 0)
		    {
		        // Avoid division by zero; assign equal priors
		        priorPriceIncrease = 0.5;
		        priorPriceDecrease = 0.5;
				 // Optionally, log the updated priors for debugging
		   
		    }
		    else
		    {
		        priorPriceIncrease = (double)historicalAskIncreases.Count / totalEvents;
		        priorPriceDecrease = (double)historicalAskDecreases.Count / totalEvents;
		
		        // Ensure that priors sum to 1
		        double sum = priorPriceIncrease + priorPriceDecrease;
		        if (sum != 1.0)
		        {
		            priorPriceIncrease /= sum;
		            priorPriceDecrease /= sum;
		        }
					 // Optionally, log the updated priors for debugging
		   // Print($"[{Time[0]}] Updated Priors -> P(H=1): {priorPriceIncrease}, P(H=0): {priorPriceDecrease}");
				
		    }
		
		   
		}

        /// <summary>
        /// Computes the total imbalance Itotal by summing (BidVolume - AskVolume) across all price levels.
        /// </summary>
        /// <returns>Total Imbalance (Itotal)</returns>
        private double ComputeImbalance()
        {
            double itotal = 0;
           foreach (var kvp in buysAtBar)
			{
			    // Declare a variable to hold the value from sellsAtBar
			    double sells;
			
			    // Check if sellsAtBar contains the same key as buysAtBar
			    if (sellsAtBar.TryGetValue(kvp.Key, out sells))
			    {
			        // Perform the subtraction and update itotal
			        itotal += kvp.Value - sells;
			    }
			}

            return itotal;
        }

		/// <summary>
		/// Calculates the posterior probabilities P(H=1|D) and P(H=0|D) using Bayes' Theorem, incorporating technical indicators.
		/// </summary>
		/// <param name="volumeImbalance">Current volume imbalance (bid - ask volume)</param>
		/// <param name="totalAskVolume">Total Ask Volume</param>
		/// <param name="totalBidVolume">Total Bid Volume</param>
		/// <returns>Tuple containing (P(H=1|D), P(H=0|D))</returns>
		private (double, double) CalculatePosteriors()
		{
		    // Calculate likelihoods based on cumulative buys
		    double p_d_h1_buys = GaussianPDF(cumulativeBuys, muAskIncrease, sigmaAskIncrease); // P(D_buys|H=1)
		    double p_d_h0_buys = GaussianPDF(cumulativeBuys, muAskDecrease, sigmaAskDecrease); // P(D_buys|H=0)
		    
		    // Calculate likelihoods based on cumulative sells
		    double p_d_h1_sells = GaussianPDF(cumulativeSells, muBidIncrease, sigmaBidIncrease); // P(D_sells|H=1)
		    double p_d_h0_sells = GaussianPDF(cumulativeSells, muBidDecrease, sigmaBidDecrease); // P(D_sells|H=0)
		    
		    // Combine likelihoods (assuming independence)
		    double p_d_h1 = p_d_h1_buys * p_d_h1_sells; // P(D|H=1)
		    double p_d_h0 = p_d_h0_buys * p_d_h0_sells; // P(D|H=0)
		
		
		    // Incorporate RSI into the likelihood calculation
		    double rsi = RSI(5, 3)[0];
		    double p_rsi_h1 = GaussianPDF(rsi, muRsiIncrease, sigmaRsiIncrease);
		    double p_rsi_h0 = GaussianPDF(rsi, muRsiDecrease, sigmaRsiDecrease);
		    p_d_h1 *= p_rsi_h1; // P(D|H=1) *= P(RSI|H=1)
		    p_d_h0 *= p_rsi_h0; // P(D|H=0) *= P(RSI|H=0)
		
		    // Incorporate ADX into the likelihood calculation
		    double adx = ADX(5)[0];
		    double p_adx_h1 = GaussianPDF(adx, muAdxIncrease, sigmaAdxIncrease);
		    double p_adx_h0 = GaussianPDF(adx, muAdxDecrease, sigmaAdxDecrease);
		    p_d_h1 *= p_adx_h1; // P(D|H=1) *= P(ADX|H=1)
		    p_d_h0 *= p_adx_h0; // P(D|H=0) *= P(ADX|H=0)
		
		    // Incorporate MACD into the likelihood calculation
		    double macd = MACD(12, 26, 9).Diff[0];
		    double p_macd_h1 = GaussianPDF(macd, muMacdIncrease, sigmaMacdIncrease);
		    double p_macd_h0 = GaussianPDF(macd, muMacdDecrease, sigmaMacdDecrease);
		    p_d_h1 *= p_macd_h1; // P(D|H=1) *= P(MACD|H=1)
		    p_d_h0 *= p_macd_h0; // P(D|H=0) *= P(MACD|H=0)
		
		
		    // Calculate marginal likelihood P(D)
		    double p_d = (p_d_h1 * priorPriceIncrease) + (p_d_h0 * priorPriceDecrease);
		
		    // Handle zero marginal likelihood
		    if (p_d == 0)
		    {
		        Print("[DEBUG] Marginal Likelihood is zero, returning neutral priors.");
		        return (0.5, 0.5);  // Neutral priors when likelihood is zero
		    }
		
		    // Calculate posterior probabilities
		    double p_h1_d = (p_d_h1 * priorPriceIncrease) / p_d; // P(H=1|D)
		    double p_h0_d = (p_d_h0 * priorPriceDecrease) / p_d; // P(H=0|D)
		
		    // Ensure non-zero posteriors
		    if (p_h1_d < 1e-10) p_h1_d = 1e-10;
		    if (p_h0_d < 1e-10) p_h0_d = 1e-10;
		
		    // Print the posterior probabilities
		    Print($"Posteriors: P(H=1|D): {p_h1_d}, P(H=0|D): {p_h0_d}");
		
		    return (p_h1_d, p_h0_d);
		}



/// <summary>
/// Calculates the probability density of a value x for a Gaussian distribution.
/// </summary>
/// <param name="x">Value</param>
/// <param name="mu">Mean</param>
/// <param name="sigma">Standard Deviation</param>
/// <returns>Probability density P(x)</returns>
private double GaussianPDF(double x, double mu, double sigma)
{
    if (sigma <= 0)
    {
       
        return 0;
    }
    double exponent = -Math.Pow(x - mu, 2) / (2 * Math.Pow(sigma, 2));
    double pdf = (1 / (Math.Sqrt(2 * Math.PI) * sigma)) * Math.Exp(exponent);
    
 
    
    return pdf;
}


	}
}