namespace FootballEngine.Player.Decision

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.SimStateOps
open FootballEngine.Types



module ActionContext =
    let build (ctx: MatchContext) (state: SimState) : ActionContext =
        let attSide = state.AttackingSide
        let att = buildTeamPerspective attSide ctx state
        let def = buildTeamPerspective (ClubSide.flip attSide) ctx state
        let zone = ofBallX state.Ball.Position.X att.AttackDir
        let momentum = PhysicsContract.momentumDelta att.AttackDir state.Momentum

        { Att = att
          Def = def
          Zone = zone
          Momentum = momentum }
