# Ground truth: save "BRR" (FM27 build 27.0.0+2493426)

These are in-game profile screenshots Paul took on Wed 4/2/2037, 12:00. I use them to check the reader layout (`fm27-layout.json`).
Re-run `FMScout.Probe report "Erling Braut Haaland" "Lamine Yamal" "Bart Verbruggen"` and compare against this file after any layout change.

## Result of the check (2026-11-09)

| Field | Haaland | Yamal | Verbruggen | Reader |
|---|---|---|---|---|
| Current club | Real Madrid | Barcelona | Como | ✅ person+0xA8 → contract +0x10 → team +0x30 → club +0xC0 |
| Competition | LaLiga | LaLiga | Serie A | ✅ team +0x50 → +0x48 |
| DOB | 21/7/2000 | 13/7/2007 | 18/8/2002 | ✅ |
| Height | 6'5" | 5'10" | 6'4" | ✅ 195 / 178 / 193 cm |
| Wage (UI range) | £240K-£325K | £525K-£700K | £210K-£275K | ✅ 313,849 / 686,852 / 227,849 (exact value, inside the range) |
| Contract expiry | 30/6/2038 | 30/6/2040 | 30/6/2037 | ✅ contract +0x48 |
| Value (UI range) | Not for Sale | £202M-£222M | £19.5M-£25M | ⚠️ person−0x54: 300,000,000 / 221,931,024 / 25,167,012. Two are inside or near the range; Haaland's looks like a cap or sentinel. Tentative. |
| Game date | 4/2/2037 | | | ✅ module+0x5706018 = day 35, 2037 |
| Attributes | all 36 | all 36 | 13 GK + 14 mental + 8 physical + FK, Pen, Technique | ✅ every displayed value equals round(raw/5) |
| Feet | L Very Strong, R Reasonable | L Very Strong, R Fairly Strong | L Reasonable, R Very Strong | ✅ raw 100 = Very Strong, 60 = Fairly Strong, 55 = Reasonable |
| Positions | ST | M/AM (R) | GK | ✅ ST=20; AMR=20, MR=16; GK=20 |

The person+0x108 club pointer is **not** the current club (Haaland → Manchester City, Yamal → Barcelona Atlètic, Verbruggen → Real Madrid). Its meaning is unknown; it may be a favourite or previous club.

Still not checked: personality block order (UI shows Professional / Driven / Perfectionist), traits, reputation, playing-time status, caps.

## Raw UI values

### Erling Haaland: Real Madrid (LaLiga), ST (C), b. 21/7/2000, Norway, 6'5"
Personality Professional. L foot Very Strong, R Reasonable. £240K-£325K p/w to 30/6/2038. Regular Starter. Not for Sale. 158 caps / 140 goals. GK rating 3/10.

* Technical: Crossing 14, Dribbling 9, Finishing 20, First Touch 17, Heading 15, Long Shots 12, Marking 11, Passing 14, Tackling 9, Technique 16, Corners 7, Free Kick 11, Long Throws 5, Penalty 18
* Mental: Aggression 15, Anticipation 19, Bravery 15, Composure 16, Concentration 16, Decisions 16, Determination 20, Flair 13, Leadership 13, Off The Ball 19, Positioning 12, Teamwork 18, Vision 14, Work Rate 13
* Physical: Acceleration 13, Agility 13, Balance 15, Jumping Reach 17, Natural Fitness 19, Pace 14, Stamina 11, Strength 12
* Traits: Moves Into Channels, Places Shots, Likes To Try To Beat Offside Trap, Gets Crowd Going, Tries First Time Shots, Runs With Ball Rarely

### Bart Verbruggen: Como (Serie A), GK, b. 18/8/2002, Netherlands, 6'4"
Personality Perfectionist. L foot Reasonable, R Very Strong. Value £19.5M-£25M. £210K-£275K p/w to 30/6/2037. Important Player. Wnt status. 145 caps. Outfield rating 4/10.

* Goalkeeping: Aerial Reach 15, Command Of Area 16, Communication 15, Eccentricity 6, First Touch 13, Handling 14, Kicking 17, One On Ones 18, Passing 16, Punching 14, Reflexes 15, Rushing Out 12, Throwing 17
* Mental: Aggression 9, Anticipation 14, Bravery 13, Composure 16, Concentration 16, Decisions 14, Determination 14, Flair 12, Leadership 14, Off The Ball 3, Positioning 15, Teamwork 13, Vision 17, Work Rate 12
* Physical: Acceleration 10, Agility 14, Balance 12, Jumping Reach 16, Natural Fitness 14, Pace 10, Stamina 10, Strength 8
* Also shown: Free Kick 3, Penalty 7, Technique 14
* Traits: Winds Up Opponents, Argues With Officials, Avoids Using Weaker Foot

