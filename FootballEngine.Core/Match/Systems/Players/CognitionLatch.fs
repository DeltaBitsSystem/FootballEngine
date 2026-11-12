namespace FootballEngine.Core.Match

/// The one exchange between cognition and the body, at the start of every tick. The body's verdict on the
/// intention it followed goes back to cognition, then what cognition decided on the previous tick goes to the
/// body. Neither side touches the other's state outside this step, so they can run side by side.
module CognitionLatch =

    let private returnExits (frame: TeamFrame) =
        let intent, steered = frame.Intent, frame.SteerIntent

        for slot = 0 to frame.SlotCount - 1 do
            let exit = frame.SteerExit.[slot]

            if exit <> ExitTrigger.None then
                frame.SteerExit.[slot] <- ExitTrigger.None

                // A verdict on an intention cognition has since replaced would close the new one unfinished.
                if
                    intent.Kind.[slot] = steered.Kind.[slot]
                    && intent.TargetX.[slot] = steered.TargetX.[slot]
                    && intent.TargetY.[slot] = steered.TargetY.[slot]
                    && intent.CommittedUntil.[slot] = steered.CommittedUntil.[slot]
                then
                    intent.Phase.[slot] <- IntentPhase.Completed
                    intent.ExitTrigger.[slot] <- exit

    let private latch (team: TeamWorld) =
        let frame = team.Frame
        returnExits frame
        let source, copy = frame.Intent, frame.SteerIntent
        System.Array.Copy(source.Kind, copy.Kind, source.Kind.Length)
        System.Array.Copy(source.TargetX, copy.TargetX, source.TargetX.Length)
        System.Array.Copy(source.TargetY, copy.TargetY, source.TargetY.Length)
        System.Array.Copy(source.Phase, copy.Phase, source.Phase.Length)
        System.Array.Copy(source.CommittedUntil, copy.CommittedUntil, source.CommittedUntil.Length)
        System.Array.Copy(team.Shape.TargetsX, frame.SteerShapeX, frame.SteerShapeX.Length)
        System.Array.Copy(team.Shape.TargetsY, frame.SteerShapeY, frame.SteerShapeY.Length)
        System.Array.Copy(frame.Cognitive, frame.SteerCognitive, frame.SteerCognitive.Length)

        team.Latched <-
            { OurPhase = team.Blackboard.OurPhase
              InTransition = team.Defense.Episode |> Option.exists (fun episode -> episode.EndedAt.IsNone) }

        // A newer proposal replaces the one the body had not executed yet, as a fresh decision always has.
        if team.Proposals.Count > 0 then
            for KeyValue(slot, action) in team.Proposals do
                team.PendingActions[slot] <- action

            team.Proposals.Clear()

    let step: SystemStep =
        fun _ rng world _ ->
            latch world.Home
            latch world.Away
            rng
