namespace FootballEngine.Core.Match

/// The world cognition reads while the body moves. Its teams are copies of the live ones: every body field the
/// body writes during a tick is an array of the view's own, filled at the barrier; everything else is the live
/// object itself. After cognition the team fields it may have reassigned go back to the live teams.
///
/// The field lists are the ownership contract. A test fails when TeamFrame or TeamWorld gains a field neither
/// list names, so a new field cannot silently be shared between the two halves of a tick.
module CognitionView =

    /// Per-slot arrays the body writes during a tick: cognition reads its own copy taken at the barrier.
    let bodyFrameFields =
        [ "PosX"; "PosY"; "VelX"; "VelY"; "PosZ"; "VelZ"; "TargetVelX"; "TargetVelY"; "TargetFacing"
          "JumpRequested"; "JumpSequence"; "BodyFacing"; "BodyAngularVelocity"; "BodyAngularAcceleration"
          "BodyTravelDistance"; "GaitPhase"; "ContactGeneration"; "ContactState"; "ContactActions"; "Condition"
          "SteerExit" ]

    /// Frame fields cognition and the body share by reference: cognition's own state, or state only the
    /// barrier writes.
    let sharedFrameFields =
        [ "Mental"; "Intent"; "SteerIntent"; "SteerShapeX"; "SteerShapeY"; "SteerCognitive"; "Occupancy"
          "SlotRoles"; "DefensiveRoles"; "CollectiveIntents"; "SupportX"; "SupportY"; "VisibilityTm"
          "VisibilityOpp"; "OppsNear"; "PressCount"; "LaneClear"; "FaceCone"; "Cognitive"; "RoleBias"; "RunLookup"
          "ActiveCount"; "SlotCount"; "ActiveCache" ]

    /// Team fields the body reassigns during a tick: never copied back from cognition's view.
    let bodyTeamFields = [ "Frame"; "Dive" ]

    /// Team fields that are the same object in the view and the live team for the whole match.
    let fixedTeamFields =
        [ "RosterIds"; "Defense"; "Participation"; "Proposals"; "PendingActions"; "Attack" ]

    /// Team fields copied into the view at the barrier and back to the live team after cognition.
    let cognitionTeamFields =
        [ "Tactics"; "Memory"; "Cards"; "SubsUsed"; "Shape"; "Directive"; "Blackboard"; "Emergent"; "Intercept"
          "Stats"; "CFrame"; "EventWindow"; "Substitution"; "LastCarrierSlot"; "LastRoleAssignmentTick"
          "FormationCache"; "LateralPairs"; "GoalkeeperSlot"; "OwnPositions"; "MirroredPositions"
          "FormationTargets"; "EvaluatorScratch"; "Latched" ]

    let private copyFrame (live: TeamFrame) =
        { live with
            PosX = Array.copy live.PosX
            PosY = Array.copy live.PosY
            VelX = Array.copy live.VelX
            VelY = Array.copy live.VelY
            PosZ = Array.copy live.PosZ
            VelZ = Array.copy live.VelZ
            TargetVelX = Array.copy live.TargetVelX
            TargetVelY = Array.copy live.TargetVelY
            TargetFacing = Array.copy live.TargetFacing
            JumpRequested = Array.copy live.JumpRequested
            JumpSequence = Array.copy live.JumpSequence
            BodyFacing = Array.copy live.BodyFacing
            BodyAngularVelocity = Array.copy live.BodyAngularVelocity
            BodyAngularAcceleration = Array.copy live.BodyAngularAcceleration
            BodyTravelDistance = Array.copy live.BodyTravelDistance
            GaitPhase = Array.copy live.GaitPhase
            ContactGeneration = Array.copy live.ContactGeneration
            ContactState = Array.copy live.ContactState
            ContactActions = Array.copy live.ContactActions
            Condition = Array.copy live.Condition
            SteerExit = Array.copy live.SteerExit }

    let inline private blit (source: 'T[]) (target: 'T[]) =
        System.Array.Copy(source, target, source.Length)

    /// The live frame with the view's own body arrays, refilled from the live ones. TeamFrame is a value, so
    /// every shared field comes along with the copy.
    let private syncFrame (live: TeamFrame) (mine: TeamFrame) : TeamFrame =
        blit live.PosX mine.PosX
        blit live.PosY mine.PosY
        blit live.VelX mine.VelX
        blit live.VelY mine.VelY
        blit live.PosZ mine.PosZ
        blit live.VelZ mine.VelZ
        blit live.TargetVelX mine.TargetVelX
        blit live.TargetVelY mine.TargetVelY
        blit live.TargetFacing mine.TargetFacing
        blit live.JumpRequested mine.JumpRequested
        blit live.JumpSequence mine.JumpSequence
        blit live.BodyFacing mine.BodyFacing
        blit live.BodyAngularVelocity mine.BodyAngularVelocity
        blit live.BodyAngularAcceleration mine.BodyAngularAcceleration
        blit live.BodyTravelDistance mine.BodyTravelDistance
        blit live.GaitPhase mine.GaitPhase
        blit live.ContactGeneration mine.ContactGeneration
        blit live.ContactState mine.ContactState
        blit live.ContactActions mine.ContactActions
        blit live.Condition mine.Condition
        blit live.SteerExit mine.SteerExit

        { live with
            PosX = mine.PosX
            PosY = mine.PosY
            VelX = mine.VelX
            VelY = mine.VelY
            PosZ = mine.PosZ
            VelZ = mine.VelZ
            TargetVelX = mine.TargetVelX
            TargetVelY = mine.TargetVelY
            TargetFacing = mine.TargetFacing
            JumpRequested = mine.JumpRequested
            JumpSequence = mine.JumpSequence
            BodyFacing = mine.BodyFacing
            BodyAngularVelocity = mine.BodyAngularVelocity
            BodyAngularAcceleration = mine.BodyAngularAcceleration
            BodyTravelDistance = mine.BodyTravelDistance
            GaitPhase = mine.GaitPhase
            ContactGeneration = mine.ContactGeneration
            ContactState = mine.ContactState
            ContactActions = mine.ContactActions
            Condition = mine.Condition
            SteerExit = mine.SteerExit }

    let private syncTeam (live: TeamWorld) (view: TeamWorld) =
        // A substitution can give the live team a new frame; the view then needs copies of the new one.
        view.Frame <-
            if view.Frame.SlotCount <> live.Frame.SlotCount then
                copyFrame live.Frame
            else
                syncFrame live.Frame view.Frame

        view.Dive <- live.Dive
        view.Tactics <- live.Tactics
        view.Memory <- live.Memory
        view.Cards <- live.Cards
        view.SubsUsed <- live.SubsUsed
        view.Shape <- live.Shape
        view.Directive <- live.Directive
        view.Blackboard <- live.Blackboard
        view.Emergent <- live.Emergent
        view.Intercept <- live.Intercept
        view.Stats <- live.Stats
        view.CFrame <- live.CFrame
        view.EventWindow <- live.EventWindow
        view.Substitution <- live.Substitution
        view.LastCarrierSlot <- live.LastCarrierSlot
        view.LastRoleAssignmentTick <- live.LastRoleAssignmentTick
        view.FormationCache <- live.FormationCache
        view.LateralPairs <- live.LateralPairs
        view.GoalkeeperSlot <- live.GoalkeeperSlot
        view.OwnPositions <- live.OwnPositions
        view.MirroredPositions <- live.MirroredPositions
        view.FormationTargets <- live.FormationTargets
        view.EvaluatorScratch <- live.EvaluatorScratch
        view.Latched <- live.Latched

    let private commitTeam (view: TeamWorld) (live: TeamWorld) =
        live.Tactics <- view.Tactics
        live.Memory <- view.Memory
        live.Cards <- view.Cards
        live.SubsUsed <- view.SubsUsed
        live.Shape <- view.Shape
        live.Directive <- view.Directive
        live.Blackboard <- view.Blackboard
        live.Emergent <- view.Emergent
        live.Intercept <- view.Intercept
        live.Stats <- view.Stats
        live.CFrame <- view.CFrame
        live.EventWindow <- view.EventWindow
        live.Substitution <- view.Substitution
        live.LastCarrierSlot <- view.LastCarrierSlot
        live.LastRoleAssignmentTick <- view.LastRoleAssignmentTick
        live.FormationCache <- view.FormationCache
        live.LateralPairs <- view.LateralPairs
        live.GoalkeeperSlot <- view.GoalkeeperSlot
        live.OwnPositions <- view.OwnPositions
        live.MirroredPositions <- view.MirroredPositions
        live.FormationTargets <- view.FormationTargets
        live.EvaluatorScratch <- view.EvaluatorScratch
        live.Latched <- view.Latched

    let private syncWorld (live: World) (view: World) =
        view.State <- live.State
        view.Officials <- live.Officials
        view.NextRestartId <- live.NextRestartId
        view.NextContactId <- live.NextContactId

    let private journal (world: World) =
        match world.Cognition.Direct with
        | ValueSome direct -> direct.Observation
        | ValueNone -> ObservationBuffer.Deferred world.Observation

    /// The live teams seen through the journal: cognition run before the body, on one thread.
    let direct (world: World) : World =
        match world.Cognition.Direct with
        | ValueSome direct ->
            syncWorld world direct
            direct
        | ValueNone ->
            let direct = { world with Observation = ObservationBuffer.Deferred world.Observation }
            world.Cognition.Direct <- ValueSome direct
            direct

    /// Cognition's own copy of the world as the tick began, refreshed in place.
    let refresh (world: World) : World =
        match world.Cognition.View with
        | ValueSome view ->
            syncWorld world view
            syncTeam world.Home view.Home
            syncTeam world.Away view.Away
            view
        | ValueNone ->
            let team (live: TeamWorld) = { live with Frame = copyFrame live.Frame }

            let view =
                { world with
                    Home = team world.Home
                    Away = team world.Away
                    Observation = journal world }

            world.Cognition.View <- ValueSome view
            view

    /// Hands the team fields cognition may have reassigned back to the live teams.
    let commit (view: World) (world: World) =
        commitTeam view.Home world.Home
        commitTeam view.Away world.Away
