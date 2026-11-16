using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BgLuck
{
	internal sealed class LuckOverlayControl : UserControl
	{
		private readonly TextBlock _status;
		private readonly TextBlock _actualValue;
		private readonly TextBlock _expectedValue;
		private readonly TextBlock _liveScore;
		private readonly TextBlock _compactExpectedHealth;
		private readonly TextBlock _oddPlayerStatus;
		private readonly TextBlock _ghostPriorityStatus;
		private readonly TextBlock _score;
		private readonly TextBlock _worstCombatLine;
		private readonly TextBlock _bestCombatLine;
		private readonly TextBlock _healthLine;
		private readonly TextBlock _healthPercentileLine;
		private readonly Button _turnSummary;
		private string _turnSummaryLabel;
		private readonly Button _openRecapButton;
		private readonly Button _boardStatsToggle;
		private readonly StackPanel _boardStatsPanel;
		private readonly StackPanel _turnDetails;
		private readonly StackPanel _detailsPanel;
		private readonly StackPanel _reviewContent;
		private readonly ScrollViewer _reviewPanel;
		private TextBlock _finalCompositionLine;
		private string _finalComposition;
		private readonly TextBlock _lobbyMessage;
		private readonly TextBlock _averageOpponentMmrLine;
		private readonly StackPanel _header;
		private readonly StackPanel _content;
		private readonly StackPanel _damageLine;
		private readonly Thumb _resizeGrip;
		private readonly Button _closeButton;
		private readonly DispatcherTimer _detailsCloseTimer;
		private readonly Image _icon;
		private readonly Brush _gold = new SolidColorBrush(Color.FromRgb(255, 204, 64));
		private readonly Brush _labelBlue = new SolidColorBrush(Color.FromRgb(120, 190, 210));
		private readonly Brush _dividerBrush = new SolidColorBrush(Color.FromArgb(130, 120, 190, 210));
		private readonly Brush _opponentWinRing = new SolidColorBrush(Color.FromRgb(120, 170, 130));
		private readonly Brush _opponentTieRing = new SolidColorBrush(Color.FromRgb(190, 160, 90));
		private readonly Brush _opponentLossRing = new SolidColorBrush(Color.FromRgb(190, 100, 105));
		private readonly Brush _green = new SolidColorBrush(Color.FromRgb(104, 210, 132));
		private readonly Brush _red = new SolidColorBrush(Color.FromRgb(242, 105, 105));
		private readonly Brush _neutral = new SolidColorBrush(Color.FromRgb(225, 225, 225));
		private readonly Brush _lightGreen = new SolidColorBrush(Color.FromRgb(169, 224, 152));
		private readonly Brush _mediumGreen = new SolidColorBrush(Color.FromRgb(115, 210, 113));
		private readonly Brush _strongGreen = new SolidColorBrush(Color.FromRgb(43, 175, 91));
		private readonly Brush _lightRed = new SolidColorBrush(Color.FromRgb(242, 156, 156));
		private readonly Brush _mediumRed = new SolidColorBrush(Color.FromRgb(235, 105, 105));
		private readonly Brush _strongRed = new SolidColorBrush(Color.FromRgb(190, 45, 55));
		private readonly Brush _turnGreen = new SolidColorBrush(Color.FromRgb(145, 190, 135));
		private bool _dismissed;
		private bool _dragging;
		private bool _turnsExpanded;
		private bool _boardStatsExpanded;
		private bool _detailsExpanded;
		private bool _liveMode;
		private bool _finalSummaryMode;
		private bool _detailsExpandedUpward;
		private bool _suppressHoverUntilLeave;
		private Point _dragStart;
		private double _startLeft;
		private double _startTop;
		private double _userWidth = 350;
		private double? _userLiveWidth;
		private double _userHeight = 155;

		public LuckOverlayControl()
		{
			Width = 190;
			Height = 70;
			HorizontalAlignment = HorizontalAlignment.Left;
			VerticalAlignment = VerticalAlignment.Top;

			_status = new TextBlock
			{
				Text = "BG Luck",
				Foreground = Brushes.White,
				FontSize = 16,
				FontWeight = FontWeights.SemiBold,
				VerticalAlignment = VerticalAlignment.Center
			};
			_liveScore = new TextBlock
			{
				Foreground = _neutral,
				FontSize = 16,
				VerticalAlignment = VerticalAlignment.Center,
				Cursor = Cursors.Help,
				Margin = new Thickness(5, 0, 0, 0)
			};
			_compactExpectedHealth = new TextBlock
			{
				Foreground = _neutral,
				FontSize = 12,
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = "Expected health; the value in parentheses is expected health minus current health."
			};
			_oddPlayerStatus = new TextBlock
			{
				Foreground = _labelBlue,
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center,
				ToolTip = "GameTag.BACON_ODD_PLAYER_OUT is set to 1 for this player.",
				Visibility = Visibility.Collapsed
			};
			_ghostPriorityStatus = new TextBlock
			{
				FontSize = 12,
				VerticalAlignment = VerticalAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = 320,
				ToolTip = "Hero associated with Battlegrounds player ID 1."
			};
			_actualValue = new TextBlock { Foreground = _neutral, FontSize = 12, ToolTip = "Damage taken" };
			_expectedValue = new TextBlock { Foreground = _neutral, FontSize = 12, ToolTip = "Expected damage taken" };
			_score = new TextBlock { Foreground = _neutral, FontSize = 13, Visibility = Visibility.Collapsed };
			_worstCombatLine = new TextBlock { Foreground = _neutral, FontSize = 12, Visibility = Visibility.Collapsed };
			_bestCombatLine = new TextBlock { Foreground = _neutral, FontSize = 12, Visibility = Visibility.Collapsed };
			_healthLine = new TextBlock { Foreground = _neutral, FontSize = 12, Visibility = Visibility.Collapsed };
			_healthPercentileLine = new TextBlock { Foreground = _neutral, FontSize = 12, Visibility = Visibility.Collapsed };
			_turnSummary = new Button
			{
				Foreground = _labelBlue,
				Background = Brushes.Transparent,
				BorderBrush = _dividerBrush,
				BorderThickness = new Thickness(0, 1, 0, 1),
				Padding = new Thickness(0, 3, 0, 3),
				FontSize = 12,
				FontWeight = FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(0, 3, 0, 3),
				Focusable = false,
				Cursor = Cursors.Hand,
				ToolTip = "Show turn-by-turn damage",
				Visibility = Visibility.Collapsed
			};
			_turnDetails = new StackPanel { Visibility = Visibility.Collapsed };
			_boardStatsPanel = new StackPanel { Visibility = Visibility.Collapsed };
			_boardStatsToggle = new Button
			{
				Content = "Stats by turns  \u25B6",
				Foreground = _labelBlue,
				Background = Brushes.Transparent,
				BorderBrush = _dividerBrush,
				BorderThickness = new Thickness(0, 1, 0, 1),
				Padding = new Thickness(0, 3, 0, 3),
				FontSize = 12,
				FontWeight = FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(0, 3, 0, 3),
				Focusable = false,
				Cursor = Cursors.Hand
			};
			_detailsPanel = new StackPanel { Visibility = Visibility.Collapsed };
			_reviewContent = new StackPanel();
			_reviewPanel = new ScrollViewer
			{
				Content = _reviewContent,
				MaxHeight = 230,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Visibility = Visibility.Collapsed
			};
			_lobbyMessage = new TextBlock
			{
				Text = "Plugin Ready - Waiting for game to start...",
				Foreground = _neutral,
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center,
				Visibility = Visibility.Collapsed
			};
			_averageOpponentMmrLine = new TextBlock
			{
				Foreground = _neutral,
				FontSize = 12,
				ToolTip = "Estimated average opponent MMR based on your rating, rating change, and final placement."
			};
			_content = new StackPanel
			{
				Margin = new Thickness(10, 4, 10, 4),
				VerticalAlignment = VerticalAlignment.Center
			};
			_openRecapButton = new Button
			{
				Content = "Open Recap",
				Height = 22,
				MinWidth = 92,
				Padding = new Thickness(8, 0, 8, 0),
				Foreground = _neutral,
				Background = new SolidColorBrush(Color.FromArgb(70, 190, 200, 215)),
				BorderBrush = new SolidColorBrush(Color.FromArgb(140, 190, 200, 215)),
				BorderThickness = new Thickness(1),
				FontSize = 11,
				Focusable = false,
				Cursor = Cursors.Hand,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 1, 0, 1),
				Visibility = Visibility.Collapsed
			};
			_icon = new Image
			{
				Source = new System.Windows.Media.Imaging.BitmapImage(
					new Uri("pack://application:,,,/BgLuck;component/Assets/OFC.png")),
				Width = 32,
				Height = 32,
				Stretch = Stretch.Uniform,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0),
				ToolTip = "BG Luck"
			};
			var luckLine = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Center
			};
			luckLine.Children.Add(_status);
			luckLine.Children.Add(_liveScore);
			var compactMetrics = new StackPanel
			{
				Orientation = Orientation.Vertical,
				VerticalAlignment = VerticalAlignment.Center
			};
			compactMetrics.Children.Add(luckLine);
			compactMetrics.Children.Add(_compactExpectedHealth);
			_header = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Height = 36,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center
			};
			_header.Children.Add(_icon);
			_header.Children.Add(compactMetrics);
			_content.Children.Add(_header);
			_content.Children.Add(_openRecapButton);
			_detailsPanel.Children.Add(new Border
			{
				Height = 1,
				Margin = new Thickness(0, 5, 0, 4),
				Background = new SolidColorBrush(Color.FromArgb(95, 190, 200, 215))
			});
			_detailsPanel.Children.Add(_healthLine);
			_detailsPanel.Children.Add(_healthPercentileLine);
			_detailsPanel.Children.Add(_oddPlayerStatus);
			_detailsPanel.Children.Add(_ghostPriorityStatus);
			_damageLine = CreateDamageLine();
			_detailsPanel.Children.Add(_damageLine);
			_detailsPanel.Children.Add(_score);
			_detailsPanel.Children.Add(_worstCombatLine);
			_detailsPanel.Children.Add(_bestCombatLine);
			_detailsPanel.Children.Add(_reviewPanel);
			_content.Children.Add(_detailsPanel);
			_content.Children.Add(_lobbyMessage);

			_closeButton = new Button
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
				Focusable = false,
				Visibility = Visibility.Collapsed
			};
			_closeButton.Click += (sender, args) => Dismiss();
			_openRecapButton.Click += (sender, args) => SetDetailsExpanded(!_detailsExpanded);
			_turnSummary.Click += (sender, args) => SetTurnsExpanded(!_turnsExpanded);
			_boardStatsToggle.Click += (sender, args) => SetBoardStatsExpanded(!_boardStatsExpanded);
			_detailsCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
			_detailsCloseTimer.Tick += (sender, args) =>
			{
				_detailsCloseTimer.Stop();
				if(!IsMouseOver)
					SetDetailsExpanded(false);
			};
			MouseEnter += (sender, args) =>
			{
				_detailsCloseTimer.Stop();
				if(_liveMode && !_dragging && !_suppressHoverUntilLeave)
					SetDetailsExpanded(true);
			};
			MouseLeave += (sender, args) =>
			{
				if(_dragging)
					return;
				if(_suppressHoverUntilLeave)
				{
					_suppressHoverUntilLeave = false;
					return;
				}
				if(_liveMode)
				{
					_detailsCloseTimer.Stop();
					_detailsCloseTimer.Start();
				}
			};
			var layout = new Grid();
			layout.Children.Add(_content);
			layout.Children.Add(_closeButton);
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
				ToolTip = "Drag to resize",
				Visibility = Visibility.Collapsed
			};
			_resizeGrip.DragDelta += (sender, args) =>
			{
				Width = Math.Max(190, Width + args.HorizontalChange);
				Height = Math.Max(44, Height + args.VerticalChange);
				if(_detailsExpanded)
				{
					if(_finalSummaryMode)
						_userWidth = Width;
					else
						_userLiveWidth = Width;
					_userHeight = Height;
					UpdateReviewPanelMaxHeight();
				}
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

			_header.Cursor = Cursors.SizeAll;
			_header.MouseLeftButtonDown += (sender, args) =>
			{
				if(!(Parent is Canvas canvas))
					return;
				_header.CaptureMouse();
				_dragging = true;
				_suppressHoverUntilLeave = true;
				_detailsCloseTimer.Stop();
				if(_detailsExpanded)
					SetDetailsExpanded(false);
				_dragStart = args.GetPosition(canvas);
				_startLeft = Canvas.GetLeft(this);
				_startTop = Canvas.GetTop(this);
				args.Handled = true;
			};
			_header.MouseMove += (sender, args) =>
			{
				if(!_dragging || !(Parent is Canvas canvas))
					return;
				var current = args.GetPosition(canvas);
				var maxLeft = Math.Max(0, canvas.ActualWidth - ActualWidth);
				var maxTop = Math.Max(0, canvas.ActualHeight - ActualHeight);
				Canvas.SetLeft(this, Math.Max(0, Math.Min(maxLeft, _startLeft + current.X - _dragStart.X)));
				Canvas.SetTop(this, Math.Max(0, Math.Min(maxTop, _startTop + current.Y - _dragStart.Y)));
			};
			_header.MouseLeftButtonUp += (sender, args) =>
			{
				_dragging = false;
				_header.ReleaseMouseCapture();
				if(!IsMouseOver)
					_suppressHoverUntilLeave = false;
			};
		}

		public void ShowLive(
			int actualDamageTaken,
			double expectedDamageTaken,
			int scoredActualDamageTaken,
			double? ratio,
			int? currentHealth,
			double? expectedHealth,
			double? healthPercentile,
			bool isSpectator,
			int? oddPlayerId,
			string ghostPriorityHeroName)
		{
			_liveMode = true;
			_finalSummaryMode = false;
			SetContentVerticalAlignment(true);
			_openRecapButton.Visibility = Visibility.Collapsed;
			_status.Text = "BG Luck";
			SetLiveScoreLabel(ratio);
			_liveScore.ToolTip = BuildLiveScoreTooltip(
				actualDamageTaken, scoredActualDamageTaken, expectedDamageTaken,
				ratio, currentHealth, expectedHealth, healthPercentile);
			_liveScore.Foreground = GetScoreColor(ratio);
			SetCompactExpectedHealth(currentHealth, expectedHealth, ratio);
			_oddPlayerStatus.Text = oddPlayerId.HasValue
				? "Odd player: #" + oddPlayerId.Value
				: "";
			_oddPlayerStatus.Visibility = oddPlayerId.HasValue ? Visibility.Visible : Visibility.Collapsed;
			_ghostPriorityStatus.Inlines.Clear();
			_ghostPriorityStatus.Inlines.Add(new Run("Ghost Prio: ") { Foreground = _labelBlue });
			_ghostPriorityStatus.Inlines.Add(new Run(
				string.IsNullOrEmpty(ghostPriorityHeroName) ? "N/A" : ghostPriorityHeroName)
			{
				Foreground = _gold
			});
			_ghostPriorityStatus.Visibility = Visibility.Visible;
			SetDamageValue(
				_actualValue,
				"DT: ",
				FormatDamageTaken(actualDamageTaken),
				GetDamageComparisonColor(actualDamageTaken, expectedDamageTaken));
			SetDamageValue(_expectedValue, "Exp. DT: ", FormatDamageTaken(expectedDamageTaken), _gold);
			_damageLine.Visibility = Visibility.Visible;
			SetHealthLine(currentHealth, expectedHealth ?? (double?)currentHealth);
			_healthLine.Visibility = Visibility.Visible;
			SetHealthPercentileLine(healthPercentile);
			_healthPercentileLine.Visibility = healthPercentile.HasValue
				? Visibility.Visible
				: Visibility.Collapsed;
			_lobbyMessage.Visibility = Visibility.Collapsed;
			_score.Visibility = Visibility.Collapsed;
			_worstCombatLine.Visibility = Visibility.Collapsed;
			_bestCombatLine.Visibility = Visibility.Collapsed;
			_turnSummary.Visibility = Visibility.Collapsed;
			_turnDetails.Visibility = Visibility.Collapsed;
			_reviewPanel.Visibility = Visibility.Collapsed;
			if(IsMouseOver && !_dragging && !_suppressHoverUntilLeave)
			{
				_detailsCloseTimer.Stop();
				SetDetailsExpanded(true);
			}
			else if(!_detailsExpanded)
			{
				Width = 190;
				Height = GetCompactHeight();
				KeepWithinCanvas();
			}
			ShowIfNotDismissed();
		}

		public void ResetForNewGame()
		{
			_dismissed = false;
			_liveMode = false;
			_finalSummaryMode = false;
			_suppressHoverUntilLeave = false;
			SetContentVerticalAlignment(false);
			_oddPlayerStatus.Visibility = Visibility.Collapsed;
			_ghostPriorityStatus.Visibility = Visibility.Collapsed;
			_header.Height = 36;
			_detailsCloseTimer.Stop();
			SetTurnsExpanded(false);
			SetDetailsExpanded(false);
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
			double? healthPercentile,
			IList<Tuple<int, int, double?, string>> turnSummaries,
			IList<TierChartSeries> tierSeries,
			IList<BoardStatsTurn> boardStats,
			string finalComposition,
			double? finalPlacement,
			double? averageOpponentMmr,
			bool averageOpponentMmrPending,
			Tuple<int, string, double> worstCombat,
			Tuple<int, string, double> bestCombat,
			bool isSpectator)
		{
			_liveMode = false;
			_finalSummaryMode = true;
			SetContentVerticalAlignment(true);
			_detailsCloseTimer.Stop();
			SetTurnsExpanded(false);
			_status.Text = "BG Luck";
			SetLiveScoreLabel(ratio);
			_liveScore.Foreground = GetScoreColor(ratio);
			SetCompactExpectedHealth(
				actualHealth.HasValue ? (int?)Math.Max(0, actualHealth.Value) : null,
				expectedHealth,
				ratio);
			SetDamageValue(
				_actualValue,
				"DT: ",
				actualDamageTaken == 0 ? "No damage taken!" : FormatDamageTaken(actualDamageTaken),
				actualDamageTaken == 0 ? _neutral : GetDamageComparisonColor(actualDamageTaken, expectedDamageTaken));
			SetDamageValue(_expectedValue, "Exp. DT: ", FormatDamageTaken(expectedDamageTaken), _gold);
			_damageLine.Visibility = Visibility.Visible;
			SetHealthLine(actualHealth, expectedHealth);
			_healthLine.Visibility = Visibility.Visible;
			SetHealthPercentileLine(healthPercentile);
			_healthPercentileLine.Visibility = healthPercentile.HasValue
				? Visibility.Visible
				: Visibility.Collapsed;
			_oddPlayerStatus.Visibility = Visibility.Collapsed;
			_ghostPriorityStatus.Visibility = Visibility.Collapsed;
			_header.Height = 36;
			_turnSummaryLabel = "Turns: " + turnCount + "  Recap";
			_turnSummary.Visibility = turnSummaries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
			_turnDetails.Children.Clear();
			foreach(var turn in turnSummaries)
				_turnDetails.Children.Add(CreateTurnSummary(turn.Item1, turn.Item2, turn.Item3, turn.Item4));
			_turnDetails.Visibility = Visibility.Collapsed;
			SetTurnsExpanded(false);
			BuildReview(
				tierSeries, boardStats, finalComposition, finalPlacement,
				averageOpponentMmr, averageOpponentMmrPending);
			_score.Text = isSpectator
				? "Spectator game - not ranked"
				: ratio.HasValue
				? "Damage taken ratio: " + ratio.Value.ToString("0.00")
				: actualDamageTaken == 0 && expectedDamageTaken == 0
					? "Damage taken ratio: N/A"
					: "Damage taken ratio: N/A";
			SetCombatOutcomeLine(_worstCombatLine, "Worst combat", worstCombat);
			SetCombatOutcomeLine(_bestCombatLine, "Best combat", bestCombat);
			_worstCombatLine.Visibility = Visibility.Visible;
			_bestCombatLine.Visibility = Visibility.Visible;
			_score.ToolTip = BuildScoreTooltip(
				actualDamageTaken, scoredActualDamageTaken, expectedDamageTaken, scoredCombats,
				ratio, expectedHealth, actualHealth, healthPercentile);
			_score.Cursor = Cursors.Help;
			_score.Visibility = Visibility.Visible;
			_lobbyMessage.Visibility = Visibility.Collapsed;
			_score.Foreground = GetScoreColor(ratio);
			_liveScore.ToolTip = BuildLiveScoreTooltip(
				actualDamageTaken, scoredActualDamageTaken, expectedDamageTaken,
				ratio, actualHealth, expectedHealth, healthPercentile);
			SetDetailsExpanded(false);
			ShowIfNotDismissed();
		}

		public void ShowLobby()
		{
			_liveMode = false;
			_finalSummaryMode = false;
			_detailsCloseTimer.Stop();
			SetTurnsExpanded(false);
			SetDetailsExpanded(false);
			_status.Text = "BG Luck";
			_compactExpectedHealth.Visibility = Visibility.Collapsed;
			_oddPlayerStatus.Visibility = Visibility.Collapsed;
			_ghostPriorityStatus.Visibility = Visibility.Collapsed;
			_header.Height = 36;
			_damageLine.Visibility = Visibility.Collapsed;
			_healthLine.Visibility = Visibility.Collapsed;
			_healthPercentileLine.Visibility = Visibility.Collapsed;
			_score.Visibility = Visibility.Collapsed;
			_turnSummary.Visibility = Visibility.Collapsed;
			_turnDetails.Visibility = Visibility.Collapsed;
			_reviewPanel.Visibility = Visibility.Collapsed;
			_lobbyMessage.Visibility = Visibility.Visible;
			_openRecapButton.Visibility = Visibility.Collapsed;
			Width = 190;
			Height = 70;
			KeepWithinCanvas();
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

		private TextBlock CreateTurnSummary(int turn, int actualDamage, double? expectedDamage, string result)
		{
			var actualColor = GetTurnDamageColor(actualDamage, expectedDamage);
			var summary = new TextBlock { FontSize = 12 };
			summary.Inlines.Add(new Run("T" + turn + ": ") { Foreground = _neutral });
			summary.Inlines.Add(new Run(FormatDamageTaken(actualDamage)) { Foreground = actualColor });
			summary.Inlines.Add(new Run(" (exp ") { Foreground = _neutral });
			summary.Inlines.Add(new Run(expectedDamage.HasValue
				? FormatDamageTaken(expectedDamage.Value)
				: "N/A") { Foreground = expectedDamage.HasValue ? _gold : _neutral });
			summary.Inlines.Add(new Run(")") { Foreground = _neutral });
			var resultLabel = result == "Win" ? "W" : result == "Tie" ? "T" : result == "Loss" ? "L" : null;
			if(resultLabel != null)
				summary.Inlines.Add(new Run(" " + resultLabel) { Foreground = GetTurnResultColor(resultLabel) });
			return summary;
		}

		private Brush GetTurnDamageColor(int actualDamage, double? expectedDamage)
		{
			if(!expectedDamage.HasValue)
				return _neutral;
			if(expectedDamage.Value == 0)
				return actualDamage == 0 ? _gold : _strongRed;
			var ratio = actualDamage / expectedDamage.Value;
			return ratio < 1 ? _turnGreen : GetScoreColor(ratio);
		}

		private Brush GetTurnResultColor(string result) =>
			result == "W" ? _turnGreen : result == "T" ? _gold : _red;

		private Brush GetOpponentResultRingColor(string result) =>
			result == "Win" ? _opponentWinRing
			: result == "Tie" ? _opponentTieRing
			: result == "Loss" ? _opponentLossRing
			: Brushes.White;

		private void SetDamageValue(TextBlock textBlock, string label, string value, Brush valueColor)
		{
			textBlock.Inlines.Clear();
			textBlock.Inlines.Add(new Run(label) { Foreground = _neutral });
			textBlock.Inlines.Add(new Run(value) { Foreground = valueColor });
		}

		private void SetCombatOutcomeLine(
			TextBlock line,
			string label,
			Tuple<int, string, double> combat)
		{
			line.Inlines.Clear();
			line.Inlines.Add(new Run(label + ": ") { Foreground = _labelBlue });
			if(combat == null)
			{
				line.Inlines.Add(new Run("N/A") { Foreground = _neutral });
				return;
			}

			line.Inlines.Add(new Run("Turn " + combat.Item1 + " ") { Foreground = _neutral });
			var result = combat.Item2 == "Win" ? "W" : "L";
			line.Inlines.Add(new Run(combat.Item2 + " ") { Foreground = GetTurnResultColor(result) });
			line.Inlines.Add(new Run((combat.Item3 * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%")
			{
				Foreground = _neutral
			});
		}

		private static ToolTip BuildScoreTooltip(
			int actualDamageTaken,
			int scoredActualDamageTaken,
			double expectedDamageTaken,
			int scoredCombats,
			double? ratio,
			double? expectedHealth,
			int? actualHealth,
			double? healthPercentile)
		{
			var ratioExplanation = ratio.HasValue
				? "Damage taken ratio = (scored actual damage + 1) / (expected damage + 1), calculated once from match totals. Below 1.00 means less damage than expected."
				: "Damage taken ratio is unavailable.";
			var text = new TextBlock
			{
				Text = "Actual DT (all fights): " + FormatDamageTaken(actualDamageTaken)
					+ "\nActual DT (scored fights): " + FormatDamageTaken(scoredActualDamageTaken)
					+ "\nExpected DT: " + FormatDamageTaken(expectedDamageTaken)
					+ "\nScored fights: " + scoredCombats
					+ "\n" + ratioExplanation
					+ "\nActual health: " + (actualHealth?.ToString() ?? "N/A")
					+ (expectedHealth.HasValue ? "\nExpected HP: " + expectedHealth.Value.ToString("0.##") : "\nExpected health: N/A")
					+ (healthPercentile.HasValue ? "\n" + ExplainHealthPercentile(healthPercentile) : ""),
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

		private static ToolTip BuildLiveScoreTooltip(
			int actualDamageTaken,
			int scoredActualDamageTaken,
			double expectedDamageTaken,
			double? ratio,
			int? actualHealth,
			double? expectedHealth,
			double? healthPercentile)
		{
			var ratioText = ratio.HasValue
				? "\nDamage taken ratio ((scored actual + 1) / (expected + 1)): " + ratio.Value.ToString("0.00")
				: "";
			var text = new TextBlock
			{
				Text = "Live estimate for this game."
					+ "\nActual DT (all fights): " + FormatDamageTaken(actualDamageTaken)
					+ "\nActual DT (scored fights): " + FormatDamageTaken(scoredActualDamageTaken)
					+ "\nExpected DT (scored fights): " + FormatDamageTaken(expectedDamageTaken)
					+ ratioText
					+ "\nCurrent health: " + (actualHealth?.ToString() ?? "N/A")
					+ "\nExpected health: " + (expectedHealth?.ToString("0.##") ?? "N/A")
					+ (healthPercentile.HasValue ? "\n" + ExplainHealthPercentile(healthPercentile) : ""),
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

		private static string ExplainHealthPercentile(double? percentile) =>
			percentile.HasValue
				? "Health percentile: " + percentile.Value.ToString("0") + "%."
					+ "\nApproximation combines fight distributions rather than replaying alternate game paths."
					+ "\nA 15th percentile means less health than about 85% of modeled outcomes."
				: "";

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
			_healthLine.Inlines.Add(new Run("Expected health: ")
			{
				Foreground = _labelBlue,
				FontWeight = FontWeights.SemiBold
			});
			_healthLine.Inlines.Add(new Run(expectedHealth?.ToString("0.##") ?? "N/A")
			{
				Foreground = _gold,
				FontWeight = FontWeights.SemiBold
			});
			_healthLine.Inlines.Add(new Run(" "));
			_healthLine.Inlines.Add(CreateHealthIcon(expectedHealth));
			_healthLine.Inlines.Add(new Run("  |  Current: " + (actualHealth?.ToString() ?? "N/A") + " ")
			{
				FontSize = 12,
				Foreground = _neutral
			});
			_healthLine.Inlines.Add(CreateHealthIcon(actualHealth));
		}

		private void SetCompactExpectedHealth(int? currentHealth, double? expectedHealth, double? ratio)
		{
			_compactExpectedHealth.Inlines.Clear();
			_compactExpectedHealth.Visibility = Visibility.Visible;
			_compactExpectedHealth.Inlines.Add(new Run("Exp. HP: ") { Foreground = _neutral });
			if(!expectedHealth.HasValue)
			{
				_compactExpectedHealth.Inlines.Add(new Run("N/A") { Foreground = _neutral });
				return;
			}

			_compactExpectedHealth.Inlines.Add(new Run(expectedHealth.Value.ToString("0.0"))
			{
				Foreground = _gold,
				FontWeight = FontWeights.SemiBold
			});
			_compactExpectedHealth.Inlines.Add(new Run(" "));
			_compactExpectedHealth.Inlines.Add(CreateHealthIcon(expectedHealth));
			if(currentHealth.HasValue)
			{
				var difference = expectedHealth.Value - currentHealth.Value;
				_compactExpectedHealth.Inlines.Add(new Run(" (" + difference.ToString("+0;-0;0") + ")")
				{
					Foreground = GetHealthDeltaColor(difference, ratio)
				});
			}
		}

		private void SetHealthPercentileLine(double? percentile)
		{
			_healthPercentileLine.Inlines.Clear();
			if(!percentile.HasValue)
			{
				_healthPercentileLine.ToolTip = null;
				return;
			}
			_healthPercentileLine.Inlines.Add(new Run("Health percentile: ") { Foreground = _neutral });
			_healthPercentileLine.Inlines.Add(new Run(percentile.Value.ToString("0") + "%")
			{
				Foreground = GetPercentileColor(percentile),
				FontWeight = FontWeights.SemiBold
			});
			_healthPercentileLine.ToolTip = ExplainHealthPercentile(percentile);
		}

		private Brush GetHealthDeltaColor(double difference, double? ratio)
		{
			if(ratio.HasValue)
				return GetScoreColor(ratio);
			if(difference < -0.5)
				return _mediumGreen;
			if(difference > 0.5)
				return _mediumRed;
			return _gold;
		}

		private void BuildReview(
			IList<TierChartSeries> tierSeries,
			IList<BoardStatsTurn> boardStats,
			string finalComposition,
			double? finalPlacement,
			double? averageOpponentMmr,
			bool averageOpponentMmrPending)
		{
			_reviewContent.Children.Clear();
			_reviewContent.Children.Add(CreateDivider());
			_reviewContent.Children.Add(CreateSectionHeader("Final comp"));
			_finalComposition = finalComposition;
			_finalCompositionLine = new TextBlock
			{
				Foreground = Brushes.White,
				FontSize = 14,
				FontWeight = FontWeights.SemiBold
			};
			UpdateFinalPlacement(finalPlacement);
			_reviewContent.Children.Add(_finalCompositionLine);
			_reviewContent.Children.Add(_averageOpponentMmrLine);
			UpdateAverageOpponentMmr(averageOpponentMmr, averageOpponentMmrPending);
			_reviewContent.Children.Add(CreateDivider());
			_reviewContent.Children.Add(CreateSectionHeader("Tier progression"));
			if(tierSeries != null && tierSeries.Count > 0)
			{
				_reviewContent.Children.Add(CreateTierChart(tierSeries, boardStats));
				var legend = new TextBlock { FontSize = 10, Margin = new Thickness(0, 0, 0, 5) };
				legend.Inlines.Add(new Run("You") { Foreground = _gold });
				legend.Inlines.Add(new Run("  |  Lobby average") { Foreground = new SolidColorBrush(Color.FromRgb(105, 220, 235)) });
				legend.Inlines.Add(new Run("  |  Faced opponent") { Foreground = new SolidColorBrush(Color.FromRgb(240, 150, 170)) });
				_reviewContent.Children.Add(legend);
				var opponentResultLegend = new TextBlock
				{
					FontSize = 10,
					Margin = new Thickness(0, 0, 0, 5)
				};
				opponentResultLegend.Inlines.Add(new Run("Opponent ring: ") { Foreground = _labelBlue });
				opponentResultLegend.Inlines.Add(new Run("Win") { Foreground = _opponentWinRing });
				opponentResultLegend.Inlines.Add(new Run(" / ") { Foreground = _neutral });
				opponentResultLegend.Inlines.Add(new Run("Tie") { Foreground = _opponentTieRing });
				opponentResultLegend.Inlines.Add(new Run(" / ") { Foreground = _neutral });
				opponentResultLegend.Inlines.Add(new Run("Loss") { Foreground = _opponentLossRing });
				if(boardStats != null && boardStats.Any(x => x.OpponentTier.HasValue))
					_reviewContent.Children.Add(opponentResultLegend);
			}
			else
				_reviewContent.Children.Add(new TextBlock
				{
					Text = "Tier history was not available.",
					Foreground = _neutral,
					FontSize = 11
				});

			_boardStatsPanel.Children.Clear();
			if(boardStats != null && boardStats.Count > 0)
			{
				foreach(var turn in boardStats)
					_boardStatsPanel.Children.Add(CreateBoardStatsLine(turn));
			}
			else
				_boardStatsPanel.Children.Add(new TextBlock
				{
					Text = "No recruit-phase snapshots were captured.",
					Foreground = _neutral,
					FontSize = 11
				});
			_reviewContent.Children.Add(_boardStatsToggle);
			_reviewContent.Children.Add(_boardStatsPanel);
			_reviewContent.Children.Add(_turnSummary);
			_reviewContent.Children.Add(_turnDetails);
			SetBoardStatsExpanded(false);

			_reviewContent.Children.Add(CreateSectionHeader("Dark Gifts"));
			_reviewContent.Children.Add(new TextBlock
			{
				Text = "This log has tag observations, but cannot yet confirm which Gift or minion was selected.",
				Foreground = _neutral,
				FontSize = 11,
				TextWrapping = TextWrapping.Wrap,
				MaxWidth = 320
			});
			_reviewPanel.Visibility = Visibility.Visible;
		}

		public void UpdateAverageOpponentMmr(double? averageOpponentMmr, bool pending)
		{
			_averageOpponentMmrLine.Text = averageOpponentMmr.HasValue
				? "Avg opp: " + averageOpponentMmr.Value.ToString("N0", CultureInfo.CurrentCulture)
				: pending ? "Avg opp: Calculating..." : "Avg opp: N/A";
			_averageOpponentMmrLine.Foreground = averageOpponentMmr.HasValue ? _labelBlue : _neutral;
		}

		public void UpdateFinalPlacement(double? finalPlacement)
		{
			if(_finalCompositionLine == null)
				return;

			_finalCompositionLine.Text = (string.IsNullOrEmpty(_finalComposition) ? "Unavailable" : _finalComposition)
				+ (finalPlacement.HasValue ? " (" + FormatPlacement(finalPlacement.Value) + ")" : "");
		}

		private TextBlock CreateSectionHeader(string text) => new TextBlock
		{
			Text = text,
			Foreground = _labelBlue,
			FontSize = 12,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0, 5, 0, 3)
		};

		private Border CreateDivider() => new Border
		{
			Height = 1,
			Background = _dividerBrush,
			Margin = new Thickness(0, 3, 0, 3)
		};

		private TextBlock CreateBoardStatsLine(BoardStatsTurn turn)
		{
			var opponentTier = turn.OpponentTier.HasValue
				? "T" + turn.OpponentTier.Value
				: "T?";
			var line = new TextBlock
			{
				FontSize = 11,
				Margin = new Thickness(2, 1, 0, 1)
			};
			line.Inlines.Add(new Run("T" + turn.Turn + "  ") { Foreground = _neutral });
			line.Inlines.Add(new Run("You T" + turn.PlayerTier + ": "
				+ turn.PlayerAttack + "/" + turn.PlayerHealth) { Foreground = _gold });
			line.Inlines.Add(new Run("  |  Opp. " + opponentTier + ": "
				+ turn.OpponentAttack + "/" + turn.OpponentHealth)
			{
				Foreground = new SolidColorBrush(Color.FromRgb(240, 150, 170))
			});
			return line;
		}

		private static string FormatPlacement(double placement)
		{
			var rounded = Math.Round(placement);
			if(Math.Abs(placement - rounded) >= 0.001)
				return placement.ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")) + "th place";

			var rank = (int)rounded;
			var lastTwoDigits = rank % 100;
			var suffix = lastTwoDigits >= 11 && lastTwoDigits <= 13
				? "th"
				: rank % 10 == 1 ? "st"
				: rank % 10 == 2 ? "nd"
				: rank % 10 == 3 ? "rd"
				: "th";
			return rank + suffix + " place";
		}

		private FrameworkElement CreateTierChart(
			IList<TierChartSeries> tierSeries,
			IList<BoardStatsTurn> boardStats)
		{
			const double chartWidth = 320;
			const double chartHeight = 142;
			const double plotLeft = 28;
			const double plotTop = 8;
			const double plotBottom = 22;
			const double plotRight = 8;
			var plotWidth = chartWidth - plotLeft - plotRight;
			var plotHeight = chartHeight - plotTop - plotBottom;
			var maxTurn = Math.Max(
				1,
				Math.Max(
					tierSeries.SelectMany(x => x.TierUps).Select(x => x.Turn).DefaultIfEmpty(1).Max(),
					boardStats == null ? 1 : boardStats.Select(x => x.Turn).DefaultIfEmpty(1).Max()));
			var maxTier = Math.Max(6, tierSeries.SelectMany(x => x.TierUps).Select(x => x.Tier).DefaultIfEmpty(1).Max());
			var otherPlayers = tierSeries.Where(x => !x.IsLocalPlayer).ToList();
			var chart = new Canvas
			{
				Width = chartWidth,
				Height = chartHeight,
				Background = Brushes.Transparent
			};
			Func<int, double> xForTurn = turn =>
				plotLeft + (Math.Max(1, Math.Min(maxTurn, turn)) - 1) * plotWidth / Math.Max(1, maxTurn - 1);
			Func<double, double> yForTier = tier =>
				plotTop + (maxTier - Math.Max(1, Math.Min(maxTier, tier))) * plotHeight / Math.Max(1, maxTier - 1);

			for(var tier = 1; tier <= maxTier; tier++)
			{
				var y = yForTier(tier);
				chart.Children.Add(new Line
				{
					X1 = plotLeft,
					X2 = chartWidth - plotRight,
					Y1 = y,
					Y2 = y,
					Stroke = new SolidColorBrush(Color.FromArgb(65, 205, 215, 225)),
					StrokeThickness = 1
				});
				var label = new TextBlock
				{
					Text = "T" + tier,
					Width = 22,
					FontSize = 8,
					Foreground = _neutral
				};
				Canvas.SetLeft(label, 0);
				Canvas.SetTop(label, y - 6);
				chart.Children.Add(label);
			}

			var labelStep = Math.Max(1, (int)Math.Ceiling(maxTurn / 14.0));
			for(var turn = 1; turn <= maxTurn; turn += labelStep)
			{
				var label = new TextBlock
				{
					Text = turn.ToString(),
					Width = 18,
					TextAlignment = TextAlignment.Center,
					FontSize = 8,
					Foreground = _neutral
				};
				Canvas.SetLeft(label, xForTurn(turn) - 9);
				Canvas.SetTop(label, chartHeight - plotBottom + 3);
				chart.Children.Add(label);
			}

			foreach(var series in tierSeries.Where(x => x.IsLocalPlayer))
			{
				var points = new PointCollection();
				for(var turn = 1; turn <= maxTurn; turn++)
					points.Add(new Point(xForTurn(turn), yForTier(GetTierAtTurn(series, turn))));

				var color = Color.FromRgb(255, 204, 64);
				chart.Children.Add(new Polyline
				{
					Points = points,
					Stroke = new SolidColorBrush(color),
					StrokeThickness = 2.5,
					StrokeLineJoin = PenLineJoin.Round
				});
				foreach(var levelUp in series.TierUps)
				{
					var marker = new Ellipse
					{
						Width = 5,
						Height = 5,
						Fill = new SolidColorBrush(color)
					};
					Canvas.SetLeft(marker, xForTurn(levelUp.Turn) - 2.5);
					Canvas.SetTop(marker, yForTier(levelUp.Tier) - 2.5);
					chart.Children.Add(marker);
				}
			}

			if(otherPlayers.Count > 0)
			{
				var averagePoints = new PointCollection();
				var averageTiers = new List<double>();
				for(var turn = 1; turn <= maxTurn; turn++)
				{
					var averageTier = otherPlayers.Average(x => GetTierAtTurn(x, turn));
					averageTiers.Add(averageTier);
					averagePoints.Add(new Point(xForTurn(turn), yForTier(averageTier)));
				}
				chart.Children.Add(new Polyline
				{
					Points = averagePoints,
					Stroke = new SolidColorBrush(Color.FromRgb(105, 220, 235)),
					StrokeThickness = 2.5,
					StrokeLineJoin = PenLineJoin.Round
				});
				for(var index = 0; index < averageTiers.Count; index++)
				{
					var turn = index + 1;
					var marker = new Ellipse
					{
						Width = 6,
						Height = 6,
						Fill = new SolidColorBrush(Color.FromRgb(105, 220, 235)),
						Stroke = new SolidColorBrush(Color.FromRgb(28, 32, 34)),
						StrokeThickness = 1,
						ToolTip = "Turn " + turn + " lobby average: " + averageTiers[index].ToString("0.00")
					};
					Canvas.SetLeft(marker, xForTurn(turn) - 3);
					Canvas.SetTop(marker, yForTier(averageTiers[index]) - 3);
					chart.Children.Add(marker);
				}
			}

			if(boardStats != null)
			{
				foreach(var turn in boardStats.Where(x => x.OpponentTier.HasValue))
				{
					var marker = new Ellipse
					{
						Width = 7,
						Height = 7,
						Fill = new SolidColorBrush(Color.FromRgb(240, 150, 170)),
						Stroke = GetOpponentResultRingColor(turn.Result),
						StrokeThickness = string.IsNullOrEmpty(turn.Result) ? 0.75 : 1.35,
						ToolTip = "Turn " + turn.Turn + " opponent at T" + turn.OpponentTier.Value
							+ (string.IsNullOrEmpty(turn.Result) ? "" : " - " + turn.Result)
					};
					Canvas.SetLeft(marker, xForTurn(turn.Turn) - 3.5);
					Canvas.SetTop(marker, yForTier(turn.OpponentTier.Value) - 3.5);
					chart.Children.Add(marker);
				}
			}
			return chart;
		}

		private static double GetTierAtTurn(TierChartSeries series, int turn)
		{
			var tier = 1.0;
			foreach(var levelUp in series.TierUps.OrderBy(x => x.Turn))
			{
				if(levelUp.Turn > turn)
					break;
				tier = levelUp.Tier;
			}
			return tier;
		}

		private void SetBoardStatsExpanded(bool expanded)
		{
			_boardStatsExpanded = expanded;
			_boardStatsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_boardStatsToggle.Content = "Stats by turns  " + (expanded ? "\u25BC" : "\u25B6");
		}

		private static Run CreateHealthIcon(double? health) =>
			health.HasValue && health.Value <= 0 ? CreateSkull() : CreateHeart();

		private void SetTurnsExpanded(bool expanded)
		{
			_turnsExpanded = expanded;
			_turnDetails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_turnSummary.Content = _turnSummaryLabel + "  " + (expanded ? "\u25BC" : "\u25B6");
			_turnSummary.ToolTip = expanded ? "Hide turn-by-turn damage" : "Show turn-by-turn damage";
		}

		private void SetDetailsExpanded(bool expanded)
		{
			var wasExpanded = _detailsExpanded;
			var previousHeight = Height;
			_detailsExpanded = expanded;
			_detailsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_closeButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_resizeGrip.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
			_openRecapButton.Visibility = _finalSummaryMode ? Visibility.Visible : Visibility.Collapsed;
			if(_finalSummaryMode)
				_openRecapButton.Content = expanded ? "Close Recap" : "Open Recap";
			if(expanded)
			{
				Width = _finalSummaryMode
					? Math.Max(370, _userWidth)
					: Math.Max(GetLiveExpandedWidth(), _userLiveWidth ?? 0);
				Height = GetExpandedHeight();
				if(Parent is Canvas canvas)
				{
					var top = Canvas.GetTop(this);
					if(double.IsNaN(top))
						top = 0;
					if(!wasExpanded)
						_detailsExpandedUpward = top + Height > canvas.ActualHeight;
					if(_detailsExpandedUpward)
						Canvas.SetTop(this, top - (Height - previousHeight));
				}
			}
			else
			{
				Width = 190;
				Height = GetCompactHeight();
				if(wasExpanded && _detailsExpandedUpward && Parent is Canvas canvas)
				{
					var top = Canvas.GetTop(this);
					if(double.IsNaN(top))
						top = 0;
					Canvas.SetTop(this, top + previousHeight - Height);
				}
				_detailsExpandedUpward = false;
			}
			UpdateReviewPanelMaxHeight();
			KeepWithinCanvas();
		}

		private void SetContentVerticalAlignment(bool topAligned)
		{
			var alignment = topAligned ? VerticalAlignment.Top : VerticalAlignment.Center;
			var margin = new Thickness(10, topAligned ? 7 : 4, 10, 4);
			if(_content.VerticalAlignment == alignment && _content.Margin.Equals(margin))
				return;
			_content.VerticalAlignment = alignment;
			_content.Margin = margin;
		}

		private double GetCompactHeight() => _finalSummaryMode ? 70 : _liveMode ? 52 : 44;

		private double GetExpandedHeight()
		{
			var minimumHeight = Math.Max(_userHeight, _finalSummaryMode ? 390 : 150);
			if(!_finalSummaryMode)
				return minimumHeight;

			_reviewContent.Measure(new System.Windows.Size(
				Math.Max(0, Width - _content.Margin.Left - _content.Margin.Right - 2),
				double.PositiveInfinity));
			var desiredHeight = GetReviewPanelFixedHeight() + _reviewContent.DesiredSize.Height;
			var height = Math.Max(minimumHeight, desiredHeight);
			if(Parent is Canvas canvas && canvas.ActualHeight > 0)
				height = Math.Min(height, canvas.ActualHeight);
			return height;
		}

		private void UpdateReviewPanelMaxHeight()
		{
			if(_finalSummaryMode)
				_reviewPanel.MaxHeight = Math.Max(0, Height - GetReviewPanelFixedHeight());
		}

		private double GetReviewPanelFixedHeight()
		{
			var visibility = _reviewPanel.Visibility;
			_reviewPanel.Visibility = Visibility.Collapsed;
			_content.Measure(new System.Windows.Size(Math.Max(0, Width - 2), double.PositiveInfinity));
			var fixedHeight = _content.DesiredSize.Height + 2;
			_reviewPanel.Visibility = visibility;
			return fixedHeight;
		}

		private double GetLiveExpandedWidth()
		{
			_healthLine.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
			return Math.Max(
				260,
				Math.Ceiling(_healthLine.DesiredSize.Width
					+ _content.Margin.Left + _content.Margin.Right + 4));
		}

		private void KeepWithinCanvas()
		{
			if(Parent is Canvas canvas)
			{
				var left = Canvas.GetLeft(this);
				if(double.IsNaN(left))
					left = 0;
				var top = Canvas.GetTop(this);
				if(double.IsNaN(top))
					top = 0;
				Canvas.SetLeft(this, Math.Max(0, Math.Min(Math.Max(0, canvas.ActualWidth - Width), left)));
				Canvas.SetTop(this, Math.Max(0, Math.Min(Math.Max(0, canvas.ActualHeight - Height), top)));
			}
		}

		private void SetLiveScoreLabel(double? ratio)
		{
			_liveScore.Inlines.Clear();
			_liveScore.Inlines.Add(new Run(ratio.HasValue ? ratio.Value.ToString("0.00") : "N/A"));
		}

		private Brush GetPercentileColor(double? percentile)
		{
			if(!percentile.HasValue)
				return _neutral;
			if(percentile.Value < 10)
				return _strongRed;
			if(percentile.Value < 25)
				return _mediumRed;
			if(percentile.Value < 40)
				return _lightRed;
			if(percentile.Value < 60)
				return _gold;
			if(percentile.Value < 75)
				return _lightGreen;
			if(percentile.Value < 90)
				return _mediumGreen;
			return _strongGreen;
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

		private Brush GetDamageComparisonColor(int actualDamageTaken, double expectedDamageTaken)
		{
			if(expectedDamageTaken <= 0)
				return _neutral;
			return GetScoreColor((actualDamageTaken + 1.0) / (expectedDamageTaken + 1.0));
		}

		public void HideOverlay()
		{
			_detailsCloseTimer.Stop();
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
			_detailsCloseTimer.Stop();
			HideOverlay();
		}
	}

	internal sealed class TierChartSeries
	{
		public bool IsLocalPlayer { get; set; }
		public List<TierChartPoint> TierUps { get; set; } = new List<TierChartPoint>();
	}

	internal sealed class TierChartPoint
	{
		public double Tier { get; set; }
		public int Turn { get; set; }
	}

	internal sealed class BoardStatsTurn
	{
		public int Turn { get; set; }
		public int PlayerTier { get; set; }
		public int PlayerAttack { get; set; }
		public int PlayerHealth { get; set; }
		public int? OpponentTier { get; set; }
		public int OpponentAttack { get; set; }
		public int OpponentHealth { get; set; }
		public string Result { get; set; }
	}
}

/// dotnet build .\BgLuck.csproj -c Release -p:HdtDir="C:\Users\Admin\AppData\Local\HearthstoneDeckTracker\app-1.59.7"