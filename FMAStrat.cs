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
	public class MyCustomStrategy : Strategy
	{
		public string  atmStrategyId			= string.Empty;
		 public string  orderId					= string.Empty;
		 public bool	isAtmStrategyCreated	= false;
		
			private bool isRegressionMode = false;
		private bool isTrendMode = false;
		
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "FMAStrategy";
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
			
		}

		bool longFMAtouched = false;
		bool tradeTaken = false;
		bool isabove = false;
		bool isbelow = false;
		protected override void OnBarUpdate()
		{
			//if(State == State.Realtime)
			//{
			
			double FMA = TaylorFMA(MovingAverageType.EMA, 17)[0];
			var Z = ZScoreV10(8,20);
			
			if(Z.Z[0] >  Z.Upper2_Offset ) {
				isabove = true;
			}
			if(Z.Z[0] <  Z.Lower2_Offset ) {
				isbelow = true;
			}
		
		
		
			//if(orderId.Length == 0 && atmStrategyId.Length == 0  && !tradeTaken)
				//{
				if(Z.Z[0] <  Z.Upper2_Offset && isabove && Close[0] < FMA){
//					#region ATMStrat
	
//							isAtmStrategyCreated = false;  // reset atm strategy created check to false
//							atmStrategyId = GetAtmStrategyUniqueId();
//							orderId = GetAtmStrategyUniqueId();
//							AtmStrategyCreate(OrderAction.Sell, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) => {
//								//check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
//							if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
//							isAtmStrategyCreated = true;
							
//						});
//						#endregion
					isabove = false;
					if(State == State.Historical){
					EnterShort();
					SetProfitTarget(CalculationMode.Ticks,160);
					SetStopLoss(CalculationMode.Ticks,80);
					}
				}
				
				if(Z.Z[0] >  Z.Lower2_Offset && isbelow && Close[0] > FMA){
//					#region ATMStrat
	
//							isAtmStrategyCreated = false;  // reset atm strategy created check to false
//							atmStrategyId = GetAtmStrategyUniqueId();
//							orderId = GetAtmStrategyUniqueId();
//							AtmStrategyCreate(OrderAction.Buy, OrderType.Market, 0, 0, TimeInForce.Gtc, orderId, ATMStrategy, atmStrategyId, (atmCallbackErrorCode, atmCallBackId) => {
//								//check that the atm strategy create did not result in error, and that the requested atm strategy matches the id in callback
//							if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
//							isAtmStrategyCreated = true;
							
//						});
//						#endregion
					isbelow = false;
					if(State == State.Historical){
						EnterLong();
						SetProfitTarget(CalculationMode.Ticks,160);
						SetStopLoss(CalculationMode.Ticks,80);
					}
				}
			//}
			
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
		//}
		
		
		#region Properties
		
			[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="LongFMA length", Order=1, GroupName="Parameters")]
		public int longFMALength
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="shortFMA length", Order=1, GroupName="Parameters")]
		public int shortFMALength
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Long FMA type", Order=1, GroupName="Parameters")]
		public MovingAverageType longFMAtype
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="short FMA type", Order=1, GroupName="Parameters")]
		public MovingAverageType shortFMAtype
		{ get; set; }
		
		#endregion;
	}
}
