namespace FootballEngine.Types

// ── SlotRole ──────────────────────────────────────────────────────────────────
//
// Rol colectivo que TeamOrchestrator asigna a cada slot.
// BatchDecision lo lee del TeamFrame para sesgar los scores individuales.
// TeamOrchestrator lo escribe en el TeamFrame en cada tick colectivo.
//
// Vive en Types/ porque es el contrato entre los dos pipelines.
// ─────────────────────────────────────────────────────────────────────────────

type SlotRole =
    | PressFirst // primer presionador, máxima urgencia
    | PressSupport // cierra línea de pase, no persigue
    | HoldShape // mantiene posición táctica
    | AnchorDefense // no sube bajo ningún concepto
    | SupportBuild // ofrece opción de pase, corto
    | MakeRunForward // corre al espacio en profundidad
    | FreeRole // perfil propio del jugador manda

module SlotRole =

    let pressBallBias (role: SlotRole) : float =
        match role with
        | PressFirst -> 1.8
        | PressSupport -> 0.6
        | HoldShape -> 0.2
        | AnchorDefense -> 0.0
        | SupportBuild -> 0.3
        | MakeRunForward -> 0.1
        | FreeRole -> 1.0

    let supportAttackBias (role: SlotRole) : float =
        match role with
        | MakeRunForward -> 1.6
        | SupportBuild -> 1.3
        | FreeRole -> 1.0
        | PressFirst -> 0.2
        | PressSupport -> 0.3
        | HoldShape -> 0.7
        | AnchorDefense -> 0.0

    let coverSpaceBias (role: SlotRole) : float =
        match role with
        | AnchorDefense -> 1.5
        | HoldShape -> 1.2
        | PressSupport -> 1.0
        | FreeRole -> 1.0
        | SupportBuild -> 0.8
        | PressFirst -> 0.4
        | MakeRunForward -> 0.2

    let recoverBallBias (role: SlotRole) : float =
        match role with
        | PressFirst -> 1.5
        | FreeRole -> 1.0
        | _ -> 0.8
