namespace FootballEngine.World.Supervisors

open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Agents

module YouthSupervisor =

    let private applyIntent (state: GameState) (intent: Intent<ClubYouthIntent>) =
        match intent.Payload with
        | GenerateYouthPlayer clubId -> YouthGen.generateOne clubId state

    let resolve (state: GameState) (intents: Intent<ClubYouthIntent> list) : GameState =
        intents |> List.fold applyIntent state
