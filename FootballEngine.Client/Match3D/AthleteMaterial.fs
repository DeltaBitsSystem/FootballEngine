namespace FootballEngine.Client.Match3D

open System.Numerics

type AthleteMaterial =
    | Jersey = 16
    | Shorts = 17
    | Socks = 18
    | Skin = 19
    | Hair = 20
    | Boots = 21
    | Trim = 22
    | Eyes = 23

module AthleteMaterial =
    let atlasMaterials =
        [| "jersey"; "shorts"; "socks"; "skin"; "hair"; "boots"; "trim"; "eyes" |]

    // Identity comes from the authored tile containing the triangle, never from kit or skin colours.
    let ofTriangle (a: Vector2) (b: Vector2) (c: Vector2) =
        let centre = (a + b + c) / 3.0f
        let column = min 3 (max 0 (int (centre.X * 4.0f)))
        let row = min 1 (max 0 (int (centre.Y * 4.0f)))
        enum<AthleteMaterial> (16 + row * 4 + column)

    let shaderConstants =
        [ AthleteMaterial.Jersey, "ATHLETE_JERSEY"
          AthleteMaterial.Shorts, "ATHLETE_SHORTS"
          AthleteMaterial.Socks, "ATHLETE_SOCKS"
          AthleteMaterial.Skin, "ATHLETE_SKIN"
          AthleteMaterial.Hair, "ATHLETE_HAIR"
          AthleteMaterial.Boots, "ATHLETE_BOOTS"
          AthleteMaterial.Trim, "ATHLETE_TRIM"
          AthleteMaterial.Eyes, "ATHLETE_EYES" ]
        |> List.map (fun (material, name) -> $"const {name}: u32 = {uint32 material}u;\n")
        |> String.concat ""
