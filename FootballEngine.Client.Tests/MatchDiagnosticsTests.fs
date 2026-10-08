module FootballEngine.Client.Tests.MatchDiagnosticsTests

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Tools.MatchDiagnostics

let private unwrap =
    function
    | Ok value -> value
    | Error error -> failtest error

let tests =
    testList
        "MatchDiagnostics"
        [ testCase "text queries combine identities and inclusive intervals" (fun _ ->
              let options =
                  parseOptions
                      [ "--actor"
                        "2"
                        "--action"
                        "7"
                        "--event"
                        "8"
                        "--from"
                        "10"
                        "--until"
                        "20"
                        "--limit"
                        "25" ]
                  |> unwrap

              Expect.equal
                  options.Query
                  { Actor = Some(PlayerId 2)
                    Action = Some(ActionId 7UL)
                    Event = Some(EventId 8UL)
                    FromTick = 10<subtick>
                    UntilTick = 20<subtick> }
                  "all selectors reach the typed query rather than competing command paths"

              Expect.equal options.Limit 25 "text output is explicitly bounded"
              Expect.isFalse options.Verify "reading evidence does not implicitly resimulate")
          testCase "default verification and explicit metadata-only summary are distinct" (fun _ ->
              Expect.isTrue (parseOptions [] |> unwrap).Verify "bare archive verifies sporting reconstruction"
              let summary = parseOptions [ "--summary" ] |> unwrap
              Expect.isTrue summary.Summary "summary mode is explicit"
              Expect.isFalse summary.Verify "summary can inspect evidence without replaying the match"
              let verified = parseOptions [ "--verify"; "--summary" ] |> unwrap
              Expect.isTrue verified.Verify "verification remains opt-in for inspection")
          testCase "chain roots accept full uint64 occurrence identities" (fun _ ->
              let options = parseOptions [ "--chain"; string System.UInt64.MaxValue ] |> unwrap

              Expect.equal
                  options.Chain
                  (Some(ObservationId System.UInt64.MaxValue))
                  "identity does not narrow to int32")
          testCase "JSON chains reuse the same explicit root and verification options" (fun _ ->
              let options = parseOptions [ "--chain"; "17"; "--json"; "--verify" ] |> unwrap
              Expect.equal options.Chain (Some(ObservationId 17UL)) "JSON changes publication not traversal"
              Expect.isTrue options.Json "machine-readable output is requested"
              Expect.isTrue options.Verify "sporting verification remains independent")
          testCase "whole-window audit cannot silently ignore a chain or actor selector" (fun _ ->
              let options = parseOptions [ "--audit"; "--json"; "--verify" ] |> unwrap
              Expect.isTrue options.Audit "audit mode is explicit"
              Expect.isTrue options.Json "audit has machine-readable output"
              Expect.isError (parseOptions [ "--audit"; "--chain"; "1" ]) "a root cannot restrict a whole-window audit"
              Expect.isError (parseOptions [ "--audit"; "--actor"; "2" ]) "actor selector cannot be silently ignored")
          testCase "invalid selectors and unsupported combinations are rejected before archive IO" (fun _ ->
              for arguments in
                  [ [ "--from"; "20"; "--until"; "10" ]
                    [ "--from"; "-1" ]
                    [ "--until"; "2147483648" ]
                    [ "--action"; "-1" ]
                    [ "--event"; "not-an-id" ]
                    [ "--limit"; "0" ]
                    [ "--limit"; "10001" ]
                    [ "--actor"; "1"; "--actor"; "2" ]
                    [ "--chain"; "1"; "--actor"; "2" ]
                    [ "--unknown"; "1" ]
                    [ "--from" ] ] do
                  Expect.isError
                      (parseOptions arguments)
                      (sprintf "invalid request %A cannot silently ignore a selector" arguments)) ]
