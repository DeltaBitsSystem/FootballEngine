module FootballEngine.Benchmarks.Helpers

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Domain.LineupOps


let loadGame () =
    match Db.loadGame().GetAwaiter().GetResult() with
    | None -> failwith "No saved game — run generateNewGame first."
    | Some(game, clock) -> game, clock

let loadClubs () =
    let game, _clock = loadGame ()

    let readyGame =
        game.Clubs
        |> Map.toList
        |> List.fold (fun gs (clubId, _) -> ensureForClub clubId gs) game

    let clubs = readyGame.Clubs |> Map.toArray |> Array.map snd

    if clubs.Length < 2 then
        failwith "Need at least 2 clubs."

    readyGame, clubs, readyGame.Players, readyGame.Staff
