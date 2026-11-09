# FM27 Scout app (WPF)

`FM27Scout.exe` is one self-contained file; .NET doesn't need to be installed. It is **read-only**: it never writes to FM's memory and never injects anything.

## Using it
1. Start FM27 and load your save.
2. Run `FM27Scout.exe` and press **Load from game**. Loading takes about 6 s for about 140k players. Press it again any time to refresh; nothing updates live.
3. Only men's players are loaded. Players with the game's female flag, or at a women's club or competition, are excluded.

### Players tab
* **Grid:** virtualised, so it stays fast with 140k rows. Click any column header to sort; the first click on a number column sorts high to low. You can reorder columns by dragging.
* **Right-click any column header** to hide that column, show or hide others by group, or apply a preset. This works in every grid.
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
* **Profile panel** (opens on **double-click**; close it with ✕ or Esc, and the grid then uses the full width): FM-style Technical (or Goalkeeping), Mental and Physical columns, hidden attributes, best roles with score and potential, position ratings, and the contract.
* It shows the **true** values even where the game only shows scouting ranges.

### Would he join us? (estimate)
* **My club** is detected automatically from the game. The human manager has his own person class (`humanManagerVtableRva`), and his contract leads to his team and club. You can pick another club from the drop-down.
* The **Join %** and **Would join?** columns show Likely (70+), Possible (50+), Unlikely (30+) or Very unlikely.
* The sidebar has a **Gettable for us** checkbox, and you can query with `join>=60` or `willing:likely`.
* The profile lists every factor that applied, with its points:
  * the player's reputation vs your club's (softened for older players)
  * stepping down from his current club
  * free agent, or contract ending within 6 or 12 months
  * squad status (fringe players are easier)
  * wage vs your top earner, and value vs your squad value (a stand-in until club finances are read)
