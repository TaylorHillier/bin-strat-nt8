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
	public class VolumeReversal : Strategy
	{
		private bool longMode = false;
		private bool shortMode = false;
		
		//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
			private System.Windows.Controls.Grid myGrid;
		
		private void OnButtonClick(object sender, RoutedEventArgs e)
		{
		    // Handle the button click event here
		    // You can implement the logic to switch between long-only, short-only, or ranged mode
		    // For example:
			System.Windows.Controls.Button button = sender as System.Windows.Controls.Button;
		
			string buttonText = button.Content.ToString();
   			 string buttonName = button.Name;
			
		 	if (button == shortButton && buttonText == "Arm Short" && buttonName == "ShortButton")
		    {
					// Switch to short-only mode
			    shortMode =  true;
				shortButton.Content = "Armed Short";
				shortButton.Background = Brushes.Red;
				  longMode = false;
				longButton.Content = "Arm Long";
				longButton.Background = Brushes.Gray;
		    }
			
			if (button == shortButton && buttonText == "Armed Short" && buttonName == "ShortButton" || (Position.MarketPosition != MarketPosition.Flat))
		    {
					// Switch to short-only mode
			    shortMode =  false;
				shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
					
		    }
			
			
			
		    if (button == longButton && buttonText == "Arm Long" && buttonName == "LongButton")
		    {
				// Switch to short-only mode
		        longMode = true;
				longButton.Content = "Armed Long";
				longButton.Background = Brushes.Green;
				shortMode = false;
				shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
		    }
			
			if (button == longButton && buttonText == "Armed Long" && buttonName == "LongButton"  || (Position.MarketPosition != MarketPosition.Flat))
		    {
				// Switch to short-only mode
		        longMode = false;
				longButton.Content = "Arm Long";
				longButton.Background = Brushes.Gray;
					
		    }
			
		
		    // Update the button content or perform any other necessary actions
		}
		
		private void resetButtons()
		{
		    Dispatcher.Invoke(() =>
	        {
	            longMode = false;
	            shortMode = false;
	            shortButton.Content = "Arm Short";
	            longButton.Content = "Arm Long";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
			});
		}
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "VolumeReversal";
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
				
				    myGrid.RowDefinitions.Add(row1);
				
				    myGrid.ColumnDefinitions.Add(column1);
				    myGrid.ColumnDefinitions.Add(column2);
				
				    // BUTTON DEFINITIONS
				    longButton = new System.Windows.Controls.Button
				    {
				        Name = "LongButton",
				        Content = longMode ? "Armed Long" : "Arm Long",
				        Foreground = Brushes.White,
				        Background = longMode ? Brushes.Green : Brushes.Gray,
				    };
				
				    shortButton = new System.Windows.Controls.Button
				    {
				        Name = "ShortButton",
				        Content = shortMode ? "Armed Short" : "Arm Short",
				        Foreground = Brushes.White,
				        Background = shortMode ? Brushes.Red : Brushes.Gray,
				    };
				
				
				    // Assign the same Click event handler to all buttons
				    longButton.Click += OnButtonClick;
				    shortButton.Click += OnButtonClick;
				
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

				    myGrid.Children.Add(longButton);
				    myGrid.Children.Add(shortButton);
				
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
					}
				}));
			}
		}

		bool enter = false; 
		int entrybar = 0;
		protected override void OnBarUpdate()
		{
			
			if(CurrentBar < 2) return;
		
			if(Volume[0] > Bollinger(Volumes[0],1.3, 10).Upper[0] && !enter){
				enter = true;
				entrybar = CurrentBar;
			};
			
			if(Volume[0] > Bollinger(Volumes[0],1.3, 10).Upper[0] && enter){
				entrybar = CurrentBar;
			};
			
			if(CurrentBar > entrybar + 1 && (!longMode && !shortMode)){
				enter = false;
			}
			
			if(longMode && enter && (Close[0] > High[CurrentBar - entrybar] || Close[0] < Low[CurrentBar - entrybar])){
				EnterLongLimit(1, GetCurrentBid());
				SetProfitTarget(CalculationMode.Ticks, Math.Abs(High[CurrentBar - entrybar] - Low[CurrentBar - entrybar])  / TickSize);
				SetStopLoss(CalculationMode.Ticks,  Math.Abs(High[CurrentBar - entrybar] - Low[CurrentBar - entrybar])  / TickSize);
				resetButtons();
				enter = false;
				entrybar = 0;
			}
			
			if(shortMode && enter && (Close[0] > High[CurrentBar - entrybar] || Close[0] < Low[CurrentBar - entrybar])){
				EnterShortLimit(1, GetCurrentAsk());
				SetProfitTarget(CalculationMode.Ticks,  Math.Abs(High[CurrentBar - entrybar] - Low[CurrentBar - entrybar]) / TickSize);
				SetStopLoss(CalculationMode.Ticks, Math.Abs(High[CurrentBar - entrybar] - Low[CurrentBar - entrybar])  / TickSize);
				resetButtons();
				enter = false;
				entrybar = 0;
			}
		}
		
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			
		}
		

	}
}
