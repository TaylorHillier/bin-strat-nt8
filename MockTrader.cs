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

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class MLIntegratedStrategy : Strategy
    {
        // Define trade tracking variables
        private double entryPrice = 0;
        private double maxPrice = 0;
        private double minPrice = 0;
        private double maxDrawdown = 0;
        private bool inTrade = false;
        private string csvFilePath = @"C:\Trading\trade_history.csv";
        private string apiUrl = "http://localhost:5000";
        private DateTime lastModelCheck = DateTime.MinValue;
        private List<Dictionary<string, object>> completedTrades = new List<Dictionary<string, object>>();
        private HttpClient httpClient;
        private JavaScriptSerializer jsonSerializer;
        
        // Define feature variables
        private double feature1 = 0; // Example: RSI value
        private double feature2 = 0; // Example: Moving average difference
        private double feature3 = 0; // Example: Volume ratio
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Strategy that integrates with Python ML model via HTTP";
                Name = "MLIntegratedStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                
                // Define parameters
                ModelCheckInterval = 60; // Check model predictions every 60 seconds
                ApiUrl = "http://localhost:5000";
            }
            else if (State == State.Configure)
            {
                // Initialize HTTP client
                httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                // Initialize JSON serializer
                jsonSerializer = new JavaScriptSerializer();
                
                // Initialize CSV file with headers if it doesn't exist
                if (!File.Exists(csvFilePath))
                {
                    using (StreamWriter sw = new StreamWriter(csvFilePath, false))
                    {
                        sw.WriteLine("DateTime,EntryPrice,ExitPrice,MaxDrawdown,Feature1,Feature2,Feature3,Profit");
                    }
                }
                
                // Check if ML service is running
                CheckMLServiceHealth();
            }
            else if (State == State.Terminated)
            {
                // Clean up resources
                if (httpClient != null)
                {
                    httpClient.Dispose();
                }
            }
        }
        
        [Range(10, 300)]
        [NinjaScriptProperty]
        [Display(Name="Model Check Interval (seconds)", Description="How often to check for new ML model predictions", Order=1, GroupName="Parameters")]
        public int ModelCheckInterval { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="API URL", Description="URL of the Python ML model API", Order=2, GroupName="Parameters")]
        public string ApiUrl { get; set; }
        
        protected override void OnBarUpdate()
        {
            // Calculate features on each bar update
            CalculateFeatures();
            
            // Track drawdown if in a trade
            if (inTrade)
            {
                TrackDrawdown();
            }
            
            // Check if it's time to run the ML model for predictions
            if (BarsInProgress == 0 && CurrentBar > BarsRequiredToTrade)
            {
                TimeSpan timeSinceLastCheck = Time[0] - lastModelCheck;
                if (timeSinceLastCheck.TotalSeconds >= ModelCheckInterval)
                {
                    // Get prediction from ML model
                    GetPredictionAsync();
                    lastModelCheck = Time[0];
                }
            }
            
            // Position management
            ManageExistingPosition();
        }
        
        private void CalculateFeatures()
        {
            // Example feature calculations
            // Feature 1: RSI value
            double rsiValue = RSI(14, 3)[0];
            feature1 = Math.Round(rsiValue, 2);
            
            // Feature 2: Difference between fast and slow moving averages
            double fastMA = SMA(10)[0];
            double slowMA = SMA(20)[0];
            feature2 = Math.Round(fastMA - slowMA, 2);
            
            // Feature 3: Relative volume
            double avgVolume = SMA(Volume, 10)[0];
            feature3 = Math.Round(Volume[0] / (avgVolume > 0 ? avgVolume : 1), 2);
            
            // Log features for debugging
            if (CurrentBar % 20 == 0)
            {
                Print(string.Format("Features calculated - RSI: {0}, MA Diff: {1}, Vol Ratio: {2}", 
                    feature1, feature2, feature3));
            }
        }
        
        private void TrackDrawdown()
        {
            // Update max and min price since entry
            if (Position.MarketPosition == MarketPosition.Long)
            {
                maxPrice = Math.Max(maxPrice, Close[0]);
                double currentDrawdown = (maxPrice - Close[0]) / maxPrice * 100;
                maxDrawdown = Math.Max(maxDrawdown, currentDrawdown);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                minPrice = Math.Min(minPrice, Close[0]);
                double currentDrawdown = (Close[0] - minPrice) / minPrice * 100;
                maxDrawdown = Math.Max(maxDrawdown, currentDrawdown);
            }
        }
        
        private void CheckMLServiceHealth()
        {
            try
            {
                string healthEndpoint = ApiUrl + "/health";
                HttpResponseMessage response = httpClient.GetAsync(healthEndpoint).Result;
                
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    Dictionary<string, object> healthData = jsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
                    
                    bool isHealthy = healthData["status"].ToString() == "healthy";
                    bool modelLoaded = Convert.ToBoolean(healthData["model_loaded"]);
                    
                    Print(string.Format("ML Service Health Check: Healthy={0}, Model Loaded={1}", 
                        isHealthy, modelLoaded));
                }
                else
                {
                    Print("ML Service Health Check Failed: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error checking ML service health: " + ex.Message);
            }
        }
        
        private async void GetPredictionAsync()
        {
            try
            {
                // Prepare feature data to send
                Dictionary<string, object> requestData = new Dictionary<string, object>();
                requestData["Feature1"] = feature1;
                requestData["Feature2"] = feature2;
                requestData["Feature3"] = feature3;
                requestData["MaxDrawdown"] = maxDrawdown;
                
                // Serialize the dictionary to JSON
                string jsonRequest = jsonSerializer.Serialize(requestData);
                
                // Prepare HTTP request
                string predictUrl = ApiUrl + "/predict";
                StringContent content = new StringContent(
                    jsonRequest, 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                // Send request asynchronously
                HttpResponseMessage response = await httpClient.PostAsync(predictUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Dictionary<string, object> responseData = jsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
                    
                    if (responseData["status"].ToString() == "success")
                    {
                        // Convert predicted_price to double (handles different numeric types from JavaScriptSerializer)
                        double predictedPrice = Convert.ToDouble(responseData["predicted_price"]);
                        
                        // Use the predicted price for entry decision
                        EvaluatePrediction(predictedPrice);
                        
                        Print(string.Format("Received prediction: {0}", predictedPrice));
                    }
                    else
                    {
                        Print("Prediction API error: " + responseData["error"]);
                    }
                }
                else
                {
                    Print("Prediction API failed: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error getting prediction: " + ex.Message);
            }
        }
        
        private void EvaluatePrediction(double predictedPrice)
        {
            // Simple example: Enter long if current price is below predicted price
            if (!inTrade && predictedPrice > Close[0])
            {
                EnterLong();
                entryPrice = Close[0];
                maxPrice = entryPrice;
                minPrice = entryPrice;
                maxDrawdown = 0;
                inTrade = true;
                Print(string.Format("Entering LONG trade at {0} - ML predicted price: {1}", 
                    entryPrice, predictedPrice));
            }
            // Enter short if current price is above predicted price
            else if (!inTrade && predictedPrice < Close[0])
            {
                EnterShort();
                entryPrice = Close[0];
                maxPrice = entryPrice;
                minPrice = entryPrice;
                maxDrawdown = 0;
                inTrade = true;
                Print(string.Format("Entering SHORT trade at {0} - ML predicted price: {1}", 
                    entryPrice, predictedPrice));
            }
        }
        
        private void ManageExistingPosition()
        {
            // Simple example exit logic
            if (inTrade)
            {
                // Exit long if profit target is reached or stop loss is hit
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    // Example: Exit if 2% profit or 1% loss
                    if (Close[0] >= entryPrice * 1.02 || Close[0] <= entryPrice * 0.99)
                    {
                        ExitLong();
                        RecordCompletedTrade(Close[0]);
                    }
                }
                // Exit short if profit target is reached or stop loss is hit
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    // Example: Exit if 2% profit or 1% loss
                    if (Close[0] <= entryPrice * 0.98 || Close[0] >= entryPrice * 1.01)
                    {
                        ExitShort();
                        RecordCompletedTrade(Close[0]);
                    }
                }
            }
        }
        
        private void RecordCompletedTrade(double exitPrice)
        {
            // Calculate profit/loss
            double profit = Position.MarketPosition == MarketPosition.Long ? 
                exitPrice - entryPrice : entryPrice - exitPrice;
            
            // Create trade record
            Dictionary<string, object> trade = new Dictionary<string, object>();
            trade["DateTime"] = Time[0].ToString("yyyy-MM-dd HH:mm:ss");
            trade["EntryPrice"] = entryPrice;
            trade["ExitPrice"] = exitPrice;
            trade["MaxDrawdown"] = Math.Round(maxDrawdown, 2);
            trade["Feature1"] = feature1;
            trade["Feature2"] = feature2;
            trade["Feature3"] = feature3;
            trade["Profit"] = Math.Round(profit, 2);
            
            // Add to completed trades list
            completedTrades.Add(trade);
            
            // Write to CSV
            WriteTradeToCsv(trade);
            
            // Reset trade tracking variables
            inTrade = false;
            maxDrawdown = 0;
            
            // Notify ML model to retrain with new data
            NotifyTradeCompletionAsync();
        }
        
        private void WriteTradeToCsv(Dictionary<string, object> trade)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(csvFilePath, true))
                {
                    string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                        trade["DateTime"],
                        trade["EntryPrice"],
                        trade["ExitPrice"],
                        trade["MaxDrawdown"],
                        trade["Feature1"],
                        trade["Feature2"],
                        trade["Feature3"],
                        trade["Profit"]);
                    
                    sw.WriteLine(line);
                }
                
                Print("Trade recorded to CSV: " + trade["DateTime"]);
            }
            catch (Exception ex)
            {
                Print("Error writing to CSV: " + ex.Message);
            }
        }
        
        private async void NotifyTradeCompletionAsync()
        {
            try
            {
                // Trigger model retraining
                string trainUrl = ApiUrl + "/train";
                HttpResponseMessage response = await httpClient.PostAsync(trainUrl, null);
                
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Dictionary<string, object> responseData = jsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
                    
                    Print("ML Model training: " + responseData["message"]);
                }
                else
                {
                    Print("ML Model training failed: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error notifying trade completion: " + ex.Message);
            }
        }
    }
}