namespace FootballEngine.Components

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open FootballEngine.Core.Match
open FootballEngine.Domain

module CommentaryOverlay =

    let commentaryForEvent (e: MatchEvent) (playerName: string) : string =
        match e.Data with
        | EventData.Goal _ -> $"GOAL! {playerName} scores!"
        | EventData.Card(_, CardColor.Yellow) -> $"Yellow card for {playerName}"
        | EventData.Card(_, CardColor.Red)
        | EventData.RedCardIssued _ -> $"Red card! {playerName} is sent off!"
        | EventData.Save _ -> $"Great save!"
        | EventData.SetPiece(SetPieceKind.Corner, _) -> $"Corner kick"
        | EventData.Foul _ -> $"Foul!"
        | EventData.Shot(_, ShotResult.OnTarget _, _, _, _, _) -> $"Shot on target!"
        | EventData.Shot(_, ShotResult.OffTarget, _, _, _, _) -> $"Shot off target"
        | EventData.Pass(_, _, _, _, true, _, _, _, _) -> $"Nice pass"
        | _ -> ""

    let buildCommentary (text: string) : TextBlock =
        TextBlock(
            Text = text,
            FontSize = 16.0,
            FontWeight = FontWeight.Bold,
            Foreground = SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = Thickness(0.0, 0.0, 0.0, 10.0)
        )
