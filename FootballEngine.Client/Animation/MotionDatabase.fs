namespace FootballEngine.Client.Animation

open System
open System.IO
open System.Numerics
open System.Security.Cryptography
open System.Text

/// One retargeted capture. Frames are a contiguous range of the database arrays.
type MotionClip =
    { Name: string
      Source: string
      Family: string
      Start: int
      Count: int }

/// Retargeted locomotion and action frames for the runtime rig (built by Tools/MatchViewer3D/Mocap). Root values
/// are in the rig's Y-up space: ground point (X, Z), yaw about +Y, and the pelvis offset expressed in the yaw frame.
/// Rotations are joint locals in parent space, one row of DrivenJoints per frame; undriven joints keep their bind.
type MotionDatabase =
    { RateHz: int
      DrivenJoints: string[]
      Clips: MotionClip[]
      GroundX: float32[]
      GroundZ: float32[]
      Yaw: float32[]
      Pelvis: Vector3[]
      /// Bit 0 left foot planted, bit 1 right foot planted.
      Contacts: byte[]
      Rotations: Quaternion[]
      Sha256: string }

    member this.FrameCount = this.Yaw.Length

    member this.Rotation(frame: int, joint: int) =
        this.Rotations[frame * this.DrivenJoints.Length + joint]

type MotionDatabaseError =
    | NotAMotionDatabase
    | UnsupportedVersion of int
    | Truncated of offset: int
    | TrailingBytes of count: int
    | InvalidHeader of reason: string
    | InvalidClip of clip: string * reason: string

