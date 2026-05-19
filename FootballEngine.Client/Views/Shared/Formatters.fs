namespace FootballEngine

open System
open FootballEngine.Domain

module Formatters =

    let money (v: decimal) : string =
        if v >= 1_000_000m then $"${v / 1_000_000m:F1}M"
        elif v >= 1_000m    then $"${v / 1_000m:F0}K"
        else                     $"${v:F0}"

    let salary (v: decimal) : string = $"{money v}/wk"

    let age (currentDate: DateTime) (p: Player) : int =
        Player.age currentDate p

    let pct (v: int) : string = $"{v}%%"

    let relativeDate (date: DateTime) (currentDate: DateTime) : string =
        let daysAgo = int (currentDate - date).TotalDays
        if daysAgo = 0   then "Today"
        elif daysAgo = 1 then "Yesterday"
        elif daysAgo < 7 then $"{daysAgo} days ago"
        else date.ToString("dd MMM yyyy")

    let shortDate (date: DateTime) : string = date.ToString("dd MMM")
    let fullDate  (date: DateTime) : string = date.ToString("dd MMM yyyy")

    let mult (v: float) : string =
        if v >= 1.0 then $"+{v:F2}x" else $"{v:F2}x"
