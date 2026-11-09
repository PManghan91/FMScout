# FM Scout: reader probe (step 2)

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
| club `Club*` | +0x108 | likely. Club +0xC0 = name, +0xC8 = short name. Haaland → Manchester City, but Yamal → "F.C. Barcelona Atlètic", which may mean this is the *team* rather than the parent club. Needs checking in the UI. |

### Player block (sits BEFORE the person pointer)
The player object's data starts at `p - 0x288`. The bytes after `p + 0x140` belong to the *next* player in the pool. I caught this using known goalkeepers: Verbruggen, Chevalier, Bazunu and Restes all have their GK ratings 0x138 bytes *before* their own pointer.

| Field | Offset from p | Status |
|---|---|---|
| position ratings, 15 × u8 1-20 | −0x138 | confirmed as position ratings. Order is partly inferred: `GK, DL, DC, DR, ?, DM, ML, MC, MR, AML, AMC, AMR, ST, WBL?, WBR?` |
| attributes, 54 × u8 1-100 (UI value = raw/5) | −0x129 | block confirmed. Names are only partly known (see below) |
| height cm (u8) | −0x5A | confirmed (Haaland 195, Bellingham 186, Saka 178, Verbruggen 193). Range 151-210 |
| **CA** (s16) | −0x24 | confirmed. 100% of players are 1-200, it has the highest correlation with attribute averages, and Yamal is 194 |
| **PA** (s16) | −0x22 | confirmed. PA ≥ CA for 100% of 139,891 players. Haaland 195, Yamal 195 |
| weight | – | **not found** in the object. It is probably in a referenced sub-object |

### What I know about the 54 attributes (statistics over all 139,891 players)
* **GK-only** (low for about 90% of players, r ≈ 0.9 with GK rating): indices 11-16, 19, 21. Indices 31-33 are always multiples of 5, which suggests GK *tendencies*.
* **Left foot = 24, right foot = 25** (confirmed: Haaland, Saka and Yamal are left-footed, the rest right-footed).
* Strong guesses:
  * 2 = Finishing (correlates with ST rating)
  * 5 and 9 = Marking and Tackling (r = 0.84 with each other, high for DCs)
  * 39 = Jumping Reach (r = 0.87 with height)
  * 27 and 35 = Corners and Free Kicks (a pair, both rare)
  * 30 = Long Throws
* **Hidden** (always multiples of 5, no correlation with CA): 41, 45, 48. Probably also 47 and 51.
* Age-linked: 44 and 49.
* By count, the 54 bytes are: 14 technical + 14 mental + 8 physical + 11 GK + 5 hidden + 2 feet.

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
1. The **name of each attribute index** and the exact **position order**. The quickest fix: open one or two players in the game UI (one outfielder, one GK) and compare against `report` output. That would pin every index in a few minutes.
2. **Weight**.
3. Whether +0x108 is the parent club or the playing team. Check Yamal in the UI.
4. The **game date** RVA (0x5706018) is tentative.
5. The personality block order (Adaptability, Ambition, Loyalty, Pressure, Professionalism, Sportsmanship, Temperament, Controversy).
6. Common or known-as names (e.g. "Lamine Yamal" rather than "Lamine Yamal Nasraoui"), and contract, wage, value and reputation (the Player +0xA8 sub-object looks like a contract and has a back-pointer to the person).
7. The layout is per build. A game patch will very likely move the RVAs. The exploration commands make re-deriving them quick: `who <surname>`, then `arrays`, then `refsany`.

## Build and run
```
dotnet build -c Release
src\FMScout.Probe\bin\Release\net10.0\FMScout.Probe.exe report Haaland Saka
```
This needs FM27 running with a save loaded. It targets .NET 10 (the SDK installed here) on x64.
