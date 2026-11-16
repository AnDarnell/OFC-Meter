# OFC-Meter

* An HDT-plugin that compares actual Battlegrounds damage taken with Bob's Buddy's expected damage.

* The numeric value is a comparison of expected damage taken compared to the actual amount of damage taken. A number **above 1.00** means the player has taken **more** than expected, and vice versa. This is also highlighted with color coding.

* The plugin also gives a recap after each game with relevant stats from the game.

* Latest version also includes a toggleable setting for who has ghost priority + current ghost, as well as an overlay for recently faced opponents that follows the BG ruleset.
These features can be toggled on/off in Settings > Plugins > OFC-Meter.

**Known issues:** The recap feature still has issues deciding on shared placements correctly. If two players are at the exact same health total the markers can be placed in the wrong spots sometimes. Working on this! 

## Install
1. Download `OFC-Meter.dll` from the latest GitHub release.
2. In HDT, open **Options > Plugins > Plugins folder** and place `OFC-Meter.dll` inside.
3. Restart HDT and enable **OFC-Meter** under **Options > Plugins**.

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
## Feedback
I'm very open to any feedback or questions. Discord: Brugdar, Twitch: Brugdar_, Battle-Tag: Darnell#21191
