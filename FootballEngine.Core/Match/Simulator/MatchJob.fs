namespace FootballEngine.Core.Match

open System

/// No mutable match world crosses the scheduler boundary.
type MatchJob =
    { MatchId: int
      Kickoff: DateTime
      SourceRevision: uint64
      InputHash: uint64
      Input: MatchInput }

module MatchJob =
    let private fnv (h: uint64) (v: uint64) : uint64 = (h ^^^ v) * 1099511628211UL

    let private hashText hash (text: string) =
        let bytes = System.Text.Encoding.UTF8.GetBytes text

        bytes
        |> Array.fold (fun h byteValue -> fnv h (uint64 byteValue)) (fnv hash (uint64 bytes.Length))

    let rec private hashValue hash (valueType: Type) (value: obj) =
        let hash = hashText hash valueType.FullName

        if isNull value then
            fnv hash 0UL
        elif valueType = typeof<string> then
            hashText (fnv hash 1UL) (unbox value)
        elif valueType = typeof<float> then
            fnv hash (uint64 (BitConverter.DoubleToInt64Bits(unbox value)))
        elif valueType = typeof<float32> then
            fnv hash (uint64 (uint32 (BitConverter.SingleToInt32Bits(unbox value))))
        elif valueType.IsPrimitive || valueType.IsEnum || valueType = typeof<decimal> then
            hashText hash (Convert.ToString(value, Globalization.CultureInfo.InvariantCulture))
        elif Microsoft.FSharp.Reflection.FSharpType.IsRecord valueType then
            Array.zip
                (Microsoft.FSharp.Reflection.FSharpType.GetRecordFields valueType)
                (Microsoft.FSharp.Reflection.FSharpValue.GetRecordFields value)
            |> Array.fold (fun h (field, item) -> hashValue h field.PropertyType item) hash
        elif Microsoft.FSharp.Reflection.FSharpType.IsUnion valueType then
            let case, fields =
                Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(value, valueType)

            Array.zip (case.GetFields()) fields
            |> Array.fold (fun h (field, item) -> hashValue h field.PropertyType item) (hashText hash case.Name)
        elif Microsoft.FSharp.Reflection.FSharpType.IsTuple valueType then
            Array.zip
                (Microsoft.FSharp.Reflection.FSharpType.GetTupleElements valueType)
                (Microsoft.FSharp.Reflection.FSharpValue.GetTupleFields value)
            |> Array.fold (fun h (itemType, item) -> hashValue h itemType item) hash
        elif valueType.IsArray then
            let values = value :?> System.Array

            values
            |> Seq.cast<obj>
            |> Seq.fold (fun h item -> hashValue h (valueType.GetElementType()) item) (fnv hash (uint64 values.Length))
        else
            // Match inputs use ordered F# maps; their KeyValuePair properties preserve key/value boundaries.
            match value with
            | :? Collections.IEnumerable as values ->
                values
                |> Seq.cast<obj>
                |> Seq.fold (fun h item -> hashValue h (item.GetType()) item) hash
            | _ ->
                valueType.GetProperties()
                |> Array.sortBy _.Name
                |> Array.fold
                    (fun h field -> hashValue (hashText h field.Name) field.PropertyType (field.GetValue value))
                    hash

    // Runs only at the boundary, never inside ticks; includes all resolved football inputs, not just club IDs.
    let inputHash (input: MatchInput) =
        hashValue 1469598103934665603UL typeof<MatchInput> input

    let private hashInput (input: MatchInput) (kickoff: DateTime) : uint64 =
        fnv (inputHash input) (uint64 kickoff.Ticks)

    let create (matchId: int) (kickoff: DateTime) (sourceRevision: uint64) (input: MatchInput) : MatchJob =
        let input = MatchInput.snapshot input

        { MatchId = matchId
          Kickoff = kickoff
          SourceRevision = sourceRevision
          InputHash = hashInput input kickoff
          Input = input }

    let resultHash (job: MatchJob) (outcome: MatchOutcome) : uint64 =
        let mutable hash = job.InputHash
        hash <- fnv hash (uint64 outcome.Final.State.HomeScore)
        hash <- fnv hash (uint64 outcome.Final.State.AwayScore)
        fnv hash outcome.EventsHash

    let resultHashFromFacts (job: MatchJob) (homeScore: int) (awayScore: int) (eventsHash: uint64) : uint64 =
        let mutable hash = job.InputHash
        hash <- fnv hash (uint64 homeScore)
        hash <- fnv hash (uint64 awayScore)
        fnv hash eventsHash
