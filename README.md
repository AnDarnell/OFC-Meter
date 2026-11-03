# BG Luck

An HDT plugin that compares actual Battlegrounds damage taken with Bob's Buddy's expected damage.

## Install
1. Download `BgLuck.dll` from the latest GitHub release.
2. In HDT, open **Options > Plugins > Plugins folder** and place `BgLuck.dll` inside.
4. Restart HDT and enable **BG Luck** under **Options > Plugins**.

## Overlay
The overlay shows damage taken, expected damage, health, and a color-coded Luck score. After a game, expand
**Recap** (arrow) to see actual and expected damage by turn. Damage is capped at your health plus armor at the start
of each fight, so lethal overkill does not inflate the score.

## Logs
Game logs are saved as JSON in `%AppData%\HearthstoneDeckTracker\BgLuck`.

## Build from source
Requires the .NET Framework 4.7.2 targeting pack and an HDT installation:

```powershell
dotnet build -c Release -p:HdtDir="C:\path\to\HDT\app-version"
```
