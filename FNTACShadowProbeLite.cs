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
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class FNTACShadowProbeLite : Strategy
	{
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "FNTACShadowProbeLite";
				Calculate									= Calculate.OnEachTick;
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
				#region Building Shadow Arrays									
					
				/* 
				stopTargetArray Column Legend:
				column 0 Stop Value (in ticks)
				column 1 Target Value (in ticks)
				column 2 break even win % (with commissions)
				*/
				
				//populating stop column stop/target array
				int x = stopTargetMin;			
				int y = stopTargetMin;
				for (int i = 0; i < arrayHeight; i++)
				{					
					stopTargetArray[0,i] = y;	
					if (x == stopTargetMax)
					{
						x = stopTargetMin - 1;
						y++;
					}
					x++;										
				}				
				
				//populating target column in stop/target array
				x = stopTargetMin;
				for (int i = 0; i < arrayHeight; i++)
				{					
					stopTargetArray[1,i] = x;	
					if (x == stopTargetMax)
						x = stopTargetMin - 1;
					x++;						
				}			
				
				//printing s/t array for reference
				for (int i = 0; i < arrayHeight; i++)
				{					
					Print("shadow array index (row number): " + i + " / stop value: " + stopTargetArray[0,i] + " / target value: " + stopTargetArray[1,i]);
				}	
				
				//filling break even win % column
				//this column of the stopTargetArray stores the win% required to break even at the respective S/T combo				
				//used in premium version www.free-ninjatrader-algo-code.com
				
				#endregion				
			}			
		}			
	
#region Control Variables //feel free to change these variables	
		
	const int stopTargetMin = 3; //the lowest stop/target value to test with shadow trades
	const int stopTargetMax = 50; //the highest stop/target value to test with shadow trades		
	bool clearOnTimer = false;	//clear shadow array row if it's been more than x time since first shadow trade after the last clear. only used in premium version. www.free-ninjatrader-algo-code.com
	int shadowSlippage = 2; //built in slippage, in ticks, in each shadow trade
	int startTrackingTime = 80000; //time to start shadow trading
	int startTradingTime = 84500; //time to start real trading
	int endTradingTime = 150000; //time to end shadow and real trading
	double commRate = 4.04; //your commission rate
		
#endregion
		
#region Program Variables //don't change these variables	
					
	//shadow tracking array structure		
	const int arrayWidth = 29;
	const int arrayHeight = (stopTargetMax - stopTargetMin + 1) * (stopTargetMax - stopTargetMin + 1);			
	double[,] stopTargetArray = new double[3,arrayHeight];
	double[,] shadowLongArray = new double[arrayWidth,arrayHeight];
	double[,] shadowShortArray = new double[arrayWidth,arrayHeight];
		
	//shadow trading analysis
	double netProfitLong;
	double maxNetProfitLong;
	int maxNetProfitIndexLong;		
	double netProfitShort;
	double maxNetProfitShort;
	int maxNetProfitIndexShort;				
	double maxRecentNetProfitLong;
	double maxRecentNetProfitShort;
	int maxRecentNetProfitIndexLong;
	int maxRecentNetProfitIndexShort;
	double recentNetProfitLong;		
	double recentNetProfitShort;	

	//misc
	bool eodUpkeep = true;			
	double tickValue;				
	int entryOrderIndex; //stored when entry criteria is checked, before order placed		
		
#endregion
	
#region Defining Shadow Trading Method
	
	/* 
	shadowLongArray & shadowShortArray Column Legend:
	0 Loss Count
	1 Win Count				
	2 Tick tracking counter A
	3 Number of trades
	4 Net Profit ($ with commission)
	5 Couter A Active (is the counter going? 1 or 0)
	6 Average trade
	7 Timestamp of first trade after clearing (counter A)				
	*/
	
	//Shadow Long Method 	
	private void ShadowLong (int row)
	{						
		//only initiate counter when counter is inactive					
		if (shadowLongArray[5,row] == 0 && Close[0] == GetCurrentBid()) //if counter is inacive, enter on bid		
		{
			shadowLongArray[2,row] = shadowSlippage * -1; //start counter at x to simulate toxic fill and slippage 100% of the time	
			shadowLongArray[5,row] = 1;	//counter in active state							
		}
		
		else if (shadowLongArray[5,row] == 1) //continue counter if it is already active
		{
			shadowLongArray[2,row] = shadowLongArray[2,row] + Convert.ToInt32((Low[0] - Low[1]) * 4);	//increment tick counter		
			//losers
			if (shadowLongArray[2,row] <= stopTargetArray[0,row] * -1)	//if counter <= stop value, add to total losses
			{	
				shadowLongArray[2,row] = 0;	//reset tick tracking counter
				shadowLongArray[0,row]++; //increment loss count
				shadowLongArray[3,row]++; //increment total number of trades
				shadowLongArray[5,row] = 0;	//return counter to inactive state				
				shadowLongArray[4,row] = (shadowLongArray[4,row] - (stopTargetArray[0,row] * tickValue)) - (commRate);	//total profit = total profit - stop value - commissions
				shadowLongArray[6,row] = shadowLongArray[4,row] / shadowLongArray[3,row];	//avg. trade = total profit/total trades					
			}	
			
			//winners
			if (shadowLongArray[2,row] > stopTargetArray[1,row]) //if counter > target value, add to total wins. price has to move through target
			{	
				shadowLongArray[2,row] = 0; //reset tick tracking counter
				shadowLongArray[1,row]++; //increment win count
				shadowLongArray[3,row]++; //increment total number of trades
				shadowLongArray[5,row] = 0; //return counter to inactive state
				shadowLongArray[4,row] = (shadowLongArray[4,row] + (stopTargetArray[1,row] * tickValue)) - (commRate); //total profit = total profit - stop value - commissions
				shadowLongArray[6,row] = shadowLongArray[4,row] / shadowLongArray[3,row]; //avg. trade = total profit/total trades				
			}		
		}
		
	} //end long method
	
