namespace FootballEngine.Data

open FootballEngine.Domain

/// Mod system types: errors, manifest, loaded data.
module ModTypes =

    type ModId = string
    type ModVersion = string
    type ModPriority = int

    type ModError =
        | InvalidJson of path: string * message: string
        | SchemaMismatch of path: string * expected: int * actual: int
        | DuplicateCountryCode of code: string * mod1: ModId * mod2: ModId
        | DuplicateCompName of name: string * mod1: ModId * mod2: ModId
        | MissingDependency of modId: ModId * dependency: ModId
        | PathTraversal of path: string
        | FileTooLarge of path: string * size: int64 * max: int64
        | InvalidManifest of path: string * message: string
        | InvalidCountryData of modId: ModId * countryCode: string * message: string
        | InvalidInternationalComp of modId: ModId * message: string

    type ModStatus =
        | Active
        | Missing
        | Disabled

    type ActiveModInfo =
        { ModId: ModId
          Name: string
          Version: ModVersion
          Status: ModStatus }

    type ModManifest =
        { Id: ModId
          Name: string
          Author: string
          Version: ModVersion
          Priority: ModPriority
          Dependencies: ModId list
          Description: string
          MergeStrategy: MergeStrategy }

    type LoadedData =
        { Countries: Map<CountryCode, CountryData>
          InternationalComps: (string * CompetitionType) list
          ActiveMods: ModManifest list
          Errors: ModError list }
