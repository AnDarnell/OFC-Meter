using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Core = Hearthstone_Deck_Tracker.API.Core;

namespace BgLuck
{
	/// <summary>
	/// Logs, for every Battlegrounds combat: Bob's Buddy odds + damage distribution, the actual result,
	/// and how lucky/unlucky the outcome was. One JSON file per game.
	/// </summary>
	public class BgLuckPlugin : IPlugin
	{
		public string Name => "BG Luck";
		public string Description => "Logs Bob's Buddy odds and damage distribution per combat and how lucky each fight was.";
		public string ButtonText => "Open log folder";
		public string Author => "Anton";
		public Version Version => new Version(0, 1, 0);
		public MenuItem MenuItem => null;

		private static readonly string LogDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "BgLuck");

		// --- reflection handles into HDT internals (BobsBuddyInvoker is internal) ---
		private static Type _invokerType;
		private static MethodInfo _getInstance;

		private GameLog _game;            // null when no BG game is being tracked
		private object _combatInvoker;    // the BobsBuddyInvoker instance of the combat in progress
		private CombatLog _combat;        // snapshot taken when combat starts
		private LuckOverlayControl _overlay;
		private bool _showingFinalSummary;
		private bool _newGamePending;
		private string _completedGameId;
		private DateTime _lastPoll = DateTime.MinValue;
		private readonly List<string> _debug = new List<string>();

		public void OnLoad()
		{
			Directory.CreateDirectory(LogDir);
			_invokerType = typeof(Core).Assembly.GetType("Hearthstone_Deck_Tracker.BobsBuddy.BobsBuddyInvoker");
			_getInstance = _invokerType?.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
			_overlay = new LuckOverlayControl();
			Core.OverlayCanvas.Children.Add(_overlay);
			OverlayExtensions.SetIsOverlayHitTestVisible(_overlay, true);
			Canvas.SetLeft(_overlay, 24);
			Canvas.SetTop(_overlay, 24);
			GameEvents.OnGameStart.Add(OnGameStart);
			GameEvents.OnGameEnd.Add(OnGameEnd);
		}

		public void OnUnload()
		{
			Save();
			if(_overlay != null)
				Core.OverlayCanvas.Children.Remove(_overlay);
			_overlay = null;
		}

		public void OnButtonPress() { System.Diagnostics.Process.Start(LogDir); }

		private void OnGameStart()
		{
			if(_showingFinalSummary)
			{
				_newGamePending = true;
				return;
			}
			_game = null;
			_combatInvoker = null;
			_combat = null;
			_debug.Clear();
			_overlay?.ResetForNewGame();
		}

		private void OnGameEnd()
		{
			if(_combatInvoker != null && _combat != null && _game != null)
			{
				try { FinishCombat(_combatInvoker, _combat); }
				catch(Exception e) { _debug.Add("Final combat: " + e.GetType().Name + ": " + e.Message); }
			}
			if(_game != null)
			{
				var playerHero = Core.Game?.Player?.Hero;
				int? actualHealth = playerHero == null
					? (int?)null
					: playerHero.Health + playerHero.GetTag(HearthDb.Enums.GameTag.ARMOR);
				UpdateTotals(_game, actualHealth);
				Save();
				var turnSummaries = _game.Combats.OrderBy(c => c.Turn)
					.Select(c => "T" + c.Turn + ": " + FormatDamageTaken(c.ActualDamageTaken)
						+ " (exp " + (c.ExpectedDamageTaken.HasValue
							? FormatDamageTaken(c.ExpectedDamageTaken.Value)
							: "N/A") + ")")
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
					turnSummaries,
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
				var isBattlegrounds = IsBattlegroundsOverlayVisible()
					|| game != null && (game.IsBattlegroundsMatch
						|| game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Battlegrounds);
				if(!isBattlegrounds)
				{
					if(!_showingFinalSummary)
						_overlay?.HideOverlay();
					return;
				}

				if(game == null)
				{
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}

				if(game.IsInMenu && game.IsRunning)
				{
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}

				if(!game.IsRunning)
				{
					if(!_showingFinalSummary)
						_overlay?.ShowLobby();
					return;
				}
				if(_getInstance == null)
				{
					_overlay?.HideOverlay();
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
					_overlay?.ResetForNewGame();
				}
				if(_game == null)
					_game = new GameLog
					{
						GameId = currentGameId,
						Started = DateTime.Now,
						IsSpectator = game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Spectator
					};
				else
					_game.IsSpectator |= game.CurrentGameMode == Hearthstone_Deck_Tracker.Enums.GameMode.Spectator;

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
					_game.IsSpectator);
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

		private CombatLog SnapshotCombatStart(int turn)
		{
			var c = new CombatLog { Turn = turn, Time = DateTime.Now };
			try
			{
				var me = Core.Game.Player.Hero;
				var opp = Core.Game.Opponent.Hero;
				c.PlayerHealth = me?.Health ?? 0;
				c.PlayerArmor = me?.GetTag(HearthDb.Enums.GameTag.ARMOR) ?? 0;
				if(_game != null && !_game.InitialEffectiveHealth.HasValue)
					_game.InitialEffectiveHealth = c.PlayerHealth + c.PlayerArmor;
				c.PlayerTier = me?.GetTag(HearthDb.Enums.GameTag.PLAYER_TECH_LEVEL) ?? 0;
				c.OpponentHero = opp?.Card?.Name ?? opp?.CardId;
				c.OpponentHealth = opp?.Health ?? 0;
				c.OpponentArmor = opp?.GetTag(HearthDb.Enums.GameTag.ARMOR) ?? 0;
				c.PlayerBoard = Core.Game.Player.Board.Where(e => e.IsMinion)
					.OrderBy(e => e.ZonePosition).Select(e => $"{e.Card?.Name} {e.Attack}/{e.Health}").ToList();
				c.OpponentBoard = Core.Game.Opponent.Board.Where(e => e.IsMinion)
					.OrderBy(e => e.ZonePosition).Select(e => $"{e.Card?.Name} {e.Attack}/{e.Health}").ToList();
			}
			catch(Exception e) { _debug.Add("Snapshot: " + e.Message); }
			return c;
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
				c.ActualDamageTaken = heroAtk;
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
						results.Add(damage < 0 ? -damage : 0);
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

		private void Save()
		{
			if(_game == null || _game.Combats.Count == 0)
				return;
			try
			{
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
			game.DamageTakenRatio = game.TotalExpectedDamageTaken > 0
				? Math.Round(game.ScoredActualDamageTaken / game.TotalExpectedDamageTaken, 4)
				: (double?)null;
			game.ExpectedHealthAtEnd = currentEffectiveHealth.HasValue
				? Math.Round(currentEffectiveHealth.Value + game.TotalActualDamageTaken - game.TotalExpectedDamageTaken, 2)
				: game.InitialEffectiveHealth.HasValue
					? Math.Round(game.InitialEffectiveHealth.Value - game.TotalExpectedDamageTaken, 2)
					: (double?)null;
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
			public bool IsSpectator;
			public List<CombatLog> Combats = new List<CombatLog>();
			public List<string> Debug;
			[ScriptIgnore] public HashSet<object> SeenInvokers = new HashSet<object>();
		}

		private class CombatLog
		{
			public int Turn;
			public DateTime Time;
			public int PlayerHealth, PlayerArmor, PlayerTier;
			public string OpponentHero;
			public int OpponentHealth, OpponentArmor;
			public List<string> PlayerBoard, OpponentBoard;
			public string Result;
			public int ActualDamageTaken;
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
