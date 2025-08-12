#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
using System.IO;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
// add if not already present
using System.Web.Script.Serialization;

#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    // ---------- DTO --------------------------------------------------
    public class ImportantBar
    {
       public int IsUp   { get; set; }
		public int IsDown { get; set; }

        public double Range, Body, BodyRangeRatio, UpperWick, LowerWick;
        public double Lag1RangeOC, Lag2RangeOC, Lag3RangeOC;
        public double ATR, RangeATR, EMA20Dist, Volume, DeltaPct;
        public double RelToHigh, RelToLow, RSI;

        public string ToCsv() =>
            $"{IsUp},{IsDown},{Range:F5},{Body:F5},{BodyRangeRatio:F5},{UpperWick:F5},{LowerWick:F5}," +
            $"{Lag1RangeOC:F5},{Lag2RangeOC:F5},{Lag3RangeOC:F5},{ATR:F5},{RangeATR:F5},{EMA20Dist:F5}," +
            $"{Volume:F0},{DeltaPct:F5},{RelToHigh:F5},{RelToLow:F5},{RSI:F5}";
    }
    // -----------------------------------------------------------------

    public class ImportantBars : Strategy
    {
        private const int lag = 5;
        private ATR atr14; private EMA ema20;
        private StreamWriter csv; private string csvPath;
        private int signalCounter;

        // ——— params ———
        [NinjaScriptProperty, Display(Name="Use ML?", Order=1)]
        public bool optimize { get; set; }

        [NinjaScriptProperty, Display(Name="Train", Order=2)]
        public bool train { get; set; }

        [NinjaScriptProperty, Range(0.0,1.0)]
        [Display(Name="ML Prob Threshold", Order=3)]
        public double ProbThreshold { get; set; }
        // ————————————

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "ImportantBars";
                Calculate = Calculate.OnBarClose;
                BarsRequiredToTrade = 30;
                optimize = false; train = false; ProbThreshold = .70;
            }
            else if (State == State.DataLoaded)
            {
                atr14 = ATR(14);  ema20 = EMA(20);
                csvPath = @"C:\TradingStrats\importantbars.csv";
                csv = new StreamWriter(csvPath, false){AutoFlush=true};
                csv.WriteLine("isUp,isDown,range,body,bodyRatio,upperWick,lowerWick," +
                              "lag1OC,lag2OC,lag3OC,ATR,rangeATR,EMA20dist,vol,deltaPct,relHigh,relLow,RSI");
            }
            else if (State == State.Terminated) csv?.Dispose();
        }

        // —— delta accumulation ——
        private double askCum,bidCum,deltaBar;
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType!=MarketDataType.Last || e.Volume<=0) return;
            if (e.Price==e.Ask) askCum+=e.Volume;
            if (e.Price==e.Bid) bidCum+=e.Volume;
            deltaBar = askCum-bidCum;
        }

		public int nextBar = 0;
		bool up = false;
		bool down = false;
        protected override void OnBarUpdate()
        {
            if (CurrentBar<20) return;

			
            // current-bar helpers
            double range = High[0]-Low[0], body = Close[0]-Open[0];
            double upper = High[0]-Math.Max(Open[0],Close[0]);
            double lower = Math.Min(Open[0],Close[0])-Low[0];
            double bodyRatio = range==0?0:Math.Abs(body)/range;
            double rangeATR  = atr14[0]==0?0:range/atr14[0];
            double emaDist   = Close[0]-ema20[0];
            double vol=Volume[0];
            double deltaPct  = vol==0?0:deltaBar/vol;

            // label
            bool upSeq = Close[1]>Open[1] && Close[2]>Open[2] && Close[3]>Open[3] && Close[4]<Open[4];
            bool dnSeq = Close[1]<Open[1] && Close[2]<Open[2] && Close[3]<Open[3] && Close[4]>Open[4];

            // hindsight row (t-lag)
            int t=lag;
            ImportantBar row = new ImportantBar{
                IsUp   = upSeq?1:0,
                IsDown = dnSeq?1:0,
                Range           = High[t]-Low[t],
                Body            = Close[t]-Open[t],
                BodyRangeRatio  = (High[t]-Low[t])==0?0:Math.Abs(Close[t]-Open[t])/(High[t]-Low[t]),
                UpperWick       = High[t]-Math.Max(Open[t],Close[t]),
                LowerWick       = Math.Min(Open[t],Close[t])-Low[t],
                Lag1RangeOC     = Close[t+1]-Open[t+1],
                Lag2RangeOC     = Close[t+2]-Open[t+2],
                Lag3RangeOC     = Close[t+3]-Open[t+3],
                ATR             = atr14[t],
                RangeATR        = atr14[t]==0?0:(High[t]-Low[t])/atr14[t],
                EMA20Dist       = Close[t]-ema20[t],
                Volume          = Volume[t],
                DeltaPct        = Volume[t]==0?0:deltaBar/Volume[0],
                RelToHigh       = CurrentDayOHL().CurrentHigh[t]-Close[t],
                RelToLow        = Close[t]-CurrentDayOHL().CurrentLow[t],
                RSI             = RSI(14,3)[t]
            };
            if (train) csv.WriteLine(row.ToCsv());

            // -------- live inference ----------
            if (!optimize) { resetDelta(); return; }

            double[] vec = {
                range, body, bodyRatio, upper, lower,
                Close[1]-Open[1], Close[2]-Open[2], Close[3]-Open[3],
                atr14[0], rangeATR, emaDist,
                vol, deltaPct,
                CurrentDayOHL().CurrentHigh[0]-Close[0],
                Close[0]-CurrentDayOHL().CurrentLow[0],
                RSI(14,3)[0]
            };

			            // OLD –- not supported: (double pUp, double pDn) = QueryModel(vec);
			var probs  = QueryModel(vec);          // ValueTuple<double,double>
			double pUp = probs.Item1;
			double pDn = probs.Item2;

            Print($"P(up)={pUp:F2}  P(dn)={pDn:F2}");

            if (pUp>=ProbThreshold && pUp>=pDn){
                Draw.ArrowUp(this,$"UP{CurrentBar}_{signalCounter++}",false,-1,Low[0]-TickSize,Brushes.Gold);
				nextBar = CurrentBar + 1;
				up = true;
			}
            else if (pDn>=ProbThreshold && pDn>pUp){
                Draw.ArrowDown(this,$"DN{CurrentBar}_{signalCounter++}",false,-1,High[0]+TickSize,Brushes.Red);
				nextBar = CurrentBar + 1;
				down = true;
			}
			
			if(CurrentBar == nextBar){
				if(up){
					EnterLong();
					SetProfitTarget(CalculationMode.Ticks, 40);
					SetStopLoss(CalculationMode.Ticks, 40);
					up = false;
					down = false;
				}
				if(down){
					EnterShort();
					SetProfitTarget(CalculationMode.Ticks, 40);
					SetStopLoss(CalculationMode.Ticks, 40);
					down = false;
					up = false;

				}
				
			}

            resetDelta();
        }
        private void resetDelta(){askCum=bidCum=deltaBar=0;}

		// ---------- synchronous HTTP call -----------------------
		private (double,double) QueryModel(double[] feats)
		{
		    string json = "{\"features\":[" + string.Join(",", feats.Select(f => f.ToString("F5"))) + "]}";
		    byte[] data = Encoding.UTF8.GetBytes(json);
		
		    try
		    {
		        var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:5002/predict");
		        req.Method = "POST";
		        req.ContentType = "application/json";
		        req.KeepAlive = false;
		
		        using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
		
		        using (var rsp = (HttpWebResponse)req.GetResponse())
		        using (var rd  = new StreamReader(rsp.GetResponseStream()))
		        {
		            string body = rd.ReadToEnd();
		
		            var dict = new JavaScriptSerializer()
		                           .Deserialize<Dictionary<string, double>>(body);
		
		            double up  = -1, dn = -1;
		            if (dict != null)
		            {
		                dict.TryGetValue("prob_up",   out up);
		                dict.TryGetValue("prob_down", out dn);
		            }
		            return (up, dn);
		        }
		    }
		    catch (Exception ex)
		    {
		        Print("HTTP error: " + ex.Message);
		    }
		    return (-1, -1);
		}


    }
}
