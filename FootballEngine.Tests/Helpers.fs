module FootballEngine.Tests.Helpers

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

let loadGame () =
    match Db.loadGame().GetAwaiter().GetResult() with
    | None -> failtest "No saved game — run generateNewGame first."
    | Some(game, _clock) -> game

let clubPlayers (game: GameState) (club: Club) : Player list =
    club.PlayerIds |> List.choose (fun pid -> game.Players |> Map.tryFind pid)

let loadClubs () =
    let game = loadGame ()
    let clubs = game.Clubs |> Map.toArray |> Array.map snd
    if clubs.Length < 2 then
        failtest "Need at least 2 clubs."
    game, clubs, game.Players, game.Staff

let inBounds (x: float, y: float) =
    x >= 0.0 && x <= float FootballEngine.Types.PhysicsContract.PitchLength &&
    y >= 0.0 && y <= float FootballEngine.Types.PhysicsContract.PitchWidth

let allPositionsInBounds (positions: Map<int, float * float>) =
    positions |> Map.forall (fun _ pos -> inBounds pos)

let isSeasonOver (state: GameState) =
    state.Competitions
    |> Map.forall (fun _ comp -> comp.Fixtures |> Map.forall (fun _ f -> f.Played))

let getOk result =
    match result with
    | Ok v -> v
    | Error e -> failtestf $"Expected Ok but got Error: %A{e}"
