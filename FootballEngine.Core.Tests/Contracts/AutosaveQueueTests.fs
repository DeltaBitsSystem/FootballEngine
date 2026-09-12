namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Domain

module AutosaveQueueTests =

    let tests =
        testList
            "FootballEngine.Core.Contracts.AutosaveQueue"
            [ test "newer pending revision coalesces and stale revision is rejected" {
                  let first: AutosaveQueue.Request<string> =
                      { Slot = "a"
                        Revision = 1UL
                        Payload = "one" }

                  let newer =
                      { first with
                          Revision = 2UL
                          Payload = "two" }

                  let stale =
                      { first with
                          Revision = 1UL
                          Payload = "old" }

                  let accepted, state = AutosaveQueue.enqueue first AutosaveQueue.empty
                  let coalesced, state = AutosaveQueue.enqueue newer state
                  let rejected, _ = AutosaveQueue.enqueue stale state
                  Expect.equal accepted AutosaveQueue.Accepted "first request is accepted"
                  Expect.equal coalesced AutosaveQueue.Coalesced "pending request is replaced"
                  Expect.equal rejected AutosaveQueue.RejectedStale "older request cannot replace newer"
                  let started, _ = AutosaveQueue.tryStart "a" state

                  Expect.equal
                      (started |> Option.map (fun request -> request.Revision))
                      (Some 2UL)
                      "newest payload runs"
              }

              test "active revision blocks an older request and cancellation only removes pending work" {
                  let active: AutosaveQueue.Request<string> =
                      { Slot = "a"
                        Revision = 4UL
                        Payload = "active" }

                  let newer =
                      { active with
                          Revision = 5UL
                          Payload = "pending" }

                  let _, state = AutosaveQueue.enqueue active AutosaveQueue.empty
                  let _, state = AutosaveQueue.tryStart "a" state
                  let _, state = AutosaveQueue.enqueue newer state
                  let cancelled, state = AutosaveQueue.cancelPending "a" 5UL state
                  let rejected, _ = AutosaveQueue.enqueue { active with Revision = 3UL } state
                  Expect.isTrue cancelled "pending request is cancellable"
                  Expect.equal rejected AutosaveQueue.RejectedStale "active revision still protects the slot"
              } ]
