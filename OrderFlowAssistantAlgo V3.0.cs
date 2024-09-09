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
		public double TradeCount;
		public double TradeExpectancy;
		
		public int WindowId { get; set; }

		public double VolumeSpeed { get; set; }
		public double BolDif { get; set; }
		public double MADif { get; set; }
		public double TOD { get; set; }
		public double StdDev { get; set; }
		public double VROC { get; set; }
		public double VR { get; set; }
		public double Entropy { get; set; }
		public int WinCount { get; set; } = 0;
		public int LossCount { get; set; } = 0;
		public bool IsCompleted { get; set; } = false;

		public double PF { get; set; }
		
		
		
		
		public SimTrade(double imbVol, double advDetection, double ratio, double entryPrice, string direction, double volumeSpeed, double bolDif, double maDif,  double tod, double stdDev)
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
			TradeCount = totalTrades;
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
		public bool allowInTrade { get; set; } = true;
	
	    public TradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, DateTime tradeWindowEndTime)
	    {
	        ImbVolThreshold = imbVolThreshold;
	        AdvDetectionThreshold = advDetectionThreshold;
	        RatioThreshold = ratioThreshold;
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
		
		double R2;
	
		private List<SimTrade> simTrades = new List<SimTrade>();
	    private DateTime lastSampleTime = DateTime.MinValue;
		private List<SimTrade> initialSimTrades = new List<SimTrade>();
		private DateTime lastDay = DateTime.MinValue;
		
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
		double entropyValue = 0;

		protected override void OnBarUpdate()
		{
			int Period = 3;
			var entropy = Entropy(Period, 1, 1);
			
				if(StrategyStartTime == null){
					StrategyStartTime = Time[0];
				}

		    if (CurrentBar != activeBar )
		    {
				if(State==State.Realtime)
				{
				 WriteCurrentPredictiveValuesToCsv();
				}
				
				UpdateHighsAndLows();
		        activeBar = CurrentBar;
					
				tradeTaken =false;
				highOfBar = 0;
				lowOfBar = 99999999;
		        buysAtBar.Clear();
		        sellsAtBar.Clear();
		    }
			
			if (Time[0] - lastSampleTime > TimeSpan.FromSeconds(sampleInterval))
	        {
	            lastSampleTime = Time[0];
	            InitializeTradeParams();
				
	        }
			
			 var completedTradeParams = new List<TradeParameters>();

				 for (int i = tradeParamsList.Count - 1; i >= 0; i--)
				  {
				      var tradeParams = tradeParamsList[i];
				
				       if (Time[0] > tradeParams.TradeWindowEndTime)
				       {
								
				          UpdateWinRateForTradeParams(tradeParams, ProfitTarget, StopLoss);
				          completedTradeParams.Add(tradeParams);
				          tradeParamsList.RemoveAt(i);
				        }
				    }
				
				    if (completedTradeParams.Any())
				    {
				        WriteTradesToCsv(completedTradeParams);
				    }
			// Update simulated trades and check for target or stop loss
		}
		
		double buysforratio = 0;
		double sellsforratio = 0;
		double lastbarbfr = 1;
		double lastbarsfr = 1;
		double lowOfBar = 999999999;
		double highOfBar = 0;
		
		private Dictionary<double, double> currentBuys = new Dictionary<double, double>();
		private Dictionary<double, double> currentSells = new Dictionary<double, double>();

		private void ResetBuysAndSells()
		{
		    currentBuys.Clear();
		    currentSells.Clear();
		   
		}
		
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if ((State == State.Historical && trainModel) || (State == State.Realtime))
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
					
					if (State == State.Realtime)
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
				
				if (e.Price > highOfBar)
				{
					highOfBar = e.Price;
					ResetBuysAndSells();
				}
				if (e.Price < lowOfBar)
				{
					lowOfBar = e.Price;
					ResetBuysAndSells();
				}
				
				if (MLOn && Optimise && State == State.Realtime)
				{
					ReadOptimizedParamsFromCSV();
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
					}
					
					if (trainModel || incTrain)
					{
						if (CurrentBar < 2) return;
						
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
						
						var entropy = Entropy(5, 1, 1);
						double entropyMeasure = entropy.AvgEntropy[0];
						
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
									SimulateTrade(tradeParams, direction, closestPrice);
								}
							}
						}
					}
