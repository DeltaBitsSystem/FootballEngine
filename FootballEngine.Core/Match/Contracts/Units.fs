namespace FootballEngine.Core.Match

[<Measure>]
type meter

[<Measure>]
type kilogram

[<Measure>]
type second

/// 1/40 s — the simulation step
[<Measure>]
type subtick

/// Match minute (1..90+)
[<Measure>]
type matchMin

/// Relative duration, same base unit as subtick
[<Measure>]
type tickDelta

[<Measure>]
type radian

[<Measure>]
type radianPerSecond = radian / second

[<Measure>]
type radianPerSecondSquared = radian / second^2

[<Measure>]
type meterPerSecond = meter / second

[<Measure>]
type meterPerSecondSquared = meter / second^2