module MotionDatabase =
    let private magic = "FEMODB01"
    // Dequantised int16 quaternions sit within ~1e-4 of unit length; anything further is corruption, not rounding.
    let private quaternionTolerance = 2e-3f
    // Faster than any footballer sprints (~10-11 m/s) means a retarget or units error, not real motion.
    let private maximumRootSpeed = 12.0f
    // A half turn between two frames cannot come from a body; it is an unwrapped yaw or a broken clip.
    let private maximumYawStep = MathF.PI * 0.5f

    type private Reader(data: byte[]) =
        let mutable offset = 0

        member _.Offset = offset

        member _.Take(count: int) =
            if count < 0 || offset + count > data.Length then
                Error(Truncated offset)
            else
                let start = offset
                offset <- offset + count
                Ok start

        member this.UInt16() =
            this.Take 2 |> Result.map (fun at -> int (BitConverter.ToUInt16(data, at)))

        member this.UInt32() =
            this.Take 4 |> Result.map (fun at -> BitConverter.ToUInt32(data, at))

        member this.Single() =
            this.Take 4 |> Result.map (fun at -> BitConverter.ToSingle(data, at))

        member this.Byte() = this.Take 1 |> Result.map (fun at -> data[at])

        member this.String() =
            this.UInt16()
            |> Result.bind (fun length -> this.Take length |> Result.map (fun at -> Encoding.UTF8.GetString(data, at, length)))

    let private sequence (items: (unit -> Result<'T, MotionDatabaseError>) seq) =
        let output = ResizeArray<'T>()
        let mutable failure = None

        use cursor = items.GetEnumerator()

        while failure.IsNone && cursor.MoveNext() do
            match cursor.Current() with
            | Ok value -> output.Add value
            | Error error -> failure <- Some error

        match failure with
        | Some error -> Error error
        | None -> Ok(output.ToArray())

    let private validateClip (db: MotionDatabase) (clip: MotionClip) =
        let dt = 1.0f / float32 db.RateHz
        let mutable problem = None
        let joints = db.DrivenJoints.Length

        for frame = clip.Start to clip.Start + clip.Count - 1 do
            if problem.IsNone then
                let pelvis = db.Pelvis[frame]

                if
                    not (
                        Single.IsFinite db.GroundX[frame]
                        && Single.IsFinite db.GroundZ[frame]
                        && Single.IsFinite db.Yaw[frame]
                        && Single.IsFinite pelvis.X
                        && Single.IsFinite pelvis.Y
                        && Single.IsFinite pelvis.Z
                    )
                then
                    problem <- Some $"non-finite root at frame {frame - clip.Start}"
                elif db.Contacts[frame] > 3uy then
                    problem <- Some $"unknown contact bits at frame {frame - clip.Start}"
                elif frame > clip.Start then
                    let dx = db.GroundX[frame] - db.GroundX[frame - 1]
                    let dz = db.GroundZ[frame] - db.GroundZ[frame - 1]

                    if sqrt (dx * dx + dz * dz) / dt > maximumRootSpeed then
                        problem <- Some $"root faster than {maximumRootSpeed} m/s at frame {frame - clip.Start}"
                    elif abs (db.Yaw[frame] - db.Yaw[frame - 1]) > maximumYawStep then
                        problem <- Some $"yaw discontinuity at frame {frame - clip.Start}"

                for joint = 0 to joints - 1 do
                    let q = db.Rotations[frame * joints + joint]

                    if problem.IsNone && abs (q.Length() - 1.0f) > quaternionTolerance then
                        problem <- Some $"non-unit rotation for {db.DrivenJoints[joint]} at frame {frame - clip.Start}"

        match problem with
        | Some reason -> Error(InvalidClip(clip.Name, reason))
        | None -> Ok()

    /// Parses and validates a database. Rotations are renormalised after the tolerance check so consumers never see
    /// quantisation drift.
    let read (data: byte[]) : Result<MotionDatabase, MotionDatabaseError> =
        if data.Length < 8 || Encoding.ASCII.GetString(data, 0, 8) <> magic then
            Error NotAMotionDatabase
        else
            let reader = Reader data

            let header =
                reader.Take 8
                |> Result.bind (fun _ -> reader.UInt32())
                |> Result.bind (fun version ->
                    if version <> 1u then
                        Error(UnsupportedVersion(int version))
                    else
                        reader.UInt16()
                        |> Result.bind (fun rate -> reader.UInt16() |> Result.map (fun joints -> rate, joints)))

            match header with
            | Error error -> Error error
            | Ok(rate, _) when rate < 30 || rate > 120 -> Error(InvalidHeader $"rate {rate} Hz outside 30-120")
            | Ok(_, joints) when joints = 0 -> Error(InvalidHeader "no driven joints")
            | Ok(rate, jointCount) ->
                let names = sequence (Seq.init jointCount (fun _ () -> reader.String()))

                match names with
                | Error error -> Error error
                | Ok names when (Array.distinct names).Length <> names.Length -> Error(InvalidHeader "duplicate driven joint")
                | Ok names ->
                    match reader.UInt32() with
                    | Error error -> Error error
                    | Ok clipCount ->
                        let groundX = ResizeArray<float32>()
                        let groundZ = ResizeArray<float32>()
                        let yaw = ResizeArray<float32>()
                        let pelvis = ResizeArray<Vector3>()
                        let contacts = ResizeArray<byte>()
                        let rotations = ResizeArray<Quaternion>()

                        let readFrame () =
                            let values = sequence (Seq.init 6 (fun _ () -> reader.Single()))

                            match values, reader.Byte() with
                            | Ok v, Ok contact ->
                                groundX.Add v[0]
                                groundZ.Add v[1]
                                yaw.Add v[2]
                                pelvis.Add(Vector3(v[3], v[4], v[5]))
                                contacts.Add contact

                                reader.Take(jointCount * 8)
                                |> Result.map (fun at ->
                                    let scale = 1.0f / 32767.0f
                                    let component index = float32 (BitConverter.ToInt16(data, at + index * 2)) * scale

                                    for joint = 0 to jointCount - 1 do
                                        let first = joint * 4

                                        rotations.Add(
                                            Quaternion(
                                                component first,
                                                component (first + 1),
                                                component (first + 2),
                                                component (first + 3)
                                            )
                                        ))
                            | Error error, _
                            | _, Error error -> Error error

                        let readClip () =
                            match reader.String(), reader.String(), reader.String(), reader.UInt32() with
                            | Ok name, Ok source, Ok family, Ok frames ->
                                let start = yaw.Count

                                if frames = 0u then
                                    Error(InvalidClip(name, "no frames"))
                                else
                                    sequence (Seq.init (int frames) (fun _ () -> readFrame ()))
                                    |> Result.map (fun _ ->
                                        { Name = name
                                          Source = source
                                          Family = family
                                          Start = start
                                          Count = int frames })
                            | Error error, _, _, _
                            | _, Error error, _, _
                            | _, _, Error error, _
                            | _, _, _, Error error -> Error error

                        match sequence (Seq.init (int clipCount) (fun _ () -> readClip ())) with
                        | Error error -> Error error
                        | Ok _ when reader.Offset <> data.Length -> Error(TrailingBytes(data.Length - reader.Offset))
                        | Ok clips when (clips |> Array.distinctBy _.Name).Length <> clips.Length ->
                            Error(InvalidHeader "duplicate clip name")
                        | Ok clips ->
                            let db =
                                { RateHz = rate
                                  DrivenJoints = names
                                  Clips = clips
                                  GroundX = groundX.ToArray()
                                  GroundZ = groundZ.ToArray()
                                  Yaw = yaw.ToArray()
                                  Pelvis = pelvis.ToArray()
                                  Contacts = contacts.ToArray()
                                  Rotations = rotations.ToArray()
                                  Sha256 = Convert.ToHexString(SHA256.HashData data).ToLowerInvariant() }

                            match clips |> Array.tryPick (fun clip -> match validateClip db clip with Error e -> Some e | Ok() -> None) with
                            | Some error -> Error error
                            | None ->
                                for index = 0 to db.Rotations.Length - 1 do
                                    db.Rotations[index] <- Quaternion.Normalize db.Rotations[index]

                                Ok db

    let load (path: string) =
        try
            read (File.ReadAllBytes path)
        with :? IOException as error ->
            Error(InvalidHeader error.Message)
