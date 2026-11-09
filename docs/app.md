# FM27 Scout app (WPF)

`FM27Scout.exe` is one self-contained file; .NET doesn't need to be installed. It is **read-only**: it never writes to FM's memory and never injects anything.

## Using it
1. Start FM27 and load your save.
2. Run `FM27Scout.exe` and press **Load from game**. Loading takes about 6 s for about 140k players. Press it again any time to refresh; nothing updates live.
3. Only men's players are loaded. Players with the game's female flag, or at a women's club or competition, are excluded.

### Players tab
* **Grid:** virtualised, so it stays fast with 140k rows. Click any column header to sort; the first click on a number column sorts high to low. You can reorder columns by dragging.
* **Columns ▾:** pick columns by group (Basics, Roles, Club & contract, Technical, Mental, Physical, Goalkeeping, Hidden, Feet, Role scores). Visibility and order are remembered.
* **Sidebar:** collapsible sections.
  * Basics: name, age, CA, PA, nationality, height.
  * Position & role: positions, minimum rating, a role with minimum current and potential score.
  * Club & contract: club, league, max value, max wage, contract expiry, free agents.
  * Attributes: a minimum per attribute, hidden ones included.
* **Query bar** (Enter to apply). Terms are combined with AND:
  ```
  age<=21 pa>=150 pos:ST league:"Premier League" value<=30m wage<80k
  finishing>=15 pace>=14            any attribute, 1-20
  poa>=70  pot.bpcb>=80  best>=75   role scores (role code), potential role scores, best role
  pos:D,DM  r.st>=10                position groups / raw position rating
  club:arsenal nation:brazil foot:left contract<=2037  gk  free  haaland
  ```
* **Saved searches:** save the whole state (sidebar and query) under a name and pick it from the drop-down.
* **Profile panel:** FM-style Technical (or Goalkeeping), Mental and Physical columns, hidden attributes, best roles with score and potential, position ratings, and the contract.
* It shows the **true** values even where the game only shows scouting ranges.

### Clubs tab
Squads built from the loaded players: player count, average and best CA, average PA, average age, squad value, wage bill and best player. Double-click a club to list its players.

### Staff tab
Not done yet. Staff use a separate person class in the game's database, and their coaching attributes still need mapping.

## Role scores
* `score = Σ(weight × attribute) / Σ(weight) × 5`, which gives 0-100. It is then multiplied by a familiarity factor taken from the player's best rating among the role's positions.
* Weights use tiers: Key ×3, Important ×2, Useful ×1. The weights are our own.
* **Potential:** each attribute grows towards 20 by `(PA−CA)/(200−CA) × ageFactor`. The age factor drops with age and reaches 0 from 30.
* Everything lives in `%APPDATA%\FM27Scout\roles.json`. **Roles…** opens it. Edit it, then press Load from game to recalculate. Delete the file to get the defaults back.

## Patching for a new FM build
On an unknown engine build, the app says so and doesn't load. Put an updated `fm27-layout.json` next to `FM27Scout.exe` and it is used in place of the built-in one, with no rebuild needed. The probe's exploration commands help re-derive the offsets (see README).

## Files
| | |
|---|---|
| `%APPDATA%\FM27Scout\roles.json` | role weights |
| `%APPDATA%\FM27Scout\saved-searches.json` | saved searches |
| `%APPDATA%\FM27Scout\settings.json` | visible columns and their order |
| `fm27-layout.json` next to the exe | optional layout override |

## Build
```
dotnet publish src/FM27Scout -c Release -o publish      # -> publish\FM27Scout.exe
publish\FM27Scout.exe --selftest <dir>                  # headless check: loads, runs queries, renders selftest-players.png
```
