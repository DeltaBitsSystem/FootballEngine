namespace FootballEngine.Player.Intent

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


module IntentFrame =

    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)>]
    let fromMovementIntent (intent: MovementIntent) : IntentKind * float32 * float32 * int =
        match intent with
        | MaintainShape sp -> (IntentKind.MaintainShape, float32 sp.X, float32 sp.Y, 0)
        | MarkMan(pid, pos) -> (IntentKind.MarkMan, float32 pos.X, float32 pos.Y, pid)
        | PressBall pos -> (IntentKind.PressBall, float32 pos.X, float32 pos.Y, 0)
        | ExecuteRun _ ->
            // INVARIANT: La posición del run vive en ActiveRuns, no en el frame.
            // MovementEngine lee ActiveRuns directamente. El frame solo lleva el señal de tipo.
            (IntentKind.ExecuteRun, 0f, 0f, 0)
        | CoverSpace sp -> (IntentKind.CoverSpace, float32 sp.X, float32 sp.Y, 0)
        | SupportAttack sp -> (IntentKind.SupportAttack, float32 sp.X, float32 sp.Y, 0)
        | RecoverBall pos -> (IntentKind.RecoverBall, float32 pos.X, float32 pos.Y, 0)
        | MoveToSetPiecePos sp -> (IntentKind.MoveToSetPiecePos, float32 sp.X, float32 sp.Y, 0)

    let toMovementIntent
        (kind: IntentKind)
        (tx: float32)
        (ty: float32)
        (tpid: int)
        (fallback: Spatial)
        : MovementIntent =
        let sp =
            { X = float tx * 1.0<meter>
              Y = float ty * 1.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }

        match kind with
        | IntentKind.MaintainShape -> MaintainShape sp
        | IntentKind.MarkMan -> MarkMan(tpid, sp)
        | IntentKind.PressBall -> PressBall sp
        | IntentKind.ExecuteRun -> MaintainShape fallback
        | IntentKind.CoverSpace -> CoverSpace sp
        | IntentKind.SupportAttack -> SupportAttack sp
        | IntentKind.RecoverBall -> RecoverBall sp
        | IntentKind.MoveToSetPiecePos -> MoveToSetPiecePos sp
        | IntentKind.TackleAttempt -> PressBall sp
        | IntentKind.Idle -> MaintainShape fallback
        | _ -> MaintainShape fallback
