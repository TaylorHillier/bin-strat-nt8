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
	
	public class EntryAssistant : Strategy
	{
		private bool Armed = false;
		private System.Windows.Controls.Grid myGrid;
		
		private System.Windows.Controls.Button modeButton;
		
		[NinjaScriptProperty]
		[Description("Select risk to reward")]
        [Category("Parameters")]
        public double RiskToReward { get; set; }
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Enter the description for your new custom Strategy here.";
				Name										= "EntryAssistant";
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
			}else if (State == State.Historical)
            {
                if (UserControlCollection.Contains(myGrid))
                    return;

                Dispatcher.InvokeAsync(() =>
                {
                    myGrid = new System.Windows.Controls.Grid
                    {
                        Name = "MyCustomGrid",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 60)
                    };

                    // Define rows and columns
                    myGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());

                    myGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());

                    modeButton = new System.Windows.Controls.Button
                    {
                        Name = "ModeButton",
                        Foreground = Brushes.White,
                        Background = Armed ? Brushes.Green : Brushes.Gray,
                        Content = Armed ? "Armed" : "UnArmed",
                    };

                    modeButton.Click += OnButtonClick;

                    // Layout buttons in grid
                    System.Windows.Controls.Grid.SetRow(modeButton, 0);
                    System.Windows.Controls.Grid.SetColumn(modeButton, 0);
                    System.Windows.Controls.Grid.SetColumnSpan(modeButton, 2);

                    // Add buttons to grid
                    myGrid.Children.Add(modeButton);

                    UserControlCollection.Add(myGrid);
                });
            }
            else if (State == State.Terminated)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (myGrid != null)
                    {
                        if (modeButton != null)
                        {
                            myGrid.Children.Remove(modeButton);
                            modeButton.Click -= OnButtonClick;
                            modeButton = null;
                        }
                    }
                });
            }
		}

		protected override void OnBarUpdate()
		{
			if(Armed) {
				if(Close[0] > Open[0]) {
					EnterLong("Long");
					SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[0] - Low[0]) / TickSize * RiskToReward);
					SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[0] - Low[0]) / TickSize);
					resetButtons();
					Print((Close[0] - Open[0]) / TickSize * RiskToReward);
					Print((Close[0] - Open[0]) / TickSize);
				}
				if(Close[0] < Open[0]) {
					EnterShort("Short");
					SetProfitTarget(CalculationMode.Ticks, Math.Abs(Close[0] - High[0]) / TickSize * RiskToReward);
					SetStopLoss(CalculationMode.Ticks, Math.Abs(Close[0] - High[0]) / TickSize);
					resetButtons();
				}
			}
		}
		
		private void resetButtons()
        {
            Dispatcher.Invoke(() =>
            {
				Armed = false;
                modeButton.Content = "UnArmed";
                modeButton.Background = Brushes.Gray;
            });
        }
		
		  private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            string buttonText = button.Content.ToString();
            string buttonName = button.Name;

            // Mode Button logic
            if (button == modeButton && buttonName == "ModeButton")
            {
                if (buttonText == "Armed")
                {
                    Armed = false;
                    modeButton.Content = "UnArmed";
                    modeButton.Background = Brushes.Gray;
 
                }
                else if (buttonText == "UnArmed")
                {
                    Armed = true;
                    modeButton.Content = "Armed";
                    modeButton.Background = Brushes.Green;
  
                }
            }
        }
	}
}
