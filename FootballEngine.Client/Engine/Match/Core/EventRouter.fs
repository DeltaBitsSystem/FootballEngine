namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

type AgentActivation =
    | ActivatePhysics
    | ActivateCognition
    | ActivateAction of triggeredBy: SemanticEvent
    | ActivateReferee of triggeredBy: SemanticEvent
    | ActivateTeam of triggeredBy: SemanticEvent
    | ActivateManager of triggeredBy: SemanticEvent

module EventRouter =

    let route (events: SemanticEvent list) (subTick: int) (clock: SimulationClock) : AgentActivation list =

        let cognition =
            let triggered =
                events
                |> List.exists (function
                    | BallSecured _
                    | BallLost _
                    | BallLoose -> true
                    | _ -> false)

            if triggered then
                [ ActivateCognition ]
            elif subTick % (clock.SubTicksPerSecond / 2) = 0 then
                [ ActivateCognition ]
            else
                []

        let action =
            let triggered =
                events
                |> List.exists (function
                    | BallSecured _ -> true
                    | _ -> false)

            if triggered then
                events
                |> List.choose (function
                    | BallSecured _ as e -> Some(ActivateAction e)
                    | _ -> None)
            elif subTick % (clock.SubTicksPerSecond / 2) = 0 then
                [ ActivateAction(BallSecured(HomeClub, 0)) ]
            else
                []

        let referee =
            events
            |> List.choose (function
                | FoulOccurred _ as e -> Some(ActivateReferee e)
                | GoalScored _ as e -> Some(ActivateReferee e)
                | BallLoose as e -> Some(ActivateReferee e)
                | _ -> None)

        let team =
            events
            |> List.choose (function
                | BallSecured _ as e -> Some(ActivateTeam e)
                | BallLost _ as e -> Some(ActivateTeam e)
                | BallLoose as e -> Some(ActivateTeam e)
                | SetPieceAwarded _ as e -> Some(ActivateTeam e)
                | _ -> None)

        let manager =
            events
            |> List.choose (function
                | RedCardIssued _ as e -> Some(ActivateManager e)
                | MomentumShifted _ as e -> Some(ActivateManager e)
                | PlayerConditionCritical _ as e -> Some(ActivateManager e)
                | _ -> None)

        [ ActivatePhysics ] @ cognition @ action @ referee @ team @ manager
