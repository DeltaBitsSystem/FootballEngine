namespace FootballEngine.Domain

open System

/// The transfer AI that "always buys well": enforces the bench-warmer
/// invariant before any signing, and only overpays when the gap is real.
module SquadPlanner =

    /// The roles the AI tracks for the warmers invariant — the same spine
    /// SquadAnalysis.assessNeeds uses, so needs and signings speak the same
    /// language.
    let private roles: Position list = [ GK; DC; MC; ST; AML; AMR ]

    /// A player who rots: old enough to be a squad player and plays too
    /// little. A missing experience record counts as zero matches — a player
    /// who never appears in the logs is by definition not playing.
    let benchWarmers
        (cfg: TransferConfig)
        (now: DateTime)
        (squad: Player list)
        (experiences: Map<PlayerId, PlayerExperience>)
        : Player list =
        squad
        |> List.filter (fun p ->
            let matches =
                experiences
                |> Map.tryFind p.Id
                |> Option.map (fun e -> e.TotalMatchesPlayed)
                |> Option.defaultValue 0

            Player.age now p >= cfg.BenchWarmerAgeMin
            && matches < cfg.MinMatchesForSquadPlayer)

    /// A role is open for signings while its warmer count stays at or under
    /// the tolerated ceiling — the cap is "no more than N", not "fewer than N".
    let openPositions
        (cfg: TransferConfig)
        (now: DateTime)
        (squad: Player list)
        (experiences: Map<PlayerId, PlayerExperience>)
        : Set<Position> =
        let warmersPerRole =
            benchWarmers cfg now squad experiences
            |> List.countBy (fun p -> p.Position)
            |> Map.ofList

        roles
        |> List.filter (fun pos ->
            let count = warmersPerRole |> Map.tryFind pos |> Option.defaultValue 0

            count <= cfg.MaxOver25BenchWarmers)
        |> Set.ofList

    /// Signing into a closed role is throwing money at the rot: drop it. The
    /// target's role is resolved through the candidate pool.
    let refineTargets
        (cfg: TransferConfig)
        (now: DateTime)
        (squad: Player list)
        (experiences: Map<PlayerId, PlayerExperience>)
        (pool: Map<PlayerId, Player>)
        (targets: TransferTarget list)
        : TransferTarget list =
        let openPos = openPositions cfg now squad experiences

        targets
        |> List.filter (fun t ->
            match pool |> Map.tryFind t.PlayerId with
            | Some p -> openPos.Contains p.Position
            | None -> false)

    /// A genuine gap may be filled above market value; a pure upgrade must
    /// not be overpaid for — the role is already covered.
    let cappedFee (cfg: TransferConfig) (need: SquadNeed) (baseFee: decimal) : decimal =
        match need with
        | NeedsPosition _
        | NeedsDepth _ -> baseFee * (1.0m + cfg.OverpayTolerance)
        | NeedsQualityUpgrade _ -> baseFee
