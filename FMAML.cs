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
using System.IO;
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
#endregion

// This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{

    public class DataPoint 
    {
        public double MaxPriceToFMA { get; set; }   // Maximum deviation from FMA at the extreme
        public double DaysRange { get; set; }       // Range of the day at the time of the extreme
        public double ATR { get; set; }             // ATR value at the time of the extreme
        public double FMAPrice { get; set; }        // FMA value at the time of the extreme
        public double DayHighToPrice { get; set; }  // Difference between day high and the price at extreme
        public double DayLowToPrice { get; set; }   // Difference between the price at extreme and day low
        public double CurrentPrice { get; set; }    // Price at the time the sample is taken (during pullback)
        public double ReversalPrice { get; set; }   // The confirmed reversal price
		

        // Computed property: distance from the current price (sample) to the reversal price.
        public double DistanceToReversal => ReversalPrice - CurrentPrice;

        public DataPoint(double maxPriceToFMA, double daysRange, double atr, double fmaPrice, double dayHighToPrice, double dayLowToPrice, double currentPrice, double reversalPrice)
        {
            MaxPriceToFMA = maxPriceToFMA;
            DaysRange = daysRange;
            ATR = atr;
            FMAPrice = fmaPrice;
            DayHighToPrice = dayHighToPrice;
            DayLowToPrice = dayLowToPrice;
            CurrentPrice = currentPrice;
            ReversalPrice = reversalPrice;
        }
    }

    public class FMAML : Strategy
    {
        // Final, stamped data points.
        private List<DataPoint> dataPoints = new List<DataPoint>();

        // Samples collected during the pullback period.
        private List<DataPoint> pendingDataPoints = new List<DataPoint>();

        // Variables used to track the extreme deviation.
        double maxDistanceFromFMA = 0;
        bool above = false;
        bool below = false;

        // Temporary feature values captured at the time of the extreme.
        double tempDaysRange = 0;
        double tempATR = 0;
        double tempFMAPrice = 0;
        double tempDayHighToPrice = 0;
        double tempDayLowToPrice = 0;
        double tempPriceAtExtreme = 0;
        bool extremeRecorded = false;

        // Optimized parameter from server: predicted distance to reversal.
        double optimizedDistanceToReversal = 0;
		
		[NinjaScriptProperty]
        [Display(Name = "Collect Data?", GroupName = "Machine Learning", Order = 0)]
        public bool collectCsvData { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                                   = @"Enter the description for your new custom Strategy here.";
                Name                                        = "FMAML";
                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                         = OrderFillResolution.Standard;
                Slippage                                    = 0;
                StartBehavior                               = StartBehavior.WaitUntilFlat;
                TimeInForce                                 = TimeInForce.Gtc;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 4000;
                IsInstantiatedOnEachOptimizationIteration   = true;
            }
            else if (State == State.Configure)
            {
            }
        }

		   
		double FMAValue;
		double price;
		double predictedReversalPoint = 0;
		
		protected override void OnBarUpdate()
		{
		    FMAValue = TaylorFMA(MovingAverageType.EMA, 12, 0, 0)[0];
		    price = Close[0];
		
		    if (FMAValue == 0)
		        return;
		
		    // During pullback (once an extreme is recorded), sample data points each bar.
		    if (extremeRecorded)
		    {
		        DataPoint sample = new DataPoint(maxDistanceFromFMA, tempDaysRange, tempATR, tempFMAPrice, tempDayHighToPrice, tempDayLowToPrice, price, 0);
		        pendingDataPoints.Add(sample);
		        //Print("Sampling pending data point. CurrentPrice = " + price + ", Extreme recorded = " + tempPriceAtExtreme);
		    }
		
		    // Reversal detection: only trigger if we have pending samples from a recorded extreme.
		    if (extremeRecorded && pendingDataPoints.Any())
		    {
		        if (above && price < FMAValue)
		        {
		            double reversalPrice = price; // reversal confirmed.
		            //Stamp each pending sample with the reversal price.
		            foreach (var sample in pendingDataPoints)
		            {
		                sample.ReversalPrice = reversalPrice;
		            }
		            //Print("Reversal detected (above branch). CurrentPrice = " + price + ", FMAValue = " + FMAValue + ", ReversalPrice = " + reversalPrice);
		            dataPoints.AddRange(pendingDataPoints);
		            pendingDataPoints.Clear();
		
		            maxDistanceFromFMA = 0;
		            above = false;
		            extremeRecorded = false;
		        }
		        else if (below && price > FMAValue)
		        {
		            double reversalPrice = price;
		            foreach (var sample in pendingDataPoints)
		            {
		                sample.ReversalPrice = reversalPrice;
		            }
		            //Print("Reversal detected (below branch). CurrentPrice = " + price + ", FMAValue = " + FMAValue + ", ReversalPrice = " + reversalPrice);
		            dataPoints.AddRange(pendingDataPoints);
		            pendingDataPoints.Clear();
		
		            maxDistanceFromFMA = 0;
		            below = false;
		            extremeRecorded = false;
		        }
		    }
		
		    // Update extreme values when price is above or below FMA.
		    if (price > FMAValue)
		    {
		        double currentDeviation = price - FMAValue;
		        if (currentDeviation > maxDistanceFromFMA)
		        {
		            // New extreme detected: clear pending samples if any.
		            if (pendingDataPoints.Any())
		            {
		                pendingDataPoints.Clear();
		                //Print("New extreme (above) detected - clearing previous pending samples.");
		            }
		            maxDistanceFromFMA = currentDeviation;
		            tempPriceAtExtreme = price;
		            if (maxDistanceFromFMA > 2)
		            {
		                tempDaysRange = CurrentDayOHL().CurrentHigh[0] - CurrentDayOHL().CurrentLow[0];
		                tempATR = ATR(40)[0];
		                tempFMAPrice = FMAValue;
		                tempDayHighToPrice = CurrentDayOHL().CurrentHigh[0] - price;
		                tempDayLowToPrice = price - CurrentDayOHL().CurrentLow[0];
		                extremeRecorded = true;
		                //Print("Extreme recorded (above): MaxDistanceFromFMA = " + maxDistanceFromFMA.ToString("N2") + ", PriceAtExtreme = " + tempPriceAtExtreme);
		            }
		        }
		        above = true;
		        below = false;
		    }
		    else if (price < FMAValue)
		    {
		        double currentDeviation = FMAValue - price;
		        if (currentDeviation > maxDistanceFromFMA)
		        {
		            // New extreme detected: clear pending samples if any.
		            if (pendingDataPoints.Any())
		            {
		                pendingDataPoints.Clear();
		                //Print("New extreme (below) detected - clearing previous pending samples.");
		            }
		            maxDistanceFromFMA = currentDeviation;
		            tempPriceAtExtreme = price;
		            if (currentDeviation > 2)
		            {
		                tempDaysRange = CurrentDayOHL().CurrentHigh[0] - CurrentDayOHL().CurrentLow[0];
		                tempATR = ATR(40)[0];
		                tempFMAPrice = FMAValue;
		                tempDayHighToPrice = CurrentDayOHL().CurrentHigh[0] - price;
		                tempDayLowToPrice = price - CurrentDayOHL().CurrentLow[0];
		                extremeRecorded = true;
		                //Print("Extreme recorded (below): MaxDistanceFromFMA = " + maxDistanceFromFMA.ToString("N2") + ", PriceAtExtreme = " + tempPriceAtExtreme);
		            }
		        }
		        below = true;
		        above = false;
		    }
		
		    // Write CSV if any data points exist and if data collection is enabled.
		    if (dataPoints.Any() && collectCsvData)
		    {
		       // Print("Writing " + dataPoints.Count + " data points to CSV.");
		        WriteDataPointsToCSV();
		        dataPoints.Clear();
		    }
		
		    if (State == State.Realtime)
		    {
		        // Send current predictive values to server and receive optimized values.
		        OptimizePredictiveValues();

		            //predictedReversalPoint = price + optimizedDistanceToReversal; // fallback
		
		        Draw.HorizontalLine(this, "reversal point", predictedReversalPoint, Brushes.Blue, DashStyleHelper.Solid, 2);
		    }
		}

		protected override void OnMarketData(MarketDataEventArgs e){

			if(e.Price > predictedReversalPoint && e.Price > FMAValue && predictedReversalPoint > FMAValue && e.Price - FMAValue > 20){
				EnterShort();
			}
			
			if(e.Price < predictedReversalPoint && e.Price < FMAValue && predictedReversalPoint < FMAValue && FMAValue - e.Price > 20 ){
				EnterLong();
			}
			
			if(Position.MarketPosition == MarketPosition.Long && e.Price > FMAValue){
				ExitLong();
			}
			
			if(Position.MarketPosition == MarketPosition.Short && e.Price < FMAValue){
				ExitShort();
			}

		}

        // Method to write dataPoints to CSV.
        private void WriteDataPointsToCSV()
        {
            string filePath = @"C:\ML\dataPoints.csv";
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Print("Created directory: " + directory);
            }

            bool fileExists = File.Exists(filePath);
            bool headerExists = false;
            if (fileExists)
            {
                string firstLine = File.ReadLines(filePath).FirstOrDefault();
                headerExists = firstLine != null && firstLine.StartsWith("MaxPriceToFMA,DaysRange,ATR,FMAPrice,DayHighToPrice,DayLowToPrice,CurrentPrice,ReversalPrice,DistanceToReversal");
               // Print("File exists. Header exists: " + headerExists);
            }
            else
            {
                Print("CSV file does not exist. Creating new file.");
            }

            using (StreamWriter writer = new StreamWriter(filePath, append: true))
            {
                if (!headerExists)
                {
                    writer.WriteLine("MaxPriceToFMA,DaysRange,ATR,FMAPrice,DayHighToPrice,DayLowToPrice,CurrentPrice,ReversalPrice,DistanceToReversal");
                    Print("CSV header written.");
                }
                foreach (var dp in dataPoints)
                {
                    writer.WriteLine($"{dp.MaxPriceToFMA},{dp.DaysRange},{dp.ATR},{dp.FMAPrice},{dp.DayHighToPrice},{dp.DayLowToPrice},{dp.CurrentPrice},{dp.ReversalPrice},{dp.DistanceToReversal}");
                }
            }
            //Print("CSV write completed.");
        }

		private void OptimizePredictiveValues()
		{
		    var predictiveValues = new
		    {
		        MaxPriceToFMA = maxDistanceFromFMA,
		        DaysRange = tempDaysRange,
		        ATR = tempATR,
		        FMAPrice = tempFMAPrice,
		        DayHighToPrice = tempDayHighToPrice,
		        DayLowToPrice = tempDayLowToPrice,
		        CurrentPrice = Close[0]
		    };
		
		    try
		    {
		        // Send a POST request to the combined "/optimize" endpoint.
		        var optimizedParams = NewHttpClientWrapper.Post("optimize", predictiveValues);
		        if (optimizedParams == null)
		        {
		            Print("Failed to retrieve response from server.");
		            return;
		        }
		
		        // Parse the optimized predicted distance and predicted reversal price.
		        double newPredictedDistance = Convert.ToDouble(optimizedParams["OptimizedDistanceToReversal"]);
		        predictedReversalPoint = Convert.ToDouble(optimizedParams["OptimizedPredictedReversalPrice"]);
		
		        optimizedDistanceToReversal = newPredictedDistance;
		
		        if (State == State.Realtime)
		            Print("Updated optimized distance to reversal: " + optimizedDistanceToReversal +
		                  ", Predicted reversal price: " + predictedReversalPoint);
		    }
		    catch (HttpRequestException ex)
		    {
		        Print($"HTTP error updating optimized parameters from server: {ex.Message}");
		    }
		    catch (Exception ex)
		    {
		        Print($"Error updating optimized parameters from server: {ex.Message}");
		    }
		}

    }
}
