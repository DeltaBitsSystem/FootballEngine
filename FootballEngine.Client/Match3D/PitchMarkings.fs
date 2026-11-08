namespace FootballEngine.Client.Match3D

open System
open System.Globalization
open System.Numerics

/// Presentation geometry for the authored 105 x 68 metre pitch, not the sporting rules engine.
module PitchMarkings =
    let length = 105.0f
    let width = 68.0f
    let lineWidth = 0.12f
    let centerRadius = 9.15f
    let penaltyDepth = 16.5f
    let penaltyHalfWidth = 20.16f
    let goalDepth = 5.5f
    let goalHalfWidth = 9.16f
    let penaltySpotDistance = 11.0f
    let spotRadius = 0.12f
    let cornerRadius = 1.0f
    let grassTileMetres = 1.0f
    let mowingBandMetres = 7.5f

    let private segmentDistance (point: Vector2) (a: Vector2) (b: Vector2) =
        let segment = b - a

        let t =
            Math.Clamp(Vector2.Dot(point - a, segment) / segment.LengthSquared(), 0.0f, 1.0f)

        Vector2.Distance(point, a + segment * t)

    let private boxDistance (point: Vector2) (halfSize: Vector2) =
        let q = Vector2.Abs point - halfSize
        (Vector2.Max(q, Vector2.Zero)).Length() + min (max q.X q.Y) 0.0f

    let signedDistance (point: Vector2) =
        // Folding the symmetric field avoids sub-micrometre sign-dependent float error at mirrored line joins.
        let point = Vector2.Abs point
        let halfStroke = lineWidth * 0.5f
        let halfLength = length * 0.5f
        let halfWidth = width * 0.5f
        let boundary = Vector2(halfLength - halfStroke, halfWidth - halfStroke)
        let mutable distance = abs (boxDistance point boundary) - halfStroke

        distance <-
            min
                distance
                (segmentDistance point (Vector2(0.0f, -boundary.Y)) (Vector2(0.0f, boundary.Y))
                 - halfStroke)

        distance <- min distance (abs (point.Length() - centerRadius) - halfStroke)
        distance <- min distance (point.Length() - spotRadius)

        for direction in [ -1.0f; 1.0f ] do
            let p = Vector2(point.X * direction, point.Y)

            for depth, halfSpan in [ penaltyDepth, penaltyHalfWidth; goalDepth, goalHalfWidth ] do
                let front = halfLength - depth

                distance <-
                    min
                        distance
                        (segmentDistance p (Vector2(front, -halfSpan)) (Vector2(front, halfSpan))
                         - halfStroke)

                distance <-
                    min
                        distance
                        (segmentDistance p (Vector2(front, halfSpan)) (Vector2(boundary.X, halfSpan))
                         - halfStroke)

                distance <-
                    min
                        distance
                        (segmentDistance p (Vector2(front, -halfSpan)) (Vector2(boundary.X, -halfSpan))
                         - halfStroke)

            let spot = Vector2(halfLength - penaltySpotDistance, 0.0f)
            distance <- min distance (Vector2.Distance(p, spot) - spotRadius)

            let arc =
                max (abs (Vector2.Distance(p, spot) - centerRadius) - halfStroke) (p.X - (halfLength - penaltyDepth))

            distance <- min distance arc

        for x in [ -halfLength; halfLength ] do
            for y in [ -halfWidth; halfWidth ] do
                distance <- min distance (abs (Vector2.Distance(point, Vector2(x, y)) - cornerRadius) - halfStroke)

        max distance (boxDistance point (Vector2(halfLength, halfWidth)))

    let shaderConstants =
        [ "PITCH_HALF_LENGTH", length * 0.5f
          "PITCH_HALF_WIDTH", width * 0.5f
          "PITCH_HALF_STROKE", lineWidth * 0.5f
          "PITCH_CENTER_RADIUS", centerRadius
          "PITCH_PENALTY_DEPTH", penaltyDepth
          "PITCH_PENALTY_HALF_WIDTH", penaltyHalfWidth
          "PITCH_GOAL_DEPTH", goalDepth
          "PITCH_GOAL_HALF_WIDTH", goalHalfWidth
          "PITCH_PENALTY_SPOT_DISTANCE", penaltySpotDistance
          "PITCH_SPOT_RADIUS", spotRadius
          "PITCH_CORNER_RADIUS", cornerRadius
          "PITCH_GRASS_TILE", grassTileMetres
          "PITCH_MOWING_BAND", mowingBandMetres ]
        |> List.map (fun (name, value) ->
            let literal = value.ToString("R", CultureInfo.InvariantCulture)
            $"const {name}: f32 = {literal};\n")
        |> String.concat ""
