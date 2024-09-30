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
using System.Data;
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
	    public double PriceRange { get; set; } = 10; // 5 points up and down
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
		
		private double profitTargetTicks = 20;
		private double rangeTicks = 4;
		private int movementIntervalSeconds = 3; // Every x seconds
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
		
		 protected override void OnBarUpdate()
        {
            // Ensure we have enough bars to calculate
            if (CurrentBar < nBarsList.Max())
                return;

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
			
			 if (Optimize && State == State.Realtime)
			        {
							if(Time[0] - current > TimeSpan.FromMilliseconds(200)){
			            SendPreTradeDataToServer();
							current = Time[0];
							}
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
        }

		
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

                domDisplayText = sbDom.ToString();

                // Create a text format
                var textFormat = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 12);
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

					if(train || State == State.Realtime){
					  preTradeDataCollection.CurrentPrice = Close[0];

		            // Update pre-trade data
		            UpdatePreTradeData(e);
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
					
					if(price > lastPrice)
					{
						priceUp++;
				 		}
					if(price < lastPrice)
					{
						priceDown++;
					}
					
				
					if(train || inc && State == State.Realtime){
							// Start a new movement every x seconds
			            if ((lastMovementStartTime == DateTime.MinValue) || (time - lastMovementStartTime).TotalSeconds >= movementIntervalSeconds)
			            {
			                StartNewMovement(price, time);
			                lastMovementStartTime = time;
			            }
			
			      
			
			            // Update movements' current prices
			            foreach (var movement in activeMovements.ToList())
			            {
			                UpdateMovement(movement, price);
			            }
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
		
		private void StartNewMovement(double price, DateTime time)
		{
		    MovementData movement = new MovementData
		    {
		        StartingPrice = price,
		        CurrentPrice = price,
		        StartTime = time,
		        PreTradeDataAtStart = new Dictionary<double, PreTradeDataPoint>()
		    };
		
		    lock (volumeDataLock)
		    {
		        int numRanges = rangeSize;
		        double aggregationUnit = preTradeAggregationSize * TickSize;
		
		        for (int i = -numRanges; i <= numRanges; i++)
		        {
		            double priceLevel = price + i * aggregationUnit;
		            double roundedPriceLevel = Math.Round(priceLevel / aggregationUnit) * aggregationUnit;
		
		            if (volumeData.TryGetValue(roundedPriceLevel, out PreTradeDataPoint dataPoint))
		            {
		                movement.PreTradeDataAtStart[roundedPriceLevel] = new PreTradeDataPoint
		                {
		                    Price = dataPoint.Price,
		                    BidVolume = dataPoint.BidVolume,
		                    AskVolume = dataPoint.AskVolume,
		                    Time = dataPoint.Time
		                };
		            }
		            else
		            {
		                movement.PreTradeDataAtStart[roundedPriceLevel] = new PreTradeDataPoint
		                {
		                    Price = roundedPriceLevel,
		                    BidVolume = 0,
		                    AskVolume = 0,
		                    Time = time
		                };
		            }
		        }
		    }
		
		    activeMovements.Add(movement);
		}



		private void UpdateMovement(MovementData movement, double price)
		{
		    movement.CurrentPrice = price;
		
		    // Calculate ticks moved
		    double ticksMoved = Math.Abs(price - movement.StartingPrice) / TickSize;
		
		    if (ticksMoved >= profitTargetTicks)
		    {
		        // Movement completed
		        completedMovements.Add(movement);
		        activeMovements.Remove(movement);
		
		        // Write completed movements to CSV
		        WriteMovementDataToCSV(completedMovements);
		    }
		}
		
				
		
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

        // Check if we need to adjust the range based on new high or low
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
            }
            else if (e.Price < midPrice)
            {
                // Aggressive sell
                volumeData[roundedPrice].BidVolume += e.Volume;
            }
            else
            {
                // Trade at mid-price, split volume
                volumeData[roundedPrice].BidVolume += e.Volume / 2;
                volumeData[roundedPrice].AskVolume += e.Volume / 2;
            }
        }

        // Optional: Print the volume data for debugging
       // PrintVolumeData("volume");
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
		
