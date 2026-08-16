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
