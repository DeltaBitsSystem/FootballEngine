namespace FootballEngine.Player.Steering

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract


module OrganicDrift =

    let private primes = [| 2; 3; 5; 7; 11; 13; 17 |]
    let private phases = [| 0.0; 1.3; 2.7; 0.9; 3.1; 4.2; 1.7 |]
    let private ampX = [| 0.8; 0.5; 0.3; 0.2; 0.15; 0.1; 0.05 |]
    let private ampY = [| 0.3; 0.6; 0.4; 0.25; 0.2; 0.15; 0.1 |]

    let private positionMultiplier =
        function
        | GK -> 0.2
        | DR
        | DC
        | DL
        | WBR
        | WBL -> 0.7
        | DM -> 0.8
        | MC
        | MR
        | ML -> 0.9
        | AMR
        | AML
        | AMC -> 1.1
        | ST -> 1.2

    let compute position playerId simSecond =
        let seed = float playerId
        let posMul = positionMultiplier position
        let freq = 2.0 * System.Math.PI

        let mutable dx = 0.0
        let mutable dy = 0.0

        for i = 0 to 6 do
            let p = float primes[i]
            let phase = phases[i]
            dx <- dx + ampX[i] * posMul * sin (freq * (float simSecond / p + seed * 0.01 + phase))

            dy <-
                dy
                + ampY[i] * posMul * sin (freq * (float simSecond / p + seed * 0.013 + phase))

        (dx * 1.0<meter>, dy * 1.0<meter>)