private void WriteMovementDataToCSV(List<MovementData> completedMovements)
{
    string filePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\YourStrategy\movement_data.csv";
    if (string.IsNullOrEmpty(filePath))
        return;

    try
    {
        string directory = System.IO.Path.GetDirectoryName(filePath);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        bool fileExists = System.IO.File.Exists(filePath);
        bool headerExists = false;
        if (fileExists)
        {
            string firstLine = System.IO.File.ReadLines(filePath).FirstOrDefault();
            headerExists = firstLine != null && firstLine.StartsWith("StartingPrice,EndingPrice");
        }

        using (var writer = new System.IO.StreamWriter(filePath, append: true))
        {
            if (!headerExists)
            {
                // Write the CSV header with fixed column names
                StringBuilder sbHeader = new StringBuilder();
                sbHeader.Append("StartingPrice,EndingPrice");

                for (int i = -rangeSize; i <= rangeSize; i++)
                {
                    sbHeader.Append($",PreRangeGroup{i}_BidVolume,PreRangeGroup{i}_AskVolume");
                }

                writer.WriteLine(sbHeader.ToString());
            }

            foreach (var movement in completedMovements)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("{0},{1}", movement.StartingPrice, movement.CurrentPrice);

                int numRanges = rangeSize;
                double aggregationUnit = preTradeAggregationSize * TickSize;

                for (int i = -numRanges; i <= numRanges; i++)
                {
                    double priceLevel = movement.StartingPrice + i * aggregationUnit;
                    double roundedPriceLevel = Math.Round(priceLevel / aggregationUnit) * aggregationUnit;

                    if (volumeData.TryGetValue(roundedPriceLevel, out PreTradeDataPoint dataPoint))
                    {
                        sb.AppendFormat(",{0},{1}", dataPoint.BidVolume, dataPoint.AskVolume);
                    }
                    else
                    {
                        sb.Append(",0,0");
                    }
                }

                string currentData = sb.ToString();
                if (currentData != lastWrittenData)
                {
                    writer.WriteLine(currentData);
                    lastWrittenData = currentData;
                    Print($"New data written to CSV: {currentData}");
                }
                else
                {
                    Print("Data unchanged, skipping CSV write.");
                }
            }

            completedMovements.Clear();
        }
    }
    catch (Exception ex)
    {
        Print($"Error writing to CSV: {ex.Message}");
    }
}

		private void SendPreTradeDataToServer()
		{
		    var preTradeData = new Dictionary<string, double>();
		
		    // Ensure aggregationUnit and rangeSize are properly defined
		    double aggregationUnit = preTradeAggregationSize * TickSize;
		    int numRanges = rangeSize; // Make sure 'rangeSize' is initialized elsewhere
		
		    // Use a reference price; since we don't have a movement here, use the current price
		    double referencePrice = Close[0]; // Or use another appropriate price, e.g., Last price
		
		    for (int i = -numRanges; i <= numRanges; i++)
		    {
		        double priceLevel = referencePrice + i * aggregationUnit;
		        double roundedPriceLevel = Math.Round(priceLevel / aggregationUnit) * aggregationUnit;
		
		        if (volumeData.TryGetValue(roundedPriceLevel, out PreTradeDataPoint dataPoint))
		        {
		            preTradeData[$"PreRangeGroup{i}_BidVolume"] = dataPoint.BidVolume;
		            preTradeData[$"PreRangeGroup{i}_AskVolume"] = dataPoint.AskVolume;
		        }
		        else
		        {
		            preTradeData[$"PreRangeGroup{i}_BidVolume"] = 0;
		            preTradeData[$"PreRangeGroup{i}_AskVolume"] = 0;
		        }
		    }
		
		    // Compute derived features: Volume Difference and Volume Ratio
		    for (int i = -numRanges; i <= numRanges; i++)
		    {
		        string bidKey = $"PreRangeGroup{i}_BidVolume";
		        string askKey = $"PreRangeGroup{i}_AskVolume";
		
		        double bidVolume = preTradeData.ContainsKey(bidKey) ? preTradeData[bidKey] : 0;
		        double askVolume = preTradeData.ContainsKey(askKey) ? preTradeData[askKey] : 0;
		
		        preTradeData[$"VolumeDiff_{i}"] = bidVolume - askVolume;
		        preTradeData[$"VolumeRatio_{i}"] = askVolume != 0 ? bidVolume / askVolume : 0;
		    }
		
		    var dataToSend = new
		    {
		        PreTradeData = preTradeData
		    };
		
		    try
		    {
		        var response = HttpClientWrapperMovement.Post("predict", dataToSend);
		        if (response.ContainsKey("LongProbability") && response.ContainsKey("ShortProbability"))
		        {
		            double longProbability = Convert.ToDouble(response["LongProbability"]);
		            double shortProbability = Convert.ToDouble(response["ShortProbability"]);
		
		            // Process predictions as needed
		            ProcessPredictions(longProbability, shortProbability);
		        }
		        else
		        {
		            Print("Incomplete response from prediction server.");
		        }
		    }
		    catch (Exception ex)
		    {
		        Print($"Error sending pre-trade data to server: {ex.Message}");
		    }
		}
		
		// List to store bbdif values over the lookback period
