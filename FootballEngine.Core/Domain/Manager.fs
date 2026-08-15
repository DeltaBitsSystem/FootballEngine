namespace FootballEngine.Domain

type SquadNeed =
    | NeedsPosition of Position
    | NeedsDepth of Position
    | NeedsQualityUpgrade of aboveSkill: int

type TransferTarget =
    { PlayerId: PlayerId
      Priority: float
      MaxFee: decimal }

type ManagerIntent =
    { ClubId: ClubId
      CoachId: StaffId
      Needs: SquadNeed list
      Targets: TransferTarget list
      PlayersToSell: PlayerId list
      BudgetAllocation: decimal }

module SquadAnalysis =

    let assessNeeds (currentDate: System.DateTime) (squad: Player list) : SquadNeed list =
        let positionCounts =
            squad
            |> List.choose (fun p ->
                match p.Status with
                | Injured _ -> None
                | _ -> Some p.Position)
            |> List.countBy id
            |> Map.ofList

        let avgSkill =
            match squad with
            | [] -> 0
            | _ -> squad |> List.averageBy (fun p -> float p.CurrentSkill) |> int

        [ for pos in [ GK; DC; MC; ST; AML; AMR ] do
              let count = positionCounts |> Map.tryFind pos |> Option.defaultValue 0

              if count = 0 then
                  yield NeedsPosition pos
              elif count = 1 then
                  yield NeedsDepth pos
          if avgSkill < 65 then
              yield NeedsQualityUpgrade avgSkill ]

module TargetSelection =

    let selectTargets
        (_coach: Staff)
        (_club: Club)
        (needs: SquadNeed list)
        (candidates: Player list)
        : TransferTarget list =
        needs
        |> List.collect (fun need ->
            let posOpt =
                match need with
                | NeedsPosition p
                | NeedsDepth p -> Some p
                | NeedsQualityUpgrade _ -> None

            candidates
            |> List.filter (fun p ->
                match posOpt with
                | Some pos -> p.Position = pos
                | None -> true)
            |> List.sortByDescending (fun p -> p.CurrentSkill)
            |> List.truncate 2
            |> List.map (fun p ->
                { PlayerId = p.Id
                  Priority =
                    match need with
                    | NeedsPosition _ -> 3.0
                    | NeedsDepth _ -> 2.0
                    | _ -> 1.0
                  MaxFee = Player.playerValue p.CurrentSkill |> decimal }))
        |> List.distinctBy (fun t -> t.PlayerId)
