# Prompt: Auditoria de Self-Passes en Motor de Fútbol (F#)

Copia este prompt y pegalo en otra sesión de IA junto con los JSONs de los partidos.

---

## Contexto

Soy un motor de fútbol determinista en F# (.NET 10). Tenemos un tipo opaco `PassTarget` con constructor privado que **debería** impedir self-passes a nivel compile-time:

```fsharp
[<Struct>]
type PassTarget = private PassTarget of idx: int * playerId: PlayerId * position: Spatial * distance: float<meter>

module PassTarget =
    let create (passerIdx: int) (candidateIdx: int) (pid: PlayerId) (pos: Spatial) (dist: float<meter>) : PassTarget option =
        if candidateIdx = passerIdx then None  // self-pass imposible
        else Some(PassTarget(candidateIdx, pid, pos, dist))
```

**Pero los JSONs de partido muestran self-passes reales** (`PassLaunched (11, 11)`, `PassLaunched (1, 1)`, etc.)

## Archivos clave del engine

| Archivo | Rol |
|---|---|
| `Engine/Match/Types/PassTarget.fs` | Tipo opaco con guard compile-time |
| `Engine/Match/Player/Actions/PassDecision.fs` | `findBestPassTarget` — usa `PassTarget.create` ✓ |
| `Engine/Match/Player/Actions/PassLauncher.fs` | `launchShort` (recibe `PassTarget`), `launchLong`, `launchSpace`, `launchGKDistribution` |
| `Engine/Match/Player/Actions/PassAction.fs` | `resolve` (recibe `Player`, NO usa `PassTarget`), `resolveLong`, `resolveIntoSpace` |
| `Engine/Match/Player/ActionResolver.fs` | `resolveIntent` — despacha `OnBallIntent` a las acciones |
| `Engine/Match/Player/Decision/PlayerDecision.fs` | `decide` — genera `OnBallIntent.Pass targetPid` desde `BestPassTargetIdx` |
| `Engine/Match/Player/PlayerContext.fs` | Construye `AgentContext` con `BestPassTargetIdx: int voption` |
| `Engine/Match/Player/Perception/CognitiveFrameBuilder.fs` | Llena `BestPassTargetIdx[]` por slot, tiene `if j <> i` |
| `Engine/Match/Spatial/MatchSpatial.fs` | `findBestPassTargetFrame` — tiene `if i <> meIdx` |

## Flujo de un pase corto (el problema)

```
CognitiveFrameBuilder.fill BestPassTargetIdx[i] → (guard j≠i, pero usa slot index)
    ↓
PlayerContext.build → AgentContext.BestPassTargetIdx (guard ≠ meIdx)
    ↓
PlayerDecision.decide → OnBallIntent.Pass targetPid (targetPid = OwnRoster.Players[idx].Id)
    ↓
ActionResolver.resolveIntent → findActivePlayer(ctx, state, targetPid) → Player
    ↓
PassAction.resolve(target: Player) ← NO USA PassTarget, NO TIENE GUARD
```

## Tu tarea

1. **Lee los JSONs de partido** que te voy a adjuntar. Identifica todos los `PassLaunched (X, X)` donde source == target.
2. **Busca TODOS los caminos de código** donde un pass puede generarse sin pasar por `PassTarget.create`. Cada camino que use `int` en vez de `PassTarget` es un candidato a self-pass.
3. **Genera un plan de fix** en formato markdown con:
   - Lista de archivos a modificar y qué cambiar
   - Para cada fix: la idea en 1-2 oraciones (sin código)
   - Orden de aplicación (dependencias)
   - Criterio de verificación (cómo confirmar que no hay self-passes)

## Formato de salida esperado

```markdown
# Plan: Eliminar Self-Passes

## Diagnóstico
- Total self-passes encontrados: X
- Caminos vulnerables identificados: lista

## Fixes (orden de aplicación)

### Fix 1: [archivo]
**Problema:** ...
**Solución:** ...

### Fix 2: ...

## Verificación
- Comando para correr: `dotnet fsi validate_matches.fsx`
- Esperado: 0 eventos `PassLaunched (X, X)` donde source == target
```

## Notas importantes
- `PassAction.resolve` recibe un `Player` directamente — no tiene forma de saber si es el mismo que el passer
- `PlayerDecision.decide` convierte slot index → PlayerId usando `OwnRoster.Players[idx]` — si el roster order no coincide con slot order, podría confundir
- `resolveLong` itera todos los slots sin excluir al passer
- `resolveIntoSpace` tiene `if i <> pIdx` pero el fallback no
- GK distribution tiene `if i <> gkIdx`
