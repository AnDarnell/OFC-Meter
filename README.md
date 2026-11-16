# BG Luck

An HDT plugin that compares actual Battlegrounds damage taken with Bob's Buddy's expected damage.

## Install
1. Download `BgLuck.dll` from the latest GitHub release.
2. In HDT, open **Options > Plugins > Plugins folder** and place `BgLuck.dll` inside.
4. Restart HDT and enable **BG Luck** under **Options > Plugins**.

## Overlay
During a game, the compact overlay shows the accumulated **Damage taken ratio**, calculated once from match totals
as (scored actual damage + 1) divided by (expected damage + 1), and expected health with its difference from current
health. The +1 baseline means the ratio starts at 1.00 before any scored combat is available. A ratio of 1.00 is
expected; below 1.00 means less damage than expected. Hover to expand the panel with
the health percentile when available, current health, and damage details; it collapses 200 ms after the pointer leaves.
The percentile is a coarse approximation based on the combined variance of the combat damage distributions;
it does not replay alternate paths through the game. 50% is approximately expected health, while 15% means
less health than about 85% of modeled outcomes.
When `GameTag.BACON_ODD_PLAYER_OUT` is set, the hover details show the matching player ID and the hero name
for player ID 1 as **Ghost Prio**. A **G** marks the odd player's portrait and a crown marks player ID 1.
Both markers scale and reposition with the HDT game overlay, including windowed mode.

After a game, the recap includes a tier-progression chart, the faced opponent's tier by turn, recruit-phase
attack/health totals, and actual versus expected damage by turn. The recap reports the most unlikely loss and the
most unlikely win according to Bob's Buddy's simulated odds. If you concede during the final tracked turn, that
fight is counted as a loss with lethal damage capped at your health plus armor at its start. The DT row shows actual
damage across all fights; the damage taken ratio compares scored actual damage with expected damage. The tooltip
shows both totals. The **Stats by turns** and
**Turns: N Recap** sections are collapsible inside the scrollable recap list. The chart shows your tier progression
and a diagonal lobby-average tier line with fractional values shown between whole-tier lines; faced-opponent markers
are shown on turns with recorded opponent data. Damage is capped at
your health plus armor at the start of each fight, so lethal
overkill does not inflate the score; the final Health display can still show a negative value for lethal overkill.
Each faced-opponent tier marker has a subtle green/yellow/red outline for your win/tie/loss result.
The recap's final comp is classified from the last recruit-phase board: a tribe counts at three minions, two
qualifying tribes are listed together, and zero or more than two qualifying tribes are labeled **Mixed**. The
final placement normally uses HDT's result. If the combat-start and post-combat alive-player counts confirm
multiple eliminations in the same round, the recap computes the median placement directly from those counts.
**Avg opp**
estimates average opponent MMR from the player's pre-match MMR, rating change, and final placement; it appears
once HDT provides the post-match rating.

Dark Gift observations include linked entity metadata when HDT exposes it, but are not yet treated as confirmed
Gift selections because the available tags do not reliably identify the chosen Gift and minion.

## Logs
Game logs are saved as JSON in `%AppData%\HearthstoneDeckTracker\BgLuck`. Own recruit-phase board totals are
also appended to `board-stats.csv` in the same folder, one row per match and turn, with player tier, minion
count, total attack, and total health. This is a separate dataset for later board-strength analysis.
The JSON logs also include observed tavern-tier history and raw Dark Gift tag observations when available from
HDT's game data.

## Build from source
Requires the .NET Framework 4.7.2 targeting pack and an HDT installation:

```powershell
dotnet build -c Release -p:HdtDir="C:\path\to\HDT\app-version"
```
