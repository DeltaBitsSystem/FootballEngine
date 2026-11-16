# FootballEngine

**A football management game where every result has a cause.**

![A goal in the 3D match viewer, close up camera](docs/media/match/goal.webp)

> **In development.** Playable from kickoff to the end of a season, and far from finished. The first public demo is planned for January 2027.

Most management games ask you to trust a number. FootballEngine shows you the match that produced it. Twenty two players perceive, decide and move forty times a second, your choices shape how they do it, and when you lose 2 to 1 you can open the replay and point at the reason.

## What we are building

You take a club and give it an identity. Then you watch it play that way.

* **Your club plays like you built it.** Tactics, training and signings show up on the pitch, not in a hidden number.
* **Rival managers learn.** They study you and answer in their own style. No tactic wins forever.
* **You can see why.** Every match can be replayed. Lost 2 to 1? The tired full back you left on is right there.
* **People remember.** Players, the board and the press act on what you did to them.
* **The world moves without you.** Careers, rivalries and sackings happen whether you watch or not.
* **Mods come first.** The editor is a core feature, so anyone can build and share their own leagues.

## How the match works

Every match is a real simulation of 22 bodies and 22 minds, 40 times a second.

* Each player perceives, decides and moves. Nobody rolls a dice to pick the winner.
* The team thinks as a unit. Shape, pressing and transitions frame what each player chooses.
* The ball carrier plans a few moves ahead before passing, carrying or shooting.
* Fatigue hurts perception and judgement, not only speed.
* The ball has real flight, spin, bounce and drag.
* Same seed, same match, down to the last bit. Any result can be replayed and explained.
* Results are calibrated against real league statistics on matches the tuning never saw.

## By the numbers

One full 90 minute match on a Ryzen 5 5600X:

| Measure | Result |
|---|---|
| Simulating a whole match | 2.8 s on one core, 2.2 s with a second thread |
| Simulation steps | about 262,000 |
| Ball carrier decisions | about 42,000, each searching chains of up to 4 actions |
| Memory allocated | 93 MB |
| Jumping to any moment of a replay | about 6 s at most, from kickoff |
| Calibration run of 48 matches | 37 s |
| Automated tests | 1,288 |

## The match viewer

Every frame below comes from the simulation; the viewer only draws what the engine decided. All clips come from the replay of one seeded match, so anyone can watch them again exactly.

| Goal | Goal |
|---|---|
| ![Goal, broadcast camera](docs/media/match/goal-broadcast.webp) | ![Goal, touchline camera](docs/media/match/touchline-goal.webp) |
| **The same goal** from the broadcast camera. | **An equaliser** from the touchline. |

| Save | Save |
|---|---|
| ![A chance in the box and a save](docs/media/match/box-save.webp) | ![Pressure on the box and a save](docs/media/match/edge-save.webp) |
| **A chance in the box,** saved by the keeper. | **Pressure on the box** that ends in a save. |

Five cameras: broadcast, tactical, behind the goal, touchline and close up. The stadium is generated from the club's size and reputation.

| Close up | Touchline |
|---|---|
| ![Close up camera](docs/media/match/still-close-up.webp) | ![Touchline camera](docs/media/match/still-touchline.webp) |
| **Broadcast** | **Behind the goal** |
| ![Broadcast camera](docs/media/match/still-broadcast.webp) | ![Behind the goal camera](docs/media/match/still-behind-goal.webp) |

### Replays

Any match you played can be watched again. A timeline marks goals, saves, set pieces, cards and substitutions: click anywhere to jump there, forward or back, or step from one moment to the next. Nothing is stored but the seed and your decisions. The engine plays the match again and lands on the moment in seconds.

**Missing.** Crowd and crowd reactions, slow motion, weather and night games, club badges in the scoreboard, and original player models and animations.

## A tour of the game

Screens from a real career, a few weeks into the season. Each one says what the screen is for, what makes it different and what is still missing.