//					else if (initialLetters == "ES")
//				    {
				       
//				            double price = Close[0]; // Current price
				
//				             foreach (var tradeParams in tradeParamsList.Where(tp => tp.IsActive && tp.TradeWindowEndTime > currentTime))
//	    					{
//				                bool isLong = false;
//				                bool isShort = false;
				
//				                // Check for long trades starting at the current price and going down
//				                int validLongLevels = 0;		  
//				                for (int i = 0; i < imbalancestotrade + (allowTickGap ? 1 : 0); i++)
//				                {
//				                    double priceToCheck = price - i * TickSize;
//				                    if (buysAtBar.TryGetValue(priceToCheck, out double askVolume) && sellsAtBar.TryGetValue(priceToCheck - TickSize, out double bidVolume))
//				                    {
//				                        if (askVolume - bidVolume >= tradeParams.ImbVolThreshold && askVolume / bidVolume > tradeParams.RatioThreshold && bidVolume >= tradeParams.AdvDetectionThreshold)
//				                        {
//				                            validLongLevels++;
//				                        }
//				                    }
//				                }
//				                if (validLongLevels == imbalancestotrade)
//				                {
//				                    isLong = true;
//				                }
				
//				                // Check for short trades starting at the current price and going up
//				                int validShortLevels = 0;
//				                for (int i = 0; i < imbalancestotrade + (allowTickGap ? 1 : 0); i++)
//				                {
//				                    double priceToCheck = price + i * TickSize;
//				                    if (sellsAtBar.TryGetValue(priceToCheck, out double bidVolume) && buysAtBar.TryGetValue(priceToCheck + TickSize, out double askVolume))
//				                    {
//				                        if (bidVolume - askVolume >= tradeParams.ImbVolThreshold && bidVolume / askVolume > tradeParams.RatioThreshold && askVolume >= tradeParams.AdvDetectionThreshold)
//				                        {
//				                            validShortLevels++;
//				                        }
//				                    }
//				                }
//				                if (validShortLevels == imbalancestotrade)
//				                {
//				                    isShort = true;
//				                }
				
//				                string direction = "";
//				                if (isLong)
//				                {
//				                    direction = "Long";
//				                }
//				                else if (isShort)
//				                {
//				                    direction = "Short";
//				                }
				
//				                if (!string.IsNullOrEmpty(direction) && !simTrades.Any(t => t.WindowId == currentWindowId && t.ImbVol == tradeParams.ImbVolThreshold && t.AdvDetection == tradeParams.AdvDetectionThreshold && t.Ratio == tradeParams.RatioThreshold && t.Status == null))
//				                {
//				                    SimulateTrade(tradeParams, direction, closePrice);
//				                }
//				            }
				        
