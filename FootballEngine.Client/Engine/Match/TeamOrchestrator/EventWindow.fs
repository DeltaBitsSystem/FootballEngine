namespace FootballEngine.TeamOrchestrator

open FootballEngine.Domain
open FootballEngine.Types

module EventWindow =
    let recentEvents (windowSubTicks: int) (events: ResizeArray<MatchEvent>) : MatchEvent list =
        let n = events.Count

        if n = 0 then
            []
        else
            let cutoff = events[n - 1].SubTick - windowSubTicks

            let rec build idx acc =
                if idx < 0 || events[idx].SubTick < cutoff then
                    acc
                else
                    build (idx - 1) (events[idx] :: acc)

            build (n - 1) []

    [<Struct>]
    type EventRates =
        { ShortPassRate: float
          PressRate: float
          FlankRate: float }

    let computeRates
        (windowSubTicks: int)
        (events: ResizeArray<MatchEvent>)
        (currentSubTick: int)
        : EventRates * MatchEvent list =
        let halfLife = 10.0 * 40.0
        let weight age = System.Math.Exp(-float age / halfLife)

        let n = events.Count

        if n = 0 then
            { ShortPassRate = 0.5
              PressRate = 0.5
              FlankRate = 0.5 },
            []
        else
            let mutable spTotal = 0.0
            let mutable spSuccess = 0.0
            let mutable prTotal = 0.0
            let mutable prSuccess = 0.0
            let mutable flTotal = 0.0
            let mutable flSuccess = 0.0

            let recentList = System.Collections.Generic.List<MatchEvent>()

            for i = n - 1 downto 0 do
                let e = events.[i]
                let age = currentSubTick - e.SubTick

                if age <= windowSubTicks then
                    recentList.Add(e)

                    let w = weight age

                    match e.Type with
                    | MatchEventType.PassCompleted _ ->
                        spTotal <- spTotal + w
                        spSuccess <- spSuccess + w
                    | MatchEventType.PassIncomplete _ -> spTotal <- spTotal + w
                    | MatchEventType.TackleSuccess ->
                        prTotal <- prTotal + w
                        prSuccess <- prSuccess + w
                    | MatchEventType.TackleFail -> prTotal <- prTotal + w
                    | MatchEventType.DribbleFail ->
                        prTotal <- prTotal + w
                        prSuccess <- prSuccess + w
                    | MatchEventType.CrossAttempt true ->
                        flTotal <- flTotal + w
                        flSuccess <- flSuccess + w
                    | MatchEventType.CrossAttempt false -> flTotal <- flTotal + w
                    | _ -> ()

            let result = List.ofSeq recentList |> List.rev

            let calcRate total success =
                if total > 0.0 then success / total else 0.5

            { ShortPassRate = calcRate spTotal spSuccess
              PressRate = calcRate prTotal prSuccess
              FlankRate = calcRate flTotal flSuccess },
            result


    let shortPassSuccessRate (events: MatchEvent list) =
        let mutable total, success = 0, 0

        for e in events do
            match e.Type with
            | MatchEventType.PassCompleted _ ->
                total <- total + 1
                success <- success + 1
            | MatchEventType.PassIncomplete _ -> total <- total + 1
            | _ -> ()

        if total = 0 then 0.5 else float success / float total

    let pressSuccessRate (events: MatchEvent list) =
        let mutable total, success = 0, 0

        for e in events do
            match e.Type with
            | MatchEventType.TackleSuccess
            | MatchEventType.DribbleFail ->
                total <- total + 1
                success <- success + 1
            | MatchEventType.TackleFail
            | MatchEventType.DribbleSuccess -> total <- total + 1
            | _ -> ()

        if total = 0 then 0.5 else float success / float total

    let flankSuccessRate (events: MatchEvent list) =
        let mutable total, success = 0, 0

        for e in events do
            match e.Type with
            | MatchEventType.CrossAttempt true ->
                total <- total + 1
                success <- success + 1
            | MatchEventType.CrossAttempt false -> total <- total + 1
            | _ -> ()

        if total = 0 then 0.5 else float success / float total

    let consecutivePossessionLosses (events: MatchEvent list) =
        let mutable count = 0
        let mutable counting = true

        let revEvents = List.rev events

        for e in revEvents do
            if counting then
                match e.Type with
                | MatchEventType.PassIncomplete _
                | MatchEventType.DribbleFail
                | MatchEventType.FoulCommitted -> count <- count + 1
                | _ -> counting <- false

        count

    let patternResults (pattern: AttackPattern) (events: MatchEvent list) : PatternRecord =
        let mutable attempts = 0
        let mutable successes = 0
        let mutable totalXG = 0.0

        for e in events do
            let isMatch, isSuccess =
                match pattern, e.Type with
                | AttackPattern.LeftFlank, MatchEventType.CrossAttempt ok
                | AttackPattern.RightFlank, MatchEventType.CrossAttempt ok -> true, ok

                | AttackPattern.Central, MatchEventType.PassCompleted _ -> true, true
                | AttackPattern.Central, MatchEventType.PassIncomplete _ -> true, false

                | AttackPattern.LongBall, MatchEventType.LongBall ok -> true, ok

                | AttackPattern.ShortPass, MatchEventType.PassCompleted _ -> true, true
                | AttackPattern.ShortPass, MatchEventType.PassIncomplete _ -> true, false

                | _ -> false, false

            if isMatch then
                attempts <- attempts + 1

                if isSuccess then
                    successes <- successes + 1

            match e.Type with
            | MatchEventType.Goal -> totalXG <- totalXG + 0.3
            | MatchEventType.ShotOffTarget
            | MatchEventType.ShotBlocked
            | MatchEventType.Save -> totalXG <- totalXG + 0.1
            | _ -> ()

        { Pattern = pattern
          Attempts = max attempts 1
          Successes = successes
          TotalXG = totalXG }