### Lamine Yamal: Barcelona (LaLiga), M/AM (R), b. 13/7/2007, Spain, 5'10"
Personality Driven. L foot Very Strong, R Fairly Strong. Value £202M-£222M. £525K-£700K p/w to 30/6/2040. Important Player. 158 caps / 58 goals.

* Technical: Crossing 19, Dribbling 18, Finishing 16, First Touch 18, Heading 8, Long Shots 15, Marking 11, Passing 17, Tackling 8, Technique 19, Corners 15, Free Kick 16, Long Throws 6, Penalty 16
* Mental: Aggression 9, Anticipation 16, Bravery 13, Composure 17, Concentration 20, Decisions 15, Determination 18, Flair 20, Leadership 13, Off The Ball 17, Positioning 13, Teamwork 12, Vision 19, Work Rate 13
* Physical: Acceleration 15, Agility 19, Balance 17, Jumping Reach 7, Natural Fitness 15, Pace 15, Stamina 15, Strength 13
* Traits: Curls Ball, Uses Outside Of Foot, Tries Tricks, Runs With Ball Often, Cuts Inside From Right Wing

## Second batch (Wed 4/2/2037 23:00)

### Jonas Urbig: Chelsea (Premier League), GK, b. 8/8/2003, Germany, 6'2"
Personality Professional. L foot Fairly Strong, R Very Strong. Value £37M-£58M. £350K-£400K p/w to 30/6/2038. Domestic Cup Goalkeeper. 45 caps.

* Goalkeeping: Aerial Reach 14, Command Of Area 15, Communication 13, Eccentricity 12, First Touch 14, Handling 14, Kicking 17, One On Ones 18, Passing 16, Punching 8, Reflexes 17, Rushing Out 18, Throwing 18
* Mental: Aggression 12, Anticipation 16, Bravery 15, Composure 16, Concentration 16, Decisions 15, Determination 16, Flair 10, Leadership 15, Off The Ball 8, Positioning 16, Teamwork 12, Vision 16, Work Rate 13
* Physical: Acceleration 8, Agility 15, Balance 14, Jumping Reach 18, Natural Fitness 14, Pace 9, Stamina 10, Strength 12
* Also shown: Free Kick 3, Penalty 7, Technique 14

Reader: every value matches. This settles the GK ties: Aerial Reach is index 12, Communication 14, Reflexes 21, Kicking 15, Throwing 16. Wage 353,404 and value 40,745,096 are both inside the UI ranges. Height 189 cm, shown as 6'2".

### Javier Ortiz: Man City (Premier League), D/WB (R), b. 26/4/2011, El Salvador, 6'2"
Not fully scouted, so the UI shows ranges. L foot Fairly Strong, R Very Strong. Value £81M-£133M. £350K-£500K p/w to 30/6/2040. Regular Starter.

Pitch: green dot at DR, yellow-green at WBR, orange at DL.

Reader: positions DR=20, WBR=15, DL=7, which confirms the position order. All exact UI values match (Crossing 16, First Touch 15, Marking 16, Passing 13, Tackling 15, Positioning 14, Determination 15, Acceleration 18, Pace 16, Strength 13). Every ranged value falls inside its range (e.g. Technique 17 in 15-19, Anticipation 17 in 16-20). Wage 395,662 and value 111,375,000 are inside the UI ranges.

**Note:** the reader shows the TRUE attribute values, even where the game UI only shows scouting ranges.

## How the attribute indices were solved
For every attribute name, I kept only the raw indices where round(raw/5) equals the UI value for all three players, then eliminated indices already taken. That gives a unique index for all 36 outfield attributes and for 6 GK ones.

The GK ties left over from Verbruggen (Aerial Reach, Communication and Reflexes at indices 12, 14 and 21; Kicking and Throwing at 15 and 16) are now confirmed by Urbig.

The 5 leftover indices are the hidden attributes, also named from that order: 41 Dirtiness, 44 Consistency, 47 Important Matches, 48 Injury Proneness, 49 Versatility. They are always multiples of 5 and don't correlate with CA.

