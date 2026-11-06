module FootballEngine.Client.Tests.CharacterAnimationTests

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open System.Text.Json
open Expecto
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D
open FootballEngine.Core.Match

let private path =
    Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

let private asset =
    lazy
        (match CharacterAsset.load path with
         | Ok value -> value
         | Error error -> failtestf "first-party character failed validation: %s" error)

let private sample id time =
    let asset = asset.Value
    let palette = Array.create asset.JointCount Matrix4x4.Identity
    CharacterPose.sampleInto asset asset.Clips[id] time (CharacterPose.createScratch asset) palette 0
    palette

let private jointPositions id time =
    let loaded = asset.Value
    let world = CharacterPose.createScratch loaded
    let palette = Array.create loaded.JointCount Matrix4x4.Identity
    CharacterPose.sampleInto loaded loaded.Clips[id] time world palette 0
    let model = SharpGLTF.Schema2.ModelRoot.Load path

    model.LogicalSkins[0].Joints
    |> Seq.mapi (fun index joint -> joint.Name, world[loaded.JointNodes[index]].Translation)
    |> Map.ofSeq

let tests =
    testList
        "CharacterAnimation"
        [ testCase "posed palms and thumb-side vertices retain an anatomical hand frame" (fun _ ->
              let loaded = asset.Value
              let rig = loaded.Rig
              let model = SharpGLTF.Schema2.ModelRoot.Load path
              let skin = model.LogicalSkins[0]
              let names = skin.Joints |> Seq.mapi (fun index node -> node.Name, index) |> Map.ofSeq
              let root =
                  { Actor = PlayerId 1; Generation = 1; Position = Vector3.Zero; Velocity = Vector3.Zero
                    Facing = 0.0f; AngularVelocity = 0.0f; Condition = 1.0f; AttentionTarget = ValueNone
                    Morphology = PhysicalMorphology.standard; Active = true }
              let config = AnimationConfig.defaultConfig
              for speed in [| 0.0f; 4.0f |] do
                  let sample = { root with Velocity = Vector3(speed, 0.0f, 0.0f) }
                  let plan = SupportPlanner.initialize config 0 sample
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  PoseEvaluation.evaluateInto (config, rig, plan, { Tick = 0; Fraction = 0.0f }, Vector3.Zero, MotionInput.facing sample.Facing, scratch, palette, 0) |> ignore
                  for side, chain, inward in [| "L", rig.LeftArm, -Vector3.UnitX; "R", rig.RightArm, Vector3.UnitX |] do
                      let elbow = scratch.World[chain.Lower].Translation
                      let wrist = scratch.World[chain.End].Translation
                      let direction = Vector3.Normalize(wrist - elbow)
                      let forward = Vector3.Normalize(Vector3.TransformNormal(chain.SocketForward, scratch.World[chain.End]))
                      let palm = Vector3.Normalize(Vector3.TransformNormal(chain.SocketNormal, scratch.World[chain.End]))
                      Expect.isGreaterThan (Vector3.Dot(forward, direction)) 0.99f "wrist continues the forearm"
                      Expect.isGreaterThan (Vector3.Dot(palm, inward)) 0.95f "palm faces the thigh"
                      let indexJoint = rig.JointNodes[names["index_01." + side]]
                      let littleJoint = rig.JointNodes[names["little_01." + side]]
                      let spread = scratch.World[indexJoint].Translation - scratch.World[littleJoint].Translation
                      Expect.isGreaterThan spread.Z 0.0f "thumb/index edge faces forward on both hands"
                      let handJoint = names["hand." + side]
                      let mutable handBind = Matrix4x4.Identity
                      Expect.isTrue (Matrix4x4.Invert(rig.InverseBind[handJoint], &handBind)) "hand bind is invertible"
                      let bindPalm = Vector3.Normalize(Vector3.TransformNormal(chain.SocketNormal, handBind))
                      let vertices =
                          loaded.Batches[0].Vertices
                          |> Array.filter (fun v -> int v.Joints.X = handJoint && v.Weights.X > 0.8f && Vector3.Dot(v.Normal, bindPalm) > 0.8f)
                      Expect.isGreaterThan vertices.Length 0 "hand has visible palm-facing skinned vertices"
                      for vertex in vertices do
                          let transformed = Vector3.Transform(vertex.Position, palette[handJoint])
                          let normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, palette[handJoint]))
                          Expect.isTrue (Single.IsFinite transformed.X && Single.IsFinite transformed.Y && Single.IsFinite transformed.Z) "hand vertices stay finite"
                          Expect.isGreaterThan (Vector3.Dot(normal, inward)) 0.7f "visible palm surface turns towards the thigh")
          testCase "runtime correctives match Blender basis rotations under rotated parents" (fun _ ->
              use golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corrective-golden.json")))
              let matrix (value: JsonElement) =
                  let a = value.EnumerateArray() |> Seq.collect (fun row -> row.EnumerateArray()) |> Seq.map _.GetSingle() |> Seq.toArray
                  Matrix4x4(a[0], a[4], a[8], a[12], a[1], a[5], a[9], a[13], a[2], a[6], a[10], a[14], a[3], a[7], a[11], a[15])

              let pose (matrix: Matrix4x4) : RigLocalPose =
                  { Translation = matrix.Translation
                    Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix matrix)
                    Scale = Vector3.One }

              for entry in golden.RootElement.GetProperty("cases").EnumerateArray() do
                  let get (name: string) = matrix (entry.GetProperty(name))
                  let parent = get "parentWorld"
                  let source = get "sourceWorld"
                  let expected = get "supportWorld"
                  let bindSource = get "sourceBindLocal"
                  let bindHelper = get "supportBindLocal"
                  let rig =
                      { asset.Value.Rig with
                          Parents = [| -1; 0; 0 |]
                          DescendantEnds = [| 2; 1; 2 |]
                          Bind = [| pose Matrix4x4.Identity; pose bindSource; pose bindHelper |]
                          Correctives = [| { Source = 1; Support = 2; Weight = 0.5f
                                             SourceBindInverse = Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion((pose bindSource).Rotation))
                                             SupportBindRotation = Matrix4x4.CreateFromQuaternion((pose bindHelper).Rotation) } |] }

                  let world = [| parent; source; bindHelper * parent |]
                  ConstraintSolve.correctives rig world
                  let beforeSource = world[1]
                  let a, b = world[2], expected
                  let error =
                      [| a.M11-b.M11; a.M12-b.M12; a.M13-b.M13; a.M21-b.M21; a.M22-b.M22; a.M23-b.M23
                         a.M31-b.M31; a.M32-b.M32; a.M33-b.M33; a.M41-b.M41; a.M42-b.M42; a.M43-b.M43 |]
                      |> Array.map abs |> Array.max
                  Expect.isLessThan error 0.0001f (entry.GetProperty("source").GetString())
                  Expect.equal beforeSource source "corrective does not rotate the source twice")
          testCase "ground and ball sockets come from the live boot bind geometry" (fun _ ->
              let rig = asset.Value.Rig
              let world = Array.create rig.Bind.Length Matrix4x4.Identity
              for i = 0 to world.Length - 1 do
                  let p = rig.Bind[i]
                  let local = Matrix4x4.CreateFromQuaternion(p.Rotation) * Matrix4x4.CreateTranslation(p.Translation)
                  world[i] <- if rig.Parents[i] < 0 then local else local * world[rig.Parents[i]]
              for leg in [| rig.LeftLeg; rig.RightLeg |] do
                  let ground = Vector3.Transform(leg.Socket, world[leg.End])
                  let ball = Vector3.Transform(leg.BallSocket, world[leg.End])
                  Expect.isLessThan (abs (ground.Y - 0.005f)) 0.00001f "ground socket on sole"
                  Expect.isLessThan (abs (ground.Z - 0.0875f)) 0.00001f "sole footprint center"
                  Expect.isLessThan (abs (ball.Z - 0.275f)) 0.00001f "ball socket on front cap"
              Expect.equal rig.Correctives.Length 8 "all authored support rules loaded")
          testCase "contact time maps to the authored marker regardless of windup duration" (fun _ ->
              let clip = asset.Value.Clips["shot_right"]

              Expect.equal
                  (CharacterPose.contactSampleTime clip 1.0 1.15 1.45 1.15)
                  clip.ContactTime.Value
                  "same impact time"

              Expect.equal (CharacterPose.contactSampleTime clip 1.0 1.15 1.45 1.45) clip.Duration "recovery end"
              Expect.equal (CharacterPose.contactSampleTime clip 1.0 1.15 1.45 1.0) 0.0 "preparation start")
          testCase "contact IK preserves both leg lengths and leaves the other leg untouched" (fun _ ->
              let loaded = asset.Value

              for left in [ false; true ] do
                  let world = CharacterPose.createScratch loaded
                  let palette = Array.create loaded.JointCount Matrix4x4.Identity
                  CharacterPose.sampleInto loaded loaded.Clips["idle"] 0.0 world palette 0
                  let struct (hip, knee, ankle, _) = if left then loaded.LeftLeg else loaded.RightLeg

                  let struct (otherHip, otherKnee, otherAnkle, _) =
                      if left then loaded.RightLeg else loaded.LeftLeg

                  let other = [| world[otherHip]; world[otherKnee]; world[otherAnkle] |]

                  let upper, lower =
                      Vector3.Distance(world[hip].Translation, world[knee].Translation),
                      Vector3.Distance(world[knee].Translation, world[ankle].Translation)

                  CharacterPose.alignFootInto loaded left (Vector3(0.15f, 0.12f, 0.45f)) 1.0f world palette 0

                  Expect.floatClose
                      Accuracy.medium
                      (float (Vector3.Distance(world[hip].Translation, world[knee].Translation)))
                      (float upper)
                      "femur stays rigid"

                  Expect.floatClose
                      Accuracy.medium
                      (float (Vector3.Distance(world[knee].Translation, world[ankle].Translation)))
                      (float lower)
                      "shin stays rigid"

                  Expect.equal
                      [| world[otherHip]; world[otherKnee]; world[otherAnkle] |]
                      other
                      "support leg is not displaced")
          testCase "canonical anatomical pack contains weighted geometry and all authored motions" (fun _ ->
              let asset = asset.Value
              Expect.equal asset.JointCount 60 "canonical rig joint count"
              Expect.equal asset.Clips.Count 18 "all authored motions are present"
              Expect.isGreaterThan asset.Batches[0].Vertices.Length 0 "players have geometry"

              Expect.isTrue
                  (asset.Batches[0].Vertices
                   |> Array.forall (fun vertex ->
                       abs (vertex.Weights.X + vertex.Weights.Y + vertex.Weights.Z + vertex.Weights.W - 1.0f) < 0.0001f))
                  "every player vertex has normalized skin weights")
          testCase "an authored kick reaches a nearby surface contact with either boot" (fun _ ->
              let loaded = asset.Value

              for left in [ false; true ] do
                  let world = CharacterPose.createScratch loaded
                  let palette = Array.create loaded.JointCount Matrix4x4.Identity
                  let clip = loaded.Clips[if left then "pass_left" else "pass_right"]
                  CharacterPose.sampleInto loaded clip clip.ContactTime.Value world palette 0
                  let target = Vector3((if left then -0.1f else 0.1f), 0.11f, 0.30f)
                  CharacterPose.alignFootInto loaded left target 1.0f world palette 0
                  let struct (_, _, _, toe) = if left then loaded.LeftLeg else loaded.RightLeg

                  Expect.isLessThan
                      (Vector3.Distance(world[toe].Translation, target))
                      0.03f
                      "boot contact error remains below three centimetres")
          testCase "vertex ABI offsets match the WGSL input" (fun _ ->
              Expect.equal (Marshal.SizeOf<GpuVertex>()) 88 "vertex stride"

              let vertex =
                  { Position = Vector3.Zero
                    Normal = Vector3.UnitY
                    Color = Vector4.One
                    Uv = Vector2.Zero
                    RootIndex = 0u
                    Joints = Vector4(12.0f)
                    Weights = Vector4(0.25f)
                    Surface = uint32 StadiumSurface.Concrete }

              let bytes = MemoryMarshal.AsBytes(ReadOnlySpan<GpuVertex>([| vertex |])).ToArray()
              Expect.equal (BitConverter.ToSingle(bytes, 52)) 12.0f "joint attribute offset"
              Expect.equal (BitConverter.ToSingle(bytes, 68)) 0.25f "weight attribute offset"

              Expect.equal
                  (BitConverter.ToUInt32(bytes, 84))
                  (uint32 StadiumSurface.Concrete)
                  "material attribute does not overlap skin weights")
          testCase "stadium materials have explicit surface identities without tagging kits as grass" (fun _ ->
              for name, expected in
                  [ "Grass | stripe 0.003", StadiumSurface.TurfLight
                    "Grass | stripe 1", StadiumSurface.TurfDark
                    "Runoff grass", StadiumSurface.TurfRunoff
                    "Architectural concrete", StadiumSurface.Concrete
                    "Midnight steel", StadiumSurface.Steel
                    "Ivory enamel", StadiumSurface.Enamel
                    "Match kit 1 shirt", StadiumSurface.Plain
                    "Chalk | warm white", StadiumSurface.Plain ] do
                  Expect.equal
                      (StadiumSurface.ofMaterialName name)
                      expected
                      "material role follows the asset contract, not its colour")
          testCase "surface pack validates all offline mip layers and hashes" (fun _ ->
              let directory =
                  Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "Surfaces")

              match SurfaceTexturePack.load directory with
              | Error error -> failtest error
              | Ok pack ->
                  Expect.equal (pack.Size, pack.Layers, pack.Levels) (1024, 2, 11) "complete tile mip chain"

                  let bytes =
                      [ 0..10 ]
                      |> List.sumBy (fun level -> let width = 1024 >>> level in width * width * 8)

                  for data in [ pack.Albedo; pack.Normal; pack.Arm ] do
                      Expect.equal data.Length bytes "all mips and both layers are present"

                  Expect.notEqual pack.Albedo pack.Normal "normal is not colour data")
          testCase "tampered surface pack is rejected before uploading to WebGPU" (fun _ ->
              let directory =
                  Path.Combine("/tmp/opencode", "surface-pack-test-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory directory |> ignore

              try
                  let source =
                      Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "Surfaces")

                  for name in [ "manifest.json"; "albedo.bin" ] do
                      File.Copy(Path.Combine(source, name), Path.Combine(directory, name))

                  let path = Path.Combine(directory, "albedo.bin")
                  let bytes = File.ReadAllBytes path
                  bytes[0] <- bytes[0] ^^^ 1uy
                  File.WriteAllBytes(path, bytes)
                  Expect.isError (SurfaceTexturePack.load directory) "edited pack cannot silently render"
              finally
                  Directory.Delete(directory, true))
          testCase "sampling does not depend on previously displayed frames" (fun _ ->
              let expected = sample "run" 0.23
              sample "shot_right" 0.75 |> ignore
              sample "idle" 100.0 |> ignore
              Expect.equal (sample "run" 0.23) expected "seek reconstructs the same palette")
          testCase "motion really deforms the skeleton" (fun _ ->
              Expect.notEqual (sample "run" 0.1) (sample "run" 0.3) "running has distinct skeletal poses")
          testCase "idle wrists hang beside the torso instead of retaining the bind pose" (fun _ ->
              let joints = jointPositions "idle" 0.0

              for suffix in [ "L"; "R" ] do
                  let shoulder = joints["upper_arm." + suffix]
                  let wrist = joints["hand." + suffix]
                  Expect.isLessThan (abs (wrist.X - shoulder.X)) 0.15f "arms do not flare sideways"
                  Expect.isLessThan wrist.Y (shoulder.Y - 0.3f) "wrists hang below the shoulders")
          testCase "walking and running animate both arms opposite the advancing legs" (fun _ ->
              for id in [ "walk"; "run"; "sprint" ] do
                  let duration = asset.Value.Clips[id].Duration
                  let first = jointPositions id 0.0
                  let second = jointPositions id (duration * 0.5)

                  for suffix in [ "L"; "R" ] do
                      let hand = "hand." + suffix
                      let foot = "foot." + suffix
                      let armTravel = first[hand].Z - second[hand].Z
                      let footTravel = first[foot].Z - second[foot].Z
                      let minimumSwing = if id = "walk" then 0.08f else 0.15f

                      Expect.isGreaterThan
                          (abs armTravel)
                          minimumSwing
                          "each arm has a speed-appropriate forward/backward swing"

                      Expect.isLessThan (armTravel * footTravel) 0.0f "ipsilateral arm counterbalances the leg")
          testCase "locomotion elbows stay below the shoulders throughout the stride" (fun _ ->
              for id in [ "walk"; "run"; "sprint" ] do
                  for phase in [ 0.0; 0.25; 0.5; 0.75 ] do
                      let joints = jointPositions id (asset.Value.Clips[id].Duration * phase)

                      for suffix in [ "L"; "R" ] do
                          Expect.isLessThan
                              joints["forearm." + suffix].Y
                              joints["upper_arm." + suffix].Y
                              "elbows do not invert upward during the backswing")
          testCase "running keeps a bent elbow and preserves segment lengths through the whole cycle" (fun _ ->
              let model = SharpGLTF.Schema2.ModelRoot.Load path

              let bindLength name =
                  model.LogicalSkins[0].Joints
                  |> Seq.find (fun joint -> joint.Name = name)
                  |> fun joint -> float (joint.LocalTransform.Translation.Length())

              for id in [ "run"; "sprint" ] do
                  for phase in 0..23 do
                      let joints = jointPositions id (asset.Value.Clips[id].Duration * float phase / 24.0)

                      for suffix in [ "L"; "R" ] do
                          let shoulder = joints["upper_arm." + suffix]
                          let elbow = joints["forearm." + suffix]
                          let wrist = joints["hand." + suffix]
                          let upper = shoulder - elbow
                          let lower = wrist - elbow

                          let angle =
                              Math.Acos(float (Vector3.Dot(Vector3.Normalize upper, Vector3.Normalize lower)))
                              * 180.0
                              / Math.PI

                          Expect.isGreaterThan angle 85.0 "elbow does not collapse into the upper arm"
                          Expect.isLessThan angle 115.0 "running does not straighten the elbow into a rigid pendulum"

                          // Exported translation curves carry submillimetre quantization, not perfectly rigid doubles.
                          Expect.isLessThan
                              (abs (float (upper.Length()) - bindLength ("forearm." + suffix)))
                              0.0001
                              "upper-arm length remains anatomical"

                          Expect.isLessThan
                              (abs (float (lower.Length()) - bindLength ("hand." + suffix)))
                              0.0001
                              "forearm length remains anatomical")
          testCase "loop sampling preserves phase across repeated cycles" (fun _ ->
              let duration = asset.Value.Clips["walk"].Duration
              let first, later = sample "walk" 0.25, sample "walk" (duration * 10.0 + 0.25)
              Expect.equal later first "absolute phase is independent of loop count")
          testCase "one-shot does not repeat its contact after its end" (fun _ ->
              let duration = asset.Value.Clips["shot_right"].Duration

              Expect.equal
                  (sample "shot_right" (duration + 20.0))
                  (sample "shot_right" duration)
                  "one-shot clamps at recovery end")
          testCase "anatomical pack carries readable albedo and normal roughness atlases" (fun _ ->
              let loaded = asset.Value

              for texture in [ loaded.Albedo; loaded.NormalRoughness ] do
                  Expect.equal texture.Levels.Length 9 "all mips are authored offline"

                  for level, mip in Array.indexed texture.Levels do
                      let size = 4096 >>> level
                      Expect.equal (mip.Width, mip.Height) (size, size) "atlas mip dimensions"
                      Expect.equal mip.Pixels.Length (size * size * 4) "RGBA byte count"

              Expect.notEqual
                  loaded.Albedo.Levels[0].Pixels
                  loaded.NormalRoughness.Levels[0].Pixels
                  "detail is not the diffuse image"

              Expect.isGreaterThan
                  loaded.Batches[0].Vertices.Length
                  12000
                  "facial, finger and clothing topology is retained")
          testCase "athlete albedo mips average radiance rather than sRGB bytes" (fun _ ->
              let levels = asset.Value.Albedo.Levels

              for level in 1 .. levels.Length - 1 do
                  let source, target = levels[level - 1], levels[level]

                  for x, y in [ target.Width / 5, target.Height / 7; target.Width / 2, target.Height / 3 ] do
                      for channel in 0..2 do
                          let linear =
                              [ 0, 0; 1, 0; 0, 1; 1, 1 ]
                              |> List.averageBy (fun (dx, dy) ->
                                  float32 source.Pixels[((y * 2 + dy) * source.Width + x * 2 + dx) * 4 + channel]
                                  / 255.0f
                                  |> HdrLighting.srgbToLinear)

                          let expected =
                              linear
                              |> HdrLighting.linearToSrgb
                              |> fun value -> int (MathF.Round(value * 255.0f))

                          let actual = int target.Pixels[(y * target.Width + x) * 4 + channel]

                          Expect.isLessThanOrEqual
                              (abs (expected - actual))
                              2
                              "offline float chain only differs by byte quantization")
          testCase "athlete mip normals remain normalized at every viewing distance" (fun _ ->
              for mip in asset.Value.NormalRoughness.Levels |> Array.skip 1 do
                  for x, y in [ mip.Width / 5, mip.Height / 7; mip.Width / 2, mip.Height / 3 ] do
                      let offset = (y * mip.Width + x) * 4

                      let normal =
                          Vector3(
                              float32 mip.Pixels[offset],
                              float32 mip.Pixels[offset + 1],
                              float32 mip.Pixels[offset + 2]
                          )
                          / 127.5f
                          - Vector3.One

                      Expect.isLessThan
                          (abs (normal.Length() - 1.0f))
                          0.015f
                          "normal filtering cannot shrink the shading frame")
          testCase "athlete material identity is explicit and stable across every triangle" (fun _ ->
              let batch = asset.Value.Batches[0]
              let expected = [ 16u .. 23u ] |> Set.ofList

              let actual =
                  batch.Vertices |> Array.map (fun vertex -> vertex.Surface) |> Set.ofArray

              Expect.isTrue (Set.isSubset actual expected) "only declared atlas materials survive loading"

              Expect.isTrue
                  (Set.isSubset (Set.ofList [ 16u; 17u; 18u; 19u; 21u; 23u ]) actual)
                  "cloth, skin, boots and eyes retain distinct identities"

              for slot in 0..7 do
                  let uv =
                      Vector2((float32 (slot % 4) + 0.5f) / 4.0f, (float32 (slot / 4) + 0.5f) / 4.0f)

                  Expect.equal (AthleteMaterial.ofTriangle uv uv uv |> uint32) (16u + uint32 slot) "authored slot ABI"

              for triangle in batch.Indices |> Array.chunkBySize 3 do
                  let surfaces =
                      triangle |> Array.map (fun index -> batch.Vertices[int index].Surface)

                  Expect.equal surfaces[0] surfaces[1] "no interpolated material identity"
                  Expect.equal surfaces[1] surfaces[2] "no interpolated material identity")
          testCase "all instances share one canonical mesh and kit atlas coordinates" (fun _ ->
              let vertices = asset.Value.Batches[0].Vertices

              Expect.isTrue
                  (vertices |> Array.forall (fun vertex -> vertex.RootIndex = 0u))
                  "no per-player geometry duplication"

              Expect.isTrue
                  (vertices |> Array.forall (fun vertex -> vertex.Uv.Y <= 0.5f))
                  "canonical home atlas half; instance shader selects away half"

              Expect.isTrue
                  (vertices |> Array.exists (fun vertex -> vertex.Uv <> Vector2.Zero))
                  "UVs are not discarded"

              let model = SharpGLTF.Schema2.ModelRoot.Load path

              let sourceCount =
                  model.LogicalMeshes
                  |> Seq.sumBy (fun mesh ->
                      mesh.Primitives
                      |> Seq.sumBy (fun primitive -> primitive.GetVertexAccessor("POSITION").Count))

              Expect.equal vertices.Length sourceCount "one copy of each authored vertex, not 22")
          testCase "an edited texture is rejected before it reaches the GPU" (fun _ ->
              let directory =
                  Path.Combine("/tmp/opencode", "character-texture-test-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory directory |> ignore

              try
                  let source = Path.GetDirectoryName path

                  for name in [ "player.glb"; "player-motion.json"; "player-albedo.png" ] do
                      File.Copy(Path.Combine(source, name), Path.Combine(directory, name))

                  let texturePath = Path.Combine(directory, "player-albedo.png")
                  let bytes = File.ReadAllBytes texturePath
                  bytes[bytes.Length - 1] <- bytes[bytes.Length - 1] ^^^ 1uy
                  File.WriteAllBytes(texturePath, bytes)

                  match CharacterAsset.load (Path.Combine(directory, "player.glb")) with
                  | Error message ->
                      Expect.stringContains message "does not match the manifest" "texture integrity failure"
                  | Ok _ -> failtest "modified texture was accepted"
              finally
                  Directory.Delete(directory, true))
          testCase "missing pack reports a structured load error" (fun _ ->
              match CharacterAsset.load (Path.Combine(AppContext.BaseDirectory, "missing-first-party-player.glb")) with
              | Error _ -> ()
              | Ok _ -> failtest "missing character was silently accepted") ]
