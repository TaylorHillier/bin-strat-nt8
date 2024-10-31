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
        }

        #endregion

        #region Properties

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Threshold", Description = "Minimum number of consecutive bars at a price level to trigger a line.", Order = 1, GroupName = "Parameters")]
        public int Threshold
        {
            get { return threshold; }
            set { threshold = value; }
        }
		
		[Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Number of Bars to Show", Description = "Number of Lines to Show.", Order = 2, GroupName = "Parameters")]
        public int numBars
       	{ get; set;}
		
        [XmlIgnore]
        [Display(Name = "Line Color", Description = "Color of the streak lines.", Order = 3, GroupName = "Parameters")]
        public Brush LineColor
        {
            get { return lineBrush; }
            set { lineBrush = value; }
        }
		
		[XmlIgnore]
        [Display(Name = "Line Color Up", Description = "Color of the streak lines.", Order = 3, GroupName = "Parameters")]
        public Brush LineColorUp
        {
            get { return lineBrushUp; }
            set { lineBrushUp = value; }
        }
		
		[XmlIgnore]
        [Display(Name = "Line Color Down", Description = "Color of the streak lines.", Order = 3, GroupName = "Parameters")]
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
        [Display(Name = "Line Thickness", Description = "Thickness of the streak lines.", Order = 4, GroupName = "Parameters")]
        public float LineThickness
        {
            get { return lineThickness; }
            set { lineThickness = value; }
        }

        [ NinjaScriptProperty]
        [Display(Name = "Use Ask/Bid Sizes", Description = "Use Ask/Bid volumes over volume", Order = 4, GroupName = "Parameters")]
        public bool UseAskBid {get; set;}
        
		[NinjaScriptProperty]
        [Display(Name = "Collect Data for Model", Description = "Collect Data for model", Order = 2, GroupName = "Parameters")]
        public bool CollectData {get; set;}
		
		
		[NinjaScriptProperty]
        [Display(Name = "Optimise w ML", Description = "Optimise w ML", Order = 2, GroupName = "Parameters")]
        public bool Optimise {get; set;}
		
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
				LineColor = Brushes.Red;
                LineColorUp = Brushes.Green;
				LineColorDown = Brushes.Red;
                LineThickness = 2f;
            }
            else if (State == State.Configure)
            {
              
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
			
				if(Close[0] > Open[0]){
					direction = "Up";
				}
				else if(Close[0] < Open[0]){
					direction = "Down";
				}
				
				if(e.MarketDataType != MarketDataType.Last)
					return;
				currentPrice = e.Price;
				double askPrice = e.Ask;
				double bidPrice = e.Bid;
				double volume = e.Volume;
			
				double lastMove;
				if(currentPrice - lastPrice != 0){
					lastMove = currentPrice - lastPrice;
				}else {
					lastMove = 0;
				}
			
	            if (Instrument.FullName.StartsWith("NQ") ? currentPrice == previousPrice: currentPrice == previousPrice)
	            {
		
	                currentStreak+=e.Volume;
					
						if(currentPrice > (bidPrice + askPrice) / 2){
							upVolume  += volume;
							totalvolume += volume;
							//downVolume = 0;
							//Print(upVolume + " up volume at " + currentPrice);
						}
						
						if(currentPrice < (bidPrice + askPrice) / 2){
							
							downVolume += volume;
							totalvolume += volume;
							//upVolume = 0;
							//Print(downVolume + " down volume at " + currentPrice);
						}
						if(currentPrice == (bidPrice + askPrice) / 2){
							
							if(lastMove > 0)
							{
								upVolume  += volume;
							}
							if(lastMove < 0)
							{
								downVolume  += volume;
							}
							
							totalvolume += volume;
						}
						
					
						
						 if (upVolume >= Threshold || downVolume >= Threshold)
				            {
								
								takeTrade = true;
								
				
								if(!takeTrade){
								upVolume = 0;
								downVolume = 0;
								totalvolume = 0;
								}
								referencePrice = currentPrice;
							}
	            }
	            if(Instrument.FullName.StartsWith("NQ"))
				{
					if(currentPrice > previousPrice + 0 * TickSize || currentPrice < previousPrice - 0 * TickSize)
			            {
					
								
			                currentStreak = 1;
							
							if(!takeTrade){
							upVolume = 0;
							downVolume = 0;
							totalvolume = 0;
							previousPrice = currentPrice;
							}
							
			            }
						
				}
				else 
				{
					if(currentPrice != previousPrice )
				    {
							
			                currentStreak = 1;
							upVolume = 0;
							downVolume = 0;
						
							previousPrice = currentPrice;
							
			         }
				
				}
				
				lastPrice = currentPrice;
				if(takeTrade && direction != "na")
				{
					
					int streakStartBar = CurrentBars[0] ;
	                streakLines.Add(new StreakLine
	                {
	                    StartBar = streakStartBar,
	                    PriceLevel = previousPrice,
						Direction = direction
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
					
					
					if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
		        	return;
						 
		            if (UseAskBid ? direction == "Up" && isTrendMode && isLongMode : isLongMode )
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
						resetButtons();
						
		            }
		
		            if ( UseAskBid ? direction == "Down"  && isTrendMode &&  isShortMode  : isShortMode )
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
		                Print($"[{Time[0]}] Entering Short Position (Auto Arm) ");
						resetButtons();
						
		            }
					
					if(!UseAskBid)
						return;
					
					if (direction == "Down"  && isShortMode && isRegressionMode )
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
						resetButtons();
						
		            }
					
					if (direction == "Up"  && isLongMode && isRegressionMode )
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
						resetButtons();
						
		            }
					
	            }
				
				
				
				if(streakLines.Count() > numBars){
					streakLines.RemoveAt(0);
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

                // Define the end X position (right edge of the chart)
                float endX = chartControl.CanvasRight;

                // Create SharpDX brush (fully qualified to avoid ambiguity)
                SharpDX.Direct2D1.Brush dxBrush = CreateDxBrush(line.Direction == "Up" ? LineColorUp : LineColorDown, RenderTarget);

                // Draw the line using SharpDX.Vector2 fully qualified
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(x, y),
                    new SharpDX.Vector2(endX, y),
                    dxBrush,
                    LineThickness);

                // Dispose the brush to free resources
                dxBrush.Dispose();
            }
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
		
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }
		
		#endregion;
    }
}

