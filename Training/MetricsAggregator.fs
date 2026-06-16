namespace Training

open FootballEngine.Domain
open FSharp.Stats

module MetricsAggregator =

    let private goalsPerMatch (results: SimResult list) : MatchMetrics =
        let metricsList =
            results |> List.map (fun r -> MatchMetrics.extract r.Events r.HomeId r.AwayId)

        MatchMetrics.average metricsList

    let private momentumVariance (results: SimResult list) : float =
        let momentums = results |> List.map (fun r -> r.FinalState.Momentum)

        if List.isEmpty momentums then
            0.0
        else
            let mean = momentums |> List.average
            let variance = momentums |> List.averageBy (fun m -> (m - mean) ** 2.0)
            variance

    let private eventDistributionEntropy (results: SimResult list) : float =
        let totalEvents = results |> List.sumBy (fun r -> r.Events |> List.length)

        if totalEvents = 0 then
            0.0
        else
            let minuteBuckets = Array.zeroCreate 90

            for r in results do
                for e in r.Events do
                    let minute = max 0 (min 89 (e.SubTick / 1200))
                    minuteBuckets.[minute] <- minuteBuckets.[minute] + 1

            let probs =
                minuteBuckets
                |> Array.map (fun c -> float c / float totalEvents)
                |> Array.filter (fun p -> p > 0.0)

            probs |> Array.sumBy (fun p -> -p * log p)

    let private lateGoalFrequency (results: SimResult list) : float =
        let totalGoals = results |> List.sumBy (fun r -> r.HomeScore + r.AwayScore)

        if totalGoals = 0 then
            0.0
        else
            let lateGoals =
                results
                |> List.sumBy (fun r ->
                    r.Events
                    |> List.filter (fun e ->
                        match e.Type with
                        | MatchEventType.Goal -> e.SubTick > 75 * 1200
                        | _ -> false)
                    |> List.length)

            float lateGoals / float totalGoals

    let private leadChanges (results: SimResult list) : int =
        if List.isEmpty results then
            0
        else
            let changes =
                results
                |> List.averageBy (fun r ->
                    let mutable homeLead = 0
                    let mutable changes = 0
                    let mutable lastLeader = 0

                    for e in r.Events do
                        match e.Type with
                        | MatchEventType.Goal ->
                            if e.ClubId = r.HomeId then
                                homeLead <- homeLead + 1
                            else
                                homeLead <- homeLead - 1

                            let currentLeader = sign homeLead

                            if currentLeader <> lastLeader && currentLeader <> 0 then
                                changes <- changes + 1

                            lastLeader <- currentLeader
                        | _ -> ()

                    float changes)

            int changes

    let private comebackRate (results: SimResult list) : float =
        let comebacks =
            results
            |> List.sumBy (fun r ->
                let mutable maxHomeLead = 0
                let mutable maxAwayLead = 0
                let mutable homeLead = 0

                for e in r.Events do
                    match e.Type with
                    | MatchEventType.Goal ->
                        if e.ClubId = r.HomeId then
                            homeLead <- homeLead + 1
                        else
                            homeLead <- homeLead - 1

                        maxHomeLead <- max maxHomeLead homeLead
                        maxAwayLead <- max maxAwayLead (-homeLead)
                    | _ -> ()

                let finalResult = sign (r.HomeScore - r.AwayScore)

                if maxHomeLead > 0 && finalResult < 0 then 1
                elif maxAwayLead > 0 && finalResult > 0 then 1
                else 0)

        let total = results |> List.length
        if total = 0 then 0.0 else float comebacks / float total

    let aggregate (results: SimResult list) : MatchMetrics * ExperienceMetrics =
        let matchMetrics = goalsPerMatch results

        let exp =
            { MomentumVariance = momentumVariance results
              EventDistributionEntropy = eventDistributionEntropy results
              LateGoalFrequency = lateGoalFrequency results
              LeadChanges = leadChanges results
              ComebackRate = comebackRate results }

        matchMetrics, exp
