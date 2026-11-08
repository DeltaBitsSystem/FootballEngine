module FootballEngine.Client.Tests.ViewerQualityTests

open Expecto
open FootballEngine.Client.Match3D
open FootballEngine.Settings

let private presets =
    [ ViewerQuality.low
      ViewerQuality.medium
      ViewerQuality.high
      ViewerQuality.ultra ]

let tests =
    testList
        "Viewer quality"
        [ testCase "every preset validates and each tier maps to its preset"
          <| fun _ ->
              for q in presets do
                  Expect.isEmpty (ViewerQuality.validate q) $"{q.Name} is valid"

              let names = ViewerQualityTier.all |> List.map (ViewerQuality.ofTier >> _.Name)
              Expect.equal names [ "low"; "medium"; "high"; "ultra" ] "tier mapping"

          testCase "validation rejects values the GPU or the passes cannot honour"
          <| fun _ ->
              let rejected q =
                  not (List.isEmpty (ViewerQuality.validate q))

              let u = ViewerQuality.ultra
              Expect.isTrue (rejected { u with MsaaSamples = 3 }) "msaa 3"
              Expect.isTrue (rejected { u with ShadowCascades = 0 }) "no cascades"
              Expect.isTrue (rejected { u with ShadowMapSize = 3000 }) "non power of two"
              Expect.isTrue (rejected { u with AoRadiusMetres = -1.0f }) "negative radius"
              Expect.isTrue (rejected { u with ShadowPcfTaps = 5 }) "odd tap count"

          testCase "cheaper tiers never cost more than the tier above"
          <| fun _ ->
              let pairs = List.pairwise presets

              for lower, upper in pairs do
                  Expect.isLessThanOrEqual lower.ShadowMapSize upper.ShadowMapSize $"{lower.Name} shadow map"
                  Expect.isLessThanOrEqual lower.BloomMips upper.BloomMips $"{lower.Name} bloom mips"
                  Expect.isLessThanOrEqual lower.AoSlices upper.AoSlices $"{lower.Name} AO slices"
                  Expect.isLessThanOrEqual lower.Anisotropy upper.Anisotropy $"{lower.Name} anisotropy"
                  Expect.isLessThanOrEqual lower.GrassGrid upper.GrassGrid $"{lower.Name} grass"
                  Expect.isLessThanOrEqual lower.MsaaSamples upper.MsaaSamples $"{lower.Name} msaa"

          testCase "the selected tier is what the next viewer reads, and Ultra is the default"
          <| fun _ ->
              Expect.equal MatchViewSettings.defaults.ViewerQuality ViewerQualityTier.Ultra "development default"
              let before = ViewerQualitySelection.current ()

              try
                  ViewerQualitySelection.select ViewerQualityTier.Low
                  Expect.equal (ViewerQualitySelection.current ()) ViewerQualityTier.Low "selected"
              finally
                  ViewerQualitySelection.select before

          testCase "the broadcast grade is a gentle lift, never a stylised look"
          <| fun _ ->
              let g = BroadcastGrade.standard
              Expect.isTrue (g.Saturation >= 1.0f && g.Saturation <= 1.15f) "saturation"
              Expect.isTrue (g.Contrast >= 1.0f && g.Contrast <= 1.1f) "contrast" ]
