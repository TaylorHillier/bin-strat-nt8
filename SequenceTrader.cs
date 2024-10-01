#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.IO; // For file operations
using System.Net.Http; // For HTTP client
using System.Web.Script.Serialization; // For JSON serialization
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Structure to hold separated bid and ask volume data.
    /// </summary>
    public struct SeparatedTradeData
    {
        public DateTime Time;
        public float BidVolume;
        public float AskVolume;

        public SeparatedTradeData(DateTime time, float bidVolume, float askVolume)
        {
            Time = time;
            BidVolume = bidVolume;
            AskVolume = askVolume;
        }
    }

    /// <summary>
    /// Class to handle HTTP communication with the Python server.
    /// </summary>
    public class HttpClientWrapperSequence
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BaseUrl = "http://192.168.1.116:5000"; // Your server address
        private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public static Dictionary<string, object> Post(string endpoint, object data)
        {
            string json = serializer.Serialize(data);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = client.PostAsync($"{BaseUrl}/{endpoint}", content).Result;
                response.EnsureSuccessStatusCode();
                string responseBody = response.Content.ReadAsStringAsync().Result;
                return serializer.Deserialize<Dictionary<string, object>>(responseBody);
            }
            catch (Exception ex)
            {
                NinjaTrader.Code.Output.Process($"HTTP POST Error: {ex.Message}", PrintTo.OutputTab1);
                return null;
            }
        }
    }

    public class SequenceTrader : Strategy
    {
        #region Parameters and Variables

        // Sequence length: number of past transactions to track
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Sequence Length", Order = 1, GroupName = "Parameters")]
        public int SequenceLength { get; set; } = 10;

        // Profit target in ticks for simulated trades
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Sim Profit Target (Ticks)", Order = 2, GroupName = "Parameters")]
        public int SimProfitTargetTicks { get; set; } = 10;

        // Flag to indicate simulation mode
        [NinjaScriptProperty]
        [Display(Name = "Is Simulation Mode", Order = 3, GroupName = "Parameters")]
        public bool IsSimulationMode { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "ATMStrategy", Order = 4, GroupName = "Parameters")]
        public string ATMStrategy { get; set; }

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Sim Trade Interval (Seconds)", Order = 3, GroupName = "Parameters")]
		public int SimTradeIntervalSeconds { get; set; } = 60;

        // Queue to hold the last X SeparatedTradeData entries
        private Queue<SeparatedTradeData> tradeQueue;

        // Variables for simulated trades
        private bool isInSimTrade = false;
        private List<SeparatedTradeData> simEntrySequence;
        private double simEntryPrice;
        private int simTradeDirection; // 1 for Upward, -1 for Downward
        private bool simProfitTargetReached = false;
        private double simHighestPriceSinceEntry;
        private double simLowestPriceSinceEntry;

		private DateTime lastSimTradeTime;

        // StreamWriter for recording simulated trades
        private StreamWriter dataWriter;

        // File path to store the recorded data
        private string DataFilePath = @"C:\Users\hilli\Documents\NinjaTrader 8\templates\SimulatedTrades.csv";

        // Variables for live trades
        private string atmStrategyId = string.Empty;
        private string orderId = string.Empty;
        private bool isAtmStrategyCreated = false;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Strategy that separates simulated trades for data collection from live trades using HMM predictions.";
                Name = "SequenceTrader";
                Calculate = Calculate.OnEachTick; // Use OnEachTick to capture MarketData events
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.Infinite;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Day;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // Initialize the trade queue with the specified sequence length
                tradeQueue = new Queue<SeparatedTradeData>(SequenceLength);
            }
            else if (State == State.DataLoaded)
            {
                if (IsSimulationMode)
                {
                    // Initialize the CSV file for data recording (for simulated trades)
                    try
                    {
                        // Ensure the directory exists
                        string directory = Path.GetDirectoryName(DataFilePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        // Check if the file exists; if not, create and write headers
                        bool fileExists = File.Exists(DataFilePath);
                        dataWriter = new StreamWriter(DataFilePath, append: true);
                        if (!fileExists)
                        {
                            // Write CSV headers
                            StringBuilder header = new StringBuilder();
                            header.Append("EntryTime,SuccessfulDirection");
                            for (int i = 1; i <= SequenceLength; i++)
                            {
                                header.Append($",BidVolume_{i},AskVolume_{i}");
                            }
                            
                            dataWriter.WriteLine(header.ToString());
                            dataWriter.Flush();
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"Error initializing data writer: {ex.Message}");
                    }
                }
            }
            else if (State == State.Terminated)
            {
                // Close the StreamWriter when the strategy is terminated
                if (dataWriter != null)
                {
                    dataWriter.Close();
                    dataWriter = null;
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Only process MarketDataType.Last
            if (e.MarketDataType == MarketDataType.Last)
            {
                // Initialize bid and ask volumes
                float bidVolume = 0;
                float askVolume = 0;

                // Separate bid and ask volumes
                if (e.Price == e.Bid)
                {
                    // Trade occurred at bid price; seller initiated
                    bidVolume = e.Volume;
                }
                else if (e.Price == e.Ask)
                {
                    // Trade occurred at ask price; buyer initiated
                    askVolume = e.Volume;
                }
                else
                {
                    // Mid-price trade; cannot determine side
                    return;
                }

                // Create a SeparatedTradeData instance with the current time and separated bid/ask volumes
                SeparatedTradeData tradeData = new SeparatedTradeData(e.Time, bidVolume, askVolume);

                // Enqueue the new trade data
                if (tradeQueue.Count == SequenceLength)
                {
                    tradeQueue.Dequeue(); // Remove the oldest trade data to maintain the fixed size
                }
                tradeQueue.Enqueue(tradeData);

                // Only proceed if we have enough data in the queue
                if (tradeQueue.Count < SequenceLength)
                    return;

                if (IsSimulationMode)
                {
                    // Handle simulated trades for data collection without any server communication
                    HandleSimulatedTrades(e);
                }
                else if(State == State.Realtime)
                {
                    // Live trading mode: Send current values to the server and execute trades based on predictions
                    HandleLiveTrades(e);
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

        /// <summary>
        /// Handles simulated trades for data collection without server communication.
        /// </summary>
        private void HandleSimulatedTrades(MarketDataEventArgs e)
        {
            // If not in a simulated trade, decide whether to enter one based on your criteria
            if (!isInSimTrade)
            {
                 if (lastSimTradeTime == DateTime.MinValue || (e.Time - lastSimTradeTime).TotalSeconds >= SimTradeIntervalSeconds)
			    {
			        EnterSimTrade(e.Price);
			        lastSimTradeTime = e.Time;
			    }
		    }
            else
            {
                // Update the highest and lowest prices since entry
                if (e.Price > simHighestPriceSinceEntry)
                    simHighestPriceSinceEntry = e.Price;
                if (e.Price < simLowestPriceSinceEntry)
                    simLowestPriceSinceEntry = e.Price;

                // Check if profit target is reached in either direction
				double profitTargetPriceUp = simEntryPrice + (TickSize * SimProfitTargetTicks);
				double profitTargetPriceDown = simEntryPrice - (TickSize * SimProfitTargetTicks);
				
				if (!simProfitTargetReached)
				{
				    if (simHighestPriceSinceEntry >= profitTargetPriceUp)
				    {
				        // Upward profit target reached first
				        ExitSimTrade("Up");
				    }
				    else if (simLowestPriceSinceEntry <= profitTargetPriceDown)
				    {
				        // Downward profit target reached first
				        ExitSimTrade("Down");
				    }
				}

            }
        }

        /// <summary>
        /// Handles live trades by communicating with the server to get predictions.
        /// </summary>
        private void HandleLiveTrades(MarketDataEventArgs e)
        {
            // Send current values to the server and get predictions
            var tradeDataList = tradeQueue.ToList();

            // Prepare the data in a format suitable for serialization
            var tradeDataForSerialization = tradeDataList.Select(td => new Dictionary<string, object>
            {
                { "Time", td.Time.ToString("o") },
                { "BidVolume", td.BidVolume },
                { "AskVolume", td.AskVolume }
            }).ToList();

            // Send data to Python server and get prediction
            Dictionary<string, object> prediction = null;
            try
            {
                prediction = HttpClientWrapperSequence.Post("predict", new { trade_data_list = tradeDataForSerialization });
            }
            catch (Exception ex)
            {
                // Log the exception and exit the method
                NinjaTrader.Code.Output.Process($"Error in HTTP POST: {ex.Message}", PrintTo.OutputTab1);
                return;
            }

            if (prediction != null)
            {
                // Extract probabilities or predicted direction
                double probability_up = Convert.ToDouble(prediction["probability_up"]);
                double probability_down = Convert.ToDouble(prediction["probability_down"]);

				Print(probability_up + " up");
				Print(probability_down + " down");
                // Decide to enter a live trade based on probabilities
               
                  if (probability_up > 0.99)
                    {
                        EnterLiveTrade(1);
                    }
                    else if (probability_down > 0.99)
                    {
                        EnterLiveTrade(-1);
                    }
			}
            
        }

        /// <summary>
        /// Enters a simulated trade for data collection.
        /// </summary>
		private void EnterSimTrade(double entryPrice)
		{
		    // Record the entry price
		    simEntryPrice = entryPrice;
		
		    // Initialize highest and lowest prices since entry
		    simHighestPriceSinceEntry = simEntryPrice;
		    simLowestPriceSinceEntry = simEntryPrice;
		
		    // Reset profit target reached flag
		    simProfitTargetReached = false;
		
		    // Capture the current sequence of bid/ask data
		    simEntrySequence = tradeQueue.ToList();
		
		    isInSimTrade = true;
		
		    // Log the entry
		    Print($"[Sim] Entered trade at {Time[0]} with price {simEntryPrice}");
		}


        /// <summary>
        /// Exits a simulated trade and records the outcome.
        /// </summary>
        private void ExitSimTrade(string successfulDirection)
        {
            simProfitTargetReached = true;

             // Prepare the CSV line
		    StringBuilder csvLine = new StringBuilder();
		    csvLine.Append($"{simEntrySequence[0].Time.ToString("o")},{successfulDirection}");
		
		    // Append the bid/ask volumes in sequence order
		    foreach (var trade in simEntrySequence)
		    {
		        csvLine.Append($",{trade.BidVolume},{trade.AskVolume}");
		    }

            // Write to the CSV file
            try
            {
                if (dataWriter != null)
                {
                    dataWriter.WriteLine(csvLine.ToString());
                    dataWriter.Flush();
                }
            }
            catch (Exception ex)
            {
                Print($"Error writing to CSV: {ex.Message}");
            }

            // Log the exit
            Print($"[Sim] Exited {(simTradeDirection == 1 ? "Upward" : "Downward")} trade at {Time[0]}");

            // Reset simulated trade variables
            isInSimTrade = false;
            simTradeDirection = 0;
            simProfitTargetReached = false;
            simEntrySequence.Clear();
        }

        /// <summary>
        /// Enters a live trade using ATM strategies based on the HMM prediction.
        /// </summary>
        private void EnterLiveTrade(int direction)
        {
            // Ensure we are flat before entering a new live trade
            if (Position.MarketPosition == MarketPosition.Flat && orderId.Length == 0 && atmStrategyId.Length == 0)
            {
                isAtmStrategyCreated = false;  // reset atm strategy created check to false
                atmStrategyId = GetAtmStrategyUniqueId();
                orderId = GetAtmStrategyUniqueId();

                if (direction == 1)
                {
                    AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
                    {
                        // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        {
                            isAtmStrategyCreated = true;
                        }
                    });
                }
                else if (direction == -1)
                {
                    AtmStrategyCreate(OrderAction.SellShort, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) =>
                    {
                        // Check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
                        if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
                        {
                            isAtmStrategyCreated = true;
                        }
                    });
                }

//                isInLiveTrade = true;

                // Log the live trade entry
                Print($"[Live] Entered {(direction == 1 ? "Long" : "Short")} trade using ATM strategy at {Time[0]}");
            }
        }
    }
}
