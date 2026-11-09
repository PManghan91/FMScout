# FM27 Scout

**The app:** see [docs/app.md](docs/app.md). It is a WPF, single-file `FM27Scout.exe` with Players and Clubs tabs, role scores, filters, a query bar and saved searches.

The rest of this README documents the memory reader and how the layout was found.

## Reader probe (step 2)

A read-only proof that we can read player data out of a running **Football Manager 27** process.

* `src/FMScout.Reader`: class library. It opens the process with `PROCESS_VM_READ | PROCESS_QUERY_INFORMATION` only, so it can never write to the game. It provides `ReadProcessMemory`, `VirtualQueryEx` region enumeration, pattern and pointer scanning, and `FmGame`, which decodes players using the layout file.
* `src/FMScout.Probe`: console app.
  * `FMScout.Probe report [names...]` is the real probe. It uses only `fm27-layout.json` and does no scanning.
  * The other commands (`find`, `refsany`, `dump`, `follow`, `strings`, `who`, `fieldscan`, `attrcorr`, `arrays`, `dates`, …) are the scan-based exploration tools I used to work out the layout. They are slow: a full-memory scan takes about 55 s.
* `fm27-layout.json`: every offset, keyed by engine build. Nothing in the reader code is hardcoded.

I found everything from scratch against the running game: string search, then pointer back-references, then cross-checking statistics over all players. I did not inject anything, write to memory, decompile, or look at any third-party scouting tool.

## Build tested

| | |
|---|---|
| `fm.exe` | 6000.3.18.9883062. This is the Unity player, version `6000.3.18f1-custom` |
| `game_plugin.dll` (native engine, holds all game data) | **ProductVersion `27.0.0+2493426`**, which is the layout key |
| Steam build id | 26340721 |
| Save | "BRR". The in-game date is about **2 Feb 2037**, so the real-world stars in it are in their 30s, and Kane and Mbappé are no longer players. |

## How to get to the data

```
[game_plugin.dll + 0x57056E0]        -> DB object
DB + 0x138 / +0x140                  -> std::vector<Person*> begin/end  (420,392 people)
Person* p where *(p) == game_plugin.dll + 0x4D29758   -> it is a player (139,891)
```

Other person classes share the same base layout (UID and name offsets) but have different vtables. For example, staff use vtable RVA `0x4D8DE78` (Harry Kane, now staff). Retired or historic people are small 0x30-byte records with vtable RVA `0x4CC4D88` (Kylian Mbappé).

### Strings
An `FMString*` points at `[u32 length][UTF-8 bytes]`. The allocation has a 12-byte header before that (`[u32 cap][u32 0x227][u32 refcount]`). Names are stored as *name-table entries*: the person holds a pointer to an entry, and the entry's first qword is the `FMString*`.

### Dates
A date is a `u16` day-of-year (1-based) followed by a `u16` year. Some dates use the upper 7 bits of the day field for other data, so mask the day with `0x1FF`.

### Person (offsets from the person pointer)

| Field | Offset | Status |
|---|---|---|
| UID (u32) | +0x0C | confirmed (Haaland 29179241) |
| full name `FMString*` | +0x40 | confirmed (only present when it differs, e.g. "Erling Braut Haaland") |
| first name entry | +0x50 | confirmed |
| last name entry | +0x58 | confirmed |
| nation `Nation*` | +0x68 | confirmed. Nation +0x18 = name, +0x28 = code, +0x30 = nationality |
| personality, 8 × u8 1-20 | +0x70 | values look right, order unknown |
| birth city `City*` | +0x80 | confirmed (Haaland → Leeds). City +0x18 = name |
| date of birth | +0x88 | confirmed (Haaland 2000-07-21, Bellingham 2003-06-29, Saka 2001-09-05) |
| contract `Contract*` | +0xA8 | confirmed. Contract +0x10 = Team\*, +0x20 = u32 weekly wage (GBP), +0x48 = expiry date. Team +0x30 = **current Club\*** (club +0xC0 = name, +0xC8 = short name), team +0x50 = competition (+0x48 = name) |
| some other `Club*` | +0x108 | **not** the current club (Haaland → Man City, Yamal → Barcelona Atlètic, Verbruggen → Real Madrid). Meaning unknown. |

### Player block (sits BEFORE the person pointer)
The player object's data starts at `p - 0x288`. The bytes after `p + 0x140` belong to the *next* player in the pool. I caught this using known goalkeepers: Verbruggen, Chevalier, Bazunu and Restes all have their GK ratings 0x138 bytes *before* their own pointer.

