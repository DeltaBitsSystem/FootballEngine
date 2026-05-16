module FootballEngine.Tests.Layer2.VARTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

let varTests =
    testList
        "VAR"
        [ test "AddReview sets CurrentReview and IsChecking" {
              let varState = VARState()
              varState.AddReview(VARReviewableIncident.GoalCheck(HomeClub, None, false, 0<subtick>), 0<subtick>, 100 * 1<tickDelta>)
              Expect.isTrue varState.IsChecking "IsChecking should be true"

              match varState.CurrentReview with
              | Some _ -> ()
              | None -> failwith "CurrentReview should be set"
          }

          test "CompleteReview saves to history and clears IsChecking" {
              let varState = VARState()
              let incident = VARReviewableIncident.GoalCheck(HomeClub, None, false, 0<subtick>)
              varState.AddReview(incident, 0<subtick>, 100 * 1<tickDelta>)
              varState.CompleteReview(VARDecision.CheckComplete)
              Expect.isFalse varState.IsChecking "IsChecking should be false after completion"
              Expect.equal varState.History.Length 1 "history should have one entry"
          }

          test "History preserves all reviews" {
              let varState = VARState()

              let incidents =
                  [ 1..5 ]
                  |> List.map (fun i -> VARReviewableIncident.GoalCheck(HomeClub, None, false, (i * 100) * 1<subtick>))

              incidents
              |> List.iter (fun incident ->
                  let startSubTick =
                      match incident with
                      | VARReviewableIncident.GoalCheck(_, _, _, st) -> st
                      | _ -> 0<subtick>

                  varState.AddReview(incident, startSubTick, 100 * 1<tickDelta>)
                  varState.CompleteReview(VARDecision.CheckComplete))

              Expect.equal varState.History.Length 5 "should have 5 reviews in history"
          }

          test "Clear resets state" {
              let varState = VARState()
              let incident = VARReviewableIncident.GoalCheck(HomeClub, None, false, 0<subtick>)
              varState.AddReview(incident, 0<subtick>, 100 * 1<tickDelta>)
              varState.Clear()
              Expect.isFalse varState.IsChecking "IsChecking should be false"
              Expect.isNone varState.CurrentReview "CurrentReview should be None"
          }

          test "GoalCheck incident is correctly structured" {
              let incident = VARReviewableIncident.GoalCheck(HomeClub, Some 1, false, 1000<subtick>)

              match incident with
              | VARReviewableIncident.GoalCheck(club, Some pid, isOG, subTick) ->
                  Expect.equal club HomeClub ""
                  Expect.equal pid 1 ""
                  Expect.isFalse isOG ""
                  Expect.equal subTick 1000<subtick> ""
              | _ -> failwith "unexpected incident type"
          }

          test "PenaltyCheck incident is correctly structured" {
              let incident = VARReviewableIncident.PenaltyCheck(AwayClub, 85.0, 34.0)

              match incident with
              | VARReviewableIncident.PenaltyCheck(club, fx, fy) ->
                  Expect.equal club AwayClub ""
                  Expect.equal fx 85.0 ""
              | _ -> failwith "unexpected incident type"
          }

          test "RedCardCheck incident is correctly structured" {
              let incident = VARReviewableIncident.RedCardCheck(5, FoulSeverity.SeriousFoulPlay)

              match incident with
              | VARReviewableIncident.RedCardCheck(pid, sev) ->
                  Expect.equal pid 5 ""
                  Expect.equal sev FoulSeverity.SeriousFoulPlay ""
              | _ -> failwith "unexpected incident type"
          } ]
