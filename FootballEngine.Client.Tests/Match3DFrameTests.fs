module FootballEngine.Client.Tests.Match3DFrameTests

open Expecto
open FootballEngine.Client.Match3D

let private pass name reads writes =
    { Pass = name
      Reads = reads
      Writes = writes }

let private draw mesh shadow layer =
    { Mesh = mesh
      IndexCount = 3u
      InstanceCount = 1u
      Shadow = shadow
      Layer = layer }

let tests =
    testList
        "Match3D frame plan and draw lists"
        [ testCase "the stadium plan is valid and runs shadow, main, bloom, output in order"
          <| fun () ->
              match FramePlan.validate FramePlan.stadium with
              | Ok plan ->
                  Expect.equal
                      (plan |> List.map _.Pass)
                      [ ShadowPass; MainPass; BloomPass; OutputPass ]
                      "the validated plan keeps the encoding order"
              | Error error -> failtest (FramePlan.describe error)

          testCase "output before main is rejected because the HDR scene is not produced yet"
          <| fun () ->
              let reordered =
                  [ FramePlan.stadium[0]
                    FramePlan.stadium[3]
                    FramePlan.stadium[1]
                    FramePlan.stadium[2] ]

              Expect.equal
                  (FramePlan.validate reordered)
                  (Error(ReadBeforeWrite(OutputPass, HdrScene)))
                  "a pass reading a target before its writer must name both"

          testCase "a missing writer is rejected"
          <| fun () ->
              let withoutBloom =
                  FramePlan.stadium |> List.filter (fun step -> step.Pass <> BloomPass)

              Expect.equal
                  (FramePlan.validate withoutBloom)
                  (Error(ReadBeforeWrite(OutputPass, BloomChain)))
                  "output reads the bloom chain, which nobody writes"

          testCase "two writers of one target are rejected"
          <| fun () ->
              let plan =
                  [ pass ShadowPass [] [ ShadowMap ]
                    pass MainPass [ ShadowMap ] [ ShadowMap; SceneColor ] ]

              Expect.equal
                  (FramePlan.validate plan)
                  (Error(DuplicateWriter(MainPass, ShadowMap)))
                  "overwriting an earlier pass's product would silently discard it"

          testCase "external targets need no writer inside the frame"
          <| fun () ->
              let plan = [ pass OutputPass [ HudImage; ShadowStaticCache ] [ SurfaceColor ] ]
              Expect.isOk (FramePlan.validate plan) "the HUD upload and cached static shadow exist before encoding"

          testCase "shadow casters are grouped by kind and keep submission order"
          <| fun () ->
              let draws =
                  [ draw "stadium" StaticCaster SceneLayer
                    draw "ball" DynamicCaster SceneLayer
                    draw "marker" NoShadow OverlayLayer
                    draw "players" DynamicCaster SceneLayer ]

              Expect.equal
                  (DrawList.shadowCasters DynamicCaster draws |> List.map _.Mesh)
                  [ "ball"; "players" ]
                  "dynamic casters render every frame in system order"

              Expect.equal
                  (DrawList.shadowCasters StaticCaster draws |> List.map _.Mesh)
                  [ "stadium" ]
                  "static casters render into the cached map only"

              Expect.isEmpty
                  (DrawList.shadowCasters NoShadow draws
                   |> List.filter (fun d -> d.Shadow <> NoShadow))
                  "annotations never leak into a caster group"

          testCase "overlay draws are separated from scene geometry"
          <| fun () ->
              let draws =
                  [ draw "marker" NoShadow OverlayLayer
                    draw "stadium" StaticCaster SceneLayer
                    draw "ball" DynamicCaster SceneLayer ]

              Expect.equal (DrawList.inLayer SceneLayer draws |> List.map _.Mesh) [ "stadium"; "ball" ] "scene layer"
              Expect.equal (DrawList.inLayer OverlayLayer draws |> List.map _.Mesh) [ "marker" ] "overlay layer" ]
