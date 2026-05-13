module FootballEngine.Tests.Infrastructure.Assertions

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

let shouldBeControlledBy (expectedClub: ClubSide) (state: SimState) =
    match state.Ball.Control with
    | Controlled(club, _) -> Expect.equal club expectedClub $"Ball should be controlled by {expectedClub}, got {club}"
    | other -> failtestf $"Ball should be Controlled(%A{expectedClub}, _), got %A{other}"

let shouldHaveScore (expectedHome: int) (expectedAway: int) (state: SimState) =
    Expect.equal state.HomeScore expectedHome $"Home score: expected {expectedHome}, got {state.HomeScore}"
    Expect.equal state.AwayScore expectedAway $"Away score: expected {expectedAway}, got {state.AwayScore}"

let scoreIsNonNegative (state: SimState) =
    Expect.isGreaterThanOrEqual state.HomeScore 0 "Home score must be non-negative"
    Expect.isGreaterThanOrEqual state.AwayScore 0 "Away score must be non-negative"

let shouldContainEventType (predicate: MatchEventType -> bool) (label: string) (events: MatchEvent list) =
    Expect.isTrue
        (events |> List.exists (fun e -> predicate e.Type))
        $"Expected event matching: {label}. Got: {events |> List.map _.Type}"

let shouldContainKickOff (events: MatchEvent list) =
    shouldContainEventType (fun t -> t = MatchEventType.KickOff) "KickOff" events

let shouldContainOutput (predicate: SystemOutput -> bool) (label: string) (outputs: SystemOutput list) =
    Expect.isTrue
        (outputs |> List.exists predicate)
        $"Expected output matching: {label}. Got: {outputs |> List.map (fun o -> o.GetType().Name)}"

let shouldContainBallUpdate (outputs: SystemOutput list) =
    shouldContainOutput
        (fun o ->
            match o with
            | BallUpdate _ -> true
            | _ -> false)
        "BallUpdate"
        outputs

let engineMustNotCrash (f: unit -> unit) =
    try
        f ()
    with ex ->
        failtestf $"Engine threw exception: %s{ex.Message}\n%s{ex.StackTrace}"
