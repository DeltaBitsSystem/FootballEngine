namespace FootballEngine.Domain

/// Pure per-slot state machine used by the client autosave worker.
module AutosaveQueue =

    type Request<'a> =
        { Slot: string
          Revision: uint64
          Payload: 'a }

    type State<'a> =
        { Active: Map<string, uint64>
          Pending: Map<string, Request<'a>> }

    type EnqueueDecision =
        | Accepted
        | Coalesced
        | RejectedStale

    let empty<'a> : State<'a> =
        { Active = Map.empty
          Pending = Map.empty }

    let enqueue (request: Request<'a>) (state: State<'a>) : EnqueueDecision * State<'a> =
        let activeRevision = state.Active |> Map.tryFind request.Slot
        let pending = state.Pending |> Map.tryFind request.Slot

        if
            activeRevision |> Option.exists (fun revision -> revision >= request.Revision)
            || pending |> Option.exists (fun current -> current.Revision >= request.Revision)
        then
            RejectedStale, state
        else
            let decision = if pending.IsSome then Coalesced else Accepted

            decision,
            { state with
                Pending = state.Pending |> Map.add request.Slot request }

    let tryStart (slot: string) (state: State<'a>) : Request<'a> option * State<'a> =
        match state.Pending |> Map.tryFind slot with
        | None -> None, state
        | Some request ->
            Some request,
            { state with
                Pending = state.Pending |> Map.remove slot
                Active = state.Active |> Map.add slot request.Revision }

    let complete (request: Request<'a>) (state: State<'a>) : State<'a> =
        match state.Active |> Map.tryFind request.Slot with
        | Some revision when revision = request.Revision ->
            { state with
                Active = state.Active |> Map.remove request.Slot }
        | _ -> state

    let cancelPending (slot: string) (revision: uint64) (state: State<'a>) : bool * State<'a> =
        match state.Pending |> Map.tryFind slot with
        | Some request when request.Revision = revision ->
            true,
            { state with
                Pending = state.Pending |> Map.remove slot }
        | _ -> false, state
