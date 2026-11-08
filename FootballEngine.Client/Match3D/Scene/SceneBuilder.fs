namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open FootballEngine.Client.Views.Components

type SceneError =
    | InvalidPalettes of string
    | InvalidFrame of string
    | SingularCamera

type SceneInput =
    {
        CameraMode: ViewerCamera
        Aspect: float32
        /// The caller chooses the clock: the live viewer passes wall time, the harness passes a pinned value.
        TimeSeconds: float
        Frame: RenderFrame
        JointPalettes: Matrix4x4[]
        JointCount: int
        BallMeshRadius: float32
        Sun: SunState
        LightViewProjection: Matrix4x4
    }

module SceneBuilder =
    // Unused roots sit far below the pitch so their geometry is never visible without removing their instances.
    let private hidden = Matrix4x4.CreateTranslation(0.0f, -100000.0f, 0.0f)

    /// At broadcast height the grass blades are subpixel; drawing them would add shimmer instead of detail.
    let grassDetailVisible (cameraHeight: float32) = cameraHeight < 12.0f

    /// Rows are M11..M44 in order, the same element order DirectionalShadow.matrixData produces.
    let matrixOfElements (values: float32[]) : Result<Matrix4x4, string> =
        if values.Length <> 16 then
            Error $"a 4x4 matrix needs 16 elements, got {values.Length}"
        else
            Ok(
                Matrix4x4(
                    values[0],
                    values[1],
                    values[2],
                    values[3],
                    values[4],
                    values[5],
                    values[6],
                    values[7],
                    values[8],
                    values[9],
                    values[10],
                    values[11],
                    values[12],
                    values[13],
                    values[14],
                    values[15]
                )
            )

    let private playerRoot (player: RenderPlayer) =
        (if player.IsHome then 0 else SceneRoot.awayFirst) + player.VisualSlot

    let private seatPlayer
        (seats: RenderPlayer option[], carrier: RenderPlayer option)
        (player: RenderPlayer)
        : Result<RenderPlayer option[] * RenderPlayer option, SceneError> =
        let carrierAfter =
            match player.HasBall, carrier with
            | true, Some _ -> Error(InvalidFrame "a render frame cannot have multiple ball carriers")
            | true, None -> Ok(Some player)
            | false, _ -> Ok carrier

        carrierAfter
        |> Result.bind (fun carrier ->
            if player.VisualSlot < 0 || player.VisualSlot >= SceneRoot.teamSize then
                Error(InvalidFrame $"player {player.Id} has invalid visual slot {player.VisualSlot}")
            elif seats[playerRoot player].IsSome then
                Error(InvalidFrame $"multiple players occupy visual slot {player.VisualSlot} for the same team")
            else
                seats[playerRoot player] <- Some player
                Ok(seats, carrier))

    /// Seats are indexed by root so each of the 22 visual slots maps to exactly one transform.
    let private seatPlayers (players: RenderPlayer[]) =
        players
        |> Array.fold
            (fun state player ->
                state
                |> Result.bind (fun (seats, carrier) -> seatPlayer (seats, carrier) player))
            (Ok(Array.create SceneRoot.playerCount None, None))

    let private playerTransform (player: RenderPlayer) =
        let yaw = float32 player.BodyFacing + MathF.PI / 2.0f

        let position =
            Vector3(float32 player.Position.X, float32 player.Height, -float32 player.Position.Y)

        Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(position)

    /// Returns the root transforms and the camera focus. The focus follows the carrier when there is one and the
    /// ball otherwise; a lob changes ball height, not the sideline framing, so only X and Z are used.
    let private placeActors
        (input: SceneInput)
        (seats: RenderPlayer option[])
        (carrier: RenderPlayer option)
        : Matrix4x4[] * Vector3 =
        let transforms: Matrix4x4[] = Array.create SceneRoot.count hidden
        transforms[SceneRoot.stadium] <- Matrix4x4.Identity

        for root = 0 to SceneRoot.playerCount - 1 do
            transforms[root] <-
                match seats[root] with
                | Some player -> playerTransform player
                | None -> hidden

        let ball = input.Frame.Ball
        let ballRadius = float32 ball.Radius
        let ballScale = ballRadius / input.BallMeshRadius
        // Core is Z-up; the viewer is Y-up with pitch Y mapped to negative Z.
        let q = ball.Orientation
        let ballRotation = Quaternion(q.X, q.Z, -q.Y, q.W)

        let ballPosition =
            Vector3(float32 ball.Position.X, ballRadius + float32 ball.Height, -float32 ball.Position.Y)

        transforms[SceneRoot.ball] <-
            Matrix4x4.CreateScale(ballScale)
            * Matrix4x4.CreateFromQuaternion(ballRotation)
            * Matrix4x4.CreateTranslation(ballPosition)

        let focus =
            match carrier with
            | Some player ->
                let position = Vector3(float32 player.Position.X, 0.0f, -float32 player.Position.Y)
                transforms[SceneRoot.carrierMarker] <- Matrix4x4.CreateTranslation(position)
                Vector3(position.X, 1.0f, position.Z)
            | None ->
                transforms[SceneRoot.carrierMarker] <- hidden
                Vector3(ballPosition.X, 1.0f, ballPosition.Z)

        transforms, focus

    let private cameraFor (mode: ViewerCamera) (aspect: float32) (focus: Vector3) : Result<CameraState, SceneError> =
        let frame = ViewerCamera.frame mode aspect focus
        let viewProjection = ViewerCamera.viewProjection aspect frame
        let mutable inverse = Matrix4x4.Identity

        if Matrix4x4.Invert(viewProjection, &inverse) then
            let state: CameraState =
                { ViewProjection = viewProjection
                  Inverse = inverse
                  Position = frame.Position
                  NearPlane = frame.NearPlane
                  FarPlane = frame.FarPlane
                  FieldOfView = frame.FieldOfView
                  Aspect = aspect }

            Ok state
        else
            Error SingularCamera

    /// Pure: the same input always yields the same frame, and nothing from a previous call leaks into the next.
    let build (input: SceneInput) : Result<SceneFrame, SceneError> =
        if input.JointPalettes.Length <> SceneRoot.playerCount * input.JointCount then
            Error(InvalidPalettes "procedural palette cardinality does not match the canonical rig")
        else
            seatPlayers input.Frame.Players
            |> Result.bind (fun (seats, carrier) ->
                let rootTransforms, focus = placeActors input seats carrier

                cameraFor input.CameraMode input.Aspect focus
                |> Result.map (fun camera ->
                    let scene: SceneFrame =
                        { Camera = camera
                          Sun = input.Sun
                          LightViewProjection = input.LightViewProjection
                          // Wind phase is float32 on the GPU; wrapping keeps it precise after hours of play.
                          TimeSeconds = input.TimeSeconds % 3600.0
                          RootTransforms = rootTransforms
                          JointPalettes = input.JointPalettes
                          ShowGrassDetail = grassDetailVisible camera.Position.Y }

                    scene))
