# BG Luck – HDT plugin (v0.1 skeleton)

Logs every Battlegrounds combat to a JSON file: Bob's Buddy odds and incoming damage distribution, the actual
result, and match-level actual versus expected damage taken.

## Build
1. Find your HDT folder (the one with `Hearthstone Deck Tracker.exe`; usually `%LocalAppData%\HearthstoneDeckTracker\app-1.x.x`).
2. `dotnet build -c Release -p:HdtDir="C:\path\to\that\folder"`
   (or edit `HdtDir` in `BgLuck.csproj`; open it in Visual Studio if you prefer).

## Install
1. In HDT: Options > Plugins > "Plugins folder" (it opens `%AppData%\HearthstoneDeckTracker\Plugins`).
2. Create a subfolder `BgLuck` and copy `bin\Release\net472\BgLuck.dll` into it.
3. Restart HDT, enable "BG Luck" under Options > Plugins. The plugin button opens the log folder.
4. Logs: `%AppData%\HearthstoneDeckTracker\BgLuck\<date>_<gameid>.json`. It is rewritten after each combat.

## Output per combat
- `Result`, `ActualDamageTaken`, start health/armor/tier, both boards, opponent hero.
- `WinRate/TieRate/LossRate/TheirDeathRate/MyDeathRate`, `Simulations`.
- `Distribution`: probability per amount of damage taken. `ExpectedDamageTaken` is its mean, counting dealt
  damage and ties as zero damage taken.
- Match totals include `ScoredCombats`, `TotalActualDamageTaken`, and `TotalExpectedDamageTaken`.
  `TotalActualDamageTaken` includes all recorded fights; expected totals and `DamageTakenRatio` use only fights
  with valid simulations. The ratio compares actual and expected damage within those scored fights (1 = as
  expected, above 1 = more damage than expected, below 1 = less).
  The ratio is null when total expected damage is zero.
- A movable overlay control is attached to HDT's overlay canvas, so it follows HDT's game-window visibility
  and disappears when Hearthstone is not active. The panel can be resized using the bottom-right grip; its
  default size is 190 × 100 pixels. During the match it shows accumulated `DT` (damage taken) and `Exp. DT`
  (expected damage taken), plus a small color-coded `Luck*` score on the top row. The asterisk indicates
  that the score is preliminary and includes completed fights only. Actual damage is green below expectation,
  gold at expectation, and red above expectation. The Luck score uses progressively lighter/darker shades:
  red for ratios above 1 (1-1.5, 1.5-2, above 2), gold at 1, and green below 1 (<1-0.75, 0.75-0.5, below 0.5).
  At match end it shows the totals, final luck-score ratio, health, and turn count directly in the panel.
  Expand the turn row to review actual and expected damage for each turn. Hovering the score explains its
  calculation and includes the actual/expected values.
  Expected health is adjusted from current effective health by adding back recorded actual combat damage and
  subtracting expected combat damage, so damage taken outside combat is reflected in the estimate.
  The final summary remains visible in the Battlegrounds lobby until a new match starts or the overlay is closed.
- Before the first game, the overlay is available on the Battlegrounds lobby page and shows a short ready
  message. Spectator matches are identified from HDT's current game mode and marked as `Spectating` in the
  overlay and saved game log.

## Things to verify with your first logged game
1. `damageResults` sign convention. The code assumes one int per simulation (+ I deal, - I take, 0 tie),
   and converts only negative values to damage taken.
   If `Note` says it is not an int sequence, or the sign is flipped, send me the note/JSON.
2. `Core.Game.GetTurnNumber()`, `Player.Hero`, `CurrentGameStats.GameId` exist in your HDT version (compile errors will tell).
3. `ActualDamageTaken` is the hero's attack at the last opponent-vs-hero hit (tavern tier + surviving minions),
   or zero if you won/tied. Bob's Buddy applies a damage cap, so compare against `Distribution` bounds if they differ.

## How it works (why reflection)
`BobsBuddyInvoker` is `internal`, but `GetInstance(gameId, turn, create)` and its members (`Output`, `State`,
`LastAttackingHero`, `LastAttackingHeroAttack`) are `public`. The plugin finds the invoker by reflection every ~400 ms,
notes when `State == Combat`, and reads the results when the state flips to `Shopping`/`GameOver`.
It never creates or changes HDT's instances. If a future HDT update renames these, the plugin logs the error into `Debug`.
