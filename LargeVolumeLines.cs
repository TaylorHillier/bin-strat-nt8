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
	
	public class HttpClientWrapperLVL
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
		
	public class SimTradeLVL
	{
		public double EntryPrice { get; set; }
		public double Streak { get; set; }
		public string Direction { get; set; }
		public string Status { get; set; }
		public double VolumeSpeed { get; set; }
		public bool IsCompleted { get; set; }
		
		public double ADX { get; set; }
		public double RSI { get; set; }
	
		public SimTradeLVL(double entryPrice, double streak, string direction, double volumeSpeed, double adx, double rsi)
		{
		    EntryPrice = entryPrice;
			Streak = streak;
			Direction = direction;
			VolumeSpeed = volumeSpeed;
			ADX = adx;
			RSI = rsi;
		}
		
	}
	
	public class ScoreChange
	{
	    public int ScoreDelta { get; set; }
	    public int ScoreUpDelta { get; set; }
	    public int ScoreDownDelta { get; set; }
	
	    public ScoreChange(int scoreDelta, int scoreUpDelta, int scoreDownDelta)
	    {
	        ScoreDelta = scoreDelta;
	        ScoreUpDelta = scoreUpDelta;
	        ScoreDownDelta = scoreDownDelta;
	    }
	}
	
	public class ProbabilityChange
	{
	    public double p_h1_d { get; set; }
	    public double p_h0_d { get; set; }
	
	    public ProbabilityChange(double p_h1, double p_h0)
	    {
	        p_h1_d = p_h1;
	        p_h0_d = p_h0;
	    }
	}
	
    public class BayesianVolumeStreakStrategy : Strategy
    {
		[Browsable(false)]
		[XmlIgnore]
		public List<StreakLine> StreakLines
		{
		    get { return streakLines; }
		}
		
        #region Variables

        private int threshold = 3; // Default threshold
        private Brush lineBrush = Brushes.Red;
		private Brush lineBrushUp = Brushes.Green;
		private Brush lineBrushDown = Brushes.Red;
        private float lineThickness = 2f;

        private double currentStreak = 0;
        private double previousPrice = 0.0;

        // List to store lines to be drawn
        private List<StreakLine> streakLines = new List<StreakLine>();

        // Class to represent a streak line
        public class StreakLine
        {
            public int StartBar { get; set; }
            public double PriceLevel { get; set; }
			public string Direction { get; set; }
			public bool HasCrossed { get; set; }
			public int EndBar { get; set; }
			public double UpBound { get; set; }
			public double DownBound { get; set; }
        }

        #endregion

        #region Properties

		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Entry Conditions")]
		public string ATMStrategy
		{ get; set; }
		
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Threshold", Description = "Minimum number of consecutive bars at a price level to trigger a line.", Order = 2, GroupName = "Entry Conditions")]
        public int Threshold
        {
            get { return threshold; }
            set { threshold = value; }
        }
		
			[Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Time Below", Description = "Time at Level to Warrant trade", Order = 3, GroupName = "Entry Conditions")]
        public double LowTimeThreshold
       	{ get; set;}
		
				[Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Time Above", Description = "Time at Level to Warrant trade", Order = 3, GroupName = "Entry Conditions")]
        public double HighTimeThreshold
       	{ get; set;}
		
			[Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Price Range for Accumulation (Ticks)", Description = "Price Range for Accumulation (Ticks)", Order = 4, GroupName = "Entry Conditions")]
        public double PriceRange
       	{ get; set;}
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Number of Bars to Show", Description = "Number of Lines to Show.", Order = 3, GroupName = "Personalization")]
        public int numBars
       	{ get; set;}

		[XmlIgnore]
        [Display(Name = "Line Color Up", Description = "Color of the streak lines.", Order = 5, GroupName = "Personalization")]
        public Brush LineColorUp
        {
            get { return lineBrushUp; }
            set { lineBrushUp = value; }
        }
		
		[XmlIgnore]
        [Display(Name = "Line Color Down", Description = "Color of the streak lines.", Order = 6, GroupName = "Personalization")]
        public Brush LineColorDown
        {
            get { return lineBrushDown; }
            set { lineBrushDown = value; }
        }

        [Browsable(false)]
        public string LineColorSerializable
        {
            get { return Serialize.BrushToString(lineBrush); }
            set { lineBrush = Serialize.StringToBrush(value); }
        }

        [Range(1f, 5f), NinjaScriptProperty]
        [Display(Name = "Line Thickness", Description = "Thickness of the streak lines.", Order = 7, GroupName = "Personalization")]
        public float LineThickness
        {
            get { return lineThickness; }
            set { lineThickness = value; }
        }

        [ NinjaScriptProperty]
        [Display(Name = "Use Ask/Bid Sizes", Description = "Use Ask/Bid volumes over volume", Order = 4, GroupName = "Parameters")]
        public bool UseAskBid {get; set;}
		
		[NinjaScriptProperty]
        [Display(Name = "Collect Data for Model", Description = "Collect Data for model", Order = 8, GroupName = "Machine Learning")]
        public bool CollectData {get; set;}
		
		
		[NinjaScriptProperty]
        [Display(Name = "Optimise w ML", Description = "Optimise w ML", Order = 9, GroupName = "Machine Learning")]
        public bool Optimise {get; set;}
		
		
		[Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Limit order offset", Description = "distance from signal to take trade", Order = 1, GroupName = "Order Handling")]
        public int limitOrderOffset
       	{ get; set;}
		
		[Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Profit Target for Lines and ML", Description = "Profit Target for Lines and ML", Order = 3, GroupName = "Order Handling")]
        public double profitTarget
       	{ get; set;}
		
		[NinjaScriptProperty]
        [Display(Name = "Use Limit Orders", Description = "Use Limit Orders", Order = 2, GroupName = "Order Handling")]
        public bool UseLimit {get; set;}
		
		[NinjaScriptProperty]
        [Display(Name = "Use Moving Average to determine trade direction", Description = "Use Moving Average to determine trade direction", Order = 4, GroupName = "Order Handling")]
        public bool ma {get; set;}
		
        #endregion
		
        private VolumeStreakLines volumeStreakLines;

		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;

		private bool isRegressionMode = false;
		private bool isTrendMode = false;
	
		private int lastProcessedStreakStartBar = -1;
		
		private bool isLongMode = false;
		private bool isShortMode = false;
		private bool isAutoArm = false;
			//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button armButton;
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Bayesian Inference Strategy using VolumeStreakLines Indicator.";
                Name = "BayesianVolumeStreakStrategy";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true; // Overlay on price panel
                
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
				ATMStrategy = "NQ Hyperscalp";
				
                // Set other default properties as needed
				
				   // Default parameters
                Threshold = 50;
                LineColorUp = Brushes.Green;
				LineColorDown = Brushes.Red;
                LineThickness = 1f;
				PriceRange = 4;
				HighTimeThreshold = 2;
				LowTimeThreshold = 10;
				numBars = 20;
            }
            else if (State == State.Configure)
            {
//               scoreChangeQueue = new Queue<ScoreChange>();
//			   probabilityChangeQueue = new Queue<ProbabilityChange>();
            }
            else if (State == State.DataLoaded)
            {
            
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

		double activeBar = -1;
        protected override void OnBarUpdate()
        {
				
//			if(CurrentBar > 1){
//             var posteriors = CalculatePosteriors();

//            p_h1_d = posteriors.Item1;
//            p_h0_d = posteriors.Item2;
//			}
			
			if(CurrentBar != activeBar && State == State.Realtime && Optimise){
			
				WriteCurrentPredictiveValuesToServer();
				ReadOptimizedParamsFromServer();
				activeBar = CurrentBar;
			}
      
			if(UseAskBid)
                return;
            // Ensure we have at least one bar
            if (CurrentBars[0] < 1)
                return;

            double currentPrice = Closes[1][0];
			
			
            if (currentPrice == previousPrice)
            {
		
                currentStreak++;
			
				
            }
            else if(currentPrice != previousPrice)
            {
			
                currentStreak = 1;
                
            }

            if (currentStreak == Threshold)
            {
                int streakStartBar = CurrentBars[0] ;
                streakLines.Add(new StreakLine
                {
                    StartBar = streakStartBar,
                    PriceLevel = currentPrice
                });
            }
			
			if(streakLines.Count() > numBars){
				streakLines.RemoveAt(0);
			}
			
			previousPrice = currentPrice;
			

            
     
        }
			
		public double upVolume;
		public double downVolume;
		public string direction;
		public string currentDirection;
		public double totalvolume;
		double referencePrice = 0;
		bool takeTrade = false;
		double volumeSpeed;
		double currentPrice;
		DateTime lastTime = DateTime.MinValue;
		double lastPrice;
		double insideAsk;
		double insideBid;
		double previousAsk = 0;
		double previousBid = 0;
		bool barset = false;
		double bar = 0;
		double score = 0;
		
		bool checkDirection = false;
		 double entry = 0;
		
		bool last;
		
		bool isInTrade = false;
		
		double midRange;
		double highBound = 0;
		double lowBound = 99999999;
		
		private List<TimeSpan> timeSpentInZones = new List<TimeSpan>();
		
		// Fields for tracking time spent in zones
		private double totalTimeSpentInZones = 0.0; // Running total of all time spent in zones (in seconds)
		private int zoneExitCount = 0; // Counter for the number of times a zone was exited
		
		private const int maxZonePoints = 200;  // Number of points to average
		   double lowSideTime = 0;
		
		TimeSpan timeSpent;
		
		bool below = false;
		bool above = false;
		protected override void OnMarketData(MarketDataEventArgs e)
		{
		    if (!UseAskBid)
		        return;
		
		    // Ensure we have at least one bar
		    if (CurrentBar < 5)
		        return;
		
		    if (CollectData)
		    {
		        UpdateSimTrades(18, 16);
		    }
		
		    volumeSpeed = Volume[0] + Volume[1] + Volume[2];
		
		    direction = "na";
		
		    if (e.MarketDataType == MarketDataType.Bid)
		    {
		        insideBid = (double)e.Price;
		    }
		    else if (e.MarketDataType == MarketDataType.Ask)
		    {
		        insideAsk = (double)e.Price;
		    }
		
		    if (e.MarketDataType == MarketDataType.Last)
			{
			    if (previousPrice == 0)
			    {
			        previousPrice = e.Price;
			    }
			
			    // Track current price and last price to determine movement
			    currentPrice = e.Price;
			    double lastMove = currentPrice - lastPrice;
			
			    // Detect volume accumulation at a particular price level
			    if (Instrument.FullName.StartsWith("NQ"))
			    {
			        // Accumulate volume without significant price movement
			        if (e.Volume > 0)  // Only accumulate if there's volume
			        {
			            if (e.Price > (e.Ask + e.Bid) / 2)
			            {
			                upVolume += e.Volume;
			            }
			            else if (e.Price < (e.Ask + e.Bid) / 2)
			            {
			                downVolume += e.Volume;
			            }
			            else
			            {
			                if (lastMove > 0)
			                {
			                    upVolume += e.Volume;
			                }
			                else if (lastMove < 0)
			                {
			                    downVolume += e.Volume;
			                }
			            }
			        }
					
					        // Update bounds
			        if (currentPrice > highBound)
			        {
			            highBound = currentPrice;
						above = true;
						below = false;
			        }
			
			        if (currentPrice < lowBound)
			        {
			            lowBound = currentPrice;
						below = true;
						above = false;
			        }
					
					TimeSpan timeChange = e.Time - lastTime;
			        // If the price range is exceeded, reset zone and accumulate time spent
			        if (highBound - lowBound > PriceRange * TickSize)
			        {
			
			            // Reset for the next zone
			            previousPrice = currentPrice;
			            upVolume = 0;
			            downVolume = 0;
			           
			        }
					
					
			
			        midRange = (highBound + lowBound) / 2;
			        totalvolume += e.Volume;
			
			        // Compare the current time to the threshold (average time * TimeThreshold)
			        if((upVolume / downVolume > Threshold && downVolume > 0 || downVolume / upVolume > Threshold && upVolume > 0))
			        {
					
			            if (lastTime != DateTime.MinValue)
			            {
			                takeTrade = true;
			                checkDirection = true;
			                referencePrice = currentPrice;
			
							direction = upVolume > downVolume ? "Up" : "Down";
			           
							
			                // Reset volume variables after deciding to take a trade
			                upVolume = 0;
			                downVolume = 0;
			                totalvolume = 0;
			
			                // Reset the time after taking a trade
			                lastTime = e.Time;  // Set to the current event time
						
			            }
			        }
					
					        // If the price range is exceeded, reset zone and accumulate time spent
			        if (highBound - lowBound > PriceRange * TickSize)
			        {

			            lowBound = currentPrice;
			            highBound = currentPrice;
				
			        }
					
			    }
			
		
		        // Add logic to manage streak lines
		        if (streakLines.Count != 0)
		        {
		            foreach (var line in streakLines)
		            {
		                if (line.Direction == "Up" && e.Price < line.PriceLevel - profitTarget * TickSize && CurrentBar > line.StartBar && !line.HasCrossed)
		                {
		                    line.HasCrossed = true;
		                    line.EndBar = CurrentBars[0];
		                }
		
		                if (line.Direction == "Down" && e.Price > line.PriceLevel + profitTarget * TickSize && CurrentBar > line.StartBar && !line.HasCrossed)
		                {
		                    line.HasCrossed = true;
		                    line.EndBar = CurrentBars[0];
		                }
		            }
		        }
		
		        // Take trade if conditions are met
		        if (takeTrade && direction != "na")
		        {
		            int streakStartBar = CurrentBars[0];
		            streakLines.Add(new StreakLine
		            {
		                StartBar = streakStartBar,
		                PriceLevel = midRange,
		                Direction = direction,
		                HasCrossed = false,
						UpBound = highBound,
						DownBound = lowBound
		            });
		
		            if (State == State.Historical && CollectData)
		            {
		                if (!isInTrade)
		                {
		                    SimTradeLVL newTrade = new SimTradeLVL(
		                        currentPrice,
		                        Math.Max(upVolume, downVolume),
		                        direction,
		                        volumeSpeed,
		                        ADX(14)[0],
		                        RSI(14, 2)[0]
		                    );
		
		                    simTrades.Add(newTrade);
		                    isInTrade = true;
		                }
		            }

		            takeTrade = false;
		
		            // Manage ATM strategies and orders
		            if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime || Position.MarketPosition != MarketPosition.Flat)
		                return;
		
		            if (UseAskBid ? direction == "Up" && isTrendMode && isLongMode : isLongMode)
		            {
		                entry = currentPrice;
		                currentDirection = "Up";
		                isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Buy,
		                    UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ? previousPrice - limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
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
		
		            if (UseAskBid ? direction == "Down" && isTrendMode && isShortMode : isShortMode)
		            {
		                entry = currentPrice;
		                currentDirection = "Down";
		                isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Sell,
		                    UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ? previousPrice + limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
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
					
					   if (UseAskBid ? direction == "Up" && isRegressionMode && isLongMode : isLongMode)
		            {
		                entry = currentPrice;
		                currentDirection = "Up";
		                isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Sell,
		                    UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ? previousPrice - limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
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
		
		            if (UseAskBid ? direction == "Down" && isRegressionMode && isShortMode : isShortMode)
		            {
		                entry = currentPrice;
		                currentDirection = "Down";
		                isAtmStrategyCreated = false;
		                orderId = GetAtmStrategyUniqueId();
		                atmStrategyId = GetAtmStrategyUniqueId();
		
		                AtmStrategyCreate(
		                    OrderAction.Buy,
		                    UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ? previousPrice + limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
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
		
		        if (streakLines.Count() > numBars)
		        {
		            streakLines.RemoveAt(0);
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
		
		        if (atmStrategyId.Length > 0 && UseLimit)
		        {
		            if (GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat &&
		                (e.Price > midRange + 20 * TickSize || e.Price < midRange - 20 * TickSize))
		            {
		                AtmStrategyClose(atmStrategyId);
		            }
		        }
		    }
		}

			private void UpdateTradeStatus(SimTradeLVL trade, string status)
			{
			    trade.Status = status;
				
			    trade.IsCompleted = true;
				isInTrade = false;
				WriteTradesToCsv();
			}
		
		private void UpdateSimTrades(double target, double stopLoss)
		{
		    foreach (SimTradeLVL trade in simTrades.Where(t => t.Status == null))
		    {
		        double entryPrice = trade.EntryPrice;
		        string positionType = trade.Direction;
		
		        if (trade.Direction == "Up")
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
		        else if (trade.Direction == "Down")
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
		
		private List<SimTradeLVL> simTrades = new List<SimTradeLVL>();
		
			private void WriteTradesToCsv()
			{
			    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\TaylorML\train_model.csv" ;
			
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
			            headerExists = firstLine != null && firstLine.StartsWith("Streak,Status,Volume,ADX,RSI");
			        }
			
			        using (StreamWriter writer = new StreamWriter(filePath, append: true))
			        {
			            if (!headerExists)
			            {
			                writer.WriteLine("Streak,Status,Volume,ADX,RSI");
			            }
			
			            StringBuilder sb = new StringBuilder();
			            foreach (var trade in simTrades.Where(t => t.IsCompleted))
			            {
					
			                    sb.AppendFormat("{0},{1},{2},{3},{4}\n",
			                        Math.Round(trade.Streak, 2),
			                        trade.Status,
			                        trade.VolumeSpeed,
									trade.ADX,
									trade.RSI
			                    );
			            
			            }
			            writer.Write(sb.ToString());
			        }
			      
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
					Volume = volumeSpeed,
					ADX = ADX(14)[0],
					RSI = RSI(14,2)[0]
				};
				
				try
				{
					HttpClientWrapperLVL.Post("current_predictive_values", predictiveValues);
				}
				catch (Exception ex)
				{
					Print($"Error updating current predictive values: {ex.Message}");
				}
			}

		private void ReadOptimizedParamsFromServer()
		{
		    try
		    {
		        // Attempt to retrieve optimized parameters
		        var optimizedParams = HttpClientWrapperLVL.Get("optimized_params");
		
		        if (optimizedParams == null)
		        {
		            Print("Failed to retrieve response from server.");
		            return;
		        }
		
		        // Check if expected key "Streak" exists in the response
		        if (optimizedParams.TryGetValue("Streak", out object streakValue))
		        {
		            int newThreshold = Convert.ToInt32(streakValue);
		            Threshold = newThreshold;
		            Print($"Updated Threshold: {Threshold}");
		        }
		        else
		        {
		            Print("Key 'Streak' not found in response.");
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
			
			
		
		    // Update the button content or perform any other necessary actions
			
		    
		}

	    }
}

