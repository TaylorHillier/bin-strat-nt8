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

// This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class BayesianVolumeStreakStrategy : Strategy
    {
        private VolumeStreakLines volumeStreakLines;

        // Bayesian Parameters
        private double priorH1 = 0.5; // P(H=1): Probability of price increasing
        private double priorH0 = 0.5; // P(H=0): Probability of price decreasing

		
		public string  atmStrategyId			= string.Empty;
		public string  orderId					= string.Empty;
		public bool	isAtmStrategyCreated	= false;
		
        // Gaussian Parameters (Initialize with example values; adjust based on historical data)
        private double muPriceH1 = 0.0;
        private double sigmaPriceH1 = 1.0;
        private double muPriceH0 = 0.0;
        private double sigmaPriceH0 = 1.0;

        // Historical Return Data for Gaussian Parameter Estimation
        private List<double> historicalReturnsH1 = new List<double>();
        private List<double> historicalReturnsH0 = new List<double>();

        // Window Size for Rolling Gaussian Parameter Calculation
        private int windowSize = 200;

        // Flags to ensure priors are recalculated after each update
        private bool priorsCalculated = false;

		
			private bool isRegressionMode = false;
		private bool isTrendMode = false;
	
		  private int lastProcessedStreakStartBar = -1;
		
		private bool isLongMode = false;
		private bool isShortMode = false;
			private bool isAutoArm = false;
			//buttons/grid
		private System.Windows.Controls.Button longButton;
		private System.Windows.Controls.Button shortButton;
		private System.Windows.Controls.Button armButton;
		private System.Windows.Controls.Button modeButton;
		private System.Windows.Controls.Grid myGrid;
		
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Bayesian Inference Strategy using VolumeStreakLines Indicator.";
                Name = "BayesianVolumeStreakStrategy";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true; // Overlay on price panel
                
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
				ATMStrategy = "NQ Hyperscalp";
                // Set other default properties as needed
            }
            else if (State == State.Configure)
            {
                // Add the VolumeStreakLines indicator with desired parameters
                // Example: Threshold=40, NumBars=8, LineThickness=2
                volumeStreakLines = VolumeStreakLines(20, 30, 1f);
                AddChartIndicator(volumeStreakLines);
            }
            else if (State == State.DataLoaded)
            {
            
            }else if (State == State.Historical)
			{
			if (UserControlCollection.Contains(myGrid))
					return;
				
				Dispatcher.InvokeAsync((() =>
				{
					myGrid = new System.Windows.Controls.Grid
					{
						Name = "MyCustomGrid", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,    Margin = new Thickness(0, 0, 0, 60) // Adjust the bottom margin as needed
					};
					
					System.Windows.Controls.ColumnDefinition column1 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column2 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column3 = new System.Windows.Controls.ColumnDefinition();
					System.Windows.Controls.ColumnDefinition column4 = new System.Windows.Controls.ColumnDefinition();
					
					myGrid.ColumnDefinitions.Add(column1);
					myGrid.ColumnDefinitions.Add(column2);
					myGrid.ColumnDefinitions.Add(column3);
					myGrid.ColumnDefinitions.Add(column4);
					
					
					longButton = new System.Windows.Controls.Button
					{
					    Name = "LongButton",
					    Content = isLongMode ? "Armed Long" : "Arm Long",
					    Foreground = Brushes.White,
					    Background = isLongMode ? Brushes.Green : Brushes.Gray,
					};
					
					shortButton = new System.Windows.Controls.Button
					{
					    Name = "ShortButton",
					    Content = isShortMode ? "Armed Short" : "Arm Short",
					    Foreground = Brushes.White,
					    Background = isShortMode ? Brushes.Red : Brushes.Gray,
					};
					
					armButton = new System.Windows.Controls.Button
					{
					    Name = "ArmButton",
					    Content = isAutoArm ? "Auto Arm On" : "Auto Arm Off",
					    Foreground = Brushes.White,
					    Background = Brushes.Blue,
					};
					
					modeButton = new System.Windows.Controls.Button
					{
					    Name = "ModeButton",
					    Foreground = Brushes.White,
					    Background = isRegressionMode ? Brushes.Teal : Brushes.Purple,
					    Content = isRegressionMode ? "Regression" : "Trend",
					};
					
					longButton.Click += OnButtonClick;
					shortButton.Click += OnButtonClick;
					armButton.Click += OnButtonClick;
					modeButton.Click += OnButtonClick;
					
					System.Windows.Controls.Grid.SetColumn(longButton, 1);
					System.Windows.Controls.Grid.SetColumn(shortButton, 2);
					System.Windows.Controls.Grid.SetColumn(armButton, 3);
					System.Windows.Controls.Grid.SetColumn(modeButton, 0);
					
					myGrid.Children.Add(longButton);
					myGrid.Children.Add(shortButton);
					myGrid.Children.Add(armButton);
					myGrid.Children.Add(modeButton);
					
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
						if (armButton != null)
						{
							myGrid.Children.Remove(armButton);
							armButton.Click -= OnButtonClick;
							armButton = null;
						}
						if (modeButton != null)
						{
							myGrid.Children.Remove(modeButton);
							modeButton.Click -= OnButtonClick;
							modeButton = null;
						}
					}
				}));
			}
        }

        protected override void OnBarUpdate()
        {
      
        }
			
		int previousCount = 0;
		
		  protected override void OnMarketData(MarketDataEventArgs e)
        {
			
            if (CurrentBar < 1)
                return;

			
			
                 // Reference the existing VolumeStreakLines indicator
            List<VolumeStreakLines.StreakLine> streaks = volumeStreakLines.StreakLines;

            if (streaks == null || streaks.Count == 0)
                return;

            // Only process Last price updates
            if (e.MarketDataType != MarketDataType.Last)
                return;

            double currentPrice = e.Price;

            // Identify new streak lines that have not been processed yet
            List<VolumeStreakLines.StreakLine> newStreaks = streaks
                .Where(s => s.StartBar > lastProcessedStreakStartBar)
                .ToList();

            // Debugging: Print information to verify detection
            Print($"Order ID Length: {orderId.Length}");
            Print($"ATM Strategy ID Length: {atmStrategyId.Length}");
            Print($"New Streaks Count: {newStreaks.Count}");
            Print($"Previous Count: {previousCount}");
			 
      
			// Process each new streak
            foreach (var streak in newStreaks)
            {
                // Update the last processed streak start bar
                if (streak.StartBar > lastProcessedStreakStartBar)
                    lastProcessedStreakStartBar = streak.StartBar;

                double streakPrice = streak.PriceLevel;
			 
				 	 if (orderId.Length > 0 || atmStrategyId.Length > 0 || State != State.Realtime)
	        	return;
					 
	            if (isLongMode )
	            {
	                 isAtmStrategyCreated = false;
			    	orderId = GetAtmStrategyUniqueId();
			    	atmStrategyId = GetAtmStrategyUniqueId();
			
			    	AtmStrategyCreate(
			        OrderAction.Buy,
			        OrderType.Market, 0, 0, TimeInForce.Gtc,
			        orderId, ATMStrategy, atmStrategyId,
			        (atmCallbackErrorCode, atmCallBackId) =>
			        {
			            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
			            {
			                isAtmStrategyCreated = true;
			            }
			        });
	                Print($"[{Time[0]}] Entering Long Position (Auto Arm)");
	            }
	
	            if (isShortMode)
	            {
	                isAtmStrategyCreated = false;
			    	orderId = GetAtmStrategyUniqueId();
			    	atmStrategyId = GetAtmStrategyUniqueId();
			
			    	AtmStrategyCreate(
			        OrderAction.Sell,
			        OrderType.Market, 0, 0, TimeInForce.Gtc,
			        orderId, ATMStrategy, atmStrategyId,
			        (atmCallbackErrorCode, atmCallBackId) =>
			        {
			            if (atmCallbackErrorCode == ErrorCode.NoError && atmCallBackId == atmStrategyId)
			            {
			                isAtmStrategyCreated = true;
			            }
			        });
	                Print($"[{Time[0]}] Entering Short Position (Auto Arm) ");
	            }
			 }
            
            previousCount = streaks.Count;
			
			   // Manage ATM Strategies and Orders
            if (State == State.Realtime){
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
			    isShortMode =  true;
				shortButton.Content = "Armed Short";
				shortButton.Background = Brushes.Red;
					
		    }
			 if (button == shortButton && buttonText == "Armed Short" && buttonName == "ShortButton" || (Position.MarketPosition != MarketPosition.Flat))
		    {
					// Switch to short-only mode
			    isShortMode =  false;
				shortButton.Content = "Arm Short";
				shortButton.Background = Brushes.Gray;
					
		    }
		   if (button == armButton && buttonName == "ArmButton" && buttonText == "Auto Arm On")
		    {
		        // Switch to ranged mode
		        isAutoArm = false;
				longButton.Content = "Arm Long";
				shortButton.Content = "Arm Short";
				 isLongMode = false;
				 isShortMode = false;
				armButton.Content = "Auto Arm Off";
				shortButton.Background = Brushes.Gray;
				longButton.Background = Brushes.Gray;
			
				    Print($"Auto Arm deactivated: Long mode = {isLongMode}, Short mode = {isShortMode}, Auto Arm = {isAutoArm}");

		    }
			  if (button == armButton && buttonName == "ArmButton" && buttonText == "Auto Arm Off")
		    {
		        // Switch to ranged mode
		         isAutoArm = true;
				longButton.Content = "Armed Long";
				shortButton.Content = "Armed Short";
				 isLongMode = true;
				  isShortMode = true;
				armButton.Content = "Auto Arm On";
				shortButton.Background = Brushes.Red;
				longButton.Background = Brushes.Green;
				    Print($"Auto Arm activated: Long mode = {isLongMode}, Short mode = {isShortMode}, Auto Arm = {isAutoArm}");

			
		    }
		    if (button == longButton && buttonText == "Arm Long" && buttonName == "LongButton")
		    {
					// Switch to short-only mode
			        isLongMode = true;
					longButton.Content = "Armed Long";
					longButton.Background = Brushes.Green;
		    }
			 if (button == longButton && buttonText == "Armed Long" && buttonName == "LongButton")
		    {
					// Switch to short-only mode
			        isLongMode = false;
					longButton.Content = "Arm Long";
					longButton.Background = Brushes.Gray;
					
		    }
			if (buttonText == "Trend" && buttonName == "ModeButton" && button == modeButton)
		    {
		     
				isRegressionMode = true;
				isTrendMode = false;
				modeButton.Content = "Regression";
				modeButton.Background = Brushes.Teal;
				Print("regression - " + isRegressionMode);
    Print($"Mode changed: Regression mode activated, Trend mode deactivated");
				
		    }
		    else if (buttonText == "Regression" && buttonName == "ModeButton" && button == modeButton)
		    {
		   		isRegressionMode = false;
				isTrendMode = true;
				modeButton.Content = "Trend";
				modeButton.Background = Brushes.Purple;
    Print($"Mode changed: Trend mode activated, Regression mode deactivated");

		    }
		
		    // Update the button content or perform any other necessary actions
			
		    
		}
		
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="ATMStrategy", Order=1, GroupName="Parameters")]
		public string ATMStrategy
		{ get; set; }
		#endregion;
    }
}

