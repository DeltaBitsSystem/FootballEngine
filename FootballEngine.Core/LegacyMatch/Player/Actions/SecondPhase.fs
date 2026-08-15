namespace FootballEngine.Player.Actions

open FootballEngine.Types


type SecondPhaseState =
    | NoSecondPhase
    | AttackingShape of pressureExpiry: int
    | DefendingShape of clearExpiry: int

module SecondPhase =

    let initiateAttackingShape (subTick: int) (clock: SimulationClock) : SecondPhaseState =
        let duration = clock.SubTicksPerSecond * 5
        AttackingShape(subTick + duration)

    let initiateDefendingShape (subTick: int) (clock: SimulationClock) : SecondPhaseState =
        let duration = clock.SubTicksPerSecond * 4
        DefendingShape(subTick + duration)

    let isActive (subTick: int) (state: SecondPhaseState) : bool =
        match state with
        | AttackingShape expiry -> subTick < expiry
        | DefendingShape expiry -> subTick < expiry
        | NoSecondPhase -> false

    let reset () : SecondPhaseState = NoSecondPhase
