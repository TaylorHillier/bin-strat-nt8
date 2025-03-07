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

namespace NinjaTrader.NinjaScript.Strategies
{

    public class L1Test : Strategy
    {
        #region Variables for Bar Data Collection
        // Aggregation variables for the current bar
        private double barTotalAsk = 0;
        private double barTotalBid = 0;
        private double barHigh = double.MinValue;
        private double barLow = double.MaxValue;
        private double barStartTime = double.MinValue;
        private double barEndTime = double.MinValue;

        // CSV file path for saving collected bar data (adjust path as needed)
        private string csvFilePath = @"C:\L1ML\bar_data.csv";
        // A flag to write header once
        private bool csvHeaderWritten = false;
        #endregion

        #region Strategy Setup
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Collects L1 data, aggregates bar-level information over a rolling window, writes to CSV (each row contains last 5 bars as features and the next bar's range as label), and sends via HTTP when live.";
                Name = "L1Test";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                // Create CSV directory if it doesn't exist.
                string directory = Path.GetDirectoryName(csvFilePath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
            }
            else if (State == State.Historical)
            {
                // Initialization for historical data if needed.
            }
        }
        #endregion

        #region Market Data Collection and Aggregation
        protected override void OnMarketData(MarketDataEventArgs e)
        {
    
        }
        #endregion

        #region Bar Finalization, Rolling Window CSV Writing, and HTTP Posting
	
        protected override void OnBarUpdate()
        {
         
        }
		
		#endregion 
		
		#region Properties

        [NinjaScriptProperty]
        [Display(Name = "Collect Training Data?", Order = 1, GroupName = "Parameters")]
        public bool Train { get; set; }
		
		#endregion
    }
}
