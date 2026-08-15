namespace FootballEngine.Domain



module FormationData =
    let private createSlot index pos x y =
        { Index = index
          Role = pos
          X = x
          Y = y
          PlayerId = None }

    let getFormation (formation: Formation) : LineupSlot list =
        match formation with
        | F442 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.85 0.75
              createSlot 5 ML 0.10 0.45
              createSlot 6 MC 0.40 0.50
              createSlot 7 MC 0.60 0.50
              createSlot 8 MR 0.85 0.45
              createSlot 9 ST 0.35 0.15
              createSlot 10 ST 0.65 0.15 ]
        | F442Diamond ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.85 0.75
              createSlot 5 DM 0.50 0.60
              createSlot 6 MC 0.25 0.45
              createSlot 7 MC 0.75 0.45
              createSlot 8 AMC 0.50 0.30
              createSlot 9 ST 0.35 0.15
              createSlot 10 ST 0.65 0.15 ]
        | F433 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 MC 0.30 0.55
              createSlot 6 DM 0.50 0.62
              createSlot 7 MC 0.70 0.55
              createSlot 8 AML 0.15 0.20
              createSlot 9 ST 0.50 0.15
              createSlot 10 AMR 0.85 0.20 ]
        | F433Flat ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 MC 0.25 0.50
              createSlot 6 MC 0.50 0.50
              createSlot 7 MC 0.75 0.50
              createSlot 8 AML 0.15 0.20
              createSlot 9 ST 0.50 0.15
              createSlot 10 AMR 0.85 0.20 ]
        | F451 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 ML 0.10 0.48
              createSlot 6 MC 0.30 0.52
              createSlot 7 MC 0.50 0.55
              createSlot 8 MC 0.70 0.52
              createSlot 9 MR 0.90 0.48
              createSlot 10 ST 0.50 0.15 ]
        | F4141 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 DM 0.50 0.63
              createSlot 6 ML 0.10 0.45
              createSlot 7 MC 0.35 0.48
              createSlot 8 MC 0.65 0.48
              createSlot 9 MR 0.90 0.45
              createSlot 10 ST 0.50 0.15 ]
        | F4231 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 DM 0.35 0.62
              createSlot 6 DM 0.65 0.62
              createSlot 7 AML 0.15 0.35
              createSlot 8 AMC 0.50 0.32
              createSlot 9 AMR 0.85 0.35
              createSlot 10 ST 0.50 0.15 ]
        | F4312 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 MC 0.25 0.52
              createSlot 6 MC 0.50 0.55
              createSlot 7 MC 0.75 0.52
              createSlot 8 AMC 0.50 0.33
              createSlot 9 ST 0.35 0.15
              createSlot 10 ST 0.65 0.15 ]
        | F4321 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DL 0.10 0.75
              createSlot 2 DC 0.35 0.80
              createSlot 3 DC 0.65 0.80
              createSlot 4 DR 0.90 0.75
              createSlot 5 MC 0.25 0.55
              createSlot 6 MC 0.50 0.58
              createSlot 7 MC 0.75 0.55
              createSlot 8 AML 0.30 0.30
              createSlot 9 AMR 0.70 0.30
              createSlot 10 ST 0.50 0.15 ]
        | F352 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DC 0.20 0.78
              createSlot 2 DC 0.50 0.82
              createSlot 3 DC 0.80 0.78
              createSlot 4 WBL 0.10 0.52
              createSlot 5 MC 0.30 0.55
              createSlot 6 MC 0.50 0.58
              createSlot 7 MC 0.70 0.55
              createSlot 8 WBR 0.90 0.52
              createSlot 9 ST 0.35 0.15
              createSlot 10 ST 0.65 0.15 ]
        | F343 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DC 0.20 0.78
              createSlot 2 DC 0.50 0.82
              createSlot 3 DC 0.80 0.78
              createSlot 4 MC 0.20 0.52
              createSlot 5 MC 0.45 0.55
              createSlot 6 MC 0.65 0.55
              createSlot 7 MC 0.85 0.52
              createSlot 8 AML 0.20 0.20
              createSlot 9 ST 0.50 0.15
              createSlot 10 AMR 0.80 0.20 ]
        | F3421 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 DC 0.20 0.78
              createSlot 2 DC 0.50 0.82
              createSlot 3 DC 0.80 0.78
              createSlot 4 WBL 0.10 0.52
              createSlot 5 MC 0.35 0.55
              createSlot 6 MC 0.65 0.55
              createSlot 7 WBR 0.90 0.52
              createSlot 8 AML 0.30 0.28
              createSlot 9 AMR 0.70 0.28
              createSlot 10 ST 0.50 0.15 ]
        | F532 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 WBL 0.08 0.65
              createSlot 2 DC 0.25 0.78
              createSlot 3 DC 0.50 0.82
              createSlot 4 DC 0.75 0.78
              createSlot 5 WBR 0.92 0.65
              createSlot 6 MC 0.30 0.52
              createSlot 7 MC 0.50 0.55
              createSlot 8 MC 0.70 0.52
              createSlot 9 ST 0.35 0.15
              createSlot 10 ST 0.65 0.15 ]
        | F541 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 WBL 0.08 0.65
              createSlot 2 DC 0.25 0.78
              createSlot 3 DC 0.50 0.82
              createSlot 4 DC 0.75 0.78
              createSlot 5 WBR 0.92 0.65
              createSlot 6 ML 0.10 0.48
              createSlot 7 MC 0.35 0.52
              createSlot 8 MC 0.65 0.52
              createSlot 9 MR 0.90 0.48
              createSlot 10 ST 0.50 0.15 ]
        | F523 ->
            [ createSlot 0 GK 0.50 0.90
              createSlot 1 WBL 0.08 0.65
              createSlot 2 DC 0.25 0.78
              createSlot 3 DC 0.50 0.82
              createSlot 4 DC 0.75 0.78
              createSlot 5 WBR 0.92 0.65
              createSlot 6 MC 0.35 0.52
              createSlot 7 MC 0.65 0.52
              createSlot 8 AML 0.20 0.20
              createSlot 9 ST 0.50 0.15
              createSlot 10 AMR 0.80 0.20 ]
