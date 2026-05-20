namespace FootballEngine

open System

module MathPipelines =

    /// Raíz cuadrada: la condición degrada con retornos decrecientes.
    /// Un jugador al 80% rinde más del 80% — el cuerpo compensa.
    let applyConditionDecay (condition: float32) (score: float) : float =
        score * (0.90 + 0.10 * sqrt (float condition / 100.0))

    /// Exponencial: la fatiga extrema colapsa abruptamente, no en línea recta.
    /// Por debajo del threshold el decay se acelera exponencialmente.
    let applyFatigueCollapse (threshold: int) (decay: float) (condition: int) : float =
        let norm = float condition / 100.0
        let t = float threshold / 100.0
        if norm < t then Math.Exp(-(t - norm) * decay) else 1.0

    /// Sigmoid: la ventaja de habilidad entre jugadores no es lineal.
    /// 18 vs 10 es dominante. 13 vs 12 es marginal. Steepness controla esa curva.
    let applySkillGapCurve (steepness: float) (skillDiff: float) : float =
        1.0 / (1.0 + Math.Exp(-steepness * skillDiff))

    /// Exponencial en distancia: la probabilidad de gol no cae linealmente.
    /// A 25m es dramáticamente peor que a 15m — datos reales muestran esta curva.
    let xgDistanceComponent (factor: float) (distMeters: float) : float =
        Math.Exp(-factor * distMeters)

    /// Normaliza un stat de 1-20 a 0.0-1.0. Único lugar donde vive esta operación.
    let normStat (v: int) : float = float v / 20.0

    /// Normaliza condition 0-100 a 0.0-1.0.
    let normCondition (v: int) : float = float v / 100.0

    /// Normaliza capacidad de estadio contra el máximo de referencia.
    /// 80000 es el estadio más grande relevante — Wembley, Camp Nou.
    let normStadiumCapacity (capacity: int) (maxCapacity: int) : float =
        float capacity / float maxCapacity

    /// Divide score entre máximo posible para normalizar a [0,1]
    /// independientemente de cuántos términos se sumen.
    let normalizeScore (maxPossible: float) (score: float) : float =
        if maxPossible > 0.0 then Math.Clamp(score / maxPossible, 0.0, 1.0) else 0.0
