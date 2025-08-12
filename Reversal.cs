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
	public class BarData
	{
	    // bar index in your series
	    public int BarIndex { get; }
	    // net delta for that bar
	    public int Delta    { get; set; }
	
	    public BarData(int barIndex)
	    {
	        BarIndex  = barIndex;
	        Delta     = 0;
	    }
	}
	
	   public class LevelVolume
    {
        public double AskVolume { get; set; }
        public double BidVolume { get; set; }
        public double Total => AskVolume + BidVolume;
    }

	public class Reversal : Strategy
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "Reversal";
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
				// Disable this property for performance gains in Strategy Analyzer optimizations
				// See the Help Guide for additional information
				IsInstantiatedOnEachOptimizationIteration	= true;
			}
			else if (State == State.Configure)
			{
			}
		}

		double swingHigh;
		double swingLow;
		int swingHighBar;
		int swingLowBar;
		
		List<BarData> barData = new List<BarData>();
		int totalDelta = 0;
		
		  private Dictionary<int, Dictionary<double, LevelVolume>> barProfile 
            = new Dictionary<int, Dictionary<double, LevelVolume>>();
		
        protected override void OnBarUpdate()
        {
            // update swing pivots
            swingHigh     = Swing(2).SwingHigh[0];
            swingLow      = Swing(2).SwingLow[0];
            swingHighBar  = Swing(2).SwingHighBar(0, 1, 50);
            swingLowBar   = Swing(2).SwingLowBar(0, 1, 50);

            // ensure we have a BarData entry for delta (unchanged)
            if (barData.Count == 0 || barData.Last().BarIndex != CurrentBar)
                barData.Add(new BarData(CurrentBar));

            // ensure we have a profile dictionary for this bar
            if (!barProfile.ContainsKey(CurrentBar))
                barProfile[CurrentBar] = new Dictionary<double, LevelVolume>();
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || State != State.Realtime)
                return;

            // 1) update your existing bar‐delta
            var bd = barData.Find(d => d.BarIndex == CurrentBar);
            if (bd != null)
            {
                if (e.Price >= e.Ask)
                    bd.Delta += (int)e.Volume;
                else if (e.Price <= e.Bid)
                    bd.Delta -= (int)e.Volume;
            }

            // 2) update your per‐price profile for this bar
            var lvlMap = barProfile[CurrentBar];
            if (!lvlMap.TryGetValue(e.Price, out var lv))
            {
                lv = new LevelVolume();
                lvlMap[e.Price] = lv;
            }
            if (e.Price >= e.Ask)
                lv.AskVolume += e.Volume;
            else if (e.Price <= e.Bid)
                lv.BidVolume += e.Volume;

            // 3) drop any bars (and their profiles) outside current swing range
            int rangeStart = Math.Min(swingHighBar, swingLowBar);
            barData.RemoveAll(d => d.BarIndex < rangeStart);
            foreach (var old in barProfile.Keys.Where(idx => idx < rangeStart).ToArray())
                barProfile.Remove(old);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
		    // 1) figure our swing‐range bar indices
		    int startBar = CurrentBar - Math.Max(swingHighBar, swingLowBar);
		
		    // 2) guard: must be valid series indices
		    if (startBar < 0 || 
		        startBar >= Bars.Count )
		        return;
		
		    // 3) get X coords
		    float xStart = chartControl.GetXByBarIndex(ChartBars, startBar);
		    float xEnd   = chartControl.GetXByBarIndex(ChartBars, CurrentBar + 2 );
	
		    // 4) sort left/right
		    float left  = Math.Min(xStart, xEnd);
		    float right = Math.Max(xStart, xEnd);
		    float width = right - left;
		
		    // 5) get Y coords for your swings
		    float ySwingHigh = chartScale.GetYByValue(swingHigh);
		    float ySwingLow  = chartScale.GetYByValue(swingLow);
		
		    // 6) sort top/bottom
		    float top    = Math.Min(ySwingHigh, ySwingLow);
		    float bottom = Math.Max(ySwingHigh, ySwingLow);
		    float height = bottom - top;

		    // 7) if no area, bail
		    if (width <= 0 || height <= 0)
		        return;
		
		    // 8) draw the background rectangle
		    var bg = new SharpDX.Color4(155, 155, 155, 0.1f);
		    using (var brush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, bg))
		        RenderTarget.FillRectangle(new SharpDX.RectangleF(left, top, width, height), brush);
		
		    // 9) aggregate your profile just like before
		    int rangeStart = startBar;
		    var aggProfile = new SortedDictionary<double, LevelVolume>();
		    foreach (var kv in barProfile.Where(kv => kv.Key >= rangeStart))
		        foreach (var pv in kv.Value)
		        {
		            if (!aggProfile.TryGetValue(pv.Key, out var tot)) 
		                aggProfile[pv.Key] = tot = new LevelVolume();
		            tot.AskVolume += pv.Value.AskVolume;
		            tot.BidVolume += pv.Value.BidVolume;
		        }
		    if (aggProfile.Count == 0)
		        return;
		
		    // 10) find max to normalize
		    double maxTotal = aggProfile.Values.Max(l => l.Total);
		    float tickSize  = (float)Bars.Instrument.MasterInstrument.TickSize;
		
		    // 11) draw each price‐bar at the right edge
		    float profileX = right;
		    using (var vb = new SharpDX.Direct2D1.SolidColorBrush(
		               RenderTarget, new SharpDX.Color4(0.2f, 0.6f, 0.8f, 0.5f)))
		    {
		        foreach (var kv in aggProfile)
		        {
		            double price     = kv.Key;
		            LevelVolume lv   = kv.Value;
		            double normW     = (lv.Total / maxTotal) * width;
		
		            float y    = chartScale.GetYByValue(price);
		            float y2   = chartScale.GetYByValue(price - tickSize);
		            float barH = Math.Abs(y2 - y);
		
		            var barRect = new SharpDX.RectangleF(
		                profileX, 
		                y - barH/2, 
		                (float)normW, 
		                barH
		            );
		            RenderTarget.FillRectangle(barRect, vb);
		        }
		    }
		
		    // 12) redraw your delta (just so it stays on top)
		    int totalDelta = barData.Sum(d => d.Delta);
		    Draw.TextFixed(
		        this, 
		        "TotalDeltaText", 
		        $"Total Δ: {totalDelta}", 
		        TextPosition.TopRight, 
		        Brushes.White, 
		        new SimpleFont("Arial", 14), 
		        Brushes.Black, 
		        Brushes.Transparent, 
		        0
		    );
		}

	}
}
