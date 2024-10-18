 #region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Timers;
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
using SharpDX;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	
	public class HttpClientWrapperMovement
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
	
	
	public enum TradeDirection
	{
	    Buy,
	    Sell,
	    Unknown
	}
	
	public class MovementData
	{
	    public double StartingPrice { get; set; }
	    public double CurrentPrice { get; set; }
	    public DateTime StartTime { get; set; }
	    // Remove this property
	    // public Dictionary<int, RangeData> RangeDataDict { get; set; }
		public int EntryIndex { get; set; }
	    // Keep PreTradeDataAtStart to store the pre-trade data
	    public Dictionary<double, PreTradeDataPoint> PreTradeDataAtStart { get; set; }
	}


	public class RangeData
	{
	    public double RangeStartPrice { get; set; }
	    public double RangeEndPrice { get; set; }
	    public double BidVolume { get; set; }
	    public double AskVolume { get; set; }
		
	}
	
	public class PreTradeDataPoint
	{
	    public double Price { get; set; }
	    public double BidVolume { get; set; }
	    public double AskVolume { get; set; }
	    public DateTime Time { get; set; }
	}
	
	
	public class PreTradeDataCollection
	{
	    // Key: Price level, Value: PreTradeDataPoint
	    public Dictionary<double, PreTradeDataPoint> DataPoints { get; set; } = new Dictionary<double, PreTradeDataPoint>();
	    public double CurrentPrice { get; set; }
	    public double PriceRange { get; set; } = 8; // 5 points up and down
	}
	
	public class AnchorData
	{
	    public int AnchorBarIndex { get; set; }
	    public double ReferencePrice { get; set; }
	}
	
	public class ImbalanceAnalyzer : Strategy
	{
		#region Fields

		private Dictionary<int, AnchorData> anchorDataDict;
		private readonly object lockObject = new object();
		private List<int> nBarsList;
		// Removed angleDict
		// private Dictionary<int, double> angleDict; // Removed
		
		private Dictionary<double, double> buysAtBar = new Dictionary<double, double>();
		private Dictionary<double, double> sellsAtBar = new Dictionary<double, double>();
		private int activeBar = -1;
		
		private List<MovementData> activeMovements = new List<MovementData>();
		
		private double profitTargetTicks = 8;
		private double rangeTicks = 4;
		private double movementIntervalSeconds = 0.5; // Every x seconds
		private DateTime lastMovementStartTime = DateTime.MinValue;
		private List<MovementData> completedMovements = new List<MovementData>();
		private double lastPrice = 0;
		private string csvHeader;
		private bool csvHeaderWritten = false;

		private PreTradeDataCollection preTradeDataCollection = new PreTradeDataCollection();
		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
		// Added fields for DOM-like display
		private string domDisplayText = "";
		
		
		private bool isRegressionMode = false;
		private bool isTrendMode = false;
	
			//buttons/grid
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		#endregion;
		// Removed OFI related fields and methods
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "ImbalanceAnalyzer";
				Calculate									= Calculate.OnPriceChange;
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
			else if (State == State.Configure)
			{
				nBarsList = new List<int> { 5, 20, 40, 150 };
   
				anchorDataDict = new Dictionary<int, AnchorData>();
				  // angleDict = new Dictionary<int, double>(); // Removed

			}else if (State == State.Historical)
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
					
					myGrid.ColumnDefinitions.Add(column1);
					
					modeButton = new System.Windows.Controls.Button
					{
					    Name = "ModeButton",
					    Foreground = Brushes.White,
					    Background = isRegressionMode ? Brushes.Teal : Brushes.Purple,
					    Content = isRegressionMode ? "Regression" : "Trend",
					};
					
					modeButton.Click += OnButtonClick;
					
					System.Windows.Controls.Grid.SetColumn(modeButton, 0);
					
					myGrid.Children.Add(modeButton);
					
					UserControlCollection.Add(myGrid);
				}));
				
				
			}else if (State == State.Terminated)
			{
				Dispatcher.InvokeAsync((() =>
				{
					if (myGrid != null)
					{
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

		DateTime current = DateTime.MinValue;
		double lastClose = 0;
		
		// With these lists
		private List<double> historicalIncreases = new List<double>();
		private List<double> historicalDecreases = new List<double>();
		
		// No need for a key, as List manages indices automatically
		private int maxHistoricalData = 1000; // Adjust as needed


        // Bayesian parameters
        double priorPriceIncrease = 0.5; // P(H=1)
        double priorPriceDecrease = 0.5; // P(H=0)

        // Statistical parameters for likelihoods
        private double muIncrease = 0;
        private double sigmaIncrease = 1;
        private double muDecrease = 0;
        private double sigmaDecrease = 1;
		
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

		 protected override void OnBarUpdate()
        {
            // Ensure we have enough bars to calculate
            if (CurrentBar < nBarsList.Max())
                return;

			  if (CurrentBar == 0)
		    {
		        lastClose = Close[0];
		        return;
		    }
	
            lock (lockObject)
            {
                // Initialize anchor data if not already done
                foreach (int n in nBarsList)
                {
                    if (!anchorDataDict.ContainsKey(n) && CurrentBar >= n)
                    {
                        int anchorBarIndex = CurrentBar - n;
						double referencePrice = GetReferencePrice(anchorBarIndex);
                        anchorDataDict[n] = new AnchorData { AnchorBarIndex = anchorBarIndex, ReferencePrice = referencePrice };
                    }
                }

                // Update anchor data at every multiple of n
                foreach (int n in nBarsList)
                {
                    if (CurrentBar >= n && CurrentBar % n == 0)
                    {
                        int anchorBarIndex = CurrentBar - 1;
                        double referencePrice = GetReferencePrice(anchorBarIndex);
                        anchorDataDict[n] = new AnchorData { AnchorBarIndex = anchorBarIndex, ReferencePrice = referencePrice };
                    }
                }
				
				 // angleDict.Clear(); // Removed
            }
					
			if (CurrentBar != activeBar )
		    {
				activeBar = CurrentBar;
				priceDistance = 0;
				priceUp = 0;
				priceDown = 0;
				tradetaken = false;
				buysAtBar.Clear();
		        sellsAtBar.Clear();
			}
			
			bool priceIncreased = Close[0] > lastClose + profitTargetTicks * TickSize;
			
			if (Close[0] > lastClose + profitTargetTicks * TickSize || Close[0] < lastClose  - profitTargetTicks * TickSize)
            {
				
                // Update historical data
                UpdateHistoricalData(priceIncreased);


				lastClose = Close[0];
            

				
            }
			
			
						    // Get current total ask and bid volume
		    double totalAskVolume = volumeData.Sum(v => v.Value.AskVolume);
		    double totalBidVolume = volumeData.Sum(v => v.Value.BidVolume);
						
                // Compute posterior probabilities
                double itotal = ComputeImbalance();
               var posteriors = CalculatePosteriors(itotal, totalAskVolume, totalBidVolume);

                p_h1_d = posteriors.Item1;
                p_h0_d = posteriors.Item2;
			    Print($"[{Time[0]}] Enter triggered. P(H=0|D) (Down) = {p_h0_d}.  P(H=1|D) (Up) = {p_h1_d}");
			if(State == State.Realtime && Optimize){
				SendObservationSequenceToServer();
			}
			if(State == State.Realtime){
			 ProcessPredictions(predictedMovement, confidence);
			}
        }

		double p_h1_d;
			 double p_h0_d;
		 protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartBars == null || anchorDataDict.Count == 0)
                return;

            lock (lockObject)
            {
                // Removed angle-related rendering

                // Build DOM-like display text
                StringBuilder sbDom = new StringBuilder();
                sbDom.AppendLine("Pre-Range Groups:");
                 // Iterate over volumeData in ascending order of price
		        foreach (var kvp in volumeData.OrderBy(p => p.Key))
		        {
		            double price = kvp.Key;
		            double bidVolume = kvp.Value.BidVolume;
		            double askVolume = kvp.Value.AskVolume;
		
		            sbDom.AppendLine($"{price}: Bid={bidVolume}, Ask={askVolume}");
		        }
				
				sbDom.AppendLine($"Long Probability(bayesian): {Math.Round(p_h1_d,3)}");
				sbDom.AppendLine($"Short Probability(bayesian): {Math.Round(p_h0_d,3)}");

                domDisplayText = sbDom.ToString();

                // Create a text format
                var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 14);
                {
                    // Measure the text layout
                    using (var textLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, domDisplayText, textFormat, 200, float.MaxValue))
                    {
                        // Get the chart's dimensions
                        float chartRight = chartControl.CanvasRight;
                        float chartTop = 0;
                        float xPosition = chartRight - textLayout.Metrics.Width - 10; // 10 pixels padding from the right
                        float yPosition = chartTop + 10; // 10 pixels padding from the top

                        // Draw the text
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(xPosition, yPosition), textLayout, Brushes.White.ToDxBrush(RenderTarget));
                    }
                }
				
				textFormat.Dispose();
            }
        }
	
		double priceUp;
		double priceDown;
		double priceDistance;
		double bbdif;
		
		protected override void OnMarketData(MarketDataEventArgs e)
		{
				if (e.MarketDataType == MarketDataType.Last)
				{
					double price = e.Price;
					double volume = e.Volume;
					DateTime time = e.Time;

					
					 preTradeDataCollection.CurrentPrice = Close[0];

		            // Update pre-trade data
		            UpdatePreTradeData(e);
					
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
					
					if(price > lastPrice)
					{
						priceUp++;
				 		}
					if(price < lastPrice)
					{
						priceDown++;
					}
					

					lastPrice = price;
					
			   		priceDistance = priceUp + priceDown;
					
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
		
		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
		    // Handle the button click event here
		    // You can implement the logic to switch between long-only, short-only, or ranged mode
		    // For example:
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
		
			string buttonText = button.Content.ToString();
   			 string buttonName = button.Name;
			
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
		
		// Add this at the class level, outside of any methods
		private Dictionary<double, PreTradeDataPoint> aggregatedData = new Dictionary<double, PreTradeDataPoint>();
		private readonly object volumeDataLock = new object();
		
		
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
		}
		
		private double GetReferencePrice(int barIndex)
		{
		    int barsAgo = CurrentBar - barIndex;
	
		    if (barsAgo < 0 || barsAgo > CurrentBar)
		        return Close[0]; // Return current price or handle error appropriately
	
		    double priceAtAnchor = Close[barsAgo];
		    double currentPrice = Close[0];
	
		    // Determine whether to use High or Low based on the comparison
		    double referencePrice;
		    if (priceAtAnchor > currentPrice)
		        referencePrice = High[barsAgo]; // Use High if above current price
		    else
		        referencePrice = Low[barsAgo]; // Use Low if below current price
	
		    return referencePrice;
		}
		// Inside your class
		private SortedDictionary<double, PreTradeDataPoint> volumeData = new SortedDictionary<double, PreTradeDataPoint>();
		private double recentHigh;
		private double recentLow;
		private int rangeSize;
		private double aggregationUnit;
		
		private void InitializeVolumeData(double initialPrice)
	    {
	        aggregationUnit = preTradeAggregationSize * TickSize;
	
	        // Calculate rangeSize based on PriceRange and aggregationUnit
	        rangeSize = (int)Math.Floor(preTradeDataCollection.PriceRange / aggregationUnit);
	        if (rangeSize < 1) rangeSize = 1; // Ensure at least one range
	
	        // Align initialPrice to the aggregationUnit
	        double centerPrice = Math.Floor(initialPrice / aggregationUnit) * aggregationUnit;
	
	        // Initialize recentHigh and recentLow based on centerPrice
	        recentHigh = centerPrice + (rangeSize * aggregationUnit);
	        recentLow = centerPrice - (rangeSize * aggregationUnit);
	
	        BuildVolumeData();
	    }

    private void BuildVolumeData()
    {
        // Remove price levels that are now out of range
        var pricesToRemove = volumeData.Keys.Where(p => p < recentLow || p > recentHigh).ToList();
        foreach (var eprice in pricesToRemove)
        {
            volumeData.Remove(eprice);
        }

        double price = recentLow;

        // Ensure price is aligned to aggregationUnit
        price = Math.Floor(price / aggregationUnit) * aggregationUnit;

        while (price <= recentHigh + 0.0000001) // Add small epsilon to include the high price
        {
            if (!volumeData.ContainsKey(price))
            {
                volumeData[price] = new PreTradeDataPoint
                {
                    Price = price,
                    BidVolume = 0,
                    AskVolume = 0,
                    Time = DateTime.Now
                };
            }
            price += aggregationUnit;
        }
    }

		  private void UpdatePreTradeData(MarketDataEventArgs e)
		{
		    if (volumeData.Count == 0)
		    {
		        InitializeVolumeData(e.Price);
		    }
		
		    double roundedPrice = Math.Floor(e.Price / aggregationUnit) * aggregationUnit;
		
		    // Adjust the range if necessary
		    if (e.Price > recentHigh)
		    {
		        AdjustVolumeDataRange(true);
		    }
		    else if (e.Price < recentLow)
		    {
		        AdjustVolumeDataRange(false);
		    }
		
		    // Update volume data
		    if (volumeData.ContainsKey(roundedPrice))
		    {
		        double midPrice = (e.Bid + e.Ask) / 2;
		
		        if (e.Price > midPrice)
		        {
		            // Aggressive buy
		            volumeData[roundedPrice].AskVolume += e.Volume;
		            //Print($"Aggressive Buy Recorded: Price {roundedPrice}, Volume {e.Volume}");
		        }
		        else if (e.Price < midPrice)
		        {
		            // Aggressive sell
		            volumeData[roundedPrice].BidVolume += e.Volume;
		           // Print($"Aggressive Sell Recorded: Price {roundedPrice}, Volume {e.Volume}");
		        }
		        else
		        {
		            // Trade at mid-price, split volume
		            double halfVolume = e.Volume / 2;
		            volumeData[roundedPrice].BidVolume += halfVolume;
		            volumeData[roundedPrice].AskVolume += halfVolume;
		            Print($"Mid-Price Trade Recorded: Price {roundedPrice}, Bid Volume {halfVolume}, Ask Volume {halfVolume}");
		        }
		    }
		    else
		    {
		        Print($"Price {roundedPrice} not found in volumeData.");
		    }
		
		    // Optional: Print the volume data for debugging
		    //PrintVolumeData("After UpdatePreTradeData");
		}

    private void AdjustVolumeDataRange(bool isNewHigh)
    {
        if (aggregationUnit == 0)
            aggregationUnit = preTradeAggregationSize * TickSize;

        if (isNewHigh)
        {
            // Shift the range up by one aggregation unit
            recentLow += aggregationUnit;
            recentHigh += aggregationUnit;

            // Remove price levels that are now out of range (below recentLow)
            var pricesToRemove = volumeData.Keys.Where(p => p < recentLow).ToList();
            foreach (var price in pricesToRemove)
            {
                volumeData.Remove(price);
            }

            // Add new price level at recentHigh if not already present
            if (!volumeData.ContainsKey(recentHigh))
            {
                volumeData[recentHigh] = new PreTradeDataPoint
                {
                    Price = recentHigh,
                    BidVolume = 0,
                    AskVolume = 0,
                    Time = DateTime.Now
                };
            }
        }
        else
        {
            // Shift the range down by one aggregation unit
            recentLow -= aggregationUnit;
            recentHigh -= aggregationUnit;

            // Remove price levels that are now out of range (above recentHigh)
            var pricesToRemove = volumeData.Keys.Where(p => p > recentHigh).ToList();
            foreach (var price in pricesToRemove)
            {
                volumeData.Remove(price);
            }

            // Add new price level at recentLow if not already present
            if (!volumeData.ContainsKey(recentLow))
            {
                volumeData[recentLow] = new PreTradeDataPoint
                {
                    Price = recentLow,
                    BidVolume = 0,
                    AskVolume = 0,
                    Time = DateTime.Now
                };
            }
        }
    }

	    // Ensure to include the PrintVolumeData method for debugging
	    private void PrintVolumeData(string context)
	    {
	        Print($"--- Volume Data ({context}) ---");
	        Print($"Recent High: {recentHigh}, Recent Low: {recentLow}");
	        foreach (var kvp in volumeData.OrderByDescending(x => x.Key))
	        {
	            Print($"Price: {kvp.Key}, Bid: {kvp.Value.BidVolume}, Ask: {kvp.Value.AskVolume}");
	        }
	        Print("--- End of Volume Data ---");
	    }	

		private string lastWrittenData = "";
		
		private int FindClosestEntryPriceIndex(double entryPrice, Dictionary<double, PreTradeDataPoint> preTradeData, double tolerance = 1e-2)
		{
		    double closestPrice = double.MaxValue;
		    double closestDifference = double.MaxValue;
		
		    foreach (var kvp in anchorDataDict)
		    {
		        double price = kvp.Key;
		        double difference = Math.Abs(price - entryPrice);
		
		        // If this difference is smaller than the current closest one, update it
		        if (difference < closestDifference && difference <= tolerance)
		        {
		            closestDifference = difference;
		            closestPrice = price;
		        }
		    }
		
		    // Return the index (or price level) closest to the entry price
		    return closestPrice != double.MaxValue ? (int)closestPrice : -1;  // Adjust return value as per your needs
		}
		
		private void RecordEntryPriceIndex(MovementData movement)
		{
		    double entryPrice = movement.StartingPrice;
		
		    // Find the index that holds the entry price in the pre-trade data
		    int entryIndex = FindClosestEntryPriceIndex(entryPrice, preTradeDataCollection.DataPoints);
		
		    if (entryIndex != -1)
		    {
		        Print($"Found entry price index: {entryIndex} for entry price: {entryPrice}");
		        // Do something with this index, such as record it or use it in further analysis
		        movement.EntryIndex = entryIndex;
		    }
		    else
		    {
		        Print($"No match found for entry price: {entryPrice}");
		    }
		}
		
		private void WriteObservationSequenceToCSV()
		{
		    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\YourStrategy\hmm_training_data.csv";
		
		    try
		    {
		        string directory = System.IO.Path.GetDirectoryName(filePath);
		        if (!System.IO.Directory.Exists(directory))
		        {
		            System.IO.Directory.CreateDirectory(directory);
		        }
		
		        bool fileExists = System.IO.File.Exists(filePath);
		        bool isFileEmpty = new System.IO.FileInfo(filePath).Length == 0;
		
		        using (var writer = new System.IO.StreamWriter(filePath, append: true))
		        {
		            // Write the header if the file is new or empty
		            if (!fileExists || isFileEmpty)
		            {
		                writer.WriteLine("ObservationSequence,AskVolume,BidVolume,RSI,ADX,MACD");
		            }
		
		            // Write each tuple (observation, ask volume, bid volume) in observationData
		            foreach (var data in observationData)
		            {
		                string sequenceLine = $"{data.Observation},{data.AskVolume},{data.BidVolume},{data.Rsi},{data.Adx},{data.Macd}";
		                writer.WriteLine(sequenceLine);
		            }
		
		            // Optionally clear the sequences after writing
		            observationData.Clear();
		        }
		    }
		    catch (Exception ex)
		    {
		        Print($"Error writing observation sequence to CSV: {ex.Message}");
		    }
		}
		
		
			
		private void SendObservationSequenceToServer()
		{
		    try
		    {
		        int sequenceLength = Math.Min(observationData.Count, 50);
		
		        // Ensure we have enough observations
		        if (sequenceLength == 0)
		        {
		            Print("Not enough observations to send to the server.");
		            return;
		        }
		
		        var recentObservations = observationData.Skip(observationData.Count - sequenceLength).Take(sequenceLength).ToList();
		
		        // Extract observations, ask volumes, and bid volumes separately
		        List<int> recentSequence = recentObservations.Select(data => data.Observation).ToList();
		        List<double> recentAskVolumes = recentObservations.Select(data => data.AskVolume).ToList();
		        List<double> recentBidVolumes = recentObservations.Select(data => data.BidVolume).ToList();
		
		        // Extract corresponding RSI, ADX, MACD values
		        List<double> recentRsi = recentObservations.Select(data => data.Rsi).ToList(); // Ensure your tuple includes Rsi
		        List<double> recentAdx = recentObservations.Select(data => data.Adx).ToList(); // Ensure your tuple includes Adx
		        List<double> recentMacd = recentObservations.Select(data => data.Macd).ToList(); // Ensure your tuple includes Macd
		
		        // Prepare the data to send, including ask and bid volumes and technical indicators
		        var data = new Dictionary<string, object>
		        {
		            { "ObservationSequence", recentSequence },
		            { "AskVolume", recentAskVolumes },
		            { "BidVolume", recentBidVolumes },
		            { "RSI", recentRsi },
		            { "ADX", recentAdx },
		            { "MACD", recentMacd }
		        };
		
		        // Print the sequence for debugging
		        Print(string.Join(",", recentSequence));
		
		        // Send the data to the server's "predict" endpoint
		        var response = HttpClientWrapperMovement.Post("predict", data);
		
		        // Handle the server's response
		        if (response.ContainsKey("PredictedMovement") && response.ContainsKey("Confidence"))
		        {
		            predictedMovement = Convert.ToString(response["PredictedMovement"]);
		            confidence = Convert.ToDouble(response["Confidence"]);
		
		        }
		        else
		        {
		            Print("Incomplete response from prediction server.");
		        }
		    }
		    catch (Exception ex)
		    {
		        Print($"Error sending observation sequence to server: {ex.Message}");
		    }
		}

		string predictedMovement;
 		double confidence;
		private List<int> observationSequence = new List<int>();
		private int maxSequenceLength = 1000; // Adjust as needed
		
		private List<double> historicalAskVolumes = new List<double>();  // List to store historical ask volumes
		private List<double> historicalBidVolumes = new List<double>();  // List to store historical bid volumes
	
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
		    double totalBidVolume = volumeData.Sum(v => v.Value.BidVolume);
		    double totalAskVolume = volumeData.Sum(v => v.Value.AskVolume);
		    double volumeImbalance = totalBidVolume - totalAskVolume;
		
		    // Compute imbalance total (itotal)
		    double itotal = ComputeImbalance(); // This is equivalent to volumeImbalance
		
		    // Add the observation, ask, bid volumes, and technical indicators to observationData
		    int observation = priceIncreased ? 1 : 0;
		    observationData.Add((observation, totalAskVolume, totalBidVolume, rsi, adx, macd));
		
		    // Update historical data based on price movement
		    if (priceIncreased)
		    {
		        historicalIncreases.Add(volumeImbalance);
		        historicalRsiIncreases.Add(rsi);
		        historicalAdxIncreases.Add(adx);
		        historicalMacdIncreases.Add(macd);
		    }
		    else
		    {
		        historicalDecreases.Add(volumeImbalance);
		        historicalRsiDecreases.Add(rsi);
		        historicalAdxDecreases.Add(adx);
		        historicalMacdDecreases.Add(macd);
		    }
		
		    // Maintain the maximum sequence length
		    if (observationData.Count > maxSequenceLength)
		        observationData.RemoveAt(0);
		
		    if (historicalIncreases.Count > maxSequenceLength)
		        historicalIncreases.RemoveAt(0);
		    if (historicalDecreases.Count > maxSequenceLength)
		        historicalDecreases.RemoveAt(0);
		
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
		
		    // Optionally write the sequence to CSV periodically
		    if (train)
		    {
		        WriteObservationSequenceToCSV();
		    }
		
		    // Recalculate statistical parameters and Bayesian priors
		    RecalculateStatistics();
		}

		private void RecalculateStatistics()
		{
		    // Recalculate for price increases
		    if (historicalIncreases.Count > 0)
		    {
		        muIncrease = historicalIncreases.Average();
		        sigmaIncrease = Math.Sqrt(historicalIncreases.Average(itotal => Math.Pow(itotal - muIncrease, 2)));
		
		        muRsiIncrease = historicalRsiIncreases.Average();
		        sigmaRsiIncrease = Math.Sqrt(historicalRsiIncreases.Average(rsi => Math.Pow(rsi - muRsiIncrease, 2)));
		
		        muAdxIncrease = historicalAdxIncreases.Average();
		        sigmaAdxIncrease = Math.Sqrt(historicalAdxIncreases.Average(adx => Math.Pow(adx - muAdxIncrease, 2)));
		
		        muMacdIncrease = historicalMacdIncreases.Average();
		        sigmaMacdIncrease = Math.Sqrt(historicalMacdIncreases.Average(macd => Math.Pow(macd - muMacdIncrease, 2)));
		    }
		    else
		    {
		        muIncrease = 0;
		        sigmaIncrease = 1;
		        muRsiIncrease = 0;
		        sigmaRsiIncrease = 1;
		        muAdxIncrease = 0;
		        sigmaAdxIncrease = 1;
		        muMacdIncrease = 0;
		        sigmaMacdIncrease = 1;
		    }
		
		    // Recalculate for price decreases
		    if (historicalDecreases.Count > 0)
		    {
		        muDecrease = historicalDecreases.Average();
		        sigmaDecrease = Math.Sqrt(historicalDecreases.Average(itotal => Math.Pow(itotal - muDecrease, 2)));
		
		        muRsiDecrease = historicalRsiDecreases.Average();
		        sigmaRsiDecrease = Math.Sqrt(historicalRsiDecreases.Average(rsi => Math.Pow(rsi - muRsiDecrease, 2)));
		
		        muAdxDecrease = historicalAdxDecreases.Average();
		        sigmaAdxDecrease = Math.Sqrt(historicalAdxDecreases.Average(adx => Math.Pow(adx - muAdxDecrease, 2)));
		
		        muMacdDecrease = historicalMacdDecreases.Average();
		        sigmaMacdDecrease = Math.Sqrt(historicalMacdDecreases.Average(macd => Math.Pow(macd - muMacdDecrease, 2)));
		    }
		    else
		    {
		        muDecrease = 0;
		        sigmaDecrease = 1;
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
		    double totalEvents = historicalIncreases.Count + historicalDecreases.Count;
		
		    if (totalEvents == 0)
		    {
		        // Avoid division by zero; assign equal priors
		        priorPriceIncrease = 0.5;
		        priorPriceDecrease = 0.5;
				 // Optionally, log the updated priors for debugging
		   
		    }
		    else
		    {
		        priorPriceIncrease = (double)historicalIncreases.Count / totalEvents;
		        priorPriceDecrease = (double)historicalDecreases.Count / totalEvents;
		
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
            foreach (var kvp in volumeData)
            {
                itotal += kvp.Value.BidVolume - kvp.Value.AskVolume;
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
		private (double, double) CalculatePosteriors(double volumeImbalance, double totalAskVolume, double totalBidVolume)
		{
		    // Calculate likelihoods based on volume imbalance
		    double p_d_h1 = GaussianPDF(volumeImbalance, muIncrease, sigmaIncrease); // P(D|H=1)
		    double p_d_h0 = GaussianPDF(volumeImbalance, muDecrease, sigmaDecrease); // P(D|H=0)
		
		    // Incorporate RSI into the likelihood calculation
		    double rsi = RSI(14, 3)[0];
		    p_d_h1 *= GaussianPDF(rsi, muRsiIncrease, sigmaRsiIncrease);
		    p_d_h0 *= GaussianPDF(rsi, muRsiDecrease, sigmaRsiDecrease);
		
		    // Incorporate ADX into the likelihood calculation
		    double adx = ADX(14)[0];
		    p_d_h1 *= GaussianPDF(adx, muAdxIncrease, sigmaAdxIncrease);
		    p_d_h0 *= GaussianPDF(adx, muAdxDecrease, sigmaAdxDecrease);
		
		    // Incorporate MACD into the likelihood calculation
		    double macd = MACD(12, 26, 9).Diff[0];
		    p_d_h1 *= GaussianPDF(macd, muMacdIncrease, sigmaMacdIncrease);
		    p_d_h0 *= GaussianPDF(macd, muMacdDecrease, sigmaMacdDecrease);
		
		    // Calculate marginal likelihood P(D)
		    double p_d = (p_d_h1 * priorPriceIncrease) + (p_d_h0 * priorPriceDecrease);
		
		    // Prevent division by zero and handle underflow
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
		
		    // Debugging prints for validation
//		    Print($"Volume Imbalance: {volumeImbalance}, RSI: {rsi}, ADX: {adx}, MACD: {macd}");
//		    Print($"Likelihood P(D|H=1): {p_d_h1}, P(D|H=0): {p_d_h0}");
//		    Print($"Priors: P(H=1): {priorPriceIncrease}, P(H=0): {priorPriceDecrease}");
//		    Print($"Marginal Likelihood P(D): {p_d}");
//		    Print($"Posteriors: P(H=1|D): {p_h1_d}, P(H=0|D): {p_h0_d}");
		
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
                return 0;
            double exponent = -Math.Pow(x - mu, 2) / (2 * Math.Pow(sigma, 2));
            return (1 / (Math.Sqrt(2 * Math.PI) * sigma)) * Math.Exp(exponent);
        }
	
		public bool tradetaken = false;
		private void ProcessPredictions(string predictedMovement, double hmmConfidence)
		{
		    Print($"Received predictions: Predicted Move= {predictedMovement} Confidence: {hmmConfidence}");
			 
		    // Check if already in a trade
		    if (orderId.Length > 0 || atmStrategyId.Length > 0)
		        return;
		
			 double roundedPriceLevel = Math.Round(Close[0] / aggregationUnit) * aggregationUnit;
			double askvol;
			double bidvol;
			 if (volumeData.TryGetValue(roundedPriceLevel, out PreTradeDataPoint dataPoint)){
				 askvol= dataPoint.AskVolume;
				 bidvol = dataPoint.BidVolume;
			 }else{
				 askvol = 0;
				 bidvol = 0;
			 }
			 
		    // Verify conditions to enter a trade
		    if ((p_h1_d >= 0.7 || p_h0_d >= 0.7 ) )
		    {
		  
		            // Decide on order action based on probabilities
		            //OrderAction orderAction = longProbability > shortProbability ? OrderAction.Buy : OrderAction.Sell;
				
					if( p_h1_d >= 0.7 && isTrendMode ){
				  
		            // Create the ATM strategy with the entry price level
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
		                        Print($"ATM Strategy Created: {atmStrategyId}");
		                    }
		                    else
		                    {
		                        Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                    }
		                });
		
		            tradetaken = true;
					}
					
					if(  p_h0_d >= 0.7 && isTrendMode){
				  
		            // Create the ATM strategy with the entry price level
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
		                        Print($"ATM Strategy Created: {atmStrategyId}");
		                    }
		                    else
		                    {
		                        Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                    }
		                });
		
		            tradetaken = true;
					}
					if( p_h0_d >= 0.7 && isRegressionMode ){
				  
		            // Create the ATM strategy with the entry price level
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
		                        Print($"ATM Strategy Created: {atmStrategyId}");
		                    }
		                    else
		                    {
		                        Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                    }
		                });
		
		            tradetaken = true;
					}
					
					if(  p_h1_d >= 0.7 && isRegressionMode){
				  
		            // Create the ATM strategy with the entry price level
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
		                        Print($"ATM Strategy Created: {atmStrategyId}");
		                    }
		                    else
		                    {
		                        Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                    }
		                });
		
		            tradetaken = true;
					}
		  
		    }
		    else
		    {
		        Print("Conditions not met for trade.");
		    }
		}

	
		#region Properties
		
		[NinjaScriptProperty]
		[Display(Name="Gather Training Data", Order=1, GroupName="Train")]
		public bool train
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Gather Live Training Data", Order=2, GroupName="Train")]
		public bool inc
		{ get; set; }
		
			[NinjaScriptProperty]
		[Display(Name="Enable ML Optimization", Order=3, GroupName="Train")]
		public bool Optimize
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="ATM Strategy Name", Order=4, GroupName="Train")]
		public string ATMStrategy
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Pre-Trade Aggregation Size", Order = 1, GroupName = "Parameters")]
		public int preTradeAggregationSize { get; set; } = 4;  // Default to 4
		
		#endregion;
	
	}
}
