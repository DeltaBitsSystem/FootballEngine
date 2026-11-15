module FootballEngine.Client.Tests.ViewerPresentationTests

open System
open System.Numerics
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Client.Match3D

let tests =
    testList
        "ViewerPresentation"
        [ testCase "camera selection changes presentation without issuing a simulation command" (fun _ ->
              let original = AppTypes.initialState ()

              for camera in ViewerCamera.all do
                  let state, commands =
                      AppState.update (LiveMsg(LiveMsg.SelectViewerCamera camera)) original

                  Expect.equal state.Match.Camera camera "MVU owns selected camera"
                  Expect.equal state.Mode original.Mode "career and sporting state are unchanged"
                  Expect.equal state.Match.Live original.Match.Live "no sporting revision is applied"
                  Expect.equal state.Match.RuntimeId original.Match.RuntimeId "no runtime is replaced"
                  Expect.isEmpty commands "camera cuts are not simulation commands")
          testCase "clearing a match resets the presentation camera" (fun _ ->
              let state =
                  { MatchSlice.clearState with
                      Camera = ViewerCamera.CloseUp }

              Expect.equal
                  (MatchSlice.update LiveMsg.Close state).Camera
                  ViewerCamera.Broadcast
                  "new match has a predictable lens")
          testCase "presentation camera captions exist in both supported languages" (fun _ ->
              for catalog in [ Localization.catalog.Es; Localization.catalog.En ] do
                  for key in
                      [ TranslationKey.MatchCamera
                        TranslationKey.MatchCameraBroadcast
                        TranslationKey.MatchCameraTactical
                        TranslationKey.MatchCameraBehindGoal
                        TranslationKey.MatchCameraTouchline
                        TranslationKey.MatchCameraCloseUp ] do
                      Expect.isFalse (String.IsNullOrWhiteSpace catalog[key]) "camera selector has localized captions")
          testCase "smooth match ball retains the authoritative radius and root" (fun _ ->
              let radius = 0.11f
              let mesh = MatchBallMesh.create radius
              Expect.isGreaterThan mesh.Indices.Length (2000 * 3) "close-view silhouette is not the old low-poly proxy"

              for vertex in mesh.Vertices do
                  Expect.isLessThan
                      (abs (vertex.Position.Length() - radius))
                      0.00001f
                      "visual radius matches simulation scaling"

                  Expect.isLessThan (abs (vertex.Normal.Length() - 1.0f)) 0.00001f "smooth normals"
                  Expect.equal vertex.RootIndex 22u "authoritative ball transform"
                  Expect.equal vertex.Surface (uint32 StadiumSurface.MatchBall) "dedicated leather material"

              for triangle in mesh.Indices |> Array.chunkBySize 3 do
                  let a, b, c =
                      mesh.Vertices[int triangle[0]].Position,
                      mesh.Vertices[int triangle[1]].Position,
                      mesh.Vertices[int triangle[2]].Position

                  Expect.isGreaterThan
                      (Vector3.Cross(b - a, c - a).LengthSquared())
                      0.000000000001f
                      "no degenerate polar triangles") ]
