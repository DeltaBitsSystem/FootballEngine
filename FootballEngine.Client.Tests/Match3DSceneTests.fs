module FootballEngine.Client.Tests.Match3DSceneTests

open System
open System.Numerics
open System.Text.RegularExpressions
open Expecto
open FootballEngine.LiveMatch
open FootballEngine.Client.Match3D
open FootballEngine.Client.Views.Components

let private jointCount = 2
let private aspect = 16.0f / 9.0f

let private player (id: int) (x: float) (y: float) (slot: int) (isHome: bool) (hasBall: bool) : RenderPlayer =
    { Id = id
      Position = { X = x; Y = y }
      Height = 0.0
      VelocityZ = 0.0
      Velocity = { X = 0.0; Y = 0.0 }
      Acceleration = { X = 0.0; Y = 0.0 }
      BodyFacing = 0.0
      AngularVelocity = 0.0
      TravelDistance = 0.0
      State = Idle
      VisualSlot = slot
      ShortName = $"P{id}"
      Condition = 90
      IsHome = isHome
      HasBall = hasBall
      IsSidelined = false }

let private ballAt (x: float) (y: float) : RenderBall =
    { Position = { X = x; Y = y }
      Velocity = { X = 0.0; Y = 0.0 }
      VelocityZ = 0.0
      Orientation = Quaternion.Identity
      AngularVelocity = Vector3.Zero
      Radius = 0.11
      FootMotion = None
      Height = 0.0 }

let private renderFrame (players: RenderPlayer array) (ball: RenderBall) : RenderFrame =
    { Players = players
      Ball = ball
      Officials = [||]
      TimeSeconds = 1.1
      HomeScore = 0
      AwayScore = 0
      AttackingClubId = None
      Momentum = 0.0
      Situation = MatchSituation.Live
      HomeName = "Home FC"
      AwayName = "Away FC"
      CompetitionName = None }

let private sun: SunState =
    { Direction = Vector3.Normalize(Vector3(0.3f, 0.8f, 0.5f))
      Radiance = Vector3(1.0f, 0.9f, 0.8f)
      EnvironmentIntensity = 0.5f }

let private sceneInput (mode: ViewerCamera) (frame: RenderFrame) : SceneInput =
    { CameraMode = mode
      Aspect = aspect
      TimeSeconds = 12.5
      Frame = frame
      JointPalettes =
        Array.init (SceneRoot.playerCount * jointCount) (fun index ->
            Matrix4x4.CreateTranslation(float32 index, 0.0f, 0.0f))
      JointCount = jointCount
      BallMeshRadius = 0.11f
      ClubPrimary = Vector3.One
      ClubSecondary = Vector3.Zero
      Sun = sun
      LightViewProjection = Matrix4x4.CreateScale(2.0f) }

let private carrierPlayers =
    [| player 1 3.0 4.0 0 true true; player 12 -7.0 2.0 10 false false |]

let private looseBallPlayers =
    [| player 1 3.0 4.0 0 true false; player 12 -7.0 2.0 10 false false |]

let private standardBall = ballAt 1.5 -2.5

let private expectBuilt (input: SceneInput) : SceneFrame =
    match SceneBuilder.build input with
    | Ok scene -> scene
    | Error error -> failtestf "the scene builder rejected a valid frame: %A" error

let private translationOf (matrix: Matrix4x4) =
    Vector3(matrix.M41, matrix.M42, matrix.M43)

/// A WGSL struct member as the shader source declares it: name, byte offset from the start of the struct.
type private WgslStruct =
    { Members: (string * int) list
      Size: int }

let private wgslAlignment = 16

let private roundUp (value: int) (alignment: int) =
    (value + alignment - 1) / alignment * alignment