| Field | Offset from p | Status |
|---|---|---|
| position ratings, 15 × u8 1-20 | −0x138 | confirmed as position ratings. Order is partly inferred: `GK, DL, DC, DR, ?, DM, ML, MC, MR, AML, AMC, AMR, ST, WBL?, WBR?` |
| attributes, 54 × u8 1-100 (UI value = raw/5) | −0x129 | block confirmed. Names are only partly known (see below) |
| height cm (u8) | −0x5A | confirmed (Haaland 195, Bellingham 186, Saka 178, Verbruggen 193). Range 151-210 |
| **CA** (s16) | −0x24 | confirmed. 100% of players are 1-200, it has the highest correlation with attribute averages, and Yamal is 194 |
| **PA** (s16) | −0x22 | confirmed. PA ≥ CA for 100% of 139,891 players. Haaland 195, Yamal 195 |
| value (u32 GBP) | −0x54 | tentative. Yamal 221.9M (UI £202M-£222M), Verbruggen 25.2M (UI £19.5M-£25M), Haaland exactly 300M (shown as Not for Sale) |
| weight | – | **not found** in the object. It is probably in a referenced sub-object |

### Attribute order: solved against in-game screenshots
Every displayed value equals `round(raw/5)` for Haaland, Yamal and Verbruggen. See [docs/ground-truth-brr.md](docs/ground-truth-brr.md). The order is FM's classic internal one:

```
 0 Crossing      1 Dribbling     2 Finishing     3 Heading       4 LongShots     5 Marking
 6 OffTheBall    7 Passing       8 Penalties     9 Tackling     10 Vision       11 Handling
12 AerialReach* 13 CommandOfArea 14 Communication* 15 Kicking*  16 Throwing*    17 Anticipation
18 Decisions    19 OneOnOnes    20 Positioning  21 Reflexes*    22 FirstTouch   23 Technique
24 LeftFoot     25 RightFoot    26 Flair        27 Corners      28 Teamwork     29 WorkRate
30 LongThrows   31 Eccentricity 32 RushingOut   33 Punching     34 Acceleration 35 FreeKicks
36 Strength     37 Stamina      38 Pace         39 JumpingReach 40 Leadership   41 Dirtiness†
42 Balance      43 Bravery      44 Consistency† 45 Aggression   46 Agility      47 ImportantMatches†
48 InjuryProneness† 49 Versatility† 50 NaturalFitness 51 Determination 52 Composure 53 Concentration
```

\* GK ties on Verbruggen, now confirmed by Urbig. † Hidden attributes, named from the order.

The feet use the same 1-100 scale: 100 = Very Strong, 60 = Fairly Strong, 55 = Reasonable.

The in-game date is confirmed at `game_plugin.dll+0x5706018`. It reads 4/2/2037, matching the UI.
## Sample output (`FMScout.Probe report`)

```
Engine build: 27.0.0+2493426 (game_plugin.dll @ 0x7FFEA2DC0000)
In-game date (tentative): 2037-02-02
People in DB: 420,392; players: 139,891 (enumerated in 336 ms)
Read all 139,891 players in 2392 ms
Sanity: 100.0% have CA in 1-200; PA>=CA for 100.0%; heights 151-210 cm

## Erling Haaland (Erling Braut Haaland)   uid 29179241
   DOB 2000-07-21 (age 36)   Norway   club: Manchester City
   CA 169   PA 195   height 195 cm
   positions: GK=1 ... ST=20
## Jude Bellingham   DOB 2003-06-29  England  Borussia Dortmund  CA 167 PA 188  186 cm  (AMC 20, MC 19, DM 18)
## Lamine Yamal      DOB 2007-07-13  Spain    F.C. Barcelona Atlètic  CA 194 PA 195  178 cm  (AMR 20)
## Bukayo Saka       DOB 2001-09-05  England  (no club)  CA 164 PA 188  178 cm  (AMR 20, AML 17)
## Bart Verbruggen   DOB 2002-08-18  Netherlands  Real Madrid  CA 162 PA 165  193 cm  (GK 20)
```

Run it yourself to see the full attribute dump.

## Still unknown or to verify
1. Position-rating order `GK, SW, DL, DC, DR, DM, ML, MC, MR, AML, AMC, AMR, ST, WBL, WBR` is confirmed (Javier Ortiz screenshot).
2. Personality block order (8 bytes at +0x70).
4. Value (person−0x54) is inside the UI range for Yamal, Urbig and Ortiz. Haaland reads 300M (Not for Sale).
5. Meaning of the +0x108 club pointer.
6. Not decoded yet: known-as names, traits, reputation, playing-time status, and caps/goals.
7. The layout is per build. A game patch will very likely move the RVAs. The exploration commands make re-deriving them quick: `who <surname>`, then `arrays`, then `refsany`.
## Build and run
```
dotnet build -c Release
src\FMScout.Probe\bin\Release\net10.0\FMScout.Probe.exe report Haaland Saka
```
This needs FM27 running with a save loaded. It targets .NET 10 (the SDK installed here) on x64.




