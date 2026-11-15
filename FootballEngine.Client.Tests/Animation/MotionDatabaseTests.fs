namespace FootballEngine.Client.Tests.Animation

open System
open System.IO
open System.Numerics
open System.Text
open Expecto
open FootballEngine.Client.Animation

module MotionDatabaseTests =
    let private assetDirectory =
        Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer")

    let private shipped =
        lazy
            (match MotionDatabase.load (Path.Combine(assetDirectory, "motion-db.bin")) with
             | Ok db -> db
             | Error error -> failtestf "shipped motion database failed validation: %A" error)

    /// Independent writer for the documented format, so corruption cases do not depend on the shipped asset layout.
    let private synthetic (frames: (float32 * float32 * float32 * byte * (int16 * int16 * int16 * int16)[]) list) =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)

        let text (value: string) =
            let bytes = Encoding.UTF8.GetBytes value
            writer.Write(uint16 bytes.Length)
            writer.Write bytes

        writer.Write(Encoding.ASCII.GetBytes "FEMODB01")
        writer.Write 1u
        writer.Write 60us
        writer.Write 2us
        text "pelvis"
        text "thigh.L"
        writer.Write 1u
        text "fixture"
        text "test fixture"
        text "locomotion"
        writer.Write(uint32 (List.length frames))

        for x, z, yaw, contacts, rotations in frames do
            for value in [ x; z; yaw; 0.0f; 0.95f; 0.0f ] do
                writer.Write(value: float32)

            writer.Write(contacts: byte)

            for a, b, c, d in rotations do
                writer.Write a
                writer.Write b
                writer.Write c
                writer.Write d

        writer.Flush()
        stream.ToArray()

    let private identity = (0s, 0s, 0s, 32767s)

    let private walking =
        [ for frame in 0..9 -> float32 frame * 0.02f, 0.0f, 0.0f, 1uy, [| identity; identity |] ]

    let tests =
        testList
            "MotionDatabase"
            [ testCase "a well-formed database round-trips its root, contacts and rotations" (fun _ ->
                  match MotionDatabase.read (synthetic walking) with
                  | Error error -> failtestf "valid fixture rejected: %A" error
                  | Ok db ->
                      Expect.equal db.FrameCount 10 "every frame is read"
                      Expect.equal db.Clips[0].Count 10 "the clip owns all frames"
                      Expect.floatClose Accuracy.high (float db.GroundX[9]) 0.18 "root position survives"
                      Expect.equal db.Contacts[3] 1uy "contact bits survive"
                      Expect.floatClose Accuracy.medium (float (db.Rotation(4, 1).W)) 1.0 "rotations are dequantised")
              testCase "corrupt databases are rejected with the specific defect" (fun _ ->
                  let valid = synthetic walking
                  let flipped = Array.copy valid
                  flipped[0] <- byte 'X'
                  Expect.equal (MotionDatabase.read flipped) (Error NotAMotionDatabase) "wrong magic"

                  match MotionDatabase.read valid[.. valid.Length - 5] with
                  | Error(Truncated _) -> ()
                  | other -> failtestf "truncated file accepted or misreported: %A" other

                  match MotionDatabase.read (Array.append valid [| 0uy |]) with
                  | Error(TrailingBytes 1) -> ()
                  | other -> failtestf "trailing byte accepted or misreported: %A" other

                  let halfQuaternion =
                      synthetic [ 0.0f, 0.0f, 0.0f, 0uy, [| identity; (0s, 0s, 0s, 16000s) |] ]

                  match MotionDatabase.read halfQuaternion with
                  | Error(InvalidClip("fixture", reason)) -> Expect.stringContains reason "thigh.L" "names the joint"
                  | other -> failtestf "non-unit rotation accepted: %A" other

                  let teleport =
                      synthetic
                          [ 0.0f, 0.0f, 0.0f, 0uy, [| identity; identity |]
                            1.0f, 0.0f, 0.0f, 0uy, [| identity; identity |] ]

                  match MotionDatabase.read teleport with
                  | Error(InvalidClip(_, reason)) -> Expect.stringContains reason "root faster" "60 m/s root step"
                  | other -> failtestf "root teleport accepted: %A" other

                  let spin =
                      synthetic
                          [ 0.0f, 0.0f, 0.0f, 0uy, [| identity; identity |]
                            0.0f, 0.0f, 3.0f, 0uy, [| identity; identity |] ]

                  match MotionDatabase.read spin with
                  | Error(InvalidClip(_, reason)) -> Expect.stringContains reason "yaw" "half-turn in one frame"
                  | other -> failtestf "yaw discontinuity accepted: %A" other)
              testCase
                  "the shipped database drives joints that exist in the player rig, with provenance per clip"
                  (fun _ ->
                      let db = shipped.Force()

                      let model =
                          SharpGLTF.Schema2.ModelRoot.Load(Path.Combine(assetDirectory, "player.glb"))

                      let rigJoints = model.LogicalSkins[0].Joints |> Seq.map _.Name |> Set.ofSeq

                      for joint in db.DrivenJoints do
                          Expect.isTrue (rigJoints.Contains joint) $"driven joint {joint} exists in the rig"

                      Expect.equal db.RateHz 60 "display-rate sampling"
                      Expect.isGreaterThanOrEqual db.FrameCount 30000 "enough poses for matching"

                      for clip in db.Clips do
                          Expect.stringStarts clip.Source "CMU " $"{clip.Name} names its capture source"

                      let families = db.Clips |> Array.map _.Family |> Set.ofArray

                      for family in [ "locomotion"; "footwork"; "walking"; "kick"; "reach" ] do
                          Expect.isTrue (families.Contains family) $"family {family} is covered")
              testCase "the shipped capture has no source flips and a root that does not sway with the stride" (fun _ ->
                  // Faster than ~2400 deg/s between 60 Hz frames no joint moves, kicking knees included: such a step
                  // is an Euler wrap or marker swap that the pose would show as a one-frame twist.
                  let db = shipped.Force()
                  let joints = db.DrivenJoints.Length

                  for clip in db.Clips do
                      for frame = clip.Start + 1 to clip.Start + clip.Count - 1 do
                          for joint = 0 to joints - 1 do
                              let dot =
                                  abs (Quaternion.Dot(db.Rotation(frame - 1, joint), db.Rotation(frame, joint)))

                              let degrees = 2.0 * Math.Acos(min 1.0 (float dot)) * 180.0 / Math.PI

                              Expect.isLessThan
                                  degrees
                                  40.0
                                  $"{clip.Name} {db.DrivenJoints[joint]} frame {frame - clip.Start}"

                  // Straight running: the root heading follows the path, so its spread stays well under the pelvis swing.
                  for name in [ "16_45"; "09_01"; "35_22" ] do
                      let clip = db.Clips |> Array.find (fun clip -> clip.Name = name)
                      let yaws = db.Yaw[clip.Start .. clip.Start + clip.Count - 1]
                      let spread = float (Array.max yaws - Array.min yaws) * 180.0 / Math.PI
                      Expect.isLessThan spread 6.0 $"{name} root heading spread {spread:F1} deg")
              testCase "running clips alternate short single-foot stances with flight, as a run must" (fun _ ->
                  // Labels are derived from the retargeted feet; an all-planted, never-planted or skating (stance that
                  // never ends) labeller fails here. Stance at running pace lasts ~0.1-0.3 s in the literature.
                  let db = shipped.Force()
                  let mutable checkedClips = 0

                  for clip in db.Clips do
                      let last = clip.Start + clip.Count - 1
                      let dx = db.GroundX[last] - db.GroundX[clip.Start]
                      let dz = db.GroundZ[last] - db.GroundZ[clip.Start]
                      let speed = sqrt (dx * dx + dz * dz) / (float32 clip.Count / float32 db.RateHz)

                      if clip.Family = "locomotion" && speed > 3.0f then
                          checkedClips <- checkedClips + 1
                          let frames = [| clip.Start .. last |]

                          for bit, foot in [ 1uy, "left"; 2uy, "right" ] do
                              let planted = frames |> Array.map (fun f -> db.Contacts[f] &&& bit <> 0uy)
                              let stances = ResizeArray<int * int>()
                              let mutable start = -1

                              for i = 0 to planted.Length - 1 do
                                  if planted[i] && start < 0 then
                                      start <- i

                                  if not planted[i] && start >= 0 then
                                      stances.Add(start, i)
                                      start <- -1

                              if start >= 0 then
                                  stances.Add(start, planted.Length)

                              Expect.isGreaterThan stances.Count 0 $"{clip.Name}: the {foot} foot plants at least once"

                              for first, past in stances do
                                  // Stances cut by the clip edges are partial and say nothing about duration.
                                  if first > 0 && past < planted.Length then
                                      let seconds = float (past - first) / float db.RateHz

                                      Expect.isTrue
                                          (seconds >= 0.05 && seconds <= 0.45)
                                          $"{clip.Name}: {foot} stance of {seconds:F2} s"

                          let both =
                              frames |> Array.averageBy (fun f -> if db.Contacts[f] = 3uy then 1.0 else 0.0)

                          let flight =
                              frames |> Array.averageBy (fun f -> if db.Contacts[f] = 0uy then 1.0 else 0.0)

                          Expect.isLessThan both 0.25 $"{clip.Name}: a run has almost no double support"
                          Expect.isGreaterThan flight 0.05 $"{clip.Name}: a run has flight phases"

                  Expect.isGreaterThan checkedClips 10 "the oracle covers a real set of running clips") ]