[Today](#today) · [Tactics](#tactics) · [Squad](#squad) · [Training](#training) · [Scouting](#scouting) · [Transfers](#transfers) · [Finances](#finances) · [Board, news and inbox](#board-news-and-inbox) · [Calendar](#calendar) · [Mod editor](#mod-editor)

### Today

*The next match and what needs you.*

![Today](docs/media/ui/today.webp)

**What it is for.** Your desk. The next opponent, how their manager plays, whether your lineup is ready and the messages that need a decision.

**What is different.** The rival manager has a personality and a record against you. The first time you meet, they have no reports on you. By the third, they know your habits. Time advances day by day and stops for a decision, an unavailable player or your match. It never plays for you.

**Missing.** A short summary of the last result and the form of both teams.

### Tactics

*Shape, roles and fit.*

![Tactics](docs/media/ui/tactics.webp)

**What it is for.** Pick a formation, place your eleven, set the bench.

**What is different.** Every player shows how well they fit the role and how fresh they are. A familiarity bar shows how well the team knows this shape. Changing everything the day before a match has a real cost on the pitch.

**Missing.** Set piece routines and opposition instructions.

### Squad

*Who you have.*

![Squad](docs/media/ui/squad.webp)

**What it is for.** The whole squad by line, level, age or value, with condition and morale at a glance.

**Missing.** A richer player profile with history, personality and promises.

### Training

*The week before the match.*

![Training](docs/media/ui/training.webp)

**What it is for.** Set the team's emphasis for the week and a plan for each player.

**What is different.** Training trades freshness for growth. Young players develop, tired players rest, the starting eleven sharpens. The page tells you how fresh your eleven will be on match day, and injury risk is visible before you choose.

**Missing.** Individual drills and staff advice.

### Scouting

*Knowledge has a cost.*

![Scouting](docs/media/ui/scouting.webp)

**What it is for.** Send a scout to watch a player, with a cost and a deadline.

**What is different.** You never see a player's true level. Reports give a range and a confidence, biased by how good your scout is.

**Missing.** Scouting regions and a better way to find targets than a long list.

### Transfers

*The market.*

![Transfers](docs/media/ui/transfers.webp)

**What it is for.** Search players, follow targets, make offers and track incoming and outgoing deals.

**What is different.** Other clubs buy and sell on their own, all season, whether you are watching or not.

**Missing.** Loans and contract clauses.

### Finances

*Money you can trust.*

![Finances](docs/media/ui/finances.webp)

**What it is for.** Cash, transfer budget, wage commitments and a 30 day forecast.

**What is different.** Every operation is recorded, and money is reserved before a deal closes so it is never spent twice.

**Missing.** Match day income, sponsors and a clearer history view.

### Board, news and inbox

*People who remember.*

![Board](docs/media/ui/board.webp)

**What it is for.** The board's confidence and the supporters' mood, the season objective and your recent results.

![News](docs/media/ui/news.webp)

The news follows your club, your league and the world. Stories come from what really happened.

![Inbox](docs/media/ui/inbox.webp)

Messages that need you are marked for review. The rest can wait.

**Missing.** Press conferences, player conversations and situations that ask for a decision.

### Calendar

*The season.*

![Calendar](docs/media/ui/calendar.webp)

**What it is for.** Every fixture and result of the season.

**Missing.** A month view and cup draws.

### Mod editor

*Build your own world.*

![Mod library](docs/media/ui/mod-library.webp)

**What it is for.** Start a new world or edit a copy of a built in country. The original stays intact.

![Club editor](docs/media/ui/mod-club.webp)

Clubs, kits, reputation and squads. Players you do not define are generated when a career starts. You can paste a squad from a spreadsheet.

![League editor](docs/media/ui/mod-leagues.webp)

Leagues form a pyramid with their own points rules.

![Publish](docs/media/ui/mod-publish.webp)

Publishing checks the mod exactly as the game loads it, so a career never starts broken.

**Missing.** Sharing mods online and editing competitions in depth.

## Where it stands

Early and playable. You can start a career, run a season, watch every match in 3D and replay any of them.

**The 3D assets are placeholders.** Players, animations, the stadium and the crowd were generated by asking Claude to make them, and it shows. They are unpolished and will be replaced with our own work.

The built in leagues use real club names for testing. The game will ship with its own world, and the mod editor is how players bring in the leagues they love.

## Where it is going

The demo has to make you feel five things:

* **Rivals adapt.** The other manager read you, and you had to change.
* **Matches turn on key moments.** You made a change at the right time and turned it around.
* **A living world.** There is always something to discover.
* **The mod editor.** You can build your own league.
* **Your project.** Your club finally plays the way you want.

The [roadmap](ROADMAP.md) has the details.

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/DeltaBitsSystem/FootballEngine.git
cd FootballEngine
dotnet run --project FootballEngine.Client/FootballEngine.Client.fsproj -c Release
```

Run the tests with `dotnet fsi scripts/test.fsx`.

## Built with

F#, .NET 10, Avalonia, WebGPU and SQLite. Offline, no servers, no accounts.

## Get involved

Ideas, bug reports and pull requests are all welcome. [CONTRIBUTING.md](CONTRIBUTING.md) explains how the game is built and where each change belongs; design notes in `docs/` are mostly in Spanish. If a match does something strange, open an issue with its seed: every match can be replayed exactly.

## License

[GPL 3.0](LICENSE). The code is free, and anything built on it stays free.
