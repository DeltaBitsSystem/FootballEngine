namespace FootballEngine

module ColumnDefs =

    type ColWidth =
        | Auto
        | Fixed of float
        | Star of float

    type ColDef = { Width: ColWidth }

    let col w = { Width = w }

    let toGridString (cols: ColDef list) : string =
        cols
        |> List.map (fun c ->
            match c.Width with
            | Auto -> "Auto"
            | Fixed px -> string (int px)
            | Star 1.0 -> "*"
            | Star f -> $"{f}*")
        |> String.concat ", "

    // PlayerView.row/tableHeader address columns 0..4 (PLAYER, POS, AGE, CA/PA, VALUE);
    // fewer definitions made Avalonia stack the extra children on top of each other
    // (invisible names, overlapping headers).
    let playerListCols =
        [ col (Star 1.0)
          col (Fixed 52)
          col (Fixed 48)
          col (Fixed 110)
          col (Fixed 76) ]

    let transferListCols =
        [ col Auto; col (Star 1.0); col Auto; col (Fixed 70); col (Fixed 70); col Auto ]

    let standingsCols =
        [ col (Fixed 24)
          col (Star 1.0)
          col (Fixed 60)
          col (Fixed 36)
          col (Fixed 36) ]

    let offerCols = [ col (Star 1.0); col Auto; col (Fixed 120); col (Fixed 32) ]

    let inboxListCols = [ col Auto; col (Star 1.0); col Auto ]
