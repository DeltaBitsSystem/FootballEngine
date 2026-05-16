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

let shouldContainDomainEvent (predicate: DomainEvent -> bool) (label: string) (events: DomainEvent list) =
    Expect.isTrue
        (events |> List.exists predicate)
        $"Expected domain event matching: {label}. Got: {events}"

let shouldContainBallUpdate (domainEvents: DomainEvent seq) =
    shouldContainDomainEvent
        (fun o ->
            match o with
            | DomainEvent.BallUpdate _ -> true
            | _ -> false)
        "BallUpdate"
        (domainEvents |> Seq.toList)

let engineMustNotCrash (f: unit -> unit) =
    try
        f ()
    with ex ->
        failtestf $"Engine threw exception: %s{ex.Message}\n%s{ex.StackTrace}"
