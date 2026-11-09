namespace FootballEngine.Core.Match

/// Pitch geometry in meters (FIFA: 105 x 68).
module Pitch =

    [<Literal>]
    let HalfLength = 52.5<meter>

    [<Literal>]
    let HalfWidth = 34.0<meter>

    [<Literal>]
    let Length = 105.0<meter>

    [<Literal>]
    let Width = 68.0<meter>

    [<Literal>]
    let GoalHalfWidth = 3.66<meter>

    [<Literal>]
    let GoalDepth = 2.0<meter>

    /// Height of the crossbar's lower edge.
    [<Literal>]
    let GoalHeight = 2.44<meter>

    /// Laws of the Game cap posts and bar at 12 cm; posts sit centred on the goal line.
    [<Literal>]
    let PostRadius = 0.06<meter>

    [<Literal>]
    let PenaltyAreaHalfWidth = 20.16<meter>

    [<Literal>]
    let PenaltyAreaDepth = 16.5<meter>

    [<Literal>]
    let GoalAreaHalfWidth = 9.16<meter>

    [<Literal>]
    let GoalAreaDepth = 5.5<meter>

    [<Literal>]
    let PenaltySpotDistance = 11.0<meter>

    /// Home attacks +X, defends -X.
    let homeGoalX = -HalfLength

    let awayGoalX = HalfLength

    [<Literal>]
    let BounceLimit = 100.0<meter>

    [<Literal>]
    let MaxTrajectory = 70.0<meter>

    /// How far inside the touchline a throw-in ball is placed by the referee.
    /// Keeps the restart legally in-bounds and off the band.
    [<Literal>]
    let ThrowInInset = 0.3<meter>

    let isInPenaltyArea (attackingSide: ClubSide) (x: float<meter>) (y: float<meter>) : bool =
        let goalX =
            match attackingSide with
            | ClubSide.Home -> awayGoalX
            | ClubSide.Away -> homeGoalX

        let direction = if attackingSide = ClubSide.Home then 1.0 else -1.0
        let depth = abs (float x - float goalX)
        let isInsideFromGoalLine = (float x - float goalX) * direction <= 0.0

        depth <= float PenaltyAreaDepth
        && abs (float y) <= float PenaltyAreaHalfWidth
        && isInsideFromGoalLine
