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

// SharpDX for OnRender
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SDXBrush = SharpDX.Direct2D1.SolidColorBrush;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TCNLiveTrainer : Strategy
    {
        // --------- Config ----------
        [NinjaScriptProperty]
        [Display(Name = "PredictionEndpoint", Order = 0, GroupName = "Model")]
        public string PredictionEndpoint { get; set; } = "http://127.0.0.1:5000/predict";

        [NinjaScriptProperty]
        [Display(Name = "WriteCSV", Order = 1, GroupName = "Data")]
        public bool WriteCSV { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "SequenceLength", Order = 2, GroupName = "Model")]
        public int SequenceLength { get; set; } = 50;
		
		[NinjaScriptProperty]
		[Range(0.50, 0.99)]
		[Display(Name = "ConfidenceThreshold", Order = 10, GroupName = "Model")]
		public double ConfidenceThreshold { get; set; } = 0.70;

		
		// add near other fields
		private int requiredBars = 100; // will be recomputed precisely in State.Configure


        // --------- Paths / IO ----------
        private string dataDir = @"C:\tradingstrats";
        private string csvPath;
        private bool csvHasHeader = false;

        // --------- Delta tracking ----------
        // Accumulate intrabar here (updated by OnMarketData)
        private double curAggBuyVol = 0.0;
        private double curAggSellVol = 0.0;

        // Snapshot of the bar that just closed (used once when we write that bar)
        private double prevAggBuyVol = 0.0;
        private double prevAggSellVol = 0.0;
        private double PrevBarDelta => prevAggBuyVol - prevAggSellVol;

        // Track when a bar rolls so we only write once per bar close
        private int lastProcessedBar = -1;

        // Rolling sequence buffer for model input
        private Queue<double[]> featureQueue;

        // --------- Indicators (examples—swap/add yours) ----------
        private SMA smaFast, smaSlow;
        private RSI rsi;
        private ADX adx;

        // --------- HTTP ----------
        private HttpClient httpClient;
        private JavaScriptSerializer jss = new JavaScriptSerializer();

        // --------- UI text ----------
        private string probsText = "Waiting for predictions...";
		
		// --- live scoring & logging ---
		private string predCsvPath;
		private bool predCsvHeader = false;
		private int lastPredBarIdx = -1;
		// For confidence filtering
		private double lastPredPLong = double.NaN;
		private double lastPredPShort = double.NaN;
		private bool   lastPredConfident = false;

		private int correct = 0, total = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TCNLiveTrainer";
                // We want OnMarketData intrabar; keep OnEachTick but we only WRITE at bar close rollover.
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
				
				BarsRequiredToTrade =  Math.Max(50, SequenceLength + 1);
            }
            else if (State == State.Configure)
			{
			    smaFast = SMA(14);
			    smaSlow = SMA(50);
			    rsi     = RSI(14, 3);
			    adx     = ADX(14);
			
//			    AddChartIndicator(smaFast);
//			    AddChartIndicator(smaSlow);
//			    AddChartIndicator(rsi);
//			    AddChartIndicator(adx);
			
			    featureQueue = new Queue<double[]>(SequenceLength);
			    csvPath = Path.Combine(dataDir, "training_data.csv");
				predCsvPath = Path.Combine(dataDir, "predictions_log.csv");

			    // --- Compute a conservative warmup requirement ---
			    // Slowest SMA (50) needs ~50 bars
			    int needSMA  = Math.Max(14, 50);
			    // RSI(14,3) – use base period + a small cushion
			    int needRSI  = 14 + 3 + 5;
			    // ADX(14) typically needs ~2*period to fully stabilize; add cushion
			    int needADX  = (2 * 14) + 10;
			    // We read [1] & sometimes [2], add 2 bars
			    int needIdxs = 2;
			    // Sequence needs SequenceLength closed bars
			    int needSeq  = SequenceLength;
			
			    requiredBars = Math.Max(Math.Max(needSMA, Math.Max(needRSI, needADX)) + needIdxs,
			                            needSeq + 1);
			    // Ensure at least 100 if you like; comment this if you want just the computed value
			    requiredBars = Math.Max(requiredBars, 100);
			}
            else if (State == State.DataLoaded)
            {
                httpClient = new HttpClient();
            }
            else if (State == State.Realtime)
            {
                EnsureCsvReady();
            }
            else if (State == State.Terminated)
            {
                httpClient?.Dispose();
            }
        }

        // ---------- Aggressive delta accumulation: intrabar via Last vs bid/ask ----------
        protected override void OnMarketData(MarketDataEventArgs e)
		{
		    // ⬅️ HARD GUARD: OnMarketData can arrive before any bars (“bar -1”)
		    if (Bars == null || CurrentBar < 0)
		        return;
		
		    if (e.MarketDataType != MarketDataType.Last) 
		        return;
		
		    double bid = GetCurrentBid();
		    double ask = GetCurrentAsk();
		    double price = e.Price;
		    double vol = e.Volume;
		
		    double eps = Instrument.MasterInstrument.TickSize / 2.0;
		
		    if (!double.IsNaN(ask) && price >= ask - eps)
		        curAggBuyVol += vol;
		    else if (!double.IsNaN(bid) && price <= bid + eps)
		        curAggSellVol += vol;
		}


        // ---------- Bar logic: write at bar close rollover only ----------
		protected override void OnBarUpdate()
		{
		    // We need enough history for SMA/RSI/ADX + sequence + [1]/[2] indexing
		    if (CurrentBar < requiredBars)
		    {
		        Draw.TextFixed(this, "TCN_Probs",
		            $"Warming up... ({CurrentBar+1}/{requiredBars})",
		            TextPosition.TopRight,
		            Brushes.White,
		            new SimpleFont("Segoe UI", 14) { Bold = true },
		            Brushes.Black, Brushes.Black, 4);
		        return;
		    }
		
		    // Detect first tick of *new* bar => previous bar just closed
		    if (CurrentBar != lastProcessedBar)
		    {
		        // Snapshot the finished bar’s delta BEFORE reset
		        prevAggBuyVol  = curAggBuyVol;
		        prevAggSellVol = curAggSellVol;
		
		        // Build features from JUST-CLOSED bar [1]
		        var fv = BuildFeatureVectorForIndex(0, PrevBarDelta);
		
		        EnqueueFeatures(fv);
		
		        if (WriteCSV)
		            WriteRowToCsv(fv);
		
				if(State == State.Realtime){
		        if (featureQueue.Count >= SequenceLength)
		            _ = QueryAndStorePredictionAsync();
				}
		
		        // Reset accumulators for the new, forming bar
		        curAggBuyVol  = 0.0;
		        curAggSellVol = 0.0;
		
		        lastProcessedBar = CurrentBar;
		    }
			
			// --- score the bar that just closed (CurrentBar-1) if we had a prediction for it ---
			int justClosedBarIdx = CurrentBar - 1;
			if (lastPredBarIdx == justClosedBarIdx && lastPredConfident && !double.IsNaN(lastPredPLong))
			{
			    int actualLong = (Close[0] >= Open[0]) ? 1 : 0;
			    int predLong   = (lastPredPLong >= 0.5) ? 1 : 0; // threshold for class decision; independent from confidence filter
			    int hit        = (predLong == actualLong) ? 1 : 0;
			
			    total++;
			    if (hit == 1) correct++;
			
			    // write a log row
			    try
			    {
			        if (!predCsvHeader || !File.Exists(predCsvPath))
			        {
			            using (var sw = new StreamWriter(predCsvPath, false, Encoding.UTF8))
			                sw.WriteLine("Time,BarIndex,p_long,pred_long,actual_long,hit,acc_running");
			            predCsvHeader = true;
			        }
			        double acc = (total > 0) ? (double)correct / total : 0.0;
			        using (var sw = new StreamWriter(predCsvPath, true, Encoding.UTF8))
			            sw.WriteLine($"{Times[0][0]:O},{justClosedBarIdx},{lastPredPLong.ToString(System.Globalization.CultureInfo.InvariantCulture)},{predLong},{actualLong},{hit},{acc.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
			    }
			    catch (Exception ex) { Print($"Pred log write error: {ex.Message}"); }
			
			    // show running accuracy in the overlay
			    probsText = $"{probsText} | Acc: {(100.0 * correct / Math.Max(1,total)):F1}%";
			}
			// (then continue with your existing snapshot/write/predict/reset code)

		
		    Draw.TextFixed(this, "TCN_Probs",
		        probsText,
		        TextPosition.TopRight,
		        Brushes.White,
		        new SimpleFont("Segoe UI", 14) { Bold = true },
		        Brushes.Black, Brushes.Black, 4);
		}

        // ---------- Feature builder for a specific bar index (e.g., 1 = just-closed) ----------
        private double[] BuildFeatureVectorForIndex(int idx, double deltaForBar)
        {
            double o = Open[idx];
            double h = High[idx];
            double l = Low[idx];
            double c = Close[idx];
            double range = h - l;
            double ret = (c - Close[idx + 1]); // bar-to-bar return (closed bars)

            double smaF = smaFast[idx];
            double smaS = smaSlow[idx];
            double smaDiff = smaF - smaS;
            double rsiV = rsi[idx];
            double adxV = adx[idx];

            double vol = Volume[idx];

            return new double[]
            {
                ToTick(o), ToTick(h), ToTick(l), ToTick(c),
                range, ret,
                smaF, smaS, smaDiff,
                rsiV, adxV,
                vol,
                deltaForBar
            };
        }

        // Normalize by tick size only (no MinPrice in NT8)
        private double ToTick(double price)
        {
            return price / Instrument.MasterInstrument.TickSize;
        }

        private void EnqueueFeatures(double[] fv)
        {
            featureQueue.Enqueue(fv);
            while (featureQueue.Count > SequenceLength)
                featureQueue.Dequeue();
        }

        // ---------- CSV ----------
        private void EnsureCsvReady()
        {
            try
            {
                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);

                if (!File.Exists(csvPath))
                {
                    using (var sw = new StreamWriter(csvPath, false, Encoding.UTF8))
                        sw.WriteLine(HeaderLine());
                    csvHasHeader = true;
                }
                else csvHasHeader = true;
            }
            catch (Exception ex)
            {
                Print($"CSV init error: {ex.Message}");
            }
        }

        private string HeaderLine()
        {
            return string.Join(",",
                new[]
                {
                    "OpenT","HighT","LowT","CloseT","Range","Return",
                    "SMA_F","SMA_S","SMA_Diff","RSI","ADX","Volume","Delta",
                    "LabelLong","LabelShort"
                });
        }

        // Write row for the just-closed bar (features already from [1])
        private void WriteRowToCsv(double[] fv)
        {
            try
            {
                if (!csvHasHeader) EnsureCsvReady();

                // Simple bar-close label for the just-closed bar:
                // 1 if Close[1] >= Open[1], else 0 (adjust to your target/definition)
                double lblLong  = (Close[0] >= Open[0]) ? 1 : 0;
                double lblShort = 1 - lblLong;

                var row = new List<string>();
                foreach (var x in fv)
                    row.Add(x.ToString(System.Globalization.CultureInfo.InvariantCulture));
                row.Add(lblLong.ToString(System.Globalization.CultureInfo.InvariantCulture));
                row.Add(lblShort.ToString(System.Globalization.CultureInfo.InvariantCulture));

                using (var sw = new StreamWriter(csvPath, true, Encoding.UTF8))
                    sw.WriteLine(string.Join(",", row));
            }
            catch (Exception ex)
            {
                Print($"CSV write error: {ex.Message}");
            }
        }

        // ---------- HTTP prediction (no Newtonsoft / no System.Text.Json) ----------
        private async Task QueryAndStorePredictionAsync()
        {
            try
            {
                var seq = featureQueue.ToArray();

                var payload = new
                {
                    instrument = Instrument.MasterInstrument.Name,
                    timeframe  = BarsPeriod.BarsPeriodType.ToString(),
                    sequence   = seq
                };

                string json = jss.Serialize(payload);

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var resp = await httpClient.PostAsync(PredictionEndpoint, content))
                {
                    resp.EnsureSuccessStatusCode();
                    var body = await resp.Content.ReadAsStringAsync();

                    var pred = jss.Deserialize<PredictionResponse>(body);
                   if (pred != null)
					{
					    lastPredPLong  = pred.p_long;
					    lastPredPShort = pred.p_short;
					    lastPredConfident = (Math.Max(lastPredPLong, lastPredPShort) >= ConfidenceThreshold);
					    lastPredBarIdx = CurrentBar;  // prediction refers to the bar that just started
					
					    string confTag = lastPredConfident ? $"CONF ≥{ConfidenceThreshold:P0}" : $"< {ConfidenceThreshold:P0}";
					    probsText = $"Long: {lastPredPLong:F2} | Short: {lastPredPShort:F2} | {confTag}";
					}

                }
            }
            catch (Exception ex)
            {
                probsText = $"Predict err: {ex.Message}";
            }
			

        }

        private class PredictionResponse
        {
            public double p_long { get; set; }
            public double p_short { get; set; }
        }
    }
}