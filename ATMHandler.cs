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
	public class ATMHandler : Strategy
	{
		
		//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button closeButton;
			private System.Windows.Controls.Grid myGrid;
		
		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
		    // Handle the button click event here
		    // You can implement the logic to switch between long-only, short-only, or ranged mode
		    // For example:
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
		
			string buttonText = button.Content.ToString();
   			 string buttonName = button.Name;
			
		 	if (button == shortButton )
		    {
				EnterShort(2, "short");
				SetProfitTarget(CalculationMode.Ticks,  currentBarRange / TickSize * 2);
				SetStopLoss(CalculationMode.Ticks, currentBarRange / TickSize);
			    
		    }
			
			
			
			
		    if (button == longButton )
		    {
				EnterLong(2, "long");
				SetProfitTarget(CalculationMode.Ticks,  currentBarRange / TickSize * 2);
				SetStopLoss(CalculationMode.Ticks, currentBarRange / TickSize);
		    }
			
			if (button == closeButton)
		    {
					if(Position.MarketPosition == MarketPosition.Short){
						ExitShort("short");
					}
					
					if(Position.MarketPosition == MarketPosition.Long){
						ExitLong("long");
					}
					
		    }
			
		
		    // Update the button content or perform any other necessary actions
		}
	
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "ATMHandler";
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
			else if (State == State.Historical)
			{
				if (UserControlCollection.Contains(myGrid))
				    return;
				
				Dispatcher.InvokeAsync((() =>
				{
				    myGrid = new System.Windows.Controls.Grid
				    {
				        Name = "MyCustomGrid", 
				        HorizontalAlignment = HorizontalAlignment.Right, 
				        VerticalAlignment = VerticalAlignment.Bottom,    
				        Margin = new Thickness(0, 0, 0, 60) // Adjust bottom margin as needed
				    };
				
				    // Define 3 rows
				    System.Windows.Controls.RowDefinition row1 = new System.Windows.Controls.RowDefinition(); // Row 0
				
				    // Define 4 columns
				    System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition(); // Col 0
				    System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition(); // Col 1
					System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition(); // Col 1
				
				    myGrid.RowDefinitions.Add(row1);
				
				    myGrid.ColumnDefinitions.Add(column1);
				    myGrid.ColumnDefinitions.Add(column2);
					myGrid.ColumnDefinitions.Add(column3);
				
				    // BUTTON DEFINITIONS
				    longButton = new System.Windows.Controls.Button
				    {
				        Name = "LongButton",
				        Content = "Enter Long",
				        Foreground = Brushes.White,
				        Background = Brushes.Green,
				    };
				
				    shortButton = new System.Windows.Controls.Button
				    {
				        Name = "ShortButton",
				        Content = "Enter Short",
				        Foreground = Brushes.White,
				        Background = Brushes.Red,
				    };
					
					   closeButton = new System.Windows.Controls.Button
				    {
				        Name = "CloseButton",
				        Content = "Close Position",
				        Foreground = Brushes.White,
				        Background = Brushes.Gray,
				    };
				
				
				    // Assign the same Click event handler to all buttons
				    longButton.Click += OnButtonClick;
				    shortButton.Click += OnButtonClick;
					closeButton.Click += OnButtonClick;
				    //
				    // BUTTON LAYOUT:
				    //
				    //  - Mode (Trend/Regression) in Row 0
				    //  - Long & Short in Row 1
				    //  - Auto Arm in Row 2
				    //
				
					
				    // Long button in Row 1, Column 0
				    System.Windows.Controls.Grid.SetRow(longButton, 1);
				    System.Windows.Controls.Grid.SetColumn(longButton, 0);
				
				    // Short button in Row 1, Column 1
				    System.Windows.Controls.Grid.SetRow(shortButton, 1);
				    System.Windows.Controls.Grid.SetColumn(shortButton, 1);
					
					 System.Windows.Controls.Grid.SetRow(closeButton, 1);
				    System.Windows.Controls.Grid.SetColumn(closeButton, 2);

				    myGrid.Children.Add(longButton);
				    myGrid.Children.Add(shortButton);
					myGrid.Children.Add(closeButton);
				
				    UserControlCollection.Add(myGrid);
				}));
			}
			else if (State == State.Terminated)
			{
				Dispatcher.InvokeAsync((() =>
				{
					if (myGrid != null)
					{
						if (longButton != null)
						{
							myGrid.Children.Remove(longButton);
							longButton.Click -= OnButtonClick;
							longButton = null;
						}
						if (shortButton != null)
						{
							myGrid.Children.Remove(shortButton);
							shortButton.Click -= OnButtonClick;
							shortButton = null;
						}
						if (closeButton != null)
						{
							myGrid.Children.Remove(closeButton);
							closeButton.Click -= OnButtonClick;
							closeButton = null;
						}
					}
				}));
			}
		}

		double currentBarRange;
		protected override void OnBarUpdate()
		{
			currentBarRange = ATR(14)[0];
		}
	}
}
