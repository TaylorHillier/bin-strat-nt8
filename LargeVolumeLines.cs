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
        }

        #endregion

        #region Properties

		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }
		
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Threshold", Description = "Minimum number of consecutive bars at a price level to trigger a line.", Order = 2, GroupName = "Parameters")]
        public int Threshold
        {
            get { return threshold; }
            set { threshold = value; }
        }
		
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
                Threshold = 3;
                LineColorUp = Brushes.Green;
				LineColorDown = Brushes.Red;
                LineThickness = 2f;
            }
            else if (State == State.Configure)
            {
               scoreChangeQueue = new Queue<ScoreChange>();
			   probabilityChangeQueue = new Queue<ProbabilityChange>();
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
				
			if(CurrentBar > 1){
             var posteriors = CalculatePosteriors();

            p_h1_d = posteriors.Item1;
            p_h0_d = posteriors.Item2;
			}
			
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
		public double totalvolume;
		double referencePrice = 0;
		bool takeTrade = false;
		double volumeSpeed;
		double currentPrice;
		DateTime lastTime;
		double lastPrice;
		double insideAsk;
		double insideBid;
		double previousAsk = 0;
		double previousBid = 0;
		bool barset = false;
		double bar = 0;
		double score = 0;
		
		private const int WindowSize = 100;
	    private Queue<ScoreChange> scoreChangeQueue = new Queue<ScoreChange>();

	    // Running sums of the scores in the queues
	    private int currentScore = 0;
	    private int currentScoreUp = 0;
	    private int currentScoreDown = 0;
	
		    // Sliding Window for Probability Changes
	    private Queue<ProbabilityChange> probabilityChangeQueue = new Queue<ProbabilityChange>();
	    private double sum_p_h1_d = 0.0;
	    private double sum_p_h0_d = 0.0;
		
		double avg_p_h1_d;
		double avg_p_h0_d;
		
		bool checkDirection = false;
		
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if(!UseAskBid)
                return;
            // Ensure we have at least one bar
            if (CurrentBar < 5)
                return;

			if(CollectData){
				UpdateSimTrades(18, 16);
			}
			
         	volumeSpeed = Volume[0] + Volume[1] + Volume[2];
			
			direction = "na";	

				if ( e.MarketDataType == MarketDataType.Bid)
				{
					insideBid = (double)e.Price;
				}
				else if ( e.MarketDataType == MarketDataType.Ask)
				{
					insideAsk = (double)e.Price;
				}
				
				ProbabilityChange probChange = new ProbabilityChange(p_h1_d, p_h0_d);
		        probabilityChangeQueue.Enqueue(probChange);
		        sum_p_h1_d += probChange.p_h1_d;
		        sum_p_h0_d += probChange.p_h0_d;
		
		        if (probabilityChangeQueue.Count > WindowSize)
		        {
		            ProbabilityChange oldestProbChange = probabilityChangeQueue.Dequeue();
		            sum_p_h1_d -= oldestProbChange.p_h1_d;
		            sum_p_h0_d -= oldestProbChange.p_h0_d;
		        }
		
		        // Calculate moving averages
		        avg_p_h1_d = sum_p_h1_d / probabilityChangeQueue.Count;
		        avg_p_h0_d = sum_p_h0_d / probabilityChangeQueue.Count;
				
				if(e.MarketDataType == MarketDataType.Last){
					if(previousPrice == 0){
						previousPrice = e.Price;
					}
						
					// Initialize score changes
					currentPrice = e.Price;
			        int scoreChange = 0;
			        int scoreUpChange = 0;
			        int scoreDownChange = 0;
			
			        // Apply your scoring logic
			        if (e.Price > previousPrice &&  avg_p_h1_d> 0.6)
			        {
			            scoreChange = 1;
			            scoreUpChange = 1; // Increment scoreUp
			            // scoreDownChange remains 0
			        }
			        else if (e.Price < previousPrice &&  avg_p_h0_d> 0.6)
			        {
			            scoreChange = 1;
			            scoreDownChange = 1; // Increment scoreDown
			            // scoreUpChange remains 0
			        }
			        else if (e.Price > previousPrice && avg_p_h0_d > 0.6)      
			        {
			            scoreChange = -1;
						 scoreDownChange = -1;
						 scoreUpChange = -1;
			            // scoreUpChange and scoreDownChange remain 0
			        }
					else if(e.Price < previousPrice &&  avg_p_h1_d > 0.6)
					{
						 scoreChange = -1;
						 scoreDownChange = -1;
						 scoreUpChange = -1;
					}
			
			        // Create a ScoreChange object with the deltas
			        ScoreChange change = new ScoreChange(scoreChange, scoreUpChange, scoreDownChange);
			
			        // Enqueue the new score change
			        scoreChangeQueue.Enqueue(change);
			
			        // Update the running sums
			        currentScore += change.ScoreDelta;
			        currentScoreUp += change.ScoreUpDelta;
			        currentScoreDown += change.ScoreDownDelta;
			
			        // If the queue exceeds the window size, dequeue the oldest score change
			        if (scoreChangeQueue.Count > WindowSize)
			        {
			            ScoreChange oldestChange = scoreChangeQueue.Dequeue();
			            currentScore -= oldestChange.ScoreDelta;
			            currentScoreUp -= oldestChange.ScoreUpDelta;
			            currentScoreDown -= oldestChange.ScoreDownDelta;
			        }
			
			        // Print the current scores
//			        Print($"Current Score: {currentScore}");
//			        Print($"Current ScoreUp: {currentScoreUp}");
//			        Print($"Current ScoreDown: {currentScoreDown}");
		
					double lastMove;
					if(currentPrice - lastPrice != 0){
						lastMove = currentPrice - lastPrice;
					}else {
						lastMove = 0;
					}
			
					if(Instrument.FullName.StartsWith("NQ"))
					{
						
							if(e.Price > previousPrice + 8 * TickSize || e.Price <  previousPrice - 8 * TickSize)
				            {
								upVolume = 0;
								downVolume = 0;
								totalvolume = 0;
								previousPrice = e.Price;
				            }
							else if((e.Price <= previousPrice + 0 * TickSize || e.Price >=  previousPrice - 0 * TickSize) && e.Price == previousPrice)
								
							if(e.Price > (e.Ask + e.Bid) / 2 ){
								upVolume  += e.Volume;
//								downVolume = 0;
								//Print(upVolume + " up volume at " + currentPrice);
							}
							else if(e.Price <  (e.Ask + e.Bid) / 2 ){
								downVolume += e.Volume;
//								upVolume = 0;
								//Print(downVolume + " down volume at " + currentPrice);
							} else if(e.Price ==  (e.Ask + e.Bid) / 2 ){
								if(lastMove > 0){
									upVolume += e.Volume;
//									downVolume = 0;
								} else if(lastMove < 0){
									downVolume += e.Volume;
//									upVolume = 0;
								}
							}
						
							totalvolume += e.Volume;
						
						 if (upVolume >= Threshold || downVolume >=Threshold)
				         {
							 
								
							takeTrade = true;
							checkDirection = true;
							referencePrice = currentPrice;
							 Print($"Threshold met - UpVolume: {upVolume}, DownVolume: {downVolume}, CurrentPrice: {currentPrice}, Previous Price: {previousPrice}");
						}
//					}
				
	            }
				
				if (e.Price > EMA(20)[0])
		        {
		            direction = "Up";
				
		        }
		        else if (e.Price < EMA(20)[0])
		        {
		            direction = "Down";
			
		        }
		
				if(checkDirection){
					bool priceIncreased = Close[0] >= lastClose + profitTarget * TickSize;
			
					if ((Close[0] > lastClose + profitTarget * TickSize || Close[0] < lastClose - profitTarget * TickSize))
		            {
		                // Update historical data
		                UpdateHistoricalData(priceIncreased);
						lastClose = Close[0];
						checkDirection = false;
		            }
				}
				
				if(streakLines.Count != 0){
					foreach(var line in streakLines){
						
						if(line.Direction == "Up" && e.Price < line.PriceLevel - profitTarget * TickSize && CurrentBar > line.StartBar && !line.HasCrossed){
							line.HasCrossed = true;
							line.EndBar = CurrentBars[0] ;
						}
						
						if(line.Direction == "Down" && e.Price > line.PriceLevel + profitTarget * TickSize && CurrentBar > line.StartBar && !line.HasCrossed){
							line.HasCrossed = true;
							line.EndBar = CurrentBars[0] ;
						}
						
					}
				}
				if(takeTrade && direction != "na")
				{

					int streakStartBar = CurrentBars[0] ;
	                streakLines.Add(new StreakLine
	                {
	                    StartBar = streakStartBar,
	                    PriceLevel =  referencePrice,
						Direction = direction,
						HasCrossed = false
	                });
					
				
					
					if(State == State.Historical && CollectData)
					{
						
						SimTradeLVL newTrade = new SimTradeLVL(
					        currentPrice,
							upVolume > downVolume ? upVolume : downVolume,
							direction,
							volumeSpeed,
							ADX(14)[0],
							RSI(14,2)[0]
					    );
				
					    simTrades.Add(newTrade);
						
					}
					
						
					if(takeTrade ){
						upVolume = 0;
						downVolume = 0;
						totalvolume = 0;
						takeTrade = false;
					}
					
					if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime || Position.MarketPosition != MarketPosition.Flat)
		        	return;
			
		            if (UseAskBid ? direction == "Up" && isTrendMode && isLongMode : isLongMode )
		            {
						
		                 isAtmStrategyCreated = false;
				    	orderId = GetAtmStrategyUniqueId();
				    	atmStrategyId = GetAtmStrategyUniqueId();
				
				    	AtmStrategyCreate(
				        OrderAction.Buy,
				         UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ?  previousPrice - limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
				        orderId, ATMStrategy, atmStrategyId,
				        (atmCallbackErrorCode, atmCallBackId) =>
				        {
				            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
				            {
				                isAtmStrategyCreated = true;
				            }
				        });
		                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
//						resetButtons();
//						EnterLong(2,"Long");
//						SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[1]- Low[1]) * 4 * 2);
//						SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[1] - Low[1]) * 4 * 2);
						
		            }

		            if ( UseAskBid ? direction == "Down"  && isTrendMode &&  isShortMode   : isShortMode )
		            {
						
		                isAtmStrategyCreated = false;
				    	orderId = GetAtmStrategyUniqueId();
				    	atmStrategyId = GetAtmStrategyUniqueId();
				
				    	AtmStrategyCreate(
				        OrderAction.Sell,
				         UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ?  previousPrice + limitOrderOffset * TickSize : 0,0, TimeInForce.Gtc,
				        orderId, ATMStrategy, atmStrategyId,
				        (atmCallbackErrorCode, atmCallBackId) =>
				        {
				            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
				            {
				                isAtmStrategyCreated = true;
				            }
				        });
		                Print($"[{Time[0]}] Entering Short Position (Auto Arm) ");
//						resetButtons();
//						EnterShort(2,"Short");
//						SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[1] - High[1]) * 4 * 2);
//						SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[1]- High[1]) * 4 * 2);
						
		            }
					
					if(!UseAskBid)
						return;
					
					if (direction == "Down"  && isShortMode && isRegressionMode)
		            {
		                 isAtmStrategyCreated = false;
				    	orderId = GetAtmStrategyUniqueId();
				    	atmStrategyId = GetAtmStrategyUniqueId();
				
				    	AtmStrategyCreate(
				        OrderAction.Buy,
				        UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ?  previousPrice - limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
				        orderId, ATMStrategy, atmStrategyId,
				        (atmCallbackErrorCode, atmCallBackId) =>
				        {
				            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
				            {
				                isAtmStrategyCreated = true;
				            }
				        });
		                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
						//resetButtons();
						
//						EnterLong(2,"Long");
//						SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[1]- High[1]) * 4 * 2);
//						SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[1] - High[1]) * 4 * 2);
						
		            }
					
					if (direction == "Up"  && isLongMode && isRegressionMode  )
		            {
		                 isAtmStrategyCreated = false;
				    	orderId = GetAtmStrategyUniqueId();
				    	atmStrategyId = GetAtmStrategyUniqueId();
				
				    	AtmStrategyCreate(
				        OrderAction.Sell,
				        UseLimit ? OrderType.Limit : OrderType.Market, UseLimit ?  previousPrice + limitOrderOffset * TickSize : 0, 0, TimeInForce.Gtc,
				        orderId, ATMStrategy, atmStrategyId,
				        (atmCallbackErrorCode, atmCallBackId) =>
				        {
				            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
				            {
				                isAtmStrategyCreated = true;
				            }
				        });
		                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
//						resetButtons();
						
//							EnterShort(2,"Short");
//						SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[1] - Low[1]) * 4 * 2);
//						SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[1]- Low[1]) * 4 * 2);
						
		            }
					
	            }
				
				
				
				if(streakLines.Count() > numBars){
					streakLines.RemoveAt(0);
				}
				}
				
			   // Manage ATM Strategies and Orders
            if (State == State.Realtime){
	           if (!isAtmStrategyCreated)
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
					
					if(UseLimit){
						if(atmStrategyId.Length > 0 && GetAtmStrategyMarketPosition(atmStrategyId)  == Cbi.MarketPosition.Flat && (e.Price >  referencePrice + 12 * TickSize ||  e.Price <  referencePrice - 12 * TickSize)){
							   AtmStrategyClose(atmStrategyId);
						}
					}
			}
			
        }
		
			private void UpdateTradeStatus(SimTradeLVL trade, string status)
			{
			    trade.Status = status;
				
			    trade.IsCompleted = true;
		
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
		
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

			
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
			
			 SharpDX.Direct2D1.SolidColorBrush textBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White);
			
			    float metricsX = 10;
		    float metricsY = 10;
			
			
			 RenderTarget.DrawText($"Long Probability: {p_h1_d:F3}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY, 200, 20), 
		        textBrush);
			
			
			 RenderTarget.DrawText($"Short Probability: {p_h0_d:F3}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 20, 200, 20), 
		        textBrush);
			
				 RenderTarget.DrawText($"Trading Mode: {(isTrendMode ? "Trend" : "Regression"):F3}", metricsFormat, 
		        new SharpDX.RectangleF(metricsX, metricsY + 40, 200, 20), 
		        textBrush);
			
            if (streakLines == null || streakLines.Count == 0)
                return;

            foreach (var line in streakLines)
            {
                // Calculate bars ago
                int barsAgo = CurrentBar - line.StartBar;
                if (barsAgo < 0)
                    continue; // Future bar, skip

                // Get the pixel position for the start bar
                float x = chartControl.GetXByBarIndex(ChartBars, line.StartBar);
				//float x = chartControl.CanvasLeft;
                if (float.IsNaN(x))
                    continue; // Invalid X position

                // Get the Y position for the price level
                float y = chartScale.GetYByValue(line.PriceLevel);

				 float endX = chartControl.CanvasRight;
				
				if(line.HasCrossed == true){
					endX = chartControl.GetXByBarIndex(ChartBars, line.EndBar);
				}
			

                // Create SharpDX brush (fully qualified to avoid ambiguity)
                SharpDX.Direct2D1.Brush dxBrush = CreateDxBrush(line.Direction == "Up" ? LineColorUp : LineColorDown, RenderTarget);

                // Draw the line using SharpDX.Vector2 fully qualified
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(x, y),
                    new SharpDX.Vector2(endX, y),
                    dxBrush,
                    line.HasCrossed ? LineThickness * 3 : LineThickness);

                // Dispose the brush to free resources
                dxBrush.Dispose();
            }
			
			 textBrush.Dispose();
		   
		    textFormat.Dispose();
		    metricsFormat.Dispose();
        }

        #region Helper Classes

        // Serialization helper for Brush
        public static class Serialize
        {
            public static string BrushToString(System.Windows.Media.Brush brush)
            {
                if (brush is SolidColorBrush solidColorBrush)
                {
                    return solidColorBrush.Color.ToString();
                }
                return Brushes.Red.Color.ToString(); // Default
            }

            public static System.Windows.Media.Brush StringToBrush(string brushString)
            {
                try
                {
                    var color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(brushString);
                    return new SolidColorBrush(color);
                }
                catch
                {
                    return Brushes.Red; // Default
                }
            }
        }

        /// <summary>
        /// Creates a SharpDX.Direct2D1.Brush from a System.Windows.Media.Brush
        /// </summary>
        /// <param name="brush">The WPF brush to convert.</param>
        /// <param name="renderTarget">The render target to create the SharpDX brush.</param>
        /// <returns>A SharpDX.Direct2D1.Brush.</returns>
        private SharpDX.Direct2D1.Brush CreateDxBrush(System.Windows.Media.Brush brush, SharpDX.Direct2D1.RenderTarget renderTarget)
        {
            if (brush is SolidColorBrush solidColorBrush)
            {
                var color = solidColorBrush.Color;
                // Convert System.Windows.Media.Color to SharpDX.Color
                SharpDX.Color dxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);
                return new SharpDX.Direct2D1.SolidColorBrush(renderTarget, dxColor);
            }
            // Add more brush conversions if needed
            // Default to red if not a SolidColorBrush
            return new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color(255, 0, 0, 255));
        }

        #endregion
		
		private int maxSequenceLength = 1000; // Adjust as needed
		
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
		    double rsi = RSI(5, 3)[0] - RSI(5, 3)[1];
		    double adx =  ADX(5)[0] - ADX(5)[1];
		    double macd = MACD(12, 26, 9).Diff[0] - MACD(12, 26, 9).Diff[1];

		    // Calculate volume imbalance
		    double totalBidVolume = upVolume;
		    double totalAskVolume = downVolume;
		
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
		    {
		        // Remove the oldest observation
		        var oldestObservation = observationData[0];
		        observationData.RemoveAt(0);
		
		        bool wasPriceIncrease = oldestObservation.Observation == 1;
		
		        if (wasPriceIncrease)
		        {
		            // Remove data from increases lists
		            historicalAskIncreases.RemoveAt(0);
		            historicalBidIncreases.RemoveAt(0);
		            historicalRsiIncreases.RemoveAt(0);
		            historicalAdxIncreases.RemoveAt(0);
		            historicalMacdIncreases.RemoveAt(0);
		        }
		        else
		        {
		            // Remove data from decreases lists
		            historicalAskDecreases.RemoveAt(0);
		            historicalBidDecreases.RemoveAt(0);
		            historicalRsiDecreases.RemoveAt(0);
		            historicalAdxDecreases.RemoveAt(0);
		            historicalMacdDecreases.RemoveAt(0);
		        }
		    }
		
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
		        priorPriceIncrease = 0.5;
		        priorPriceDecrease = 0.5;
		    }
		    else
		    {
		        priorPriceIncrease = (double)historicalAskIncreases.Count / totalEvents;
		        priorPriceDecrease = (double)historicalAskDecreases.Count / totalEvents;
		    }
		
		    // No need to normalize priors here as they will naturally sum to 1
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
		    double p_d_h1_buys = GaussianPDF(upVolume, muAskIncrease, sigmaAskIncrease); // P(D_buys|H=1)
		    double p_d_h0_buys = GaussianPDF(upVolume, muAskDecrease, sigmaAskDecrease); // P(D_buys|H=0)
		    
		    // Calculate likelihoods based on cumulative sells
		    double p_d_h1_sells = GaussianPDF(downVolume, muBidIncrease, sigmaBidIncrease); // P(D_sells|H=1)
		    double p_d_h0_sells = GaussianPDF(downVolume, muBidDecrease, sigmaBidDecrease); // P(D_sells|H=0)
		    
		    // Combine likelihoods (assuming independence)
		    double p_d_h1 = p_d_h1_buys * p_d_h1_sells; // P(D|H=1)
		    double p_d_h0 = p_d_h0_buys * p_d_h0_sells; // P(D|H=0)
		
		
		    // Incorporate RSI into the likelihood calculation
		    double rsi = RSI(5, 3)[0] - RSI(5, 3)[1];
		    double p_rsi_h1 = GaussianPDF(rsi, muRsiIncrease, sigmaRsiIncrease);
		    double p_rsi_h0 = GaussianPDF(rsi, muRsiDecrease, sigmaRsiDecrease);
		    p_d_h1 *= p_rsi_h1; // P(D|H=1) *= P(RSI|H=1)
		    p_d_h0 *= p_rsi_h0; // P(D|H=0) *= P(RSI|H=0)
		
		    // Incorporate ADX into the likelihood calculation
		    double adx = ADX(5)[0] - ADX(5)[1];
		    double p_adx_h1 = GaussianPDF(adx, muAdxIncrease, sigmaAdxIncrease);
		    double p_adx_h0 = GaussianPDF(adx, muAdxDecrease, sigmaAdxDecrease);
		    p_d_h1 *= p_adx_h1; // P(D|H=1) *= P(ADX|H=1)
		    p_d_h0 *= p_adx_h0; // P(D|H=0) *= P(ADX|H=0)
		
		    // Incorporate MACD into the likelihood calculation
		    double macd = MACD(12, 26, 9).Diff[0] - MACD(12, 26, 9).Diff[1];
		    double p_macd_h1 = GaussianPDF(macd, muMacdIncrease, sigmaMacdIncrease);
		    double p_macd_h0 = GaussianPDF(macd, muMacdDecrease, sigmaMacdDecrease);
		    p_d_h1 *= p_macd_h1; // P(D|H=1) *= P(MACD|H=1)
		    p_d_h0 *= p_macd_h0; // P(D|H=0) *= P(MACD|H=0)
		
		
		    // Calculate marginal likelihood P(D)
		    double p_d = (p_d_h1 * priorPriceIncrease) + (p_d_h0 * priorPriceDecrease);
		
		    // Handle zero marginal likelihood
		    if (p_d == 0)
		    {
		        //Print("[DEBUG] Marginal Likelihood is zero, returning neutral priors.");
		        return (0.5, 0.5);  // Neutral priors when likelihood is zero
		    }
		
		    // Calculate posterior probabilities
		    double p_h1_d = (p_d_h1 * priorPriceIncrease) / p_d; // P(H=1|D)
		    double p_h0_d = (p_d_h0 * priorPriceDecrease) / p_d; // P(H=0|D)
		
		    // Ensure non-zero posteriors
		    if (p_h1_d < 1e-10) p_h1_d = 1e-10;
		    if (p_h0_d < 1e-10) p_h0_d = 1e-10;
		
		    // Print the posterior probabilities
		    //Print($"Posteriors: P(H=1|D): {p_h1_d}, P(H=0|D): {p_h0_d}");
		
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
	    }
}

