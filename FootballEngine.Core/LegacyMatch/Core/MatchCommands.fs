namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Domain.TacticalInstructions

type CommandSource =
    | User
    | AIManager
    | System

type MatchCommand =
    | RequestSubstitution of clubId: ClubId * outPlayerId: PlayerId * inPlayerId: PlayerId
    | ChangeTactics of clubId: ClubId * tactics: TeamTactics
    | ChangeInstructions of clubId: ClubId * instructions: TacticalInstructions
    | PauseSimulation
    | ResumeSimulation

type MatchCommandEnvelope =
    { CommandId: int64
      IssuedForSubTick: int
      Source: CommandSource
      Command: MatchCommand }

module MatchCommands =

    let private priority envelope =
        match envelope.Command with
        | PauseSimulation -> 0
        | ResumeSimulation -> 1
        | RequestSubstitution _ -> 10
        | ChangeTactics _ -> 20
        | ChangeInstructions _ -> 21

    let orderForTick subTick (commands: MatchCommandEnvelope seq) =
        commands
        |> Seq.filter (fun c -> c.IssuedForSubTick <= subTick)
        |> Seq.sortBy (fun c -> c.IssuedForSubTick, priority c, c.CommandId)
        |> Seq.toArray