//	//Shadow Short Method  
	private void ShadowShort (int row)
	{			
		//only initiate counter when counter is inactive
		if (shadowShortArray[5,row] == 0 && Close[0] == GetCurrentAsk()) //if counter is inacive, enter on ask
		{
			shadowShortArray[2,row] = shadowSlippage; //start counter at x to simulate toxic fill and slippage100% of the time	
			shadowShortArray[5,row] = 1; //counter in active state					
		}
					
		else if (shadowShortArray[5,row] == 1) //continue counter if it is already active
		{
			shadowShortArray[2,row] = shadowShortArray[2,row] + Convert.ToInt32((High[0] - High[1]) * 4);	//increment tick counter		
			//losers
			if (shadowShortArray[2,row] >= stopTargetArray[0,row])	//if counter >= stop value, add to total losses
			{	
				shadowShortArray[2,row] = 0; //reset tick tracking counter
				shadowShortArray[0,row]++; //increment loss count
				shadowShortArray[3,row]++; //increment total number of trades
				shadowShortArray[5,row] = 0; //return counter to inactive state
				shadowShortArray[4,row] = (shadowShortArray[4,row] - (stopTargetArray[0,row] * tickValue)) - (commRate);	//total profit = total profit - stop value - commissions
				shadowShortArray[6,row] = shadowShortArray[4,row] / shadowShortArray[3,row];	//avg. trade = total profit/total trades					
			}	
			
			//winners
			if (shadowShortArray[2,row] < (stopTargetArray[1,row] * -1))	//if counter < target value, add to total wins. price has to move through target
			{	
				shadowShortArray[2,row] = 0;	//reset tick tracking counter
				shadowShortArray[1,row]++;	//increment win count
				shadowShortArray[3,row]++;	//increment total number of trades
				shadowShortArray[5,row] = 0;	//return counter to inactive state
				shadowShortArray[4,row] = (shadowShortArray[4,row] + (stopTargetArray[1,row] * tickValue)) - (commRate);	//total profit = total profit - stop value - commissions
				shadowShortArray[6,row] = shadowShortArray[4,row] / shadowShortArray[3,row];	//avg. trade = total profit/total trades				
			}		
		}

	} //end short method				
			
#endregion
	
