module FootballEngine.Client.Tests.TacticsDraftTests

open Expecto
open FootballEngine.Domain
open FootballEngine.AppTypes
open FootballEngine.TacticsSlice

let private lineup tempo =
    { Formation = F433
      Tactics = Balanced
      Instructions =
        Some
            { TacticalInstructions.defaultInstructions with
                Tempo = tempo }
      Slots = [] }

let tacticsTests =
    testList
        "FootballEngine.Client.TacticsDraft"
        [ test "undo restores the last distinct draft and is bounded" {
              let first = lineup 0
              let second = lineup 2
              let history = recordDraft first [] |> recordDraft first |> recordDraft second

              match undoDraft history with
              | Some(previous, rest) ->
                  Expect.equal previous second "undo returns the latest draft"
                  Expect.equal rest [ first ] "undo keeps the earlier checkpoint"
              | None -> failtest "expected a draft checkpoint"
          }

          test "undo on empty history is safe" { Expect.isNone (undoDraft []) "no draft means no operation" }

          test "history keeps only reversible draft snapshots" {
              let history =
                  [ 0 .. DraftHistoryLimit + 4 ]
                  |> List.map lineup
                  |> List.fold (fun acc current -> recordDraft current acc) []

              Expect.equal history.Length DraftHistoryLimit "history has a bounded memory"
          } ]