//					}		
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
			    double segmentSize = levelstotrade * TickSize;
			    
			    foreach (var kvp in volumes)
			    {
			        double price = kvp.Key;
			        double volume = kvp.Value;
			
			        if (price >= barLow && price <= barHigh)
			        {
			            int segmentIndex = (int)Math.Floor((price - barLow) / segmentSize);
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
			        direction == "Short" ? price : price + levelstotrade * TickSize,
			        direction,
			        PATIMachineLearningInputsV2().VolumeSpeedPerSecond[1],
			        PATIMachineLearningInputsV2().BollingerDiff[0],
			        PATIMachineLearningInputsV2().MovingAvgDiff[0],
			        PATIMachineLearningInputsV2().TimeOfDay[0],
			        PATIMachineLearningInputsV2().StdDevBB[0]
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
			
			    double totalProfit = winCount * target;
			    double totalLoss = lossCount * stopLoss;
			    double avgWin = winCount > 0 ? totalProfit / winCount : 0;
			    double avgLoss = lossCount > 0 ? totalLoss / lossCount : 0;
			    double tradeExpectancy = (winRate * avgWin) - ((1 - winRate) * avgLoss);

			    foreach (var trade in tradeParams.Trades)
			    {
			        trade.WinRate = winRate;
			        trade.TradeCount = totalTrades;
			        trade.TradeExpectancy = tradeExpectancy * totalTrades;
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
			            headerExists = firstLine != null && firstLine.StartsWith("Expectancy,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,TimeOfDay,BolDif,MADif,StdBB");
			        }
			
			        using (StreamWriter writer = new StreamWriter(filePath, append: true))
			        {
			            if (!headerExists)
			            {
			                writer.WriteLine("Expectancy,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,TimeOfDay,BolDif,MADif,StdBB");
			            }
			
			            StringBuilder sb = new StringBuilder();
			            foreach (var tradeParams in completedTradeParams)
			            {
			                var lastCompletedTrade = tradeParams.Trades.FirstOrDefault(t => t.IsCompleted);
			                if (lastCompletedTrade != null)
			                {
			                    sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8}\n",
			                        Math.Round(lastCompletedTrade.TradeExpectancy, 2),
			                        lastCompletedTrade.ImbVol,
			                        Math.Round(lastCompletedTrade.Ratio, 2),
			                        lastCompletedTrade.AdvDetection,
			                        Math.Round(lastCompletedTrade.VolumeSpeed, 2),
			                        lastCompletedTrade.TOD,
			                        Math.Round(lastCompletedTrade.BolDif, 2),
			                        Math.Round(lastCompletedTrade.MADif, 2),
			                        Math.Round(lastCompletedTrade.StdDev, 2));
			                }
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

	
			private void WriteCurrentPredictiveValuesToCsv()
			{
			    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\current_predictive_values.csv";
			
			    // Open or create the CSV file and overwrite any existing content
			    using (StreamWriter writer = new StreamWriter(filePath, false)) // false to overwrite existing content
			    {
			        // Write headers
			        writer.WriteLine("VolumeSpeed,TimeOfDay,BolDif,MADif,StdBB");

					double volVel = Math.Round(PATIMachineLearningInputsV2().VolumeSpeedPerSecond[1],2);
					double bd = Math.Round(PATIMachineLearningInputsV2().BollingerDiff[0],2);
					double mad = Math.Round(PATIMachineLearningInputsV2().MovingAvgDiff[0],2);
					double tod = PATIMachineLearningInputsV2().TimeOfDay[0];
					double std = Math.Round(PATIMachineLearningInputsV2().StdDevBB[0],2);
			        // Write the current values to the CSV file
			        writer.WriteLine($"{volVel},{tod},{bd},{mad},{std}");
			    }
			
			
			    //Print("Current predictive values written to CSV.");
			}
				private int prevvol = 0;
				private double prevratio = 0;
				private int prevdet = 0;
				private const string FilePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\optimized_params.csv";

				private void ReadOptimizedParamsFromCSV()
				{
					if (!File.Exists(FilePath))
					{
						Print("Optimized parameters CSV file not found.");
						return;
					}

					try
					{
						string[] lines = File.ReadAllLines(FilePath);
						if (lines.Length < 2) return;

						string[] values = lines[1].Split(',');
						if (values.Length < 3) return;

						int newMinVolume = int.Parse(values[0]);
						double newRatio = double.Parse(values[1]);
						int newDetectionValue = int.Parse(values[2]);

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
					}
					catch (Exception ex)
					{
						Print("Error reading optimized parameters from CSV: " + ex.Message);
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
		
		private void CheckAndEnterTrade(double price)
		{
		    if (!aroundPrice || (orderId.Length > 0 || atmStrategyId.Length > 0 || tradeTaken || (!isLongMode && !isShortMode)))
		        return;

		    double cumulativeBuys = 0, cumulativeSells = 0, cumulativeBuysPB = 0, cumulativeSellsPB = 0;
		    int validPriceLevels = 0, validPriceLevelsPB = 0, validImb = 0;
		    bool normalImbalanceDetected = false, pullbackImbalanceDetected = false;

		    double startLevel = -levelstotrade / 2;
		    double endLevel = levelstotrade / 2;

		    for (int numImb = imbalancestotrade; numImb > 0 && validImb < imbalancestotrade; numImb--)
		    {
		        cumulativeBuys = cumulativeSells = cumulativeBuysPB = cumulativeSellsPB = 0;
		        validPriceLevels = validPriceLevelsPB = 0;

		        for (double j = startLevel; j < endLevel; j++)
		        {
		            double priceToCheck = price + j * TickSize;

		            if (buysAtBar.TryGetValue(priceToCheck, out double buys) && sellsAtBar.TryGetValue(priceToCheck, out double sells))
		            {
		                cumulativeBuys += buys;
		                cumulativeSells += sells;
		                validPriceLevels++;
		            }

		            if (currentBuys.TryGetValue(priceToCheck, out double buysPB) && currentSells.TryGetValue(priceToCheck, out double sellsPB))
		            {
		                cumulativeBuysPB += buysPB;
		                cumulativeSellsPB += sellsPB;
		                validPriceLevelsPB++;
		            }
		        }

		        if (!normalImbalanceDetected)
		        {
		            double rawRatio = cumulativeBuys / cumulativeSells;
		            double ratio = rawRatio < 1 ? 1 / rawRatio : rawRatio;

		            if ((Math.Abs(cumulativeBuys - cumulativeSells) >= minVolume) &&
		                Math.Min(cumulativeBuys, cumulativeSells) >= detectionValue &&
		                ratio >= this.ratio && validPriceLevels == 4)
		            {
		                validImb++;
		                normalImbalanceDetected = true;
		            }
		        }

		        if (!normalImbalanceDetected && !pullbackImbalanceDetected)
		        {
		            double rawRatioPB = cumulativeBuysPB / cumulativeSellsPB;
		            double ratioPB = rawRatioPB < 1 ? 1 / rawRatioPB : rawRatioPB;

		            if ((Math.Abs(cumulativeBuysPB - cumulativeSellsPB) >= minVolume) &&
		                Math.Min(cumulativeBuysPB, cumulativeSellsPB) >= detectionValue &&
		                ratioPB >= this.ratio && validPriceLevelsPB == 4)
		            {
		                validImb++;
		                pullbackImbalanceDetected = true;
		            }
		        }
		    }

		    if (State == State.Realtime && validImb == imbalancestotrade)
		    {
		        tradeTaken = true;
		        OrderAction action = DetermineOrderAction(normalImbalanceDetected, pullbackImbalanceDetected,
		            cumulativeBuys, cumulativeSells, cumulativeBuysPB, cumulativeSellsPB);

		        ExecuteATMStrategy(action);

		        resetButtons();
		        if (pullbackImbalanceDetected)
		        {
		            Print(action == OrderAction.Buy
		                ? $"Long Trade Executed: {cumulativeBuysPB} : {cumulativeSellsPB} (Pullback)"
		                : $"Short Trade Executed: {cumulativeBuysPB} : {cumulativeSellsPB} (Pullback)");
		        }
		        else
		        {
		            Print(action == OrderAction.Buy
		                ? $"Long Trade Executed: {cumulativeBuys} : {cumulativeSells} (Normal)"
		                : $"Short Trade Executed: {cumulativeBuys} : {cumulativeSells} (Normal)");
		        }
		    }
		}

		private OrderAction DetermineOrderAction(bool normalImbalanceDetected, bool pullbackImbalanceDetected,
		    double cumulativeBuys, double cumulativeSells, double cumulativeBuysPB, double cumulativeSellsPB)
		{
		    bool isBuyImbalance;
		    if (normalImbalanceDetected)
		    {
		        isBuyImbalance = cumulativeBuys > cumulativeSells;
		    }
		    else if (pullbackImbalanceDetected)
		    {
		        isBuyImbalance = cumulativeBuysPB > cumulativeSellsPB;
		    }
		    else
		    {
		        return OrderAction.Sell; // Default action
		    }

		    if (isRegressionMode)
		    {
		        return isBuyImbalance ? OrderAction.Sell : OrderAction.Buy;
		    }
		    else if (isTrendMode)
		    {
		        return isBuyImbalance ? OrderAction.Buy : OrderAction.Sell;
		    }
		    else
		    {
		        return OrderAction.Sell; // Default action if neither mode is active
		    }
		}

		private void ExecuteATMStrategy(OrderAction action)
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
		            }
		        });
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