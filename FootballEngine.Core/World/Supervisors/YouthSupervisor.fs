namespace FootballEngine.World.Supervisors

open FootballEngine.Domain
open FootballEngine.World
open FootballEngine.World.Agents

module YouthSupervisor =

    let promote
        (now: System.DateTime)
        (clubId: ClubId)
        (playerId: PlayerId)
        (salary: decimal)
        (years: int)
        (role: ContractRole)
        (minutes: int)
        (state: GameState)
        =
        TransferMarket.promoteYouth now clubId playerId salary years role minutes state

    let private hasCohort (clubId: ClubId) (cohort: int) (state: GameState) =
        state.Players
        |> Map.exists (fun _ p -> p.Affiliation = YouthProspect clubId && p.Birthday.Year = cohort - 16)

    let private applyIntent (state: GameState) (intent: Intent<ClubYouthIntent>) =
        match intent.Payload with
        | GenerateYouthPlayer clubId when not (hasCohort clubId state.Season state) -> YouthGen.generateOne clubId state
        | GenerateYouthPlayer _ -> state

    let resolve (state: GameState) (intents: Intent<ClubYouthIntent> list) : GameState =
        intents |> List.fold applyIntent state
