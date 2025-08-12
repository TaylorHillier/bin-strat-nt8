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

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MLTradingStrategy : Strategy
    {
        #region User Parameters
        [NinjaScriptProperty]
        [Display(Name = "CSV File Path", Order = 1, GroupName = "Parameters")]
        public string CsvFilePath { get; set; } = @"C:\TradingStrats\data.csv";

        [NinjaScriptProperty]
        [Display(Name = "Flask Server URL", Order = 2, GroupName = "Parameters")]
        public string FlaskServerUrl { get; set; } = "http://localhost:5000/predict";
		
		 [NinjaScriptProperty]
        [Display(Name = "Train", GroupName = "Machine Learning", Order = 0)]
        public bool Train { get; set; }
        #endregion

        // Volume bins for the current bar (10 bins: indices 0 through 9)
        private double[] volumeBins = new double[10];
        private double currentBarTotalVolume = 0;
        private double currentBarLow = 0;
        private double currentBarHigh = 0;

        // Pending distribution from the previous completed bar that awaits the next bar’s direction
        private double[] pendingDistribution = null;
        private DateTime pendingTime;

        // HttpClient for posting data to the Flask server
        private HttpClient httpClient;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Strategy that bins volume into 10 segments per bar, then writes the bin percentages and next bar direction to CSV and posts them to a Flask server.";
                Name = "MLTradingStrategy";
                Calculate = Calculate.OnEachTick;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                // Ensure the CSV directory exists.
                string directory = Path.GetDirectoryName(CsvFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            else if (State == State.DataLoaded)
            {
                httpClient = new HttpClient();
            }
        }

        protected override void OnBarUpdate()
        {
            // Execute on the first tick of a new bar.
            if (IsFirstTickOfBar && CurrentBar > 0)
            {
                // Compute distribution for the bar that just completed (bar index 1 relative to current bar)
                double[] distributionForBar = GetVolumeDistribution();
                DateTime completedBarTime = Time[1]; // timestamp of the just-closed bar

                // If we already have a pending distribution (from the previous bar), then we now have the next bar’s info.
                if (pendingDistribution != null)
                {
                    // The direction for the bar following the pending one is determined from the bar that just closed.
                    string direction = "NoTrade";
                    if (Close[1] > Open[1])
                        direction = "Long";
                    else if (Close[1] < Open[1])
                        direction = "Short";

					if(Train){
                    // Write pending bar distribution paired with the next bar’s direction.
                    WriteToCsv(pendingTime, pendingDistribution, direction);
					}
					if(State==State.Realtime){
                    SendDataToFlask(pendingTime, pendingDistribution, direction);
					}
                }

                // Update the pending distribution with the one we just computed.
                pendingDistribution = distributionForBar;
                pendingTime = completedBarTime;

                // Reset volume bins for the new bar using the current bar's initial values.
                Array.Clear(volumeBins, 0, volumeBins.Length);
                currentBarTotalVolume = 0;
                // For the new bar, set the initial low/high to the current bar’s values.
                currentBarLow = Low[0];
                currentBarHigh = High[0];
            }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Process only trade ticks (Last price). You could also adjust to include Bid/Ask if needed.
            if (e.MarketDataType != MarketDataType.Last)
                return;

            double price = e.Price;
            double volume = e.Volume;

            // Update the current bar's high and low if necessary.
            if (price < currentBarLow) currentBarLow = price;
            if (price > currentBarHigh) currentBarHigh = price;

            // Calculate the range and determine the appropriate bin.
            double range = currentBarHigh - currentBarLow;
            int binIndex = 0;
            if (range > 0)
            {
                double relativePosition = (price - currentBarLow) / range;
                binIndex = Math.Min(9, (int)(relativePosition * 10));
            }
            else
            {
                // If no range, assign volume to the middle bin.
                binIndex = 5;
            }

            volumeBins[binIndex] += volume;
            currentBarTotalVolume += volume;
        }

        private double[] GetVolumeDistribution()
        {
            double[] distribution = new double[10];
            if (currentBarTotalVolume > 0)
            {
                for (int i = 0; i < 10; i++)
                {
                    distribution[i] = volumeBins[i] / currentBarTotalVolume;
                }
            }
            return distribution;
        }

         private void WriteToCsv(DateTime barTime, double[] distribution, string direction)
		{
		    try
		    {
		        bool fileExists = File.Exists(CsvFilePath);
		        // Open the file for appending
		        using (StreamWriter sw = new StreamWriter(CsvFilePath, true))
		        {
		            // If the file doesn't exist or is empty, write headers.
		            if (!fileExists || new FileInfo(CsvFilePath).Length == 0)
		            {
		                sw.WriteLine("timestamp,bin0,bin1,bin2,bin3,bin4,bin5,bin6,bin7,bin8,bin9,direction");
		            }
		            // Build the CSV line: timestamp, bin0, bin1, ... bin9, direction
		            string line = barTime.ToString("yyyy-MM-dd HH:mm:ss");
		            foreach (double perc in distribution)
		            {
		                line += "," + perc.ToString("F4");
		            }
		            line += "," + direction;
		            sw.WriteLine(line);
		        }
		    }
		    catch (Exception ex)
		    {
		        Print("Error writing CSV: " + ex.Message);
		    }
		}


		 private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
		
        private async void SendDataToFlask(DateTime barTime, double[] distribution, string direction)
        {
            try
            {
                var data = new
                {
                    time = barTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    distribution = distribution,
                    direction = direction
                };
                string json = serializer.Serialize(data);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(FlaskServerUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();
                    Print("ML Prediction: " + responseString);
                }
                else
                {
                    Print("Error from Flask server: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Print("Error sending data to Flask: " + ex.Message);
            }
         
        }
    }
}
