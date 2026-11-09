namespace FootballEngine.Client.Match3D

type FrameTarget =
    | ShadowStaticCache
    | ShadowMap
    | SceneColor
    | SceneDepth
    | HdrScene
    | BloomChain
    | HudImage
    | SurfaceColor

type FramePass =
    | ShadowPass
    | MainPass
    | BloomPass
    | OutputPass

type PassIo =
    { Pass: FramePass
      Reads: FrameTarget list
      Writes: FrameTarget list }

type FramePlanError =
    | ReadBeforeWrite of pass: FramePass * target: FrameTarget
    | DuplicateWriter of pass: FramePass * target: FrameTarget

/// The frame is a fixed list of passes; new features (night lights, crowd, weather) are new entries here, and
/// `validate` keeps the order honest about who produces what.
module FramePlan =
    /// Targets valid before the frame begins: the static shadow cache persists across frames once rendered, and
    /// the HUD image is uploaded from the CPU before encoding.
    let external = [ ShadowStaticCache; HudImage ]

    let stadium =
        [ { Pass = ShadowPass
            Reads = [ ShadowStaticCache ]
            Writes = [ ShadowMap ] }
          // The MSAA colour resolves into the HDR scene target inside the main pass.
          { Pass = MainPass
            Reads = [ ShadowMap ]
            Writes = [ SceneColor; SceneDepth; HdrScene ] }
          { Pass = BloomPass
            Reads = [ HdrScene ]
            Writes = [ BloomChain ] }
          { Pass = OutputPass
            Reads = [ HdrScene; SceneDepth; BloomChain; HudImage ]
            Writes = [ SurfaceColor ] } ]

    let validate (plan: PassIo list) : Result<PassIo list, FramePlanError> =
        let rec check (written: Set<FrameTarget>) (remaining: PassIo list) =
            match remaining with
            | [] -> Ok plan
            | step :: rest ->
                match step.Reads |> List.tryFind (fun target -> not (written.Contains target)) with
                | Some target -> Error(ReadBeforeWrite(step.Pass, target))
                | None ->
                    match step.Writes |> List.tryFind written.Contains with
                    | Some target -> Error(DuplicateWriter(step.Pass, target))
                    | None -> check (Set.union written (Set.ofList step.Writes)) rest

        check (Set.ofList external) plan

    let describe (error: FramePlanError) =
        match error with
        | ReadBeforeWrite(pass, target) -> $"{pass} reads {target} before any pass writes it"
        | DuplicateWriter(pass, target) -> $"{pass} writes {target}, which an earlier pass already produced"
