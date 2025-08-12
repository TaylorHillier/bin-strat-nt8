#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using System.Net.Http;
using System.Threading;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BarPredictor : Strategy
    {
        // List to store CSV records in memory.
        private List<string> barRecords;
        // Flush threshold: flush to file when record count reaches this value.
        private const int flushThreshold = 100;
        // File path for CSV output.
        private readonly string filePath = @"C:\TradingStrats\BarTypes.csv";

        // For live prediction: store the last n bar types.
        private List<int> lastBarTypes;
        private const int predictionWindowSize = 20;  // Window size for conditioning.

        // HTTP client for calling the Flask server.
        private HttpClient httpClient;

        // Property: TradeMode. When true, the strategy is in trade mode and CSV logging is disabled.
        [NinjaScriptProperty]
        [Display(Name = "Trade Mode", Description = "If checked, the strategy is in trade mode and CSV logging is disabled; otherwise, it runs in training mode with CSV logging.", Order = 1, GroupName = "Parameters")]
        public bool TradeMode { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description									= @"Enter the description for your new custom Strategy here.";
                Name										= "BarPredictor";
                Calculate									= Calculate.OnBarClose;
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
                IsInstantiatedOnEachOptimizationIteration	= true;
                // Default mode: TradeMode false means training mode (CSV logging enabled)
                TradeMode = false;
            }
            else if (State == State.Configure)
            {
                // Initialize the in-memory lists.
                barRecords = new List<string>();
                lastBarTypes = new List<int>();
                // Ensure the output folder exists.
                string folderPath = @"C:\TradingStrats";
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                // Initialize the HttpClient.
                httpClient = new HttpClient();
            }
            else if (State == State.Terminated)
            {
//                // If in training mode, flush any remaining CSV records.
//                if (!TradeMode && barRecords.Count > 0)
//                {
//                    WriteRecordsToCSVAppend();
//                }
//                // Dispose the HttpClient.
//                if (httpClient != null)
//                    httpClient.Dispose();
            }
        }

        protected override void OnBarUpdate()
        {
            // Ensure we have enough bars and a valid range.
            if (CurrentBar < 1) return;
            double range = High[0] - Low[0];
            if (range == 0) return; // Avoid division by zero.

            // Normalize open and close positions (0 = low, 1 = high).
            double openRel = (Open[0] - Low[0]) / range;
            double closeRel = (Close[0] - Low[0]) / range;
            double body = Math.Abs(Close[0] - Open[0]);
            double bodyRatio = body / range;

            // Calculate wick lengths.
            double upperWick = High[0] - Math.Max(Open[0], Close[0]);
            double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];

            int barType = 0; // Default neutral.

            // Classification conditions:
			if (openRel >= 0.4 && openRel <= 0.6 && closeRel >= 0.4 && closeRel <= 0.6)
			{
			    barType = 0; // Neutral
			}
			else if (bodyRatio < 0.05)
			{
			    barType = (closeRel > 0.5) ? 7 : -7; // Doji-like
			}
			else if (Close[0] > Open[0]) // Bullish bars
			{
			    if (bodyRatio >= 0.7 && upperWick < 0.1 * range && lowerWick < 0.1 * range)
			        barType = 1;
			    else if (bodyRatio >= 0.7 && openRel <= 0.2 && closeRel >= 0.8)
			        barType = 2;
			    else if (bodyRatio >= 0.5 && openRel <= 0.3 && closeRel >= 0.7)
			        barType = 3;
			    else if (bodyRatio < 0.3 && openRel <= 0.3 && closeRel >= 0.7)
			        barType = 4;
			    else if (lowerWick >= 2 * body && upperWick < 0.1 * range)
			        barType = 5;
			    else if (upperWick >= 2 * body && lowerWick < 0.1 * range)
			        barType = 6;
			    else if (upperWick > lowerWick)
			        barType = 8;
			    else
			        barType = 3; // Default for bullish bars
			}
			else if (Close[0] < Open[0]) // Bearish bars
			{
			    if (bodyRatio >= 0.7 && upperWick < 0.1 * range && lowerWick < 0.1 * range)
			        barType = -1;
			    else if (bodyRatio >= 0.7 && openRel >= 0.8 && closeRel <= 0.2)
			        barType = -2;
			    else if (bodyRatio >= 0.5 && openRel >= 0.7 && closeRel <= 0.3)
			        barType = -3;
			    else if (bodyRatio < 0.3 && openRel >= 0.7 && closeRel <= 0.3)
			        barType = -4;
			    else if (upperWick >= 2 * body && lowerWick < 0.1 * range)
			        barType = -5;
			    else if (lowerWick >= 2 * body && upperWick < 0.1 * range)
			        barType = -6;
			    else if (lowerWick > upperWick)
			        barType = -8;
			    else
			        barType = -3; // Default for bearish bars
			}


            // Print the computed bar type.
            Print("Bar " + CurrentBar + " Type: " + barType);

            // If not in trade mode (i.e. training mode), log to CSV.
            if (!TradeMode)
            {
                string record = Time[0].ToString("yyyy-MM-dd HH:mm:ss") + "," + CurrentBar + "," + barType;
                barRecords.Add(record);
                if (barRecords.Count >= flushThreshold)
                {
                    WriteRecordsToCSVAppend();
                    barRecords.Clear();
                }
            }

            // Update the rolling window for live predictions.
            lastBarTypes.Add(barType);
            if (lastBarTypes.Count > predictionWindowSize)
                lastBarTypes.RemoveAt(0);

			if(State == State.Realtime){
	            // When the window is full, request prediction from the Flask server.
	            if (lastBarTypes.Count == predictionWindowSize)
	            {
	                int predictedNextBar = GetPredictionFromServer(lastBarTypes);
	                Print("Predicted Next Bar Type from Flask: " + predictedNextBar);
	                // Additional handling for the prediction (e.g., trade decisions) can be added here.
					
					if(predictedNextBar > 0) {
						EnterLong();
					} else {
						EnterShort();
					}
	            }
				
			}
        }

        // Calls the Flask server to obtain a prediction.
        private int GetPredictionFromServer(List<int> condition)
        {
            try
            {
                // Build a JSON string manually.
                string json = "{\"condition\":[" + string.Join(",", condition) + "]}";
                var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                HttpResponseMessage response = httpClient.PostAsync("http://localhost:5000/predict", content).Result;
                if (response.IsSuccessStatusCode)
                {
                    string responseJson = response.Content.ReadAsStringAsync().Result;
                    int predictedBarType = ParsePredictionResponse(responseJson);
                    return predictedBarType;
                }
                else
                {
                    Print("Flask server responded with error: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error communicating with Flask server: " + ex.Message);
            }
            return 0;
        }

        // Parses a JSON response of the form {"predicted_bar_type":2} without using external libraries.
        private int ParsePredictionResponse(string responseJson)
        {
            try
            {
                int colonIndex = responseJson.IndexOf(":");
                int endIndex = responseJson.IndexOf("}", colonIndex);
                string numberStr = responseJson.Substring(colonIndex + 1, endIndex - colonIndex - 1).Trim();
                return int.Parse(numberStr);
            }
            catch (Exception ex)
            {
                Print("Error parsing response: " + ex.Message);
                return 0;
            }
        }

        // Append the stored records to CSV at the specified file path.
        private void WriteRecordsToCSVAppend()
        {
            try
            {
                bool fileExists = File.Exists(filePath);
                bool writeHeader = !fileExists || (fileExists && new FileInfo(filePath).Length == 0);
                using (StreamWriter sw = new StreamWriter(filePath, true))
                {
                    if (writeHeader)
                        sw.WriteLine("Time,Bar,BarType");
                    foreach (string record in barRecords)
                        sw.WriteLine(record);
                }
                Print("Flushed " + barRecords.Count + " records to " + filePath);
            }
            catch (Exception ex)
            {
                Print("Error appending to CSV: " + ex.Message);
            }
        }
    }
}