/// Parses the camera struct line by line: each member sits on one line, and `array<...>` contains a comma that a
/// single-regex split would break on.
let private parseCameraStruct (source: string) : Result<WgslStruct, string> =
    let body = Regex.Match(source, @"struct\s+Camera\s*\{(?<body>[^}]*)\}")

    if not body.Success then
        Error "struct Camera was not found in the shader source"
    else
        let members =
            body.Groups["body"].Value.Split('\n')
            |> Array.map (fun line -> line.Trim().TrimEnd(','))
            |> Array.filter (fun line -> line.Contains ':')
            |> Array.map (fun line ->
                let separator = line.IndexOf ':'
                line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim())
            |> List.ofArray

        let sizeOf (typeText: string) =
            let arrayOfMatrices = Regex.Match(typeText, @"^array<mat4x4<f32>,\s*(\d+)>$")

            match typeText with
            | "mat4x4<f32>" -> Ok 64
            | "vec4<f32>" -> Ok 16
            | _ when arrayOfMatrices.Success -> Ok(64 * int arrayOfMatrices.Groups[1].Value)
            | _ -> Error $"unsupported WGSL member type '{typeText}'"

        let folded =
            members
            |> List.fold
                (fun state (name, typeText) ->
                    state
                    |> Result.bind (fun (offsetAcc: (string * int) list, cursor: int) ->
                        sizeOf typeText
                        |> Result.map (fun size ->
                            let offset = roundUp cursor wgslAlignment
                            (name, offset) :: offsetAcc, offset + size)))
                (Ok([], 0))

        folded
        |> Result.map (fun (reversed, end') ->
            { Members = List.rev reversed
              Size = roundUp end' wgslAlignment })

/// Byte offsets the layout claims for each WGSL member, derived from the float32 element indices in UniformLayout.
let private layoutOffsets =
    [ "viewProjection", UniformLayout.viewProjection
      "rootTransforms", UniformLayout.rootTransforms
      "position", UniformLayout.position
      "sunAndEnvironment", UniformLayout.sunAndEnvironment
      "sunRadiance", UniformLayout.sunRadiance
      "lightViewProjection", UniformLayout.lightViewProjection
      "inverseViewProjection", UniformLayout.inverseViewProjection
      "clubPrimary", UniformLayout.clubPrimary
      "clubSecondary", UniformLayout.clubSecondary ]
    |> List.map (fun (name, elementIndex) -> name, elementIndex * sizeof<float32>)

let private layoutMismatches (actual: WgslStruct) (claimed: (string * int) list) (claimedSize: int) : string list =
    let actualNames = actual.Members |> List.map fst
    let claimedNames = claimed |> List.map fst

    [ if actualNames <> claimedNames then
          yield $"members {actualNames} do not match the layout's {claimedNames}"

      for name, offset in actual.Members do
          match List.tryFind (fun (claimedName, _) -> claimedName = name) claimed with
          | Some(_, claimedOffset) when claimedOffset = offset -> ()
          | Some(_, claimedOffset) -> yield $"{name} sits at byte {offset} in WGSL but the layout says {claimedOffset}"
          | None -> yield $"{name} has no layout entry"

      if actual.Size <> claimedSize then
          yield $"struct is {actual.Size} bytes in WGSL but the layout says {claimedSize}" ]

let private readCameraStruct () : WgslStruct =
    match ShaderLibrary.readFile "CameraUniform.wgsl" with
    | Error error -> failtest (GpuError.describe error)
    | Ok source ->
        match parseCameraStruct source with
        | Error message -> failtest message
        | Ok parsed -> parsed

let tests =
    testList
        "Match3D scene"
        [ testCase "camera uniform offsets and size match the WGSL Camera struct" (fun _ ->
              let actual = readCameraStruct ()

              let mismatches =
                  layoutMismatches actual layoutOffsets (UniformLayout.elementCount * sizeof<float32>)

              Expect.isEmpty mismatches "the uniform layout must mirror the shader's Camera struct")

          testCase "a shifted uniform offset is reported by the layout comparison" (fun _ ->
              let actual = readCameraStruct ()

              // The baseline comes from the parsed shader, so the control still holds when the real layout has drifted.
              let shifted =
                  actual.Members
                  |> List.map (fun (name, offset) ->
                      if name = "sunRadiance" then
                          name, offset + 4
                      else
                          name, offset)

              let mismatches = layoutMismatches actual shifted actual.Size

              Expect.hasLength mismatches 1 "exactly the shifted member should be reported"
              Expect.stringContains (mismatches[0]) "sunRadiance" "the report must name the drifted member")

          testCase "uniform element indices equal the hand-derived WGSL byte offsets divided by four" (fun _ ->
              // Worked out by hand from the Camera struct: a 64-byte matrix, then 25 matrices (1600 bytes), then vectors.
              Expect.equal UniformLayout.viewProjection 0 "viewProjection at byte 0"
              Expect.equal UniformLayout.rootTransforms 16 "rootTransforms at byte 64"
              Expect.equal UniformLayout.position 416 "position at byte 1664"
              Expect.equal UniformLayout.sunAndEnvironment 420 "sunAndEnvironment at byte 1680"
              Expect.equal UniformLayout.sunRadiance 424 "sunRadiance at byte 1696"
              Expect.equal UniformLayout.lightViewProjection 428 "lightViewProjection at byte 1712"
              Expect.equal UniformLayout.inverseViewProjection 444 "inverseViewProjection at byte 1776"
              Expect.equal UniformLayout.clubPrimary 460 "clubPrimary at byte 1840"
              Expect.equal UniformLayout.clubSecondary 464 "clubSecondary at byte 1856"
              Expect.equal UniformLayout.elementCount 468 "the struct is 1872 bytes")

          testCase "packCamera writes every named field at its layout offset" (fun _ ->
              let camera: CameraState =
                  { ViewProjection =
                      Matrix4x4(
                          1.0f,
                          2.0f,
                          3.0f,
                          4.0f,
                          5.0f,
                          6.0f,
                          7.0f,
                          8.0f,
                          9.0f,
                          10.0f,
                          11.0f,
                          12.0f,
                          13.0f,
                          14.0f,
                          15.0f,
                          16.0f
                      )
                    Inverse = Matrix4x4.CreateScale(0.5f)
                    Position = Vector3(1.0f, 2.0f, 3.0f)
                    NearPlane = 0.1f
                    FarPlane = 500.0f
                    FieldOfView = 0.5f
                    Aspect = aspect }

              let scene: SceneFrame =
                  { Camera = camera
                    Sun = sun
                    LightViewProjection = Matrix4x4.CreateScale(2.0f)
                    TimeSeconds = 12.5
                    RootTransforms =
                      Array.init SceneRoot.count (fun root -> Matrix4x4.CreateTranslation(float32 root, 0.0f, 0.0f))
                    JointPalettes = [||]
                    ShowGrassDetail = false
                    ClubPrimary = Vector3(0.25f, 0.5f, 0.75f)
                    ClubSecondary = Vector3.One }

              let data = UniformLayout.packCamera scene
              Expect.equal data.Length UniformLayout.elementCount "the buffer holds exactly the layout"
              Expect.equal data[UniformLayout.viewProjection + 1] 2.0f "row-major M12 after M11"
              Expect.equal data[UniformLayout.rootTransforms + 22 * 16 + 12] 22.0f "ball root translation X"
              Expect.equal data[UniformLayout.position] 1.0f "camera position X"
              Expect.equal data[UniformLayout.position + 3] 12.5f "wind clock sits in the w of position"
              Expect.equal data[UniformLayout.sunAndEnvironment + 3] 0.5f "environment intensity sits in w"
              Expect.equal data[UniformLayout.sunRadiance + 3] 0.0f "sunRadiance w is kept zero"
              Expect.equal data[UniformLayout.lightViewProjection] 2.0f "light matrix M11"
              Expect.equal data[UniformLayout.inverseViewProjection + 5] 0.5f "inverse matrix M22"
              Expect.equal data[UniformLayout.clubPrimary + 2] 0.75f "club primary blue"
              Expect.equal data[UniformLayout.clubSecondary + 3] 1.0f "club colours are opaque")

          testCase "matrixOfElements keeps the shadow matrix row order and rejects a short array" (fun _ ->
              let elements = Array.init 16 float32

              match SceneBuilder.matrixOfElements elements with
              | Ok matrix ->
                  Expect.equal matrix.M14 3.0f "fourth element of the first row"
                  Expect.equal matrix.M41 12.0f "first element of the fourth row"
              | Error message -> failtest message

              Expect.isError (SceneBuilder.matrixOfElements (Array.zeroCreate 15)) "a 4x4 matrix needs 16 elements")

          testCase "builder places 22 player roots, the ball, the stadium and the carrier marker" (fun _ ->
              let scene =
                  expectBuilt (sceneInput ViewerCamera.Broadcast (renderFrame carrierPlayers standardBall))

              Expect.equal scene.RootTransforms.Length SceneRoot.count "one transform per root"

              Expect.equal
                  (translationOf scene.RootTransforms[0])
                  (Vector3(3.0f, 0.0f, -4.0f))
                  "home slot 0 at pitch (3, 4)"

              Expect.equal
                  (translationOf scene.RootTransforms[21])
                  (Vector3(-7.0f, 0.0f, -2.0f))
                  "away slot 10 is root 21"

              let hidden = [ 1..10 ] @ [ 12..20 ]

              for root in hidden do
                  Expect.isLessThan
                      (scene.RootTransforms[root].M42)
                      (-99999.0f)
                      $"unused root {root} is parked below the pitch"

              Expect.equal
                  (translationOf scene.RootTransforms[SceneRoot.ball])
                  (Vector3(1.5f, 0.11f, 2.5f))
                  "ball root"

              Expect.equal scene.RootTransforms[SceneRoot.stadium] Matrix4x4.Identity "stadium root is identity"

              Expect.equal
                  (translationOf scene.RootTransforms[SceneRoot.carrierMarker])
                  (Vector3(3.0f, 0.0f, -4.0f))
                  "the marker follows the carrier")

          testCase "a carrier drives the camera focus and a loose ball drives it otherwise" (fun _ ->
              let carried =
                  expectBuilt (sceneInput ViewerCamera.Broadcast (renderFrame carrierPlayers standardBall))

              let carriedFrame =
                  ViewerCamera.frame ViewerCamera.Broadcast aspect (Vector3(3.0f, 1.0f, -4.0f))

              Expect.equal carried.Camera.Position carriedFrame.Position "focus is the carrier's pitch position"

              let loose =
                  expectBuilt (sceneInput ViewerCamera.Broadcast (renderFrame looseBallPlayers standardBall))

              let looseFrame =
                  ViewerCamera.frame ViewerCamera.Broadcast aspect (Vector3(1.5f, 1.0f, 2.5f))

              Expect.equal loose.Camera.Position looseFrame.Position "focus is the ball's ground position"
              Expect.isFalse (loose.Camera.Position = carried.Camera.Position) "the two focus rules must differ")

          testCase "grass detail switches exactly at the 12 m camera height" (fun _ ->
              Expect.isTrue (SceneBuilder.grassDetailVisible 11.9f) "11.9 m is inside the detail band"
              Expect.isFalse (SceneBuilder.grassDetailVisible 12.1f) "12.1 m is outside the detail band"

              let frame = renderFrame carrierPlayers standardBall
              let closeUp = expectBuilt (sceneInput ViewerCamera.CloseUp frame)
              Expect.isLessThan closeUp.Camera.Position.Y 12.0f "the close-up camera sits near 2.9 m"
              Expect.isTrue closeUp.ShowGrassDetail "a low camera shows the grass detail"

              let broadcast = expectBuilt (sceneInput ViewerCamera.Broadcast frame)
              Expect.isGreaterThan broadcast.Camera.Position.Y 12.0f "the broadcast camera sits above the band"
              Expect.isFalse broadcast.ShowGrassDetail "a broadcast camera hides the subpixel blades")

          testCase "the same input produces the same frame regardless of earlier calls" (fun _ ->
              let input =
                  sceneInput ViewerCamera.Broadcast (renderFrame carrierPlayers standardBall)

              let first = expectBuilt input

              expectBuilt (sceneInput ViewerCamera.CloseUp (renderFrame looseBallPlayers standardBall))
              |> ignore

              let second = expectBuilt input

              Expect.equal
                  (UniformLayout.packCamera first)
                  (UniformLayout.packCamera second)
                  "packed uniform is stable"

              Expect.equal first.RootTransforms second.RootTransforms "root transforms are stable"

              Expect.isTrue
                  (obj.ReferenceEquals(first.JointPalettes, input.JointPalettes))
                  "palettes are borrowed, so a frame makes no per-frame copy")

          testCase "rejected frames name the broken invariant" (fun _ ->
              let rejected (input: SceneInput) =
                  match SceneBuilder.build input with
                  | Error error -> error
                  | Ok _ -> failtest "an invalid frame was accepted"

              let twoCarriers = [| player 1 3.0 4.0 0 true true; player 2 -1.0 0.0 1 true true |]

              Expect.equal
                  (rejected (sceneInput ViewerCamera.Broadcast (renderFrame twoCarriers standardBall)))
                  (InvalidFrame "a render frame cannot have multiple ball carriers")
                  "a second carrier is refused"

              Expect.equal
                  (rejected (
                      sceneInput ViewerCamera.Broadcast (renderFrame [| player 1 0.0 0.0 11 true false |] standardBall)
                  ))
                  (InvalidFrame "player 1 has invalid visual slot 11")
                  "slot 11 is outside a team"

              Expect.equal
                  (rejected (
                      sceneInput
                          ViewerCamera.Broadcast
                          (renderFrame [| player 1 0.0 0.0 0 true false; player 2 1.0 1.0 0 true false |] standardBall)
                  ))
                  (InvalidFrame "multiple players occupy visual slot 0 for the same team")
                  "two players cannot share a seat"

              let standard =
                  sceneInput ViewerCamera.Broadcast (renderFrame carrierPlayers standardBall)

              let shortPalettes = { standard with JointPalettes = [||] }

              match rejected shortPalettes with
              | InvalidPalettes _ -> ()
              | other -> failtestf "expected a palette rejection, got %A" other)

          testCase "a palette set for another rig is rejected before any placement" (fun _ ->
              let baseInput =
                  sceneInput ViewerCamera.Broadcast (renderFrame carrierPlayers standardBall)

              let input =
                  { baseInput with
                      JointCount = jointCount + 1 }

              match SceneBuilder.build input with
              | Error(InvalidPalettes message) ->
                  Expect.equal
                      message
                      "procedural palette cardinality does not match the canonical rig"
                      "message is stable"
              | other -> failtestf "expected a palette rejection, got %A" other) ]