protected override void OnBarUpdate()
{		

	if (State != State.Realtime)
		return;		
	
	if (Bars.Count < BarsRequiredToTrade)			
		return;			
	
#region beginning of bar upkeep	
	
	//the value of one tick (e.g. $12.50 for ES) initialized here because TickSize is not accessible at class level
	tickValue = TickSize * Instrument.MasterInstrument.PointValue;	
			
	if(ToTime(Time[0]) == startTrackingTime && eodUpkeep == true)
	{
		eodUpkeep = false;	
		Print("Beginning of Day. Shadow Tracking Started");
	}	
		
#endregion	
			
#region Running Shadow Strategies
	
	if (ToTime(Time[0]) >= startTrackingTime && ToTime(Time[0]) < endTradingTime) //running shadow strategy for longs if we're within the tracking time frame
	{											            
		for (int i = 0; i < arrayHeight; i++) 
		{		
			ShadowLong(i);
		}	
	}
	
	if (ToTime(Time[0]) >= startTrackingTime && ToTime(Time[0]) < endTradingTime) //running shadow strategy for shorts if we're within the tracking time frame
	{					            
		for (int i = 0; i < arrayHeight; i++)
		{			
			ShadowShort(i);
		}				
	}

#endregion		

#region Analyzing Shadow Data // this is where we run calculations on the data collected from our shadow trading
		
	//resetting these variables to -100000 so the for loop will find a new max
	maxNetProfitLong = -100000; 
	maxNetProfitShort = -100000;
	maxRecentNetProfitLong = -100000; 
	maxRecentNetProfitShort = -100000;		
	
	//finding maxes
	for (int i = 0; i < arrayHeight; i++)
	{			
		#region Find Stop/Target Combo with Max Net Daily Profit
		
			netProfitLong = shadowLongArray[4,i]; // row 4 of the array stores the daily net profit
			netProfitShort = shadowShortArray[4,i];		
			
			//find max daily net profit for long shadow trades			
		    if (netProfitLong > maxNetProfitLong)
		    {
		        maxNetProfitLong = netProfitLong;
		        maxNetProfitIndexLong = i; // this is the number we'll use to set our S/T
		    }			    
			
			//find max daily net profit for short shadow trades
		    if (netProfitShort > maxNetProfitShort)
		    {
		        maxNetProfitShort = netProfitShort;
		        maxNetProfitIndexShort = i; // this is the number we'll use to set our S/T
		    }				
			
		#endregion
			
		#region Find Stop/Target Combo with Max Net Profit over the last few shadow trades
			
			//find max net profit over length of profit history list for long shadow trades. list length set with profitHistLength variable.
			//used in premium version at www.free-ninjatrader-algo-code.com
			
		#endregion
			
	}
		
#endregion
	
#region Entry Logic
	
	if (Position.MarketPosition == MarketPosition.Flat && ToTime(Time[0]) > startTradingTime && ToTime(Time[0]) < endTradingTime)
	{			
		
		#region Enter based on recent net profit (the last x shadow trades, where x = profitHistLength)
			//available in premium version at www.free-ninjatrader-algo-code.com
		#endregion		
		
		#region Enter based on daily net profit					
			// entries
			if (maxNetProfitLong > 0 && maxNetProfitLong > maxNetProfitShort) // if shadow S/T combo has a positive daily P/L and the long P/L is higher than the short
			{
				// set S/T to the appropriate combination from the "Analyzing Shadow Data" region
				entryOrderIndex = maxNetProfitIndexLong;
				SetStopLoss(CalculationMode.Ticks, stopTargetArray[1,entryOrderIndex]); // the stop value of the S/T combo that has the highest net profit
				SetProfitTarget(CalculationMode.Ticks, stopTargetArray[0,entryOrderIndex]); // the target value of the S/T combo that has the highest net profit	
			
				EnterLong("long");
				Print("Long order placed with " + stopTargetArray[1,entryOrderIndex] + " tick stop and " + stopTargetArray[0,entryOrderIndex] + " tick target. Shadow array index: " + entryOrderIndex);
			}
			
			if (maxNetProfitShort > 0 && maxNetProfitShort > maxNetProfitLong) // if shadow S/T combo has a positive daily P/L and the short P/L is higher than the long
			{
				// set S/T to the appropriate combination from the "Analyzing Shadow Data" region
				entryOrderIndex = maxNetProfitIndexShort;
				SetStopLoss(CalculationMode.Ticks, stopTargetArray[1,entryOrderIndex]); // the stop value of the S/T combo that has the highest net profit
				SetProfitTarget(CalculationMode.Ticks, stopTargetArray[0,entryOrderIndex]); // the target value of the S/T combo that has the highest net profit
			
				EnterShort("short");
				Print("Short order placed with " + stopTargetArray[1,entryOrderIndex] + " tick stop and " + stopTargetArray[0,entryOrderIndex] + " tick target. Shadow array index: " + entryOrderIndex);
			}				
		#endregion
			
	}//end entry logic
	
#endregion			
	
#region End of daily session upkeep
	
	if (ToTime(Time[0]) > endTradingTime && eodUpkeep == false && Position.MarketPosition == MarketPosition.Flat)
	{							
		Print("End of day");			
		
		Print("The most profitable Shadow Probe Stop/Target combination for LONG trades for the whole day was:");		
		Print("Stop: " + stopTargetArray[0,maxNetProfitIndexLong] + " / Target: " + stopTargetArray[1,maxNetProfitIndexLong]);
		Print("The current day's P/L for LONG probes with this S/T combination was: " + shadowLongArray[4,maxNetProfitIndexLong]);
		
		Print("The most profitable Shadow Probe Stop/Target combination for SHORT trades for the whole day was:");		
		Print("Stop: " + stopTargetArray[0,maxNetProfitIndexShort] + " / Target: " + stopTargetArray[1,maxNetProfitIndexShort]);
		Print("The current day's P/L for SHORT probes with this S/T combination was: " + shadowShortArray[4,maxNetProfitIndexShort]);
		
		//end of daily session upkeep			
		Array.Clear(shadowLongArray, 0, shadowLongArray.Length);
		Array.Clear(shadowShortArray, 0, shadowShortArray.Length);				
						
		eodUpkeep = true;		
	}

	
#endregion		
				
} //end OnBarUpdate		

} //end class
	
}

//Copyright free-ninjatrader-algo-code.com 2018