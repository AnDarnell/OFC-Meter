using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Core = Hearthstone_Deck_Tracker.API.Core;

namespace BgLuck
{
	/// Logs Battlegrounds combat odds and results, plus per-player tier history and recruit-phase board stats.
	/// 
	public class BgLuckPlugin : IPlugin
	{
		public string Name => "BG Luck";
		public string Description => "Tracks cumulative combat luck, player tier history, and Battlegrounds board stats.";
		public string ButtonText => "Open log folder";
		public string Author => "Anton";
		public Version Version => new Version(0, 1, 0);
		public MenuItem MenuItem => null;

		private static readonly string LogDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "BgLuck");
		private static readonly string BoardStatsFile = Path.Combine(LogDir, "board-stats.csv");

		// --- reflection handles into HDT internals (BobsBuddyInvoker is internal) ---
		private static Type _invokerType;
		private static MethodInfo _getInstance;
		private static MethodInfo _getTavernUpTurns;

		private GameLog _game;            // null when no BG game is being tracked
		private object _combatInvoker;    // the BobsBuddyInvoker instance of the combat in progress
		private CombatLog _combat;        // snapshot taken when combat starts
		private LuckOverlayControl _overlay;
		private TextBlock _oddPlayerMarker;
		private TextBlock _playerOneMarker;
		private bool _showingFinalSummary;
		private bool _newGamePending;
		private string _completedGameId;
		private DateTime _lastPoll = DateTime.MinValue;
		private bool _lastCombatPhase;
		private int _lastCombatPhaseTurn;
		private bool _tierHistoryApiUnavailableLogged;
		private bool _tierHistoryErrorLogged;
		private int? _completedStartingMmr;
		private double? _completedPlacement;
		private bool _completedPlacementIsShared;
		private double? _completedAverageOpponentMmr;
		private bool _completedAverageOpponentMmrResolved;
		private bool _completedIsSpectator;
		private DateTime _completedRatingCheckDeadline;
		private readonly List<string> _debug = new List<string>();

		public void OnLoad()
		{
			Directory.CreateDirectory(LogDir);
			_invokerType = typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.BobsBuddy.BobsBuddyInvoker");
			_getInstance = _invokerType?.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
			var gameType = typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.Hearthstone.GameV2");
			_getTavernUpTurns = gameType?.GetMethod(
				"GetBattlegroundsHeroLatestTavernUpTurn",
				BindingFlags.Public | BindingFlags.Instance);
			_overlay = new LuckOverlayControl();
			Core.OverlayCanvas.Children.Add(_overlay);
			OverlayExtensions.SetIsOverlayHitTestVisible(_overlay, true);
			Canvas.SetLeft(_overlay, 24);
			Canvas.SetTop(_overlay, 24);
			_oddPlayerMarker = CreatePlayerMarker(
				"G",
				14,
				Brushes.White,
				new SolidColorBrush(Color.FromArgb(230, 80, 0, 150)));
			_playerOneMarker = CreatePlayerMarker(
				"\U0001F451",
				18,
				new SolidColorBrush(Color.FromRgb(255, 215, 60)),
				new SolidColorBrush(Color.FromArgb(220, 28, 32, 34)));
			_playerOneMarker.FontFamily = new FontFamily("Segoe UI Emoji");
			Core.OverlayCanvas.Children.Add(_oddPlayerMarker);
			Core.OverlayCanvas.Children.Add(_playerOneMarker);
			GameEvents.OnGameStart.Add(OnGameStart);
			GameEvents.OnGameEnd.Add(OnGameEnd);
		}

		public void OnUnload()
		{
			Save();
			Core.OverlayCanvas.Children.Remove(_oddPlayerMarker);
			Core.OverlayCanvas.Children.Remove(_playerOneMarker);
			if(_overlay != null)
				Core.OverlayCanvas.Children.Remove(_overlay);
			_oddPlayerMarker = null;
			_playerOneMarker = null;
			_overlay = null;
		}

		public void OnButtonPress() { System.Diagnostics.Process.Start(LogDir); }

		private static bool IsPlayerConceded(Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			var playerId = game?.Player?.Id;
			if(!playerId.HasValue || game.Entities == null)
				return false;
			var conceded = (int)HearthDb.Enums.PlayState.CONCEDED;
			return game.Entities.Values.Any(entity => entity != null && entity.IsPlayer
				&& entity.GetTag(HearthDb.Enums.GameTag.CONTROLLER) == playerId.Value
				&& entity.GetTag(HearthDb.Enums.GameTag.PLAYSTATE) == conceded);
		}

		private static void ApplyConcededLethalDamage(CombatLog combat)
		{
			var lethalDamage = Math.Max(0, combat.PlayerHealth + combat.PlayerArmor);
			combat.Result = "Loss";
			combat.RawDamageTaken = lethalDamage;
			combat.ActualDamageTaken = lethalDamage;
		}

		private void OnGameStart()
		{
			HidePlayerMarkers();
			if(_showingFinalSummary)
			{
				_newGamePending = true;
				return;
			}
			_game = null;
			_combatInvoker = null;
			_combat = null;
			_completedStartingMmr = null;
			_completedPlacement = null;
			_completedPlacementIsShared = false;
			_completedAverageOpponentMmr = null;
			_completedAverageOpponentMmrResolved = false;
			_completedIsSpectator = false;
			_debug.Clear();
			_lastCombatPhase = false;
			_lastCombatPhaseTurn = 0;
			_tierHistoryApiUnavailableLogged = false;
			_tierHistoryErrorLogged = false;
			_overlay?.ResetForNewGame();
		}

		private void OnGameEnd()
		{
			HidePlayerMarkers();
			var playerConceded = IsPlayerConceded(Core.Game);
			if(_game != null && Core.Game != null)
			{
				try
				{
					CaptureRecruitSnapshot(Core.Game, Core.Game.GetTurnNumber());
					CapturePlayerTierHistories(Core.Game);
					CapturePlayerPlacements(Core.Game, Core.Game.GetTurnNumber(), true);
					CaptureDarkGiftObservations(Core.Game);
				}
				catch(Exception e) { _debug.Add("Final data capture: " + e.GetType().Name + ": " + e.Message); }
			}
			if(_combatInvoker != null && _combat != null && _game != null)
			{
				try { FinishCombat(_combatInvoker, _combat); }
				catch(Exception e) { _debug.Add("Final combat: " + e.GetType().Name + ": " + e.Message); }
			}
			if(_game != null)
			{
				if(playerConceded)
				{
					var currentTurn = Core.Game?.GetTurnNumber() ?? 0;
					var concededCombat = _combat != null
						? _combat
						: _game.Combats
							.Where(c => c.Turn == currentTurn)
							.OrderByDescending(c => c.Time)
							.FirstOrDefault();
					if(concededCombat != null)
						ApplyConcededLethalDamage(concededCombat);
				}
				if(!_game.StartingBattlegroundsMmr.HasValue)
					_game.StartingBattlegroundsMmr = GetStartingBattlegroundsMmr(Core.Game);
				var localControllerId = Core.Game?.Player?.Id;
				var observedPlacement = GetFinalPlacement(_game, localControllerId);
				_completedPlacementIsShared = HasSharedEliminationPlacement(_game, localControllerId);
				var finalPlacement = _game.IsSpectator
					? (double?)null
					: _completedPlacementIsShared
						? observedPlacement ?? GetHdtReportedPlacement(Core.Game)
						: GetHdtReportedPlacement(Core.Game) ?? observedPlacement;
				var endingMmr = GetEndingBattlegroundsMmr(Core.Game);
				var gain = endingMmr.HasValue && _game.StartingBattlegroundsMmr.HasValue
					? (int?)(endingMmr.Value - _game.StartingBattlegroundsMmr.Value)
					: null;
				_completedStartingMmr = _game.StartingBattlegroundsMmr;
				_completedPlacement = finalPlacement;
				_completedAverageOpponentMmr = EstimateAverageOpponentMmr(
					_completedStartingMmr, gain, _completedPlacement);
				_completedIsSpectator = _game.IsSpectator;
				_completedAverageOpponentMmrResolved = _completedIsSpectator
					|| !_completedStartingMmr.HasValue
					|| (_completedPlacement.HasValue && endingMmr.HasValue);
				_completedRatingCheckDeadline = DateTime.Now.AddSeconds(10);

				var playerHero = Core.Game?.Player?.Hero;
				int? actualHealth = playerHero == null
					? (int?)null
					: playerHero.Health + playerHero.GetTag(HearthDb.Enums.GameTag.ARMOR);
				if(playerConceded)
					actualHealth = 0;
				var finalCombat = _game.Combats.OrderByDescending(c => c.Turn).FirstOrDefault();
				if(finalCombat != null && finalCombat.Result == "Loss"
					&& finalCombat.RawDamageTaken > finalCombat.PlayerHealth + finalCombat.PlayerArmor)
					actualHealth = finalCombat.PlayerHealth + finalCombat.PlayerArmor - finalCombat.RawDamageTaken;
				else if(!actualHealth.HasValue && finalCombat != null && finalCombat.Result == "Loss")
					actualHealth = finalCombat.PlayerHealth + finalCombat.PlayerArmor - finalCombat.RawDamageTaken;
				UpdateTotals(_game, actualHealth.HasValue ? (int?)Math.Max(0, actualHealth.Value) : null);
				Save();
				var turnSummaries = _game.Combats.OrderBy(c => c.Turn)
					.Select(c => Tuple.Create(c.Turn, c.ActualDamageTaken, c.ExpectedDamageTaken, c.Result))
					.ToList();
				_overlay?.ShowFinalSummary(
					_game.TotalActualDamageTaken,
					_game.TotalExpectedDamageTaken,
					_game.ScoredActualDamageTaken,
					_game.Combats.Count,
					_game.ScoredCombats,
					_game.DamageTakenRatio,
					actualHealth,
					_game.ExpectedHealthAtEnd,
					_game.HealthPercentile,
					turnSummaries,
					BuildTierChartSeries(_game),
					BuildBoardStatsTurns(_game),
					_game.RecruitSnapshots
						.OrderByDescending(x => x.Turn)
						.Select(x => x.PlayerComposition)
						.FirstOrDefault(x => !string.IsNullOrEmpty(x)),
					finalPlacement,
					_completedAverageOpponentMmr,
					!_completedAverageOpponentMmrResolved,
					GetUnlikeliestCombat(_game, "Loss"),
					GetUnlikeliestCombat(_game, "Win"),
					_game.IsSpectator);
				_showingFinalSummary = true;
				_completedGameId = _game.GameId;
			}
			_game = null;
			_combatInvoker = null;
			_combat = null;
		}

		/// <summary>Called by HDT roughly every 100 ms on the UI thread.</summary>
		public void OnUpdate()
		{
			if((DateTime.Now - _lastPoll).TotalMilliseconds < 400)
				return;
			_lastPoll = DateTime.Now;

			try
			{
				var game = Core.Game;
				if(_showingFinalSummary)
				{
					if(game != null && game.CurrentGameStats != null
						&& game.CurrentGameStats.GameId.ToString() == _completedGameId)
					{
						RefreshCompletedPlacement(game);
						RefreshCompletedAverageOpponentMmr(game);
					}
					else if(!_completedAverageOpponentMmrResolved
						&& DateTime.Now >= _completedRatingCheckDeadline)
						RefreshCompletedAverageOpponentMmr(null);
				}
				var isBattlegrounds = IsBattlegroundsOverlayVisible()
					|| game != null && (game.IsBattlegroundsMatch
						|| game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Battlegrounds);
				if(!isBattlegrounds)
				{
					HidePlayerMarkers();
					if(!_showingFinalSummary)
						_overlay?.HideOverlay();
					return;
				}

				if(game == null)
				{
					HidePlayerMarkers();
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}

				if(game.IsInMenu && game.IsRunning)
				{
					HidePlayerMarkers();
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}

				if(!game.IsRunning)
				{
					HidePlayerMarkers();
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}
				var gameId = game.CurrentGameStats.GameId;
				var currentGameId = gameId.ToString();
				if(_showingFinalSummary)
				{
					if(!_newGamePending || currentGameId == _completedGameId)
						return;

					_showingFinalSummary = false;
					_newGamePending = false;
					_game = null;
					_combatInvoker = null;
					_combat = null;
					_debug.Clear();
					_lastCombatPhase = false;
					_lastCombatPhaseTurn = 0;
					_tierHistoryApiUnavailableLogged = false;
					_tierHistoryErrorLogged = false;
					_completedStartingMmr = null;
					_completedPlacement = null;
					_completedPlacementIsShared = false;
					_completedAverageOpponentMmr = null;
					_completedAverageOpponentMmrResolved = false;
					_completedIsSpectator = false;
					_completedRatingCheckDeadline = DateTime.MinValue;
					_overlay?.ResetForNewGame();
				}
				if(_game == null)
					_game = new GameLog
					{
						GameId = currentGameId,
						Started = DateTime.Now,
						IsSpectator = game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Spectator,
						StartingBattlegroundsMmr = GetStartingBattlegroundsMmr(game)
					};
				else
					_game.IsSpectator |= game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Spectator;
				if(!_game.StartingBattlegroundsMmr.HasValue)
					_game.StartingBattlegroundsMmr = GetStartingBattlegroundsMmr(game);

				var currentTurn = game.GetTurnNumber();
				CapturePlayerTierHistories(game);
				CapturePlayerPlacements(game, currentTurn);
				CaptureDarkGiftObservations(game);
				if(game.IsBattlegroundsCombatPhase
					&& (!_lastCombatPhase || currentTurn != _lastCombatPhaseTurn))
					CaptureRecruitSnapshot(game, currentTurn);
				_lastCombatPhase = game.IsBattlegroundsCombatPhase;
				_lastCombatPhaseTurn = currentTurn;
				string ghostPriorityHeroName;
				var oddPlayerId = UpdatePlayerMarkers(game, out ghostPriorityHeroName);

				if(_getInstance == null)
				{
					_overlay?.HideOverlay();
					return;
				}

				if(_combatInvoker == null)
				{
					// Look for an invoker that has just entered the Combat state. createInstanceIfNoneFound = false
					// so we never create or disturb HDT's own instances.
					var turn = game.GetTurnNumber();
					var inv = _getInstance.Invoke(null, new object[] { gameId, turn, false });
					if(inv != null && StateOf(inv) == "Combat" && !_game.SeenInvokers.Contains(inv))
					{
						_combatInvoker = inv;
						_game.SeenInvokers.Add(inv);
						_combat = SnapshotCombatStart(turn);
					}
				}
				else
				{
					var state = StateOf(_combatInvoker);
					if(state.StartsWith("Shopping") || state.StartsWith("GameOver"))
					{
						FinishCombat(_combatInvoker, _combat);
						_combatInvoker = null;
						_combat = null;
					}
				}
				var playerHero = game.Player?.Hero;
				int? currentHealth = playerHero == null
					? (int?)null
					: playerHero.Health + playerHero.GetTag(HearthDb.Enums.GameTag.ARMOR);
				UpdateTotals(_game, currentHealth);
				double? expectedHealth = _game.ExpectedHealthAtEnd;
				_overlay?.ShowLive(
					_game.TotalActualDamageTaken,
					_game.TotalExpectedDamageTaken,
					_game.ScoredActualDamageTaken,
					_game.DamageTakenRatio,
					currentHealth,
					expectedHealth,
					_game.HealthPercentile,
					_game.IsSpectator,
					oddPlayerId,
					ghostPriorityHeroName);
			}
			catch(Exception e)
			{
				_debug.Add("OnUpdate: " + e.GetType().Name + ": " + e.Message);
			}
		}

		private static bool IsBattlegroundsOverlayVisible()
		{
			var overlay = Core.OverlayWindow;
			if(overlay == null)
				return false;
			if(Convert.ToBoolean(Get(overlay, "BgsGuidesPreLobbyVisible") ?? false))
				return true;
			var topBar = Get(overlay, "BgsTopBar") as System.Windows.UIElement;
			return topBar?.Visibility == System.Windows.Visibility.Visible;
		}

		private static TextBlock CreatePlayerMarker(string text, double fontSize, Brush foreground, Brush background) =>
			new TextBlock
			{
				Text = text,
				Width = 24,
				Height = 24,
				FontSize = fontSize,
				FontWeight = FontWeights.Bold,
				Foreground = foreground,
				Background = background,
				TextAlignment = TextAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center,
				RenderTransform = new ScaleTransform(1, 1),
				IsHitTestVisible = false,
				Visibility = Visibility.Collapsed
			};

		private int? UpdatePlayerMarkers(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game,
			out string ghostPriorityHeroName)
		{
			ghostPriorityHeroName = null;
			if(game?.Entities == null)
			{
				HidePlayerMarkers();
				return null;
			}

			var playerIdTag = HearthDb.Enums.GameTag.PLAYER_ID;
			var placeTag = HearthDb.Enums.GameTag.PLAYER_LEADERBOARD_PLACE;
			var oddPlayerTag = HearthDb.Enums.GameTag.BACON_ODD_PLAYER_OUT;
			var players = game.Entities.Values
				.Where(entity => entity != null
					&& entity.HasTag(playerIdTag)
					&& entity.GetTag(playerIdTag) > 0
					&& entity.HasTag(placeTag)
					&& entity.GetTag(placeTag) > 0
					&& entity.GetTag(HearthDb.Enums.GameTag.BACON_DUMMY_PLAYER) != 1)
				.GroupBy(entity => entity.GetTag(playerIdTag))
				.Select(group => new
				{
					PlayerId = group.Key,
					Place = group.Min(entity => entity.GetTag(placeTag)),
					IsOddPlayerOut = group.Any(entity => entity.GetTag(oddPlayerTag) == 1)
				})
				.OrderBy(player => player.Place)
				.ToList();

			var oddPlayer = players.FirstOrDefault(player => player.IsOddPlayerOut);
			var playerOne = players.FirstOrDefault(player => player.PlayerId == 1);
			var playerOneHero = game.Entities.Values.FirstOrDefault(entity => entity != null
				&& entity.IsHero
				&& entity.GetTag(playerIdTag) == 1);
			ghostPriorityHeroName = playerOneHero?.Card?.Name
				?? playerOneHero?.LocalizedName
				?? playerOneHero?.Name
				?? playerOneHero?.CardId;
			SetPlayerMarkerPosition(_oddPlayerMarker, oddPlayer?.Place, -8, 28);
			SetPlayerMarkerPosition(_playerOneMarker, playerOne?.Place, 38, 28);
			return oddPlayer == null ? (int?)null : oddPlayer.PlayerId;
		}

		private static void SetPlayerMarkerPosition(TextBlock marker, int? place, double offsetX, double offsetY)
		{
			var canvas = Core.OverlayCanvas;
			var canvasWidth = canvas.ActualWidth;
			var canvasHeight = canvas.ActualHeight;
			if(marker == null || !place.HasValue || place.Value < 1 || place.Value > 8
				|| canvasWidth <= 0 || canvasHeight <= 0)
			{
				if(marker != null)
					marker.Visibility = Visibility.Collapsed;
				return;
			}

			var scale = canvasHeight / 1080.0;
			var screenRatio = (4.0 / 3.0) / (canvasWidth / canvasHeight);
			var horizontalOffset = canvasWidth * (1 - screenRatio) / 2;
			var referenceLeft = 270 + (place.Value - 1) * -2.5;
			var left = horizontalOffset + (referenceLeft - 240) * scale + offsetX * scale;
			var top = (184 + (place.Value - 1) * 93 + offsetY) * scale;
			if(marker.RenderTransform is ScaleTransform markerScale)
			{
				markerScale.ScaleX = scale;
				markerScale.ScaleY = scale;
			}
			Canvas.SetLeft(marker, left);
			Canvas.SetTop(marker, top);
			marker.Visibility = Visibility.Visible;
		}

		private void HidePlayerMarkers()
		{
			if(_oddPlayerMarker != null)
				_oddPlayerMarker.Visibility = Visibility.Collapsed;
			if(_playerOneMarker != null)
				_playerOneMarker.Visibility = Visibility.Collapsed;
		}

		private CombatLog SnapshotCombatStart(int turn)
		{
			var game = Core.Game;
			CaptureRecruitSnapshot(game, turn);
			var c = new CombatLog { Turn = turn, Time = DateTime.Now };
			try
			{
				var me = game.Player.Hero;
				var opp = game.Opponent.Hero;
				c.PlayerHealth = me?.Health ?? 0;
				c.PlayerArmor = me?.GetTag(HearthDb.Enums.GameTag.ARMOR) ?? 0;
				if(_game != null && !_game.InitialEffectiveHealth.HasValue)
					_game.InitialEffectiveHealth = c.PlayerHealth + c.PlayerArmor;
				c.PlayerTier = me?.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL) ?? 0;
				c.OpponentHeroEntityId = opp?.Id;
				var opponentTier = opp?.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL) ?? 0;
				c.OpponentTier = opponentTier > 0 ? (int?)opponentTier : null;
				c.OpponentHero = opp?.Card?.Name ?? opp?.CardId;
				c.OpponentHealth = opp?.Health ?? 0;
				c.OpponentArmor = opp?.GetTag(HearthDb.Enums.GameTag.ARMOR) ?? 0;
				c.PlayerBoard = game.Player.Board.Where(e => e.IsMinion)
					.OrderBy(e => e.ZonePosition).Select(e => $"{e.Card?.Name} {e.Attack}/{e.Health}").ToList();
				c.OpponentBoard = game.Opponent.Board.Where(e => e.IsMinion)
					.OrderBy(e => e.ZonePosition).Select(e => $"{e.Card?.Name} {e.Attack}/{e.Health}").ToList();
			}
			catch(Exception e) { _debug.Add("Snapshot: " + e.Message); }
			return c;
		}

		private void CapturePlayerTierHistories(Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			if(game.Entities == null)
				return;
			if(_getTavernUpTurns == null)
			{
				if(!_tierHistoryApiUnavailableLogged)
				{
					_debug.Add("HDT does not expose Battlegrounds tavern tier history.");
					_tierHistoryApiUnavailableLogged = true;
				}
				return;
			}

			var playerHeroId = game.Player?.Hero?.Id;
			var playerId = game.Player?.Id;
			var battlegroundsPlayerId = game.Player?.Hero?.GetTag(HearthDb.Enums.GameTag.PLAYER_ID) ?? 0;
			foreach(var hero in game.Entities.Values.Where(e => e != null && e.IsHero))
			{
				var history = _game.PlayerTierHistories.FirstOrDefault(x => x.HeroEntityId == hero.Id);
				if(history == null)
				{
					history = new PlayerTierHistory { HeroEntityId = hero.Id };
					_game.PlayerTierHistories.Add(history);
				}
				history.HeroCardId = hero.CardId;
				history.ControllerId = hero.GetTag(HearthDb.Enums.GameTag.CONTROLLER);
				history.PlayerId = hero.GetTag(HearthDb.Enums.GameTag.PLAYER_ID);
				history.IsLocalPlayer = hero.Id == playerHeroId
					|| battlegroundsPlayerId > 0 && history.PlayerId == battlegroundsPlayerId
					|| battlegroundsPlayerId <= 0 && history.PlayerId <= 0
						&& playerId.HasValue && hero.IsControlledBy(playerId.Value);
				var currentTier = hero.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL);
				if(currentTier > 0)
					history.CurrentTier = currentTier;

				try
				{
					var turns = _getTavernUpTurns.Invoke(game, new object[] { hero.Id }) as IDictionary;
					if(turns == null)
						continue;
					foreach(DictionaryEntry entry in turns)
					{
						var tier = Convert.ToInt32(entry.Key);
						var turn = Convert.ToInt32(entry.Value);
						if(tier <= 0 || turn <= 0)
							continue;
						var tierUp = history.TierUps.FirstOrDefault(x => x.Tier == tier);
						if(tierUp == null)
						{
							tierUp = new TierUpLog { Tier = tier };
							history.TierUps.Add(tierUp);
						}
						tierUp.Turn = turn;
					}
					history.TierUps = history.TierUps.OrderBy(x => x.Tier).ToList();
				}
				catch(Exception e)
				{
					if(!_tierHistoryErrorLogged)
					{
						_debug.Add("Tier history capture: " + e.GetType().Name + ": " + e.Message);
						_tierHistoryErrorLogged = true;
					}
				}
			}
		}

		private void CapturePlayerPlacements(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game,
			int turn,
			bool finalCapture = false)
		{
			if(_game == null || game.Entities == null)
				return;

			var playerStates = new Dictionary<int, int>();
			foreach(var playerEntity in game.Entities.Values.Where(e => e != null && e.IsPlayer))
			{
				var controllerId = playerEntity.GetTag(HearthDb.Enums.GameTag.CONTROLLER);
				if(controllerId > 0)
					playerStates[controllerId] = playerEntity.GetTag(HearthDb.Enums.GameTag.PLAYSTATE);
			}

			var lost = (int)HearthDb.Enums.PlayState.LOST;
			var tied = (int)HearthDb.Enums.PlayState.TIED;
			var won = (int)HearthDb.Enums.PlayState.WON;
			var conceded = (int)HearthDb.Enums.PlayState.CONCEDED;
			var quit = (int)HearthDb.Enums.PlayState.QUIT;
			var disconnected = (int)HearthDb.Enums.PlayState.DISCONNECTED;
			var placeTag = HearthDb.Enums.GameTag.PLAYER_LEADERBOARD_PLACE;
			var playStateTag = HearthDb.Enums.GameTag.PLAYSTATE;
			var eliminatedStates = new HashSet<int>
			{
				lost, tied, conceded, quit, disconnected
			};
			var eliminatedControllers = new HashSet<int>(playerStates
				.Where(x => eliminatedStates.Contains(x.Value))
				.Select(x => x.Key));
			foreach(var hero in game.Entities.Values.Where(e => e != null && e.IsHero))
			{
				var controllerId = hero.GetTag(HearthDb.Enums.GameTag.CONTROLLER);
				if(controllerId > 0
					&& hero.Health + hero.GetTag(HearthDb.Enums.GameTag.ARMOR) <= 0)
					eliminatedControllers.Add(controllerId);
			}

			var aliveControllers = new HashSet<int>(playerStates.Keys
				.Where(controllerId => !eliminatedControllers.Contains(controllerId)));
			if(game.IsBattlegroundsCombatPhase
				&& (!_lastCombatPhase || turn != _lastCombatPhaseTurn))
			{
				_game.PlacementCombatStartTurn = turn;
				_game.AlivePlayersAtCombatStart = aliveControllers;
				_game.AlivePlayerCountsAtCombatStart[turn] = aliveControllers.Count;
			}
			if(_game.PlacementCombatStartTurn.HasValue
				&& (_game.PlacementCombatStartTurn.Value == turn || finalCapture))
			{
				var aliveAfterCombat = _game.AlivePlayersAtCombatStart
					.Count(aliveControllers.Contains);
				var eliminatedDuringCombat = _game.AlivePlayersAtCombatStart.Count - aliveAfterCombat;
				if(eliminatedDuringCombat > 1)
				{
					_game.SimultaneousEliminationTurns.Add(_game.PlacementCombatStartTurn.Value);
					_game.AlivePlayerCountsAfterCombat[_game.PlacementCombatStartTurn.Value] = aliveAfterCombat;
				}
			}

			var localControllerId = game.Player?.Id;
			var localHeroId = game.Player?.Hero?.Id;
			var observations = new List<PlayerPlacementLog>();
			var observationTurn = finalCapture && _game.PlacementCombatStartTurn.HasValue
				? _game.PlacementCombatStartTurn.Value
				: turn;
			foreach(var placementEntity in game.Entities.Values
				.Where(e => e != null && e.HasTag(placeTag)))
			{
				var controllerId = placementEntity.GetTag(HearthDb.Enums.GameTag.CONTROLLER);
				var place = placementEntity.GetTag(placeTag);
				if(controllerId <= 0 || place < 1 || place > 8)
					continue;
				if(localControllerId == controllerId && localHeroId.HasValue && placementEntity.IsHero
					&& placementEntity.Id != localHeroId.Value)
					continue;

				var playState = playerStates.TryGetValue(controllerId, out var state)
					? state
					: placementEntity.GetTag(playStateTag);
				var isElimination = playState == lost || playState == tied
					|| playState == conceded || playState == quit || playState == disconnected
					|| placementEntity.IsHero
						&& placementEntity.Health + placementEntity.GetTag(HearthDb.Enums.GameTag.ARMOR) <= 0;
				var isTerminal = isElimination || playState == won;
				if(!isTerminal && (!finalCapture || game.Player == null || controllerId != game.Player.Id))
					continue;
				var existing = _game.PlacementObservations.FirstOrDefault(x => x.ControllerId == controllerId);
				if(existing != null)
				{
					if(finalCapture && localControllerId == controllerId)
					{
						existing.Place = place;
						existing.IsElimination = isElimination;
						existing.PlayState = playState;
						existing.HeroEntityId = placementEntity.Id;
						existing.Turn = observationTurn;
					}
					continue;
				}
				if(observations.Any(x => x.ControllerId == controllerId))
					continue;

				observations.Add(new PlayerPlacementLog
				{
					ControllerId = controllerId,
					HeroEntityId = placementEntity.Id,
					PlayState = playState,
					Place = place,
					Turn = observationTurn,
					IsElimination = isElimination
				});
			}

			if(observations.Count == 0)
				return;

			var batch = ++_game.PlacementCaptureBatch;
			foreach(var observation in observations)
			{
				observation.Batch = batch;
				_game.PlacementObservations.Add(observation);
			}
		}

		private static double? GetFinalPlacement(GameLog game, int? localControllerId)
		{
			if(!localControllerId.HasValue)
				return null;
			var local = game.PlacementObservations
				.FirstOrDefault(x => x.ControllerId == localControllerId.Value);
			if(local == null)
				return null;
			if(!local.IsElimination)
				return local.Place;

			var concurrentEliminations = game.SimultaneousEliminationTurns.Contains(local.Turn);
			if(concurrentEliminations
				&& game.AlivePlayerCountsAtCombatStart.TryGetValue(local.Turn, out var aliveBefore)
				&& game.AlivePlayerCountsAfterCombat.TryGetValue(local.Turn, out var aliveAfter))
				return (aliveAfter + 1 + aliveBefore) / 2.0;

			var simultaneous = game.PlacementObservations
				.Where(x => x.IsElimination
					&& (concurrentEliminations
						? x.Turn == local.Turn
						: x.Place == local.Place && x.Batch == local.Batch && x.Turn == local.Turn))
				.ToList();
			var observedPlaces = simultaneous.Select(x => x.Place).Distinct().ToList();
			var firstPlace = local.Place;
			var lastPlace = local.Place;
			while(observedPlaces.Contains(firstPlace - 1))
				firstPlace--;
			while(observedPlaces.Contains(lastPlace + 1))
				lastPlace++;

			var rankedSlots = simultaneous
				.Where(x => x.Place >= firstPlace && x.Place <= lastPlace)
				.GroupBy(x => x.Place)
				.SelectMany(group => Enumerable.Range(group.Key, group.Count()))
				.OrderBy(x => x)
				.ToList();
			var middle = rankedSlots.Count / 2;
			return rankedSlots.Count % 2 == 1
				? rankedSlots[middle]
				: (rankedSlots[middle - 1] + rankedSlots[middle]) / 2.0;
		}

		private static bool HasSharedEliminationPlacement(GameLog game, int? localControllerId)
		{
			if(!localControllerId.HasValue)
				return false;

			var local = game.PlacementObservations
				.FirstOrDefault(x => x.ControllerId == localControllerId.Value);
			return local != null
				&& local.IsElimination
				&& game.SimultaneousEliminationTurns.Contains(local.Turn);
		}

		private static double? GetHdtReportedPlacement(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			var playerId = game?.Player?.Id;
			if(!playerId.HasValue || game.Entities == null)
				return null;

			var placeTag = HearthDb.Enums.GameTag.PLAYER_LEADERBOARD_PLACE;
			var hero = game.Entities.Values.FirstOrDefault(entity => entity != null
				&& entity.HasTag(placeTag)
				&& entity.IsControlledBy(playerId.Value));
			var place = hero?.GetTag(placeTag) ?? 0;
			return place >= 1 && place <= 8 ? (double?)place : null;
		}

		private static int? GetStartingBattlegroundsMmr(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			var recordedRating = game?.CurrentGameStats?.BattlegroundsRating ?? 0;
			if(recordedRating > 0)
				return recordedRating;
			var currentRating = game?.CurrentBattlegroundsRating;
			return currentRating.HasValue && currentRating.Value > 0 ? currentRating : null;
		}

		private static int? GetEndingBattlegroundsMmr(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			var rating = game?.CurrentGameStats?.BattlegroundsRatingAfter ?? 0;
			return rating > 0 ? (int?)rating : null;
		}

		private static double? EstimateAverageOpponentMmr(
			int? playerMmr,
			int? gain,
			double? placement)
		{
			if(!playerMmr.HasValue || playerMmr.Value <= 0 || !gain.HasValue || !placement.HasValue)
				return null;

			// With the actual placement known, use its avg-opponent estimate directly.
			var averageOpponentMmr = playerMmr.Value
				- 148.1181435 * (100 - ((placement.Value - 1) * (200.0 / 7.0) + gain.Value));
			if(double.IsNaN(averageOpponentMmr) || double.IsInfinity(averageOpponentMmr))
				return null;
			return averageOpponentMmr;
		}

		private void RefreshCompletedAverageOpponentMmr(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			if(_completedAverageOpponentMmrResolved)
				return;

			var endingMmr = GetEndingBattlegroundsMmr(game);
			if(endingMmr.HasValue && _completedStartingMmr.HasValue && _completedPlacement.HasValue)
			{
				var gain = (int?)(endingMmr.Value - _completedStartingMmr.Value);
				_completedAverageOpponentMmr = EstimateAverageOpponentMmr(
					_completedStartingMmr, gain, _completedPlacement);
				_completedAverageOpponentMmrResolved = true;
			}
			else if(DateTime.Now >= _completedRatingCheckDeadline)
			{
				_completedAverageOpponentMmrResolved = true;
			}
			else
				return;

			_overlay?.UpdateAverageOpponentMmr(_completedAverageOpponentMmr, false);
		}

		private void RefreshCompletedPlacement(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			if(_completedPlacementIsShared)
				return;

			var placement = game?.CurrentGameStats?.BattlegroundsDetails?.FinalPlacement;
			if(!placement.HasValue || placement.Value < 1 || placement.Value > 8)
				return;

			if(_completedPlacement == placement.Value)
				return;

			_completedPlacement = placement.Value;
			_overlay?.UpdateFinalPlacement(_completedPlacement);
			if(!_completedIsSpectator && _completedStartingMmr.HasValue)
				_completedAverageOpponentMmrResolved = false;
		}

		private void CaptureRecruitSnapshot(
			Hearthstone_Deck_Tracker.Hearthstone.GameV2 game,
			int turn)
		{
			if(_game == null || game.Player == null || game.Opponent == null || turn <= 0)
				return;

			try
			{
				var snapshot = _game.RecruitSnapshots.FirstOrDefault(x => x.Turn == turn);
				if(snapshot == null)
				{
					var playerMinions = game.Player.Board.Where(e => e.IsMinion).ToList();
					var opponentMinions = game.Opponent.Board.Where(e => e.IsMinion).ToList();
					var opponentHero = game.Opponent.Hero;
					var opponentTier = opponentHero?.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL) ?? 0;
					snapshot = new RecruitSnapshotLog
					{
						Turn = turn,
						Time = DateTime.Now,
						PlayerTier = game.Player.Hero?.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL) ?? 0,
						PlayerMinionCount = playerMinions.Count,
						PlayerTotalAttack = playerMinions.Sum(e => e.Attack),
						PlayerTotalHealth = playerMinions.Sum(e => e.Health),
						PlayerTribes = playerMinions.SelectMany(GetMinionTribes).ToList(),
						OpponentHeroEntityId = opponentHero?.Id,
						OpponentTier = opponentTier > 0 ? (int?)opponentTier : null,
						OpponentMinionCount = opponentMinions.Count,
						OpponentTotalAttack = opponentMinions.Sum(e => e.Attack),
						OpponentTotalHealth = opponentMinions.Sum(e => e.Health)
					};
					snapshot.PlayerComposition = DetermineComposition(snapshot.PlayerTribes);
					_game.RecruitSnapshots.Add(snapshot);
				}
				ExportBoardStatsSnapshot(snapshot);
			}
			catch(Exception e)
			{
				_debug.Add("Recruit snapshot: " + e.GetType().Name + ": " + e.Message);
			}
		}

		private static IEnumerable<string> GetMinionTribes(Entity minion)
		{
			if(minion?.Card == null)
				return Enumerable.Empty<string>();
			return new[] { minion.Card.RaceEnum, minion.Card.SecondaryRaceEnum }
				.Where(x => x.HasValue)
				.Select(x => x.Value)
				.Select(GetBattlegroundTribeName)
				.Where(x => !string.IsNullOrEmpty(x))
				.Distinct();
		}

		private static string GetBattlegroundTribeName(HearthDb.Enums.Race race)
		{
			switch(race)
			{
				case HearthDb.Enums.Race.ALL: return "All";
				case HearthDb.Enums.Race.BEAST: return "Beasts";
				case HearthDb.Enums.Race.DEMON: return "Demons";
				case HearthDb.Enums.Race.DRAGON: return "Dragons";
				case HearthDb.Enums.Race.ELEMENTAL: return "Elementals";
				case HearthDb.Enums.Race.MECHANICAL: return "Mechs";
				case HearthDb.Enums.Race.MURLOC: return "Murlocs";
				case HearthDb.Enums.Race.NAGA: return "Naga";
				case HearthDb.Enums.Race.PIRATE: return "Pirates";
				case HearthDb.Enums.Race.QUILBOAR: return "Quilboar";
				case HearthDb.Enums.Race.TOTEM: return "Totems";
				case HearthDb.Enums.Race.UNDEAD: return "Undead";
				default: return null;
			}
		}

		private static string DetermineComposition(IEnumerable<string> tribes)
		{
			var qualifying = tribes
				.GroupBy(x => x)
				.Select(group => new { Tribe = group.Key, Count = group.Count() })
				.Where(x => x.Count >= 3)
				.OrderByDescending(x => x.Count)
				.ThenBy(x => x.Tribe, StringComparer.Ordinal)
				.ToList();
			if(qualifying.Count == 1)
				return qualifying[0].Tribe;
			if(qualifying.Count == 2)
				return qualifying[0].Tribe + "/" + qualifying[1].Tribe;
			return "Mixed";
		}

		private void ExportBoardStatsSnapshot(RecruitSnapshotLog snapshot)
		{
			if(_game.ExportedBoardStatsTurns.Contains(snapshot.Turn))
				return;
			try
			{
				var hasHeader = File.Exists(BoardStatsFile) && new FileInfo(BoardStatsFile).Length > 0;
				var row = new StringBuilder();
				if(!hasHeader)
					row.AppendLine("game_id,match_started_utc,turn,player_tier,minion_count,total_attack,total_health");
				row.Append(EscapeCsv(_game.GameId)).Append(',')
					.Append(EscapeCsv(_game.Started.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))).Append(',')
					.Append(snapshot.Turn.ToString(CultureInfo.InvariantCulture)).Append(',')
					.Append(snapshot.PlayerTier.ToString(CultureInfo.InvariantCulture)).Append(',')
					.Append(snapshot.PlayerMinionCount.ToString(CultureInfo.InvariantCulture)).Append(',')
					.Append(snapshot.PlayerTotalAttack.ToString(CultureInfo.InvariantCulture)).Append(',')
					.Append(snapshot.PlayerTotalHealth.ToString(CultureInfo.InvariantCulture))
					.AppendLine();
				File.AppendAllText(BoardStatsFile, row.ToString(), new UTF8Encoding(false));
				_game.ExportedBoardStatsTurns.Add(snapshot.Turn);
			}
			catch(Exception e)
			{
				if(_game.BoardStatsExportErrorsLogged.Add(snapshot.Turn))
					_debug.Add("Board stats CSV export (turn " + snapshot.Turn + "): "
						+ e.GetType().Name + ": " + e.Message);
			}
		}

		private static string EscapeCsv(string value) =>
			"\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

		private void CaptureDarkGiftObservations(Hearthstone_Deck_Tracker.Hearthstone.GameV2 game)
		{
			var playerId = game.Player?.Id;
			if(game.Entities == null || !playerId.HasValue)
				return;
			var entitiesById = game.Entities.Values.Where(e => e != null).ToDictionary(e => e.Id);
			foreach(var entity in entitiesById.Values)
			{
				var darkGift = entity.GetTag(HearthDb.Enums.GameTag.DARK_GIFT);
				var hasDarkGift = entity.GetTag(HearthDb.Enums.GameTag.HAS_DARK_GIFT);
				var giftEntity = entity.GetTag(HearthDb.Enums.GameTag.DARK_GIFT_ENTITY);
				if(darkGift == 0 && hasDarkGift == 0 && giftEntity == 0)
					continue;
				if(!entity.IsControlledBy(playerId.Value))
					continue;

				var observation = _game.DarkGiftObservations.FirstOrDefault(x => x.EntityId == entity.Id);
				if(observation == null)
				{
					observation = new DarkGiftObservation
					{
						EntityId = entity.Id,
						FirstSeenTurn = game.GetTurnNumber()
					};
					_game.DarkGiftObservations.Add(observation);
				}
				observation.LastSeenTurn = game.GetTurnNumber();
				observation.CardId = entity.CardId;
				observation.Name = entity.Card?.Name ?? entity.LocalizedName ?? entity.Name;
				observation.IsMinion = entity.IsMinion;
				observation.ZoneTag = entity.GetTag(HearthDb.Enums.GameTag.ZONE);
				observation.ZonePosition = entity.ZonePosition;
				observation.DarkGiftTag = darkGift;
				observation.HasDarkGiftTag = hasDarkGift;
				observation.DarkGiftEntityTag = giftEntity;
				Entity linkedGift;
				if(giftEntity > 0 && entitiesById.TryGetValue(giftEntity, out linkedGift))
				{
					observation.LinkedGiftCardId = linkedGift.CardId;
					observation.LinkedGiftName = linkedGift.Card?.Name ?? linkedGift.LocalizedName ?? linkedGift.Name;
					observation.LinkedGiftZoneTag = linkedGift.GetTag(HearthDb.Enums.GameTag.ZONE);
				}
			}
		}

		private void FinishCombat(object invoker, CombatLog c)
		{
			// --- actual result: public fields on the invoker, set as hero-vs-hero attacks happen ---
			var lastHero = Get(invoker, "LastAttackingHero") as Entity;
			var heroAtk = Convert.ToInt32(Get(invoker, "LastAttackingHeroAttack") ?? 0);
			if(lastHero == null)
			{
				c.Result = "Tie";
				c.ActualDamageTaken = 0;
			}
			else if(lastHero.IsControlledBy(Core.Game.Player.Id))
			{
				c.Result = "Win";
				c.ActualDamageTaken = 0;
			}
			else
			{
				c.Result = "Loss";
				c.RawDamageTaken = heroAtk;
				c.ActualDamageTaken = CapDamageTaken(heroAtk, c);
			}

			// --- simulator output ---
			var output = Get(invoker, "Output");
			if(output == null)
			{
				c.Note = "No Bob's Buddy output (error state, too few simulations or unsupported interaction)";
			}
			else
			{
				c.WinRate = ToDouble(Get(output, "winRate"));
				c.TieRate = ToDouble(Get(output, "tieRate"));
				c.LossRate = ToDouble(Get(output, "lossRate"));
				c.TheirDeathRate = ToDouble(Get(output, "theirDeathRate"));
				c.MyDeathRate = ToDouble(Get(output, "myDeathRate"));
				c.Simulations = Convert.ToInt32(Get(output, "simulationCount") ?? 0);

				var raw = Get(output, "damageResults");
				try
				{
					// ASSUMPTION (verify against your first logged game): one int per simulation,
					// positive = I deal damage, negative = I take damage, 0 = tie.
					var results = new List<int>();
					foreach(var o in (IEnumerable)raw)
					{
						var damage = Convert.ToInt32(o);
						results.Add(damage < 0 ? CapDamageTaken(-damage, c) : 0);
					}
					ApplyDistribution(c, results);
				}
				catch(Exception e)
				{
					c.Note = "damageResults not an int sequence: " + (raw?.GetType().FullName ?? "null") + " / " + e.Message;
				}
			}
			_game.Combats.Add(c);
			Save();
		}

		/// <summary>Converts simulated outcomes to damage taken, counting wins and ties as zero.</summary>
		private static void ApplyDistribution(CombatLog c, List<int> results)
		{
			if(results.Count == 0)
				return;
			var n = results.Count;
			c.ExpectedDamageTaken = Math.Round(results.Average(), 2);
			c.Distribution = results.GroupBy(r => r).OrderBy(g => g.Key)
				.Select(g => new Bucket { DamageTaken = g.Key, Probability = Math.Round(g.Count() / (double)n, 4) }).ToList();
		}

		private static int CapDamageTaken(int damage, CombatLog combat)
		{
			var effectiveHealth = combat.PlayerHealth + combat.PlayerArmor;
			return effectiveHealth > 0 ? Math.Min(damage, effectiveHealth) : damage;
		}

		private void Save()
		{
			if(_game == null
				|| (_game.Combats.Count == 0
					&& _game.PlayerTierHistories.Count == 0
					&& _game.RecruitSnapshots.Count == 0
					&& _game.DarkGiftObservations.Count == 0))
				return;
			try
			{
				foreach(var snapshot in _game.RecruitSnapshots)
					ExportBoardStatsSnapshot(snapshot);
				UpdateTotals(_game, GetCurrentEffectiveHealth());
				_game.Debug = _debug.ToList();
				var file = Path.Combine(LogDir, _game.Started.ToString("yyyyMMdd_HHmmss") + "_" + _game.GameId + ".json");
				var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
				File.WriteAllText(file, json.Serialize(_game));
			}
			catch(Exception e) { _debug.Add("Save: " + e.Message); }
		}

		private static int? GetCurrentEffectiveHealth()
		{
			var hero = Core.Game?.Player?.Hero;
			return hero == null ? (int?)null : hero.Health + hero.GetTag(HearthDb.Enums.GameTag.ARMOR);
		}

		private static void UpdateTotals(GameLog game, int? currentEffectiveHealth = null)
		{
			var scoredCombats = game.Combats.Where(x => x.ExpectedDamageTaken.HasValue).ToList();
			game.ScoredCombats = scoredCombats.Count;
			game.TotalActualDamageTaken = game.Combats.Sum(x => x.ActualDamageTaken);
			game.ScoredActualDamageTaken = scoredCombats.Sum(x => x.ActualDamageTaken);
			game.TotalExpectedDamageTaken = Math.Round(scoredCombats.Sum(x => x.ExpectedDamageTaken.Value), 2);
			game.DamageTakenRatio = Math.Round(
				(game.ScoredActualDamageTaken + 1.0) / (game.TotalExpectedDamageTaken + 1.0), 4);
			if(currentEffectiveHealth.HasValue)
				game.ExpectedHealthAtEnd = Math.Round(
					currentEffectiveHealth.Value + game.TotalActualDamageTaken - game.TotalExpectedDamageTaken, 2);
			else if(!game.ExpectedHealthAtEnd.HasValue && game.InitialEffectiveHealth.HasValue)
				game.ExpectedHealthAtEnd = Math.Round(
					game.InitialEffectiveHealth.Value - game.TotalExpectedDamageTaken, 2);
			game.HealthPercentile = CalculateHealthPercentile(
				currentEffectiveHealth, game.ExpectedHealthAtEnd, scoredCombats);
		}

		private static double? CalculateHealthPercentile(
			int? actualHealth,
			double? expectedHealth,
			IEnumerable<CombatLog> combats)
		{
			if(!actualHealth.HasValue || !expectedHealth.HasValue)
				return null;

			var totalVariance = 0.0;
			var hasDistribution = false;
			foreach(var combat in combats)
			{
				if(combat.Distribution == null || combat.Distribution.Count == 0)
					continue;
				var probabilityTotal = combat.Distribution.Sum(x => Math.Max(0, x.Probability));
				if(probabilityTotal <= 0)
					continue;
				var mean = combat.Distribution.Sum(
					x => x.DamageTaken * Math.Max(0, x.Probability)) / probabilityTotal;
				totalVariance += combat.Distribution.Sum(x =>
				{
					var probability = Math.Max(0, x.Probability) / probabilityTotal;
					var difference = x.DamageTaken - mean;
					return probability * difference * difference;
				});
				hasDistribution = true;
			}

			if(!hasDistribution)
				return null;
			if(totalVariance <= 0)
				return Math.Abs(actualHealth.Value - expectedHealth.Value) < 0.005
					? 50.0
					: actualHealth.Value > expectedHealth.Value ? 100.0 : 0.0;

			var z = (actualHealth.Value - expectedHealth.Value) / Math.Sqrt(totalVariance);
			z = Math.Max(-20, Math.Min(20, z));
			return Math.Round(100.0 / (1.0 + Math.Exp(-1.702 * z)), 1);
		}

		private static IList<TierChartSeries> BuildTierChartSeries(GameLog game)
		{
			return game.PlayerTierHistories
				.Where(x => x.IsLocalPlayer || x.PlayerId > 0 || x.TierUps.Count > 0)
				.GroupBy(x => x.IsLocalPlayer
					? "local"
					: x.PlayerId > 0 ? "player:" + x.PlayerId : "entity:" + x.HeroEntityId)
				.Select(group =>
				{
					var representative = group
						.OrderByDescending(x => x.TierUps.Count)
						.ThenByDescending(x => x.IsLocalPlayer)
						.ThenByDescending(x => x.CurrentTier)
						.First();
					var isLocalPlayer = group.Any(x => x.IsLocalPlayer);
					var tierUps = isLocalPlayer && game.RecruitSnapshots.Any(x => x.PlayerTier > 0)
						? game.RecruitSnapshots
							.Where(x => x.Turn > 0 && x.PlayerTier > 0)
							.Select(x => new TierChartPoint { Tier = x.PlayerTier, Turn = x.Turn })
							.OrderBy(x => x.Turn)
							.ToList()
						: representative.TierUps
							.Where(x => x.Turn > 0 && x.Tier > 1)
							.Select(x => new TierChartPoint { Tier = x.Tier, Turn = x.Turn })
							.OrderBy(x => x.Turn)
							.ToList();
					return new TierChartSeries
					{
						IsLocalPlayer = isLocalPlayer,
						TierUps = tierUps
					};
				})
				.OrderByDescending(x => x.IsLocalPlayer)
				.ThenBy(x => x.TierUps.Count == 0 ? int.MaxValue : x.TierUps[0].Turn)
				.ToList();
		}

		private static IList<BoardStatsTurn> BuildBoardStatsTurns(GameLog game)
		{
			var combatResults = game.Combats
				.GroupBy(x => x.Turn)
				.ToDictionary(
					group => group.Key,
					group => group.OrderByDescending(x => x.Time).First().Result);
			return game.RecruitSnapshots.OrderBy(x => x.Turn).Select(x => new BoardStatsTurn
			{
				Turn = x.Turn,
				PlayerTier = x.PlayerTier,
				PlayerAttack = x.PlayerTotalAttack,
				PlayerHealth = x.PlayerTotalHealth,
				OpponentTier = x.OpponentTier,
				OpponentAttack = x.OpponentTotalAttack,
				OpponentHealth = x.OpponentTotalHealth,
				Result = combatResults.TryGetValue(x.Turn, out var result) ? result : null
			}).ToList();
		}

		private static Tuple<int, string, double> GetUnlikeliestCombat(GameLog game, string result)
		{
			return game.Combats
				.Where(x => x.Result == result && x.Simulations > 0)
				.OrderBy(x => result == "Loss" ? x.LossRate : x.WinRate)
				.ThenBy(x => x.Turn)
				.Select(x => Tuple.Create(
					x.Turn,
					x.Result,
					result == "Loss" ? x.LossRate : x.WinRate))
				.FirstOrDefault();
		}

		// ---------- reflection helpers ----------
		private static string StateOf(object invoker) => Get(invoker, "State")?.ToString() ?? "";

		private static object Get(object obj, string name)
		{
			if(obj == null) return null;
			var t = obj.GetType();
			const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			var p = t.GetProperty(name, f);
			if(p != null) return p.GetValue(obj);
			var fi = t.GetField(name, f);
			return fi?.GetValue(obj);
		}

		private static double ToDouble(object o) => o == null ? 0 : Math.Round(Convert.ToDouble(o), 4);

		private static string FormatDamageTaken(int damage) =>
			damage == 0 ? "0" : (-(long)damage).ToString();

		private static string FormatDamageTaken(double damage) =>
			damage == 0 ? "0" : (-damage).ToString("0.##");

		// ---------- log model ----------
		private class GameLog
		{
			public string GameId;
			public DateTime Started;
			public int? InitialEffectiveHealth;
			public int ScoredCombats;
			public int TotalActualDamageTaken;
			public int ScoredActualDamageTaken;
			public double TotalExpectedDamageTaken;
			public double? DamageTakenRatio;
			public double? ExpectedHealthAtEnd;
			public double? HealthPercentile;
			public bool IsSpectator;
			public int? StartingBattlegroundsMmr;
			public int PlacementCaptureBatch;
			[ScriptIgnore] public int? PlacementCombatStartTurn;
			[ScriptIgnore] public HashSet<int> AlivePlayersAtCombatStart = new HashSet<int>();
			[ScriptIgnore] public HashSet<int> SimultaneousEliminationTurns = new HashSet<int>();
			[ScriptIgnore] public Dictionary<int, int> AlivePlayerCountsAtCombatStart = new Dictionary<int, int>();
			[ScriptIgnore] public Dictionary<int, int> AlivePlayerCountsAfterCombat = new Dictionary<int, int>();
			public List<CombatLog> Combats = new List<CombatLog>();
			public List<PlayerTierHistory> PlayerTierHistories = new List<PlayerTierHistory>();
			public List<PlayerPlacementLog> PlacementObservations = new List<PlayerPlacementLog>();
			public List<RecruitSnapshotLog> RecruitSnapshots = new List<RecruitSnapshotLog>();
			public List<DarkGiftObservation> DarkGiftObservations = new List<DarkGiftObservation>();
			public List<string> Debug;
			[ScriptIgnore] public HashSet<int> ExportedBoardStatsTurns = new HashSet<int>();
			[ScriptIgnore] public HashSet<int> BoardStatsExportErrorsLogged = new HashSet<int>();
			[ScriptIgnore] public HashSet<object> SeenInvokers = new HashSet<object>();
		}

		private class PlayerTierHistory
		{
			public int HeroEntityId;
			public string HeroCardId;
			public int ControllerId;
			public int PlayerId;
			public bool IsLocalPlayer;
			public int CurrentTier;
			public List<TierUpLog> TierUps = new List<TierUpLog>();
		}

		private class TierUpLog
		{
			public int Tier;
			public int Turn;
		}

		private class PlayerPlacementLog
		{
			public int ControllerId;
			public int HeroEntityId;
			public int PlayState;
			public int Place;
			public int Turn;
			public int Batch;
			public bool IsElimination;
		}

		private class RecruitSnapshotLog
		{
			public int Turn;
			public DateTime Time;
			public int PlayerTier;
			public int PlayerMinionCount;
			public int PlayerTotalAttack;
			public int PlayerTotalHealth;
			public List<string> PlayerTribes = new List<string>();
			public string PlayerComposition;
			public int? OpponentHeroEntityId;
			public int? OpponentTier;
			public int OpponentMinionCount;
			public int OpponentTotalAttack;
			public int OpponentTotalHealth;
		}

		private class DarkGiftObservation
		{
			public int FirstSeenTurn;
			public int LastSeenTurn;
			public int EntityId;
			public string CardId;
			public string Name;
			public bool IsMinion;
			public int ZoneTag;
			public int ZonePosition;
			public int DarkGiftTag;
			public int HasDarkGiftTag;
			public int DarkGiftEntityTag;
			public string LinkedGiftCardId;
			public string LinkedGiftName;
			public int LinkedGiftZoneTag;
		}

		private class CombatLog
		{
			public int Turn;
			public DateTime Time;
			public int PlayerHealth, PlayerArmor, PlayerTier;
			public string OpponentHero;
			public int? OpponentHeroEntityId, OpponentTier;
			public int OpponentHealth, OpponentArmor;
			public List<string> PlayerBoard, OpponentBoard;
			public string Result;
			public int ActualDamageTaken;
			public int RawDamageTaken;
			public double WinRate, TieRate, LossRate, TheirDeathRate, MyDeathRate;
			public int Simulations;
			public double? ExpectedDamageTaken;
			public List<Bucket> Distribution;
			public string Note;
		}

		private class Bucket
		{
			public int DamageTaken;
			public double Probability;
		}

	}
}