private List<double> bbdifList = new List<double>();

// The lookback period for calculating standard deviation
private int period = 500;  // or 10 depending on how many bars you want to look back

// Define the standard deviation multiplier (e.g., 2 standard deviations)
private double stdMultiplier = 0;

// Method to check if the current bbdif falls within ±2 standard deviations
private bool IsCurrentBBDifWithinStdRange()
{
    // Calculate the Bollinger Bands difference (bbdif) for the current bar
    double bbdif = Bollinger(2, 5).Upper[0] - Bollinger(2, 5).Lower[0];
    
    // Add the current bbdif to the list
    bbdifList.Add(bbdif);

    // If the list exceeds the desired period, remove the oldest value
    if (bbdifList.Count > period)
    {
        bbdifList.RemoveAt(0);
    }

    // Calculate the standard deviation and mean if we have enough data points
    if (bbdifList.Count >= period)
    {
        double mean = bbdifList.Average();
        double stdbb = CalculateStandardDeviation(bbdifList);

        Print($"Mean of bbdif: {mean}, Standard deviation of bbdif: {stdbb}");
        Print($"Current bbdif: {bbdif}");

        // Check if the current bbdif falls within ±2 standard deviations
        double upperBound = mean + stdMultiplier * stdbb;
        double lowerBound = mean - stdMultiplier * stdbb;

        Print($"Current bbdif should be between {lowerBound} and {upperBound}");

        return ( bbdif <= upperBound);
    }

    return false;
}

// Method to calculate the standard deviation of a list of values
private double CalculateStandardDeviation(List<double> values)
{
    if (values.Count == 0)
        return 0;

    // Calculate the mean
    double mean = values.Average();

    // Calculate the sum of the squared differences from the mean
    double sumSquaredDiffs = values.Sum(val => Math.Pow(val - mean, 2));

    // Return the standard deviation
    return Math.Sqrt(sumSquaredDiffs / values.Count);
}

		
		public bool tradetaken = false;
		private void ProcessPredictions(double longProbability, double shortProbability)
		{
			// Removed OFI related processing
		    // Print and handle predictions based on OFI
		    // You may need to adjust this method based on your new strategy focus
	
		    Print($"Received predictions: longProbability={longProbability}, shortProbability={shortProbability}");
			
		    // Decide whether to enter a trade based on probability threshold
		    double probabilityThreshold = 0.95; // Adjust based on your strategy
	
			 bool isBBDifWithinRange = IsCurrentBBDifWithinStdRange();
			
		    if (orderId.Length > 0 || atmStrategyId.Length > 0 /*|| tradetaken*/)
		        return;
	
  if (!isBBDifWithinRange)
    {
        Print("Current market volatility is too high or too low. Skipping trade.");
        return;
    }
	
		    if ((longProbability >= probabilityThreshold && isRegressionMode || shortProbability >= probabilityThreshold && isTrendMode))
		    {
		        // Enter a Long trade
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
		                    Print($"ATM Strategy Created: {atmStrategyId} at Price: {recentHigh}"); // Optional: Use recentHigh or specific price
		                }
		                else
		                {
		                    Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                }
		            });
	
		        tradetaken = true;
		    }
		    else if ((shortProbability >= probabilityThreshold  && isRegressionMode || longProbability >= probabilityThreshold && isTrendMode))
		    {
		        // Enter a Short trade
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
		                    Print($"ATM Strategy Created: {atmStrategyId} at Price: {recentLow}"); // Optional: Use recentLow or specific price
		                }
		                else
		                {
		                    Print($"Error creating ATM Strategy: {atmCallbackErrorCode}");
		                }
		            });
	
		        tradetaken = true;
		    }
		    else
		    {
		        // Do not trade
		        Print("Probability below threshold, not entering trade.");
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