* All the weights are in `%APPDATA%\FM27Scout\join.json`.
* **Not included yet:** ambition and loyalty (the personality order isn't mapped), favourite and disliked clubs and people, real transfer and wage budgets, and work permits. FM decides for real only when you approach a player, so treat this as a guide.

### Newgens and bargains
* **Newgen** column, and the `newgen`, `regen` and `real` query terms. The game gives everyone it creates during the save (youth intakes, plus filler players generated at the start) UIDs from a new block, above the real database's IDs. The app finds that gap automatically: in BRR the real database ends at about 2,000.75M and generated people start at 2,002.04M. Every player born 2011 or later falls in the generated block.
* **Joined:** the start date of the current contract. For youth-intake kids this is the intake date, so `regen age<=17 joined>=2037` lists the latest intake.
* **Bargain:**
  * a free agent, or
  * a contract ending within 6 months, or
  * valued at no more than half the median for players of similar CA (5-point band) and age.
* **Value vs typical** is his value divided by that median.
* **Value for money** is his best potential role score per £1M of fee plus 2 years' wages. There is no fee for free agents or contracts ending within 6 months, and free agents are assumed to want a typical wage for their CA.
* **Built-in searches** (★ in the saved-search drop-down): wonderkid newgens, best young newgens we could get, latest youth intakes, bargains we could get, undervalued high potential, value for money we could get, free agents, and contracts ending.
* **Next youth intake:** this is not in memory beforehand. Nobody aged 14 or under exists, and every 15-year-old already has a club, so FM creates intake players on the day.

### Squad tab
* Pick any club (it defaults to yours). By default it shows the first team, which is the club's team with the highest average CA. Tick **Include reserves & youth** to add the other teams.
* Columns: team, age, position, CA, PA, best role with score and potential, squad status, wage, expiry and value.
* Right-click a player for these options: Add to / Remove from comparison, Add to shortlist, Open profile, and **Find upgrade ›** (pick one of his top 5 roles). Double-click opens the profile.

### Compare tab
* Shows up to 4 players side by side: key facts, Join %, every attribute (hidden ones included), and the top role scores now and at potential.
* The best value in each row is highlighted. For Dirtiness and Injury Proneness, lower counts as best.

### Find upgrade
A player only appears if **all** of these are true:
* He would really improve the role, in one of two ways:
  * he scores **at least 3 more** in the chosen role now and has **higher CA**, or
  * he is **under 23**, scores **at least 5 more at potential**, is **no more than 3 below** now, and has **higher PA**.
* He would realistically join: **Join ≥ 50** (Possible or Likely) for your club. That estimate already includes the wage and fee affordability factors.
* He isn't already at your club.

Results open in the Players tab with a **Gain** column (e.g. "+6 now" or "+9 at potential"), sorted by Gain. The sidebar Role is set to that role, so the role score columns are shown, and the profile lists the Join reasons. **Reset** clears it.

### Shortlist
Add players with right-click › Add to shortlist. Filter with the sidebar's **Shortlist only** box or the query `shortlist`. It is saved in `%APPDATA%\FM27Scout\shortlist.json`.

### Multi-select
Players, Squad, Staff and the finder results support Ctrl+click (one at a time), Shift+click (a range) and Ctrl+A. Right-click › Add/Remove N to comparison (it asks before replacing when more than 4) and Add/Remove N to shortlist apply to every selected player. Double-click still opens one profile.

### Work permit (WP)
* **Nationalities come from FM's own data:** the person's relationship list at person+0x78. Entries with kind *nation* and relation 9 are extra nationalities, e.g. Musiala → England and Nigeria.
* **The rule follows the "My club" country:**
  * **No:** he holds a passport of your club's country.
  * **No:** in the UK, any British or Irish passport counts.
  * **No (EU):** an EU passport, when your club is in the EU.
  * **For English clubs otherwise:** an **estimate** of the GBE points system, from world reputation, current league strength, CA and a youth/high-potential concession. It reads *Needed – likely OK*, *Needed – borderline* or *Needed – unlikely*.
  * **Non-EU:** elsewhere outside the EU.
* FM decides the real permit when a transfer is agreed. The profile shows the passports and the reasoning.
* Query: `wp:needed`, `wp:none`, `wp:ok`, `wp:unlikely`.
* The Join estimate gets a penalty when a permit looks unlikely, and a bonus when your club is one of his **favoured clubs** (also from FM's relationship list).

### Clubs tab
Squads built from the loaded players: player count, average and best CA, average PA, average age, squad value, wage bill and best player. Double-click a club to list its players.

### Staff tab
* Lists every staff member at men's clubs (about 60k in total, 20k employed). It shows job, club, league or team, age, nation, personality, professionalism, ambition, loyalty, wage, contract expiry and previous club.
* You can search, and filter by job, personality and "employed by a club".
* Job names are known for Coach, Physio, Scout, Manager and Assistant Manager. Other jobs show as "Job N" until they are identified.
* **Staff coaching and knowledge attributes are not shown.** FM27 displays them as word bands that don't follow from any single stored value. I tested this on 8 staff from screenshots, including within-person ordering and every byte up to two pointer hops away, and nothing reproduced them. I'd rather show nothing than show made-up numbers.

### Personality and development
* The 8 hidden personality values are read in this order: Adaptability, Ambition, Loyalty, Pressure, Professionalism, Sportsmanship, Temperament, Controversy.
* The **Personality** label (Professional, Driven, Perfectionist, Fairly Loyal, Balanced, …) reproduces all 12 labels seen in the screenshots. Other labels use the same rules and are estimates.
* **Development** (Excellent, Good, Average, Poor) is based on Professionalism, Determination and Ambition. It also adjusts the potential estimate: poor trainers reach less of their PA, good ones slightly more.
* The Join estimate now includes **ambition** (ambitious players dislike stepping down) and **loyalty** (loyal players are harder to prise away).
* Query terms: `personality:professional`, `prof>=18`, `ambition>=15`, `loyalty<=8`, `outlook:excellent`.

### Moneyball
* **Career apps** and **career goals** come from the player's stats object. They match the in-game career totals of 5 players. **Goals/game** is worked out from those two.
* Query terms: `apps>=100`, `goals>=50`, `gpg>=0.4`. There are two built-in searches: proven scorers we could get, and young players with first-team experience.
* Season-by-competition stats (average rating, xG, assists) aren't decoded yet.

### Column presets and shortcuts
* **Columns ▾ › Presets:** Overview, Attributes, Goalkeeping, Moneyball, Contract & value, Hidden & personality.
* **Shortcuts:** F5 or Ctrl+L loads or refreshes; Ctrl+F jumps to the query bar; Ctrl+1–5 switches tabs; Esc in the query bar clears it.

### Patch Doctor (after an FM update)
1. When FM27 is patched, the engine build changes and **Load from game** says the build is new. It offers to run **Patch Doctor**.
2. Patch Doctor (read-only, a few minutes):
   * finds known real players by full name (Haaland, Yamal, Bellingham, Saka);
   * from them, finds the player class and the master person list, then the static pointer to that list;
   * checks the player block against stable facts (PA and height) and re-searches it if it moved;
   * shifts the other class and data addresses by the same amounts and validates each one.
3. The new build section is saved to `%APPDATA%\FM27Scout\fm27-layout.json`, and the app loads straight away.
4. Tested by simulating a patch (static data, vtables and player block all moved): it recovered every value exactly.
5. If a patch changes the internal structures themselves, Patch Doctor reports exactly which part failed.

## Role scores
* `score = Σ(weight × attribute) / Σ(weight) × 5`, which gives 0-100. It is then multiplied by a familiarity factor taken from the player's best rating among the role's positions.
* Weights use tiers: Key ×3, Important ×2, Useful ×1. The weights are our own.
* **Potential:** fitted from your save at every load. For each position group (GK, DC, FB, DM, MC, W, AMC, ST), the app fits a least-squares model of how many CA points each attribute is worth (R² 0.97–0.99 in BRR).
  * The expected CA gain is `(PA−CA) × ageFactor`. The age factor drops with age and reaches 0 from 30; edit it under `potentialAge`.
  * That gain is spread over the attributes that matter most for the player's position group.
  * Physical attributes develop less after 23.
  * The role scores are then recalculated on the projected attributes. This is an estimate: FM decides development during play.
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







