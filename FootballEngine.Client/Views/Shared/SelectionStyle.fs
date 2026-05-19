namespace FootballEngine

module SelectionStyle =

    let rowBg (isSelected: bool) : string =
        if isSelected then Theme.AccentLight else "Transparent"

    let rowBorder (isSelected: bool) : string =
        if isSelected then Theme.Accent else "Transparent"

    let rowBorderColor (isSelected: bool) (color: string) : string =
        if isSelected then color else "Transparent"

    let rowBgColor (isSelected: bool) (color: string) : string =
        if isSelected then color + "18" else "Transparent"
