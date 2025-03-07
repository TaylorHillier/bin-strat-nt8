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

// This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    #region Helper Classes

    public class NewHttpClientWrapper
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BaseUrl = "http://127.0.0.1:5000"; // Your server address
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
        public double EntryPrice { get; set; }
        public string Direction { get; set; }
        public bool IsCompleted { get; set; } = false;
        public string Status { get; set; }
        public double PF { get; set; }
        public NewTradeParameters TradeParams { get; set; }
		public string TradeDirection { get; set; }
        public NewSimTrade(double entryPrice, string direction, NewTradeParameters tradeParams, string tradeType)
        {
            EntryPrice = entryPrice;
            Direction = direction;
            TradeParams = tradeParams;
			TradeDirection = tradeType;
        }
		
    }

    public enum TradeType
    {
        Regress,
        Trend
    }

    public class NewTradeParameters
    {
        public double ImbVolThreshold { get; }
        public double AdvDetectionThreshold { get; }
        public double RatioThreshold { get; }
        public string Direction { get; }
        public DateTime TradeWindowEndTime { get; } // Added property
        public bool IsActive { get; set; } = true;
        public bool allowInTrade { get; set; } = true;
        public TradeType TradeType { get; set; }
       
        public double VolumeSpeed { get; set; } = 0;
        public double PriceSpeed { get; set; } = 0;
        public double BolDif { get; set; } = 0;
        public double MADif { get; set; } = 0;
        public double TOD { get; set; } = 0;
        public double StdDev { get; set; } = 0;
        public string TradingMode { get; set; } = "";
        public int TrendTradeCount { get; set; } = 0;
		 public int RegressTradeCount { get; set; } = 0;
        public int TrendWinCount { get; set; } = 0;
		public int RegressWinCount { get; set; } = 0;
		
		public double TrendWinRate { get; set; }
		public double RegressionWinRate { get; set; }

        public NewTradeParameters(double imbVolThreshold, double advDetectionThreshold, double ratioThreshold, DateTime tradeWindowEndTime)
        {
            ImbVolThreshold = imbVolThreshold;
            AdvDetectionThreshold = advDetectionThreshold;
            RatioThreshold = ratioThreshold;
            TradeWindowEndTime = tradeWindowEndTime;
        }
    }

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

    #endregion

    public class ReFactoredATI : Strategy
    {
        #region Controls

        private bool longMode = false;
        private bool shortMode = false;
        private bool bothArmed = false;

        // Buttons and grid
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
            var button = sender as System.Windows.Controls.Button;
            string buttonText = button.Content.ToString();
            string buttonName = button.Name;

            // Short Button logic
            if (button == shortButton && buttonName == "ShortButton")
            {
                if (buttonText == "Arm Short" && Position.MarketPosition == MarketPosition.Flat)
                {
                    shortMode = true;
                    shortButton.Content = "Armed Short";
                    shortButton.Background = Brushes.Red;
                }
                else if (buttonText == "Armed Short" || Position.MarketPosition != MarketPosition.Flat)
                {
                    shortMode = false;
                    shortButton.Content = "Arm Short";
                    shortButton.Background = Brushes.Gray;
                }
            }
            // Arm both logic
            if (button == armButton && buttonName == "ArmButton")
            {
                if (buttonText == "Both Armed")
                {
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
                else if (buttonText == "Arm Both")
                {
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
            }
            // Long Button logic
            if (button == longButton && buttonName == "LongButton")
            {
                if (buttonText == "Arm Long")
                {
                    longMode = true;
                    longButton.Content = "Armed Long";
                    longButton.Background = Brushes.Green;
                }
                else if (buttonText == "Armed Long")
                {
                    longMode = false;
                    longButton.Content = "Arm Long";
                    longButton.Background = Brushes.Gray;
                }
            }
            // Mode Button logic
            if (button == modeButton && buttonName == "ModeButton")
            {
                if (buttonText == "Trend")
                {
                    currentMode = TradingMode.Regression;
                    modeButton.Content = "Regression";
                    modeButton.Background = Brushes.Teal;
                    Print("regression - " + currentMode);
                    Print("Mode changed: Regression mode activated, Trend mode deactivated");
                }
                else if (buttonText == "Regression")
                {
                    currentMode = TradingMode.Trend;
                    modeButton.Content = "Trend";
                    modeButton.Background = Brushes.Purple;
                    Print("Mode changed: Trend mode activated, Regression mode deactivated");
                }
            }
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
		
		private void changeButton()
		{
			if(State != State.Realtime)
				return;
			
			Dispatcher.Invoke(() =>
            {
				if(currentMode == TradingMode.Trend){
					modeButton.Content = "Trend";
                    modeButton.Background = Brushes.Purple;
                  
				} else if(currentMode == TradingMode.Regression){
					
                    modeButton.Content = "Regression";
                    modeButton.Background = Brushes.Teal;
				}
           
           
            });
		}

        #endregion

        #region Trading Variables

        private int activeBar = -1;
        private bool tradeTaken = false;

        private double price = 0;
        private VolumeData volData;
        private VolumeData pullbackData;
		private VolumeData l1Data;

        public enum ImbalanceMode
        {
            Horizontal,
            Diagonal
        }

        public string atmStrategyId = string.Empty;
        public string orderId = string.Empty;
        public bool isAtmStrategyCreated = false;

        public enum FractalLineSignal
        {
            Long,
            Short,
            Both
        }

        private bwFractal myFractal;
        private Dictionary<double, VolumeData> priceVolumeMap = new Dictionary<double, VolumeData>();
        private Dictionary<double, VolumeData> pullbackVolumeMap = new Dictionary<double, VolumeData>();
		private Dictionary<double, VolumeData> l1VolumeMap = new Dictionary<double, VolumeData>();

        #endregion

        #region Machine Learning Variables

        bool canUpdate = false;
        private DateTime StrategyStartTime;
        private List<NewTradeParameters> completedTradeParamsBuffer = new List<NewTradeParameters>();
        bool pastTime = false;

        DateTime currentTime;

        private List<NewTradeParameters> tradeParamsList = new List<NewTradeParameters>();
        private Dictionary<double, VolumeData> aggregatedVolumes;
        private Dictionary<double, VolumeData> aggregatedPullbackVol;
		private Dictionary<double, VolumeData> aggregatedL1Vol;
        private const int MaxHistoricalBars = 1; // Adjust as needed

        private List<NewSimTrade> simTrades = new List<NewSimTrade>();
        private DateTime lastSampleTime = DateTime.MinValue;
        private DateTime lastDay = DateTime.MinValue;
        private DateTime predValueWrite = DateTime.MinValue;

        // Flag to indicate whether the trades window period has ended
        private bool tradesWindowEnded = false;
        double winRate = 0;

        int currentWindowId = 0;

        #endregion

        #region State Methods

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "ReFactoredATI";
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
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
              
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

                    // Define rows and columns
                    myGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
                    myGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
                    myGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());

                    myGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                    myGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                    myGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                    myGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());

                    // Create buttons
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

                    // Assign click event
                    longButton.Click += OnButtonClick;
                    shortButton.Click += OnButtonClick;
                    armButton.Click += OnButtonClick;
                    modeButton.Click += OnButtonClick;

                    // Layout buttons in grid
                    System.Windows.Controls.Grid.SetRow(modeButton, 0);
                    System.Windows.Controls.Grid.SetColumn(modeButton, 0);
                    System.Windows.Controls.Grid.SetColumnSpan(modeButton, 2);

                    System.Windows.Controls.Grid.SetRow(longButton, 1);
                    System.Windows.Controls.Grid.SetColumn(longButton, 0);

                    System.Windows.Controls.Grid.SetRow(shortButton, 1);
                    System.Windows.Controls.Grid.SetColumn(shortButton, 1);

                    System.Windows.Controls.Grid.SetRow(armButton, 2);
                    System.Windows.Controls.Grid.SetColumn(armButton, 0);
                    System.Windows.Controls.Grid.SetColumnSpan(armButton, 2);

                    // Add buttons to grid
                    myGrid.Children.Add(modeButton);
                    myGrid.Children.Add(longButton);
                    myGrid.Children.Add(shortButton);
                    myGrid.Children.Add(armButton);

                    UserControlCollection.Add(myGrid);
                });
            }
            else if (State == State.Terminated)
            {
                Dispatcher.InvokeAsync(() =>
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
                });
            }
        }

        #endregion

        #region OnBarUpdate and OnMarketData

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 21)
                return;

            if (CurrentBar > activeBar)
            {
                activeBar = CurrentBar;
                aggregatedVolumes = AggregateVolumesIntoGroups(priceVolumeMap, lowOfBar, highOfBar);
                aggregatedPullbackVol = AggregateVolumesIntoGroups(pullbackVolumeMap, lowOfBar, highOfBar);
				aggregatedL1Vol = AggregateVolumesIntoGroups(l1VolumeMap, lowOfBar, highOfBar);

                priceVolumeMap.Clear();
                pullbackVolumeMap.Clear();
				l1VolumeMap.Clear();
                tradeTaken = false;

                highOfBar = 0;
                lowOfBar = 99999999;

                if ((State == State.Realtime && Optimise && MLOn))
                {
                    WriteCurrentPredictiveValuesToServer();
                    ReadOptimizedParamsFromServer();
					changeButton();
                }
            }
        }

        double askPrice = 0;
        double bidPrice = 0;
        double highOfBar = double.MinValue;
        double lowOfBar = double.MaxValue;
		
		double currentAsk = 0;
		double currentBid = 0;
		double askVolume = 0;
		double bidVolume = 0;
		double lastAsk = 0;
		double lastBid = 0;
		double lastAskPrice = 0;
		double lastBidPrice = 0;
		double overallAggRatio = 0;
		
        protected override void OnMarketData(MarketDataEventArgs e)
        {
			
			if(e.MarketDataType == MarketDataType.Ask){
				currentAsk = e.Volume;
			}
			
			if(e.MarketDataType == MarketDataType.Bid){
				currentBid = e.Volume;
			}
			
				
		    if (e.MarketDataType == MarketDataType.Ask) {
			  askVolume = e.Volume;
			  askPrice = e.Price;
			  double newVol = askVolume - lastAsk;
		
			  	if (!l1VolumeMap.TryGetValue(askPrice, out l1Data))
	            {
	                l1Data = new VolumeData(0, 0);
	                l1VolumeMap[askPrice] = l1Data;
	            }
				
			  l1Data.AskVolume += (askPrice == lastAskPrice ? newVol : askVolume);
			  
			  lastAsk = askVolume;
			  lastAskPrice = askPrice;
				
			  aggregatedL1Vol = AggregateVolumesIntoGroups(l1VolumeMap, lowOfBar, highOfBar);
		  	}
		  
		  	if (e.MarketDataType == MarketDataType.Bid) {
			   bidVolume = e.Volume;
			   bidPrice = e.Price;
			   double newVol = bidVolume - lastBid;
				  
				if (!l1VolumeMap.TryGetValue(bidPrice, out l1Data))
	            {
	                l1Data = new VolumeData(0, 0);
	                l1VolumeMap[bidPrice] = l1Data;
	            }
			
			   l1Data.BidVolume += (bidPrice == lastBidPrice ? newVol : bidVolume);
	           
	
			   lastBid = bidVolume;
			   lastBidPrice = bidPrice;
				
			   aggregatedL1Vol = AggregateVolumesIntoGroups(l1VolumeMap, lowOfBar, highOfBar);
				
		  	}
			
            if (e.MarketDataType != MarketDataType.Last || CurrentBar < 21)
                return;
			

            double volume = e.Volume;
            price = e.Price;
            currentTime = e.Time;
            askPrice = e.Ask;
            bidPrice = e.Bid;

            if (highOfBar == double.MinValue && lowOfBar == double.MaxValue)
            {
                highOfBar = price;
                lowOfBar = price;
            }
            if (price > highOfBar)
            {
                highOfBar = price;
                pullbackVolumeMap.Clear();
            }
            if (price < lowOfBar)
            {
                lowOfBar = price;
                pullbackVolumeMap.Clear();
            }

            // Map Ask and Bid volumes
            double midpoint = (askPrice + bidPrice) / 2;
            bool isAskSide = price > midpoint;
            bool isBidSide = price < midpoint;

            if (!priceVolumeMap.TryGetValue(price, out volData))
            {
                volData = new VolumeData(0, 0);
                priceVolumeMap[price] = volData;
            }
            if (!pullbackVolumeMap.TryGetValue(price, out pullbackData))
            {
                pullbackData = new VolumeData(0, 0);
                pullbackVolumeMap[price] = pullbackData;
            }
            if (isAskSide)
            {
                volData.AskVolume += volume;
                pullbackData.AskVolume += volume;
            }
            else if (isBidSide)
            {
                volData.BidVolume += volume;
                pullbackData.BidVolume += volume;
            }

            // Refresh aggregated volumes on every market update
            aggregatedVolumes = AggregateVolumesIntoGroups(priceVolumeMap, lowOfBar, highOfBar);
            aggregatedPullbackVol = AggregateVolumesIntoGroups(pullbackVolumeMap, lowOfBar, highOfBar);

            HandleEntryConditions();
            UpdateSimTrades(ProfitTarget, StopLoss, e.Price);

            if (e.MarketDataType == MarketDataType.Last)
            {
                if (CurrentBar < 1)
                    return;
				
                if (trainModel)
                {
                    ProcessTradeParams(e);

                    pastTime = e.Time - lastSampleTime > TimeSpan.FromSeconds(sampleInterval);
                    if (pastTime)
                    {
                        InitializeTradeParams();
                        lastSampleTime = e.Time;
                    }
                }

                if (trainModel)
                {
                    if (CurrentBar < 3)
                        return;

                    if (e.Time - lastDay > TimeSpan.FromHours(1))
                    {
                        Print("Current Date:" + Time[0]);
                        lastDay = Time[0];
                    }

                    double closePrice = e.Price;
                    double closestPrice = FindClosestKey(aggregatedVolumes, closePrice);
					double closestPBPrice = FindClosestKey(aggregatedPullbackVol, closePrice);

                    if (aggregatedVolumes.TryGetValue(closestPrice, out VolumeData segmentVol))
                    {
                        double buyVolume = segmentVol.AskVolume;
                        double buyVolumeP1 = aggregatedVolumes.ContainsKey(closestPrice + tickStacking * TickSize)
                            ? aggregatedVolumes[closestPrice + tickStacking * TickSize].AskVolume
                            : buyVolume;
                        double sellVolume = segmentVol.BidVolume;
                        double sellVolumeM1 = aggregatedVolumes.ContainsKey(closestPrice - tickStacking * TickSize)
                            ? aggregatedVolumes[closestPrice - tickStacking * TickSize].BidVolume
                            : sellVolume;

                        double imbVolP1 = sellVolume - buyVolumeP1;
                        double advDetectionP1 = buyVolumeP1;
                        double tradeRatioP1 = sellVolume / buyVolumeP1;

                        double imbVolM1 = buyVolume - sellVolumeM1;
                        double advDetectionM1 = sellVolumeM1;
                        double tradeRatioM1 = buyVolume / sellVolumeM1;

                        double imbVol = Math.Abs(buyVolume - sellVolume);
                        double advDetection = Math.Min(buyVolume, sellVolume);
                        double tradeRatio = Math.Min(buyVolume, sellVolume) > 0 ? Math.Max(buyVolume, sellVolume) / Math.Min(buyVolume, sellVolume) : Math.Max(buyVolume, sellVolume);
						
						double buyVolumePB, buyVolumeP1PB, sellVolumePB, sellVolumeM1PB, imbVolPB = 0, advDetectionPB = 0, tradeRatioPB = 0;
						if(aggregatedPullbackVol.TryGetValue(closestPBPrice, out VolumeData PBVol)){
							
							buyVolumePB = PBVol.AskVolume;
	                        buyVolumeP1PB = aggregatedVolumes.ContainsKey(closestPBPrice + tickStacking * TickSize)
	                            ? aggregatedVolumes[closestPBPrice + tickStacking * TickSize].AskVolume
	                            : buyVolume;
	                        sellVolumePB = PBVol.BidVolume;
	                        sellVolumeM1PB = aggregatedVolumes.ContainsKey(closestPBPrice - tickStacking * TickSize)
	                            ? aggregatedVolumes[closestPBPrice - tickStacking * TickSize].BidVolume
	                            : sellVolume;
							
							imbVolPB = Math.Abs(buyVolumePB - sellVolumePB);
                        	advDetectionPB = Math.Min(buyVolumePB, sellVolumePB);
                        	tradeRatioPB = Math.Min(buyVolumePB, sellVolumePB) > 0 ? Math.Max(buyVolumePB, sellVolumePB) / Math.Min(buyVolumePB, sellVolumePB) : Math.Max(buyVolumePB, sellVolumePB);
						}

                        string trendDirection = "";
                        string regressDirection = "";

                        if (calculationMode == ImbalanceMode.Horizontal)
                        {
                            if (buyVolume > sellVolume)
                            {
                                tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
                                trendDirection = "Long";
                                regressDirection = "Short";
                            }
                            else if (sellVolume > buyVolume)
                            {
                                tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
                                trendDirection = "Short";
                                regressDirection = "Long";
                            }

                            foreach (var tradeParams in tradeParamsList.Where(tp => tp.allowInTrade))
                            {
                                if ((
									advDetection >= tradeParams.AdvDetectionThreshold &&
                                    imbVol >= tradeParams.ImbVolThreshold &&
                                    tradeRatio >= tradeParams.RatioThreshold
									)
									|| 
									(
									advDetectionPB >= tradeParams.AdvDetectionThreshold &&
                                    imbVolPB >= tradeParams.ImbVolThreshold &&
                                    tradeRatioPB >= tradeParams.RatioThreshold)
									)
                                {

                                    if (trainModel)
                                    {
                                        SimulateTrade(tradeParams, trendDirection, closePrice, "Trend");
										SimulateTrade(tradeParams, regressDirection, closePrice, "Regression");
                                    }

                                    
                                }
                            }
                        }
                        else if (calculationMode == ImbalanceMode.Diagonal)
                        {
                            foreach (var tradeParams in tradeParamsList.Where(tp => tp.allowInTrade))
                            {
                                if (imbVolM1 >= tradeParams.ImbVolThreshold)
                                {
                                    tradeRatio = sellVolumeM1 > 0 ? buyVolume / sellVolumeM1 : buyVolume;
                                    trendDirection = "Long";
                                    regressDirection = "Short";
                                }
                                else if (imbVolP1 >= tradeParams.ImbVolThreshold)
                                {
                                    tradeRatio = buyVolumeP1 > 0 ? sellVolume / buyVolumeP1 : sellVolume;
                                    trendDirection = "Short";
                                    regressDirection = "Long";
                                }

                                if ((advDetectionM1 >= tradeParams.AdvDetectionThreshold &&
                                     imbVolM1 >= tradeParams.ImbVolThreshold &&
                                     tradeRatioM1 >= tradeParams.RatioThreshold) ||
                                    (advDetectionP1 >= tradeParams.AdvDetectionThreshold &&
                                     imbVolP1 >= tradeParams.ImbVolThreshold &&
                                     tradeRatioP1 >= tradeParams.RatioThreshold))
                                {

                                    if (trainModel)
                                    {
                                        SimulateTrade(tradeParams, trendDirection, closePrice, "Trend");
										 SimulateTrade(tradeParams, regressDirection, closePrice, "Regression");
                                    }

                                }
                                
                            }
                        }
                    }
                }
            }

            if (State == State.Realtime)
            {
                if (!isAtmStrategyCreated)
                    return;

                // Check pending orders
                if (orderId.Length > 0)
                {
                    string[] status = GetAtmStrategyEntryOrderStatus(orderId);
                    if (status.Length > 0)
                    {
                        if (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected")
                            orderId = string.Empty;
                    }
                }
                else if (atmStrategyId.Length > 0 && atmStrategyId != string.Empty && GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat)
                {
                    atmStrategyId = string.Empty;
                }
            }
        }

        #endregion

        #region Helper Methods

        // Returns the key closest to the target in the given dictionary.
        private double FindClosestKey(Dictionary<double, VolumeData> dict, double targetPrice)
        {
            if (dict == null || dict.Count == 0)
                return targetPrice;
            return dict.Keys.OrderBy(key => Math.Abs(key - targetPrice)).First();
        }

		private void HandleEntryConditions()
		{
		    // Early exit conditions remain the same
		    if (orderId.Length > 0 || atmStrategyId.Length > 0 || (!longMode && !shortMode) || tradeTaken)
		        return;
		
		    double currentPrice = price;
		    double closestPrice = FindClosestKey(aggregatedVolumes, currentPrice);
		    double closestL1Price = FindClosestKey(aggregatedL1Vol, currentPrice);
		    double closestPBPrice = FindClosestKey(aggregatedPullbackVol, currentPrice);
		
		    // Process aggregated volumes for entry signals
		    if (aggregatedVolumes.TryGetValue(closestPrice, out VolumeData segmentVol) && 
		        aggregatedL1Vol.TryGetValue(closestL1Price, out VolumeData l1Vol))
		    {
		        // Transaction volumes at the price level
		        double buyVolume = segmentVol.AskVolume;  // Buying volume (market buys hitting asks)
		        double sellVolume = segmentVol.BidVolume; // Selling volume (market sells hitting bids)
		        
		        // L1 order book data
		        double l1Ask = l1Vol.AskVolume;  // Available ask liquidity at this level
		        double l1Bid = l1Vol.BidVolume;  // Available bid liquidity at this level
		        
		        // Calculate nearby volume data for context
		        double buyVolumeP1 = aggregatedVolumes.ContainsKey(closestPrice + tickStacking * TickSize)
		            ? aggregatedVolumes[closestPrice + tickStacking * TickSize].AskVolume : 0;
		        double sellVolumeM1 = aggregatedVolumes.ContainsKey(closestPrice - tickStacking * TickSize)
		            ? aggregatedVolumes[closestPrice - tickStacking * TickSize].BidVolume : 0;
		            
		        // Calculate imbalance metrics
		        double volumeImbalance = buyVolume - sellVolume;  // Positive = more buying, Negative = more selling
		        double bookImbalance = l1Bid - l1Ask;            // Positive = more bids, Negative = more asks
		        
		        // Absorption ratios - how much of the available liquidity is being absorbed
		        double askAbsorptionRatio = buyVolume > 0 && l1Ask > 0 ? buyVolume / l1Ask : 0;
		        double bidAbsorptionRatio = sellVolume > 0 && l1Bid > 0 ? sellVolume / l1Bid : 0;
		        
		        bool longSignal = false;
		        bool sellSignal = false;
		        
		        // Logic for Horizontal mode - focusing on current price level dynamics
		        if (calculationMode == ImbalanceMode.Horizontal)
		        {
		            // Long signal when:
		            // 1. High buying volume relative to available ask liquidity (aggressive buying)
		            // 2. Minimum thresholds for activity
		            longSignal = askAbsorptionRatio < 0.05 && sellVolume > buyVolume && l1Ask > l1Bid * 4 && l1Ask > 100;// Buying is absorbing a significant portion of available asks

		            // Sell signal when:
		            // 1. High selling volume relative to available bid liquidity (aggressive selling)
		            // 2. Minimum thresholds for activity
		            sellSignal = bidAbsorptionRatio < 0.05 && buyVolume > sellVolume && l1Bid > l1Ask * 4 && l1Bid > 100; // Selling is absorbing a significant portion of available bids
				}
		        // Logic for Diagonal mode - considering price levels above and below
		        else if (calculationMode == ImbalanceMode.Diagonal)
		        {
		            // Calculate diagonal pressure
		            double buyPressure = buyVolume - sellVolumeM1;  // Buying at current vs selling at lower level
		            double sellPressure = sellVolume - buyVolumeP1; // Selling at current vs buying at higher level
		            
		            // Long signal when buying pressure exceeds threshold
		            longSignal = buyPressure > minVolume && 
		                        buyVolume > minDetection && 
		                        l1Ask < l1Bid * minRatio; // Less resistance above than support below
		            
		            // Sell signal when selling pressure exceeds threshold
		            sellSignal = sellPressure > minVolume && 
		                        sellVolume > minDetection && 
		                        l1Bid < l1Ask * minRatio; // Less support below than resistance above
		        }
		
		        // Execute trades based on signals and current market conditions
		        if (longMode)
		        {
		            // In Trend mode, buy when we have a long signal and spread shows potential for upward momentum
		            if (longSignal && currentMode == TradingMode.Trend )
		                SubmitAtmStrategy(OrderAction.Buy, closestPrice);
		            
		            // In Regression mode, sell to close a long position when we see strength fading
		            if (longSignal && currentMode == TradingMode.Regression)
		                SubmitAtmStrategy(OrderAction.Sell, closestPrice);
		        }
		        
		        if (shortMode)
		        {
		            // In Trend mode, sell when we have a sell signal and spread shows potential for downward momentum
		            if (sellSignal && currentMode == TradingMode.Trend )
		                SubmitAtmStrategy(OrderAction.Sell, closestPrice);
		            
		            // In Regression mode, buy to close a short position when we see weakness fading
		            if (sellSignal && currentMode == TradingMode.Regression)
		                SubmitAtmStrategy(OrderAction.Buy, closestPrice);
		        }
		    }

			
			if(tradeTaken)
				return;

//            // Process aggregated pullback volumes
//            if (aggregatedPullbackVol.TryGetValue(closestPBPrice, out VolumeData PBVol))
//            {
//                double buyVolume = PBVol.AskVolume;
//                double buyVolumeP1 = aggregatedVolumes.ContainsKey(closestPBPrice + tickStacking * TickSize)
//                    ? aggregatedVolumes[closestPBPrice + tickStacking * TickSize].AskVolume : buyVolume;
//                double sellVolume = PBVol.BidVolume;
//                double sellVolumeM1 = aggregatedVolumes.ContainsKey(closestPBPrice - tickStacking * TickSize)
//                    ? aggregatedVolumes[closestPBPrice - tickStacking * TickSize].BidVolume : sellVolume;

//                double imbVolP1 = sellVolume - buyVolumeP1;
//                double advDetectionP1 = buyVolumeP1;
//                double tradeRatioP1 = sellVolume / buyVolumeP1;

//                double imbVolM1 = buyVolume - sellVolumeM1;
//                double advDetectionM1 = sellVolumeM1;
//                double tradeRatioM1 = buyVolume / sellVolumeM1;

//                double imbVol = Math.Abs(buyVolume - sellVolume);
//                double advDetection = Math.Min(buyVolume, sellVolume);
//                double tradeRatio = Math.Min(buyVolume, sellVolume) > 0
//                    ? Math.Max(buyVolume, sellVolume) / Math.Min(buyVolume, sellVolume)
//                    : Math.Max(buyVolume, sellVolume);

//                bool longSignal = false;
//                bool sellSignal = false;

//                if (imbVol > minVolume && advDetection > minDetection && tradeRatio > minRatio)
//                {
//					if(calculationMode == ImbalanceMode.Horizontal){
//                    if (buyVolume > sellVolume)
//                        longSignal = true;
					
//                    if (sellVolume > buyVolume)
//                        sellSignal = true;
//					}
					
//					//PrintAggregatedVolumes(aggregatedVolumes, "Aggregated Volumes From PB Bar (CSV Style):");
//				}
//				if( (imbVolM1 > minVolume && advDetectionM1 > minDetection && tradeRatioM1 > minRatio) ||
//                    (imbVolP1 > minVolume && advDetectionP1 > minDetection && tradeRatioP1 > minRatio)){
//					if(calculationMode == ImbalanceMode.Diagonal){
					
//                    if (imbVolM1 > minVolume)
//                        longSignal = true;
//                    if (imbVolP1 > minVolume)
//                        sellSignal = true;
//					}

//                    //PrintAggregatedVolumes(aggregatedVolumes, "Aggregated Volumes From PB Bar (CSV Style):");
					
//                }
//                if (longMode)
//                {
//                    if (longSignal && currentMode == TradingMode.Trend)
//                        SubmitAtmStrategy(OrderAction.Buy);
//                    if (longSignal && currentMode == TradingMode.Regression)
//                        SubmitAtmStrategy(OrderAction.Sell);
//                }
//                if (shortMode)
//                {
//                    if (sellSignal && currentMode == TradingMode.Trend)
//                        SubmitAtmStrategy(OrderAction.Sell);
//                    if (sellSignal && currentMode == TradingMode.Regression)
//                        SubmitAtmStrategy(OrderAction.Buy);
//                }
//            }
        }

        // Helper to print aggregated volumes in CSV style.
        private void PrintAggregatedVolumes(Dictionary<double, VolumeData> aggVolumes, string header)
        {
            var csvList = aggVolumes.OrderBy(kvp => kvp.Key)
                                    .Select(kvp => $"{kvp.Value.BidVolume},{kvp.Value.AskVolume}")
                                    .ToList();
            string csvOutput = string.Join(",", csvList);
            Print(header);
            Print(csvOutput);
        }

        private void SubmitAtmStrategy(OrderAction action, double closestPrice)
        {
            if (State != State.Realtime)
                return;

            isAtmStrategyCreated = false;
            orderId = GetAtmStrategyUniqueId();
            atmStrategyId = GetAtmStrategyUniqueId();

			
            AtmStrategyCreate(
                action,
                OrderType.Limit,
                closestPrice,
                0,
                TimeInForce.Gtc,
                orderId,
                ATMStrategy,
                atmStrategyId,
                (atmCallbackErrorCode, atmCallBackId) =>
                {
                    if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        isAtmStrategyCreated = true;
                }
            );

            //resetButtons();
            tradeTaken = true;
        }

        int lastSampleBar = 0;
        private void InitializeTradeParams()
        {
            if (lastSampleBar == 0)
            {
                lastSampleBar = CurrentBar;
            }

            if (CurrentBar == lastSampleBar)
                return;

            lastSampleBar = CurrentBar;
            DateTime tradeWindowEndTime = currentTime.AddSeconds(tradesWindowSeconds);

            foreach (var kvp in aggregatedVolumes)
            {
                double price = kvp.Key;
                double buyVolume = kvp.Value.AskVolume;
                double sellVolume = kvp.Value.BidVolume;
                double buyVolumeP1 = aggregatedVolumes.ContainsKey(kvp.Key + tickStacking * TickSize) ? aggregatedVolumes[kvp.Key + tickStacking * TickSize].AskVolume : buyVolume;
                double sellVolumeM1 = aggregatedVolumes.ContainsKey(kvp.Key - tickStacking * TickSize) ? aggregatedVolumes[kvp.Key - tickStacking * TickSize].BidVolume : sellVolume;

                double imbVolP1 = sellVolume - buyVolumeP1;
                double advDetectionP1 = buyVolumeP1;
                double tradeRatioP1 = sellVolume / buyVolumeP1;

                double imbVol = Math.Abs(buyVolume - sellVolume);
                double advDetection = Math.Min(buyVolume, sellVolume);
                double tradeRatio = Math.Min(buyVolume, sellVolume) > 0 ? Math.Max(buyVolume, sellVolume) / Math.Min(buyVolume, sellVolume) : Math.Max(buyVolume, sellVolume);

                if (calculationMode == ImbalanceMode.Horizontal)
                {
                    if (buyVolume > sellVolume)
                    {
                        tradeRatio = sellVolume > 0 ? buyVolume / sellVolume : buyVolume;
                    }
                    else if (sellVolume > buyVolume)
                    {
                        tradeRatio = buyVolume > 0 ? sellVolume / buyVolume : sellVolume;
                    }

                    if (Math.Abs(buyVolume - sellVolume) > 0 && Math.Min(buyVolume, sellVolume) > 2)
                    {

                            NewTradeParameters tradeParams = new NewTradeParameters(
                                Math.Abs(buyVolume - sellVolume),
                                Math.Min(buyVolume, sellVolume),
                                tradeRatio,
                                tradeWindowEndTime
                            );
						
                            bool alreadyExists = tradeParamsList.Any(tp =>
                                tp.IsActive &&
                                tp.ImbVolThreshold == tradeParams.ImbVolThreshold &&
                                tp.AdvDetectionThreshold == tradeParams.AdvDetectionThreshold &&
                                tp.RatioThreshold == tradeParams.RatioThreshold);
                            if (!alreadyExists)
                            {
                                tradeParamsList.Add(tradeParams);
                            }
                        
                    }
                }
                else if (calculationMode == ImbalanceMode.Diagonal)
                {
                    double askVol = 0, bidVol = 0;
                    if (buyVolume > sellVolumeM1)
                    {
                        tradeRatio = sellVolumeM1 > 0 ? buyVolume / sellVolumeM1 : buyVolume;
                    }
                    else if (sellVolume > buyVolumeP1)
                    {
                        tradeRatio = buyVolumeP1 > 0 ? sellVolume / buyVolumeP1 : sellVolume;
                    }

                    if ((imbVolP1 > 0 && buyVolumeP1 > 2) || (imbVolP1 <= 0 && sellVolumeM1 > 2))
                    {
                        if (imbVolP1 > 0 && buyVolumeP1 > 2)
                        {
                            askVol = buyVolumeP1;
                            bidVol = sellVolume;
                        }
                        if (imbVolP1 <= 0 && sellVolumeM1 > 2)
                        {
                            askVol = buyVolume;
                            bidVol = sellVolumeM1;
                        }

                        {
                            TradeType tradeType = currentMode == TradingMode.Trend ? TradeType.Trend : TradeType.Regress;
                            NewTradeParameters tradeParams = new NewTradeParameters(
                                Math.Abs(askVol - bidVol),
                                Math.Min(askVol, bidVol),
                                tradeRatio,
                                tradeWindowEndTime
                            );
                            bool alreadyExists = tradeParamsList.Any(tp =>
                                tp.IsActive &&
                                tp.ImbVolThreshold == tradeParams.ImbVolThreshold &&
                                tp.AdvDetectionThreshold == tradeParams.AdvDetectionThreshold &&
                                tp.RatioThreshold == tradeParams.RatioThreshold);
                            if (!alreadyExists)
                            {
                                tradeParamsList.Add(tradeParams);
                            }
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
            Dictionary<double, VolumeData> aggregated = new Dictionary<double, VolumeData>();

            int ticksPerSegment = tickStacking; // Number of ticks in each segment
            double segmentSize = ticksPerSegment * TickSize;
            int totalTicks = (int)Math.Round((barHigh - barLow) / TickSize) + 1;
            int fullSegments = (totalTicks + ticksPerSegment - 1) / ticksPerSegment;
            const double epsilon = 1e-6;

            foreach (var kvp in volumes)
            {
                double price = kvp.Key;
                VolumeData data = kvp.Value;
                if (price >= barLow - epsilon && price <= barHigh + epsilon)
                {
                    int segmentIndex = (int)Math.Floor((price - barLow) / segmentSize);
                    segmentIndex = Math.Min(segmentIndex, fullSegments - 1);
                    double pointKey = barLow + segmentIndex * segmentSize;

                    if (!aggregated.TryGetValue(pointKey, out VolumeData aggData))
                    {
                        aggData = new VolumeData(0, 0);
                        aggregated[pointKey] = aggData;
                    }
                    aggData.AskVolume += data.AskVolume;
                    aggData.BidVolume += data.BidVolume;
                }
            }

            return aggregated;
        }

        private double GetAggregatedPriceLevel(double price)
        {
            int tickAggregation = Math.Max(1, tickStacking);
            double adjustedPrice = price + TickSize * 1e-6;
            int priceInTicks = (int)Math.Floor(adjustedPrice / TickSize);
            int zoneIndex = priceInTicks / tickAggregation;
            int aggregatedPriceInTicks = zoneIndex * tickAggregation;
            return aggregatedPriceInTicks * TickSize;
        }

       private void SimulateTrade(NewTradeParameters tradeParams, string direction, double price, string tradingMode)
		{
		    double entryPrice = price;
		    NewSimTrade newTrade = new NewSimTrade(price, direction, tradeParams, tradingMode);
		
		    // Initialize trade parameters if not set
		    if (string.IsNullOrEmpty(tradeParams.TradingMode) &&
		        tradeParams.BolDif == 0 &&
		        tradeParams.VolumeSpeed == 0 &&
		        tradeParams.MADif == 0 &&
		        tradeParams.StdDev == 0 &&
		        tradeParams.TOD == 0)
		    {
		        tradeParams.TradingMode = tradingMode;
		        tradeParams.BolDif = PATIMachineLearningInputsV2().BollingerDiff[0];
		        tradeParams.VolumeSpeed = PATIMachineLearningInputsV2().VolumeSpeedPerSecond[0];
		        tradeParams.MADif = PATIMachineLearningInputsV2().MovingAvgDiff[0];
		        tradeParams.StdDev = PATIMachineLearningInputsV2().StdDevBB[0];
		        tradeParams.TOD = PATIMachineLearningInputsV2().TimeOfDay[0];
		    }
		
		    // Increment duplicate parameter values to keep trades distinct
		    tradeParams.BolDif = IncrementTradeParameter(tradeParams.BolDif, "BolDif");
		    tradeParams.VolumeSpeed = IncrementTradeParameter(tradeParams.VolumeSpeed, "VolumeSpeed");
		    tradeParams.MADif = IncrementTradeParameter(tradeParams.MADif, "MADif");
		    tradeParams.StdDev = IncrementTradeParameter(tradeParams.StdDev, "StdDev");
		    tradeParams.TOD = IncrementTradeParameter(tradeParams.TOD, "TOD");
		
		    tradeParams.allowInTrade = false;
		    simTrades.Add(newTrade);
		}
		
        // Helper method to increment a parameter if a duplicate exists among trade parameters.
		private double IncrementTradeParameter(double parameter, string paramName)
		{
		    bool incremented = false;
		    foreach (var par in tradeParamsList)
		    {
		        double valueToCompare = 0;
		        switch (paramName)
		        {
		            case "BolDif":
		                valueToCompare = par.BolDif;
		                break;
		            case "VolumeSpeed":
		                valueToCompare = par.VolumeSpeed;
		                break;
		            case "MADif":
		                valueToCompare = par.MADif;
		                break;
		            case "StdDev":
		                valueToCompare = par.StdDev;
		                break;
		            case "TOD":
		                valueToCompare = par.TOD;
		                break;
		        }
		        if (!incremented && Math.Abs(valueToCompare - parameter) < 1e-6)
		        {
		            parameter += 0.01;
		            incremented = true;
		        }
		        if (incremented)
		            break;
		    }
		    return parameter;
		}

        private void UpdateSimTrades(double target, double stopLoss, double price)
        {
            foreach (NewSimTrade trade in simTrades)
            {
                double entryPrice = trade.EntryPrice;
                double currentPrice = price;
                string positionType = trade.Direction;
                if (positionType == "Long")
                {
                    if (currentPrice > entryPrice + (target * TickSize))
                        UpdateTradeStatus(trade, "Target Hit");
                    else if (currentPrice <= entryPrice - (stopLoss * TickSize))
                        UpdateTradeStatus(trade, "Stop Loss Hit");
                }
                else if (positionType == "Short")
                {
                    if (currentPrice < entryPrice - (target * TickSize))
                        UpdateTradeStatus(trade, "Target Hit");
                    else if (currentPrice >= entryPrice + (stopLoss * TickSize))
                        UpdateTradeStatus(trade, "Stop Loss Hit");
                }
            }
            simTrades.RemoveAll(tr => tr.IsCompleted);
        }

        private void UpdateTradeStatus(NewSimTrade trade, string status)
        {
            trade.Status = status;
            trade.IsCompleted = true;

            if (trade.Status == "Target Hit") {
				
				if (trade.TradeDirection == "Trend") {
                	trade.TradeParams.TrendWinCount++;
					trade.TradeParams.TrendTradeCount++;
				} else if (trade.TradeDirection == "Regression") {
					 trade.TradeParams.RegressWinCount++;
					 trade.TradeParams.RegressTradeCount++;
				}
			} else {
				if (trade.TradeDirection == "Trend") {
                	trade.TradeParams.TrendTradeCount++;
				} else if (trade.TradeDirection == "Regression") {
					 trade.TradeParams.RegressTradeCount++;
				}
			}
            if (trade.TradeParams != null)
                trade.TradeParams.allowInTrade = true;
        }

        private void UpdateWinRateForTradeParams(NewTradeParameters tradeParams, double target, double stopLoss)
        {
			tradeParams.TrendWinRate = tradeParams.TrendTradeCount > 0 ? (double) tradeParams.TrendWinCount / tradeParams.TrendTradeCount : 0; 
			tradeParams.RegressionWinRate = tradeParams.RegressTradeCount > 0 ? (double) tradeParams.RegressWinCount / tradeParams.RegressTradeCount : 0; 
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
            tradeParamsList.RemoveAll(trp => e.Time > trp.TradeWindowEndTime);
            completedTradeParamsBuffer.AddRange(justCompleted);

            if (trainModel && State == State.Historical)
            {
                WriteTradesToCsv(completedTradeParamsBuffer);
                completedTradeParamsBuffer.Clear();
            }
            else if ((justCompleted.Any() && State == State.Realtime && trainModel))
            {
                WriteTradesToCsv(completedTradeParamsBuffer);
                completedTradeParamsBuffer.Clear();
            }
        }

        private void WriteTradesToCsv(List<NewTradeParameters> completedTradeParams)
        {
            if (!trainModel)
                return;
            if (completedTradeParams == null || completedTradeParams.Count == 0)
                return;
			
            string mainTrainPath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\train_model.csv";

            try
            {
                if (trainModel)
                {
                    WriteTradesToSingleFile(completedTradeParams, mainTrainPath);
                    completedTradeParams.Clear();
                    return;
                }

                completedTradeParams.Clear();
            }
            catch (Exception ex)
            {
                Print($"Error writing to CSV: {ex.Message}");
            }
        }

        private void WriteTradesToSingleFile(List<NewTradeParameters> tradeParamsList, string filePath)
        {
            if (tradeParamsList == null || tradeParamsList.Count == 0)
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            bool fileExists = File.Exists(filePath);
            bool headerExists = false;

            if (fileExists)
            {
                string firstLine = File.ReadLines(filePath).FirstOrDefault();
                headerExists = firstLine != null && firstLine.StartsWith("TrendWinRate,RegressionWinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD");
            }

            using (StreamWriter writer = new StreamWriter(filePath, append: true))
            {
                if (!headerExists)
                {
                    writer.WriteLine("TrendWinRate,RegressionWinRate,ImbVol,ImbRatio,AdversaryDetection,VolumeSpeed,MADif,BolDif,StdDev,TOD");
                    headerExists = true;
                }

                StringBuilder sb = new StringBuilder();
                var moreThan0Trades = tradeParamsList.OrderBy(tp => tp.TOD).Where(tp => tp.RegressTradeCount > 0 || tp.TrendTradeCount > 0).ToList();
                foreach (var tradeParams in moreThan0Trades)
                {
                    sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}\n",
                        Math.Round(tradeParams.TrendWinRate, 2),
						Math.Round(tradeParams.RegressionWinRate, 2),
                        tradeParams.ImbVolThreshold,
                        Math.Round(tradeParams.RatioThreshold, 2),
                        tradeParams.AdvDetectionThreshold,
                        Math.Round(tradeParams.VolumeSpeed, 2),
                        Math.Round(tradeParams.MADif, 2),
                        Math.Round(tradeParams.BolDif, 2),
                        Math.Round(tradeParams.StdDev, 2),
                        Math.Round(tradeParams.TOD, 2)
                    );
                }
                writer.Write(sb.ToString());
            }
        }

		private void WriteCurrentPredictiveValuesToServer()
		{

		    var predictiveValues = new
		    {
		        VolumeSpeed = PATIMachineLearningInputsV2().VolumeSpeedPerSecond[0],
		        MADif = PATIMachineLearningInputsV2().MovingAvgDiff[0],
		        BolDif = PATIMachineLearningInputsV2().BollingerDiff[0],
		        StdDev = PATIMachineLearningInputsV2().StdDevBB[0],
		        TOD = PATIMachineLearningInputsV2().TimeOfDay[0],
		        // CurrentModel is used by the regression model logic,
		        // but the classification model will only use PriceDif and Delta.
		        CurrentModel = (currentMode == TradingMode.Regression) ? "Regression" : "Trend"
		    };
		
		    try
		    {
		        NewHttpClientWrapper.Post("current_predictive_values", predictiveValues);
		    }
		    catch (Exception ex)
		    {
		        Print($"Error updating current predictive values: {ex.Message}");
		    }
		}

		double prevratio;
		int prevvol;
		int prevdet;
		double trendWR;
		double regressWR;
		private void ReadOptimizedParamsFromServer()
		{
		    try
		    {
		        var optimizedParams = NewHttpClientWrapper.Get("optimized_params");
		        if (optimizedParams == null)
		        {
		            Print("Failed to retrieve response from server.");
		            return;
		        }
		
		        // 1. Parse Regression outputs
		        int newMinVolume = Convert.ToInt32(optimizedParams["ImbVol"]);
		        double newRatio = Convert.ToDouble(optimizedParams["ImbRatio"]);
		        int newDetectionValue = Convert.ToInt32(optimizedParams["AdversaryDetection"]);
				trendWR = Convert.ToDouble(optimizedParams["TrendWinRate"]);
				regressWR = Convert.ToDouble(optimizedParams["RegressionWinRate"]);
		
		        // 3. Update regression-based parameters if changed.
		        if (newMinVolume != prevvol || newRatio != prevratio || newDetectionValue != prevdet)
		        {
		            minVolume = newMinVolume;
		            minRatio = newRatio;
		            minDetection = newDetectionValue;
		            
		            if (State == State.Realtime)
		                Print($"MinVol: {minVolume}, Ratio: {minRatio}, AdvDet: {minDetection}, trendWR: {trendWR}, regressWR: {regressWR}");
		
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

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "ATMStrategy", Order = 1, GroupName = "Parameters")]
        public string ATMStrategy { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum Imbalance Volume", Order = 2, GroupName = "Imbalances")]
        public int minVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum Ratio", Order = 3, GroupName = "Imbalances")]
        public double minRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Adversary Detection ", Order = 4, GroupName = "Imbalances")]
        public int minDetection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "tick levels to sum for one imbalance", Order = 5, GroupName = "Imbalances")]
        public int tickStacking { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Number of imbalances to classify a trade", Order = 6, GroupName = "Imbalances")]
        public int imbalancesToTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Way to calculate Imbalances", Order = 7, GroupName = "Imbalances")]
        public ImbalanceMode calculationMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL for ML", GroupName = "Machine Learning Targets", Order = 0)]
        public int StopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "PT for ML", GroupName = "Machine Learning Targets", Order = 0)]
        public int ProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Target WinRate", GroupName = "Machine Learning Targets", Order = 0)]
        public double targetWinRate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Machine Learning?", GroupName = "Machine Learning", Order = 0)]
        public bool MLOn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Parameter Optmization?? (ML needs to be on)", GroupName = "Machine Learning", Order = 0)]
        public bool Optimise { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Train the model", GroupName = "Machine Learning Training", Order = 0)]
        public bool trainModel { get; set; }
		
        [NinjaScriptProperty]
        [Display(Name = "Regression or Trend", GroupName = "Machine Learning Training", Order = 0)]
        public TradingMode modelToTrain { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sample Interval (seconds)", GroupName = "Machine Learning", Order = 0)]
        public double sampleInterval { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade window (seconds)", GroupName = "Machine Learning", Order = 0)]
        public int tradesWindowSeconds { get; set; }

        #endregion
    }
}
