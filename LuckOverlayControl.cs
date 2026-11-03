using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BgLuck
{
	internal sealed class LuckOverlayControl : UserControl
	{
		private readonly TextBlock _status;
		private readonly TextBlock _actualValue;
		private readonly TextBlock _expectedValue;
		private readonly TextBlock _liveScore;
		private readonly TextBlock _score;
		private readonly TextBlock _healthLine;
		private readonly Grid _turnSummary;
		private readonly TextBlock _turnCount;
		private readonly Button _expandTurnsButton;
		private readonly StackPanel _turnDetails;
		private readonly TextBlock _lobbyMessage;
		private readonly StackPanel _content;
		private readonly StackPanel _damageLine;
		private readonly Thumb _resizeGrip;
		private readonly Image _icon;
		private readonly Brush _gold = new SolidColorBrush(Color.FromRgb(255, 204, 64));
		private readonly Brush _green = new SolidColorBrush(Color.FromRgb(104, 210, 132));
		private readonly Brush _red = new SolidColorBrush(Color.FromRgb(242, 105, 105));
		private readonly Brush _neutral = new SolidColorBrush(Color.FromRgb(225, 225, 225));
		private readonly Brush _lightGreen = new SolidColorBrush(Color.FromRgb(169, 224, 152));
		private readonly Brush _mediumGreen = new SolidColorBrush(Color.FromRgb(115, 210, 113));
		private readonly Brush _strongGreen = new SolidColorBrush(Color.FromRgb(43, 175, 91));
		private readonly Brush _lightRed = new SolidColorBrush(Color.FromRgb(242, 156, 156));
		private readonly Brush _mediumRed = new SolidColorBrush(Color.FromRgb(235, 105, 105));
		private readonly Brush _strongRed = new SolidColorBrush(Color.FromRgb(190, 45, 55));
		private bool _dismissed;
		private bool _dragging;
		private bool _turnsExpanded;
		private Point _dragStart;
		private double _startLeft;
		private double _startTop;

		public LuckOverlayControl()
		{
			Width = 190;
			Height = 100;
			HorizontalAlignment = HorizontalAlignment.Left;
			VerticalAlignment = VerticalAlignment.Top;

			_status = new TextBlock
			{
				Text = "BG Luck",
				Foreground = Brushes.White,
				FontSize = 14,
				FontWeight = FontWeights.SemiBold
			};
			_liveScore = new TextBlock
			{
				Foreground = _neutral,
				FontSize = 14,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Cursor = Cursors.Help,
				Margin = new Thickness(5, 0, 12, 0)
			};
			_actualValue = new TextBlock { Foreground = _neutral, FontSize = 13, ToolTip = "Damage taken" };
			_expectedValue = new TextBlock { Foreground = _gold, FontSize = 13, ToolTip = "Expected damage taken" };
			_score = new TextBlock { Foreground = _neutral, FontSize = 13, Visibility = Visibility.Collapsed };
			_healthLine = new TextBlock { Foreground = _neutral, FontSize = 13, Visibility = Visibility.Collapsed };
			_turnCount = new TextBlock { Foreground = _neutral, FontSize = 13 };
			_expandTurnsButton = new Button
			{
				Content = "\u25BE",
				Width = 18,
				Height = 18,
				Padding = new Thickness(0),
				Foreground = _neutral,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Focusable = false,
				ToolTip = "Show turn-by-turn damage"
			};
			_turnSummary = new Grid { Visibility = Visibility.Collapsed };
			_turnSummary.ColumnDefinitions.Add(new ColumnDefinition());
			_turnSummary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			Grid.SetColumn(_turnCount, 0);
			Grid.SetColumn(_expandTurnsButton, 1);
			_turnSummary.Children.Add(_turnCount);
			_turnSummary.Children.Add(_expandTurnsButton);
			_turnDetails = new StackPanel { Visibility = Visibility.Collapsed };
			_lobbyMessage = new TextBlock
			{
				Text = "Plugin Ready - Waiting for game to start...",
				Foreground = _neutral,
				FontSize = 12,
				Visibility = Visibility.Collapsed
			};
			_content = new StackPanel { Margin = new Thickness(12, 8, 30, 10) };
			_icon = new Image
			{
				Source = new System.Windows.Media.Imaging.BitmapImage(
					new Uri("pack://application:,,,/BgLuck;component/Assets/OFC.png")),
				Width = 19,
				Height = 19,
				Stretch = Stretch.Uniform,
				Margin = new Thickness(0, 0, 5, 0),
				ToolTip = "BG Luck"
			};
			var header = new Grid();
			header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			header.VerticalAlignment = VerticalAlignment.Center;
			Grid.SetColumn(_icon, 0);
			Grid.SetColumn(_status, 1);
			Grid.SetColumn(_liveScore, 2);
			header.Children.Add(_icon);
			header.Children.Add(_status);
			header.Children.Add(_liveScore);
			_content.Children.Add(header);
			_content.Children.Add(new Border
			{
				Height = 1,
				Margin = new Thickness(0, 5, 0, 4),
				Background = new SolidColorBrush(Color.FromArgb(95, 190, 200, 215))
			});
			_content.Children.Add(_turnSummary);
			_damageLine = CreateDamageLine();
			_content.Children.Add(_damageLine);
			_content.Children.Add(_healthLine);
			_content.Children.Add(_score);
			_content.Children.Add(new ScrollViewer
			{
				Content = _turnDetails,
				MaxHeight = 320,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
			});
			_content.Children.Add(_lobbyMessage);

			var close = new Button
			{
				Content = "x",
				Width = 22,
				Height = 22,
				Padding = new Thickness(0),
				Foreground = Brushes.White,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Focusable = false
			};
			close.Click += (sender, args) => Dismiss();
			_expandTurnsButton.Click += (sender, args) => SetTurnsExpanded(!_turnsExpanded);
			var layout = new Grid();
			layout.Children.Add(_content);
			layout.Children.Add(close);
			_resizeGrip = new Thumb
			{
				Width = 15,
				Height = 15,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Cursor = Cursors.SizeNWSE,
				Background = new SolidColorBrush(Color.FromArgb(110, 150, 160, 175)),
				BorderBrush = new SolidColorBrush(Color.FromArgb(180, 190, 200, 215)),
				BorderThickness = new Thickness(1),
				ToolTip = "Drag to resize"
			};
			_resizeGrip.DragDelta += (sender, args) =>
			{
				Width = Math.Max(190, Width + args.HorizontalChange);
				Height = Math.Max(54, Height + args.VerticalChange);
			};
			layout.Children.Add(_resizeGrip);
			Content = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(224, 28, 32, 34)),
				BorderBrush = new SolidColorBrush(Color.FromArgb(220, 88, 94, 98)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Child = layout
			};

			header.Cursor = Cursors.SizeAll;
			header.MouseLeftButtonDown += (sender, args) =>
			{
				if(!(Parent is Canvas canvas))
					return;
				_dragStart = args.GetPosition(canvas);
				_startLeft = Canvas.GetLeft(this);
				_startTop = Canvas.GetTop(this);
				header.CaptureMouse();
				_dragging = true;
				args.Handled = true;
			};
			header.MouseMove += (sender, args) =>
			{
				if(!_dragging || !(Parent is Canvas canvas))
					return;
				var current = args.GetPosition(canvas);
				var maxLeft = Math.Max(0, canvas.ActualWidth - ActualWidth);
				var maxTop = Math.Max(0, canvas.ActualHeight - ActualHeight);
				Canvas.SetLeft(this, Math.Max(0, Math.Min(maxLeft, _startLeft + current.X - _dragStart.X)));
				Canvas.SetTop(this, Math.Max(0, Math.Min(maxTop, _startTop + current.Y - _dragStart.Y)));
			};
			header.MouseLeftButtonUp += (sender, args) =>
			{
				_dragging = false;
				header.ReleaseMouseCapture();
			};
		}

		public void ShowLive(
			int actualDamageTaken,
			double expectedDamageTaken,
			int scoredActualDamageTaken,
			double? ratio,
			int? currentHealth,
			double? expectedHealth,
			bool isSpectator)
		{
			_status.Text = isSpectator ? "BG Luck - Spectating: " : "BG Luck: ";
			_actualValue.Text = "DT: " + FormatDamageTaken(actualDamageTaken);
			_expectedValue.Text = "Exp. DT: " + FormatDamageTaken(expectedDamageTaken);
			SetLiveScoreLabel(ratio, true);
			_liveScore.ToolTip = BuildLiveScoreTooltip(scoredActualDamageTaken, expectedDamageTaken, ratio);
			_liveScore.Foreground = GetScoreColor(ratio);
			_damageLine.Visibility = Visibility.Visible;
			SetHealthLine(currentHealth, expectedHealth);
			_healthLine.Visibility = Visibility.Visible;
			_lobbyMessage.Visibility = Visibility.Collapsed;
			_score.Visibility = Visibility.Collapsed;
			_turnSummary.Visibility = Visibility.Collapsed;
			_turnDetails.Visibility = Visibility.Collapsed;
			_actualValue.Foreground = GetScoreColor(ratio);
			ShowIfNotDismissed();
		}

		public void ResetForNewGame()
		{
			_dismissed = false;
			SetTurnsExpanded(false);
			HideOverlay();
		}

		public void ShowFinalSummary(
			int actualDamageTaken,
			double expectedDamageTaken,
			int scoredActualDamageTaken,
			int turnCount,
			int scoredCombats,
			double? ratio,
			int? actualHealth,
			double? expectedHealth,
			IList<string> turnSummaries,
			bool isSpectator)
		{
			Height = 100;
			_turnsExpanded = false;
			_status.Text = isSpectator ? "BG Luck - Spectating: " : "BG Luck: ";
			_actualValue.Text = actualDamageTaken == 0
				? "DT: No damage taken!"
				: "DT: " + FormatDamageTaken(actualDamageTaken);
			_expectedValue.Text = "Exp. DT: " + FormatDamageTaken(expectedDamageTaken);
			_damageLine.Visibility = Visibility.Visible;
			SetHealthLine(actualHealth, expectedHealth);
			_healthLine.Visibility = Visibility.Visible;
			_turnCount.Text = "Turns: " + turnCount;
			_turnSummary.Visibility = Visibility.Visible;
			_expandTurnsButton.Visibility = turnSummaries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			_turnDetails.Children.Clear();
			foreach(var turnSummary in turnSummaries)
				_turnDetails.Children.Add(new TextBlock
				{
					Text = turnSummary,
					Foreground = _neutral,
					FontSize = 12
				});
			_turnDetails.Visibility = Visibility.Collapsed;
			SetTurnsExpanded(false);
			_score.Text = isSpectator
				? "Spectator game - not ranked"
				: ratio.HasValue
				? "Luck score: " + ratio.Value.ToString("0.00")
				: actualDamageTaken == 0 && expectedDamageTaken == 0
					? "Luck score: No damage taken!"
					: "Luck score: N/A";
			_score.ToolTip = BuildScoreTooltip(
				actualDamageTaken, scoredActualDamageTaken, expectedDamageTaken, scoredCombats, ratio, expectedHealth);
			_score.Cursor = Cursors.Help;
			_score.Visibility = Visibility.Visible;
			_lobbyMessage.Visibility = Visibility.Collapsed;
			_score.Foreground = GetScoreColor(ratio);
			SetLiveScoreLabel(ratio, false);
			_liveScore.ToolTip = BuildLiveScoreTooltip(scoredActualDamageTaken, expectedDamageTaken, ratio);
			_liveScore.Foreground = GetScoreColor(ratio);
			_actualValue.Foreground = GetScoreColor(ratio);
			ShowIfNotDismissed();
		}

		public void ShowLobby()
		{
			SetTurnsExpanded(false);
			_status.Text = "BG Luck";
			_damageLine.Visibility = Visibility.Collapsed;
			_healthLine.Visibility = Visibility.Collapsed;
			_score.Visibility = Visibility.Collapsed;
			_turnSummary.Visibility = Visibility.Collapsed;
			_turnDetails.Visibility = Visibility.Collapsed;
			_lobbyMessage.Visibility = Visibility.Visible;
			ShowIfNotDismissed();
		}

		private StackPanel CreateDamageLine()
		{
			var line = new StackPanel { Orientation = Orientation.Horizontal };
			line.Children.Add(_actualValue);
			line.Children.Add(new TextBlock { Text = "  |  ", Foreground = Brushes.White, FontSize = 13 });
			line.Children.Add(_expectedValue);
			return line;
		}

		private static ToolTip BuildScoreTooltip(
			int actualDamageTaken,
			int scoredActualDamageTaken,
			double expectedDamageTaken,
			int scoredCombats,
			double? ratio,
			double? expectedHealth)
		{
			var scoreExplanation = ratio.HasValue
				? "Luck score = actual damage taken / expected damage taken.\nBelow 1.00 = luckier than expected; above 1.00 = unluckier; 1.00 = as expected."
				: "Luck score is undefined because expected damage taken is zero.";
			var text = new TextBlock
			{
				Text = "Actual DT (all fights): " + FormatDamageTaken(actualDamageTaken)
					+ "\nActual DT (scored fights): " + FormatDamageTaken(scoredActualDamageTaken)
					+ "\nExpected DT: " + FormatDamageTaken(expectedDamageTaken)
					+ "\nScored fights: " + scoredCombats
					+ "\n" + scoreExplanation
					+ (expectedHealth.HasValue ? "\nExpected health at end: " + expectedHealth.Value.ToString("0.##") : ""),
				Foreground = Brushes.White,
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = 320
			};
			return new ToolTip
			{
				Content = text,
				Background = new SolidColorBrush(Color.FromRgb(32, 36, 44)),
				Foreground = Brushes.White,
				Padding = new Thickness(8)
			};
		}

		private static ToolTip BuildLiveScoreTooltip(int actualDamageTaken, double expectedDamageTaken, double? ratio)
		{
			var scoreExplanation = ratio.HasValue
				? "Luck score = actual damage taken / expected damage taken.\nBelow 1.00 = luckier; above 1.00 = unluckier; 1.00 = as expected."
				: "Luck score is unavailable until expected damage is greater than zero.";
			var text = new TextBlock
			{
				Text = "Preliminary value so far this game."
					+ "\nDamage Taken: " + FormatDamageTaken(actualDamageTaken)
					+ "\nExpected Damage Taken: " + FormatDamageTaken(expectedDamageTaken)
					+ "\n" + scoreExplanation,
				Foreground = Brushes.White,
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = 300
			};
			return new ToolTip
			{
				Content = text,
				Background = new SolidColorBrush(Color.FromRgb(32, 36, 44)),
				Foreground = Brushes.White,
				Padding = new Thickness(8)
			};
		}

		private static string FormatDamageTaken(int damage) =>
			damage == 0 ? "0" : (-(long)damage).ToString();

		private static string FormatDamageTaken(double damage) =>
			damage == 0 ? "0" : (-damage).ToString("0.##");

		private static Run CreateHeart() => new Run("\u2665")
		{
			FontFamily = new FontFamily("Segoe UI Symbol"),
			FontSize = 11,
			Foreground = new SolidColorBrush(Color.FromRgb(235, 45, 55))
		};

		private static Run CreateSkull() => new Run("\U0001F480")
		{
			FontFamily = new FontFamily("Segoe UI Emoji"),
			FontSize = 11,
			Foreground = new SolidColorBrush(Color.FromRgb(235, 45, 55))
		};

		private void SetHealthLine(int? actualHealth, double? expectedHealth)
		{
			_healthLine.Inlines.Clear();
			_healthLine.Inlines.Add(new Run("Health: " + (actualHealth?.ToString() ?? "N/A") + " "));
			_healthLine.Inlines.Add(CreateHealthIcon(actualHealth));
			_healthLine.Inlines.Add(new Run("  |  Exp. Health: " + (expectedHealth?.ToString("0.##") ?? "N/A") + " "));
			_healthLine.Inlines.Add(CreateHealthIcon(expectedHealth));
		}

		private static Run CreateHealthIcon(double? health) =>
			health.HasValue && health.Value <= 0 ? CreateSkull() : CreateHeart();

		private void SetTurnsExpanded(bool expanded)
		{
			_turnsExpanded = expanded;
			_turnDetails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_expandTurnsButton.Content = expanded ? "\u25B4" : "\u25BE";
			_expandTurnsButton.ToolTip = expanded ? "Hide turn-by-turn damage" : "Show turn-by-turn damage";
			Height = expanded
				? Math.Min(440, 130 + _turnDetails.Children.Count * 16)
				: 100;
			if(Parent is Canvas canvas)
			{
				var top = Canvas.GetTop(this);
				if(double.IsNaN(top))
					top = 0;
				Canvas.SetTop(this, Math.Max(0, Math.Min(Math.Max(0, canvas.ActualHeight - Height), top)));
			}
		}

		private void SetLiveScoreLabel(double? ratio, bool preliminary)
		{
			_liveScore.Inlines.Clear();
			_liveScore.Inlines.Add(new Run(ratio.HasValue ? ratio.Value.ToString("0.00") : "N/A"));
			if(preliminary)
				_liveScore.Inlines.Add(new Run("*"));
		}

		private Brush GetScoreColor(double? ratio)
		{
			if(!ratio.HasValue)
				return _neutral;
			if(ratio.Value < 0.5)
				return _strongGreen;
			if(ratio.Value < 0.75)
				return _mediumGreen;
			if(ratio.Value < 1)
				return _lightGreen;
			if(ratio.Value == 1)
				return _gold;
			if(ratio.Value <= 1.5)
				return _lightRed;
			if(ratio.Value <= 2)
				return _mediumRed;
			return _strongRed;
		}

		public void HideOverlay()
		{
			Visibility = Visibility.Collapsed;
		}

		private void ShowIfNotDismissed()
		{
			if(!_dismissed)
				Visibility = Visibility.Visible;
		}

		private void Dismiss()
		{
			_dismissed = true;
			HideOverlay();
		}
	}
}
