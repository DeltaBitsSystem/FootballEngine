namespace FootballEngine.Data

open System.Text.Json.Serialization

// ─── Schema version ───
/// Current JSON schema version. Increment when breaking changes are made.
module SchemaVersion =
    let Current = 1

// ─── Envelope ───
/// Unified JSON envelope for all data files.
[<CLIMutable>]
type DataEnvelope =
    { SchemaVersion: int
      DataType: string
      [<JsonIgnore>]
      RawJson: string option }

// ─── Merge strategy ───
type MergeStrategy =
    /// Mod data fully replaces builtin
    | Replace
    /// Mod data is added to builtin (clubs, name pools)
    | Append
    /// Mod modifies specific fields
    | Patch

// ─── Country DTOs ───

[<CLIMutable>]
type CountryDto =
    { Code: string
      Name: string
      Confederation: string }

[<CLIMutable>]
type NamePoolDto =
    { FirstNames: string list
      LastNames: string list }

[<CLIMutable>]
type ClubEntryDto =
    { Name: string
      LeagueLevel: int
      Reputation: int option }

/// Cup format with embedded name (unlike domain CupFormat which has no name).
[<CLIMutable>]
type CupEntryDto = { Name: string; Format: string }

[<CLIMutable>]
type LeagueRulesDto =
    { PointsForWin: int
      PointsForDraw: int
      Tiebreakers: string list
      Promotion: string list
      Relegation: string list }

/// Full country data DTO — mirrors CountryData domain type.
[<CLIMutable>]
type CountryDataDto =
    { Country: CountryDto
      Names: NamePoolDto
      LeagueNames: string list
      LeagueRules: LeagueRulesDto list
      Clubs: ClubEntryDto list
      Cups: CupEntryDto list }

// ─── International Competition DTOs ───

[<CLIMutable>]
type QualificationSlotDto =
    { Kind: string
      Level: int option
      FromPos: int option
      ToPos: int option
      CompetitionName: string option
      Slots: int option }

[<CLIMutable>]
type InternationalCompDto =
    { Name: string
      Confederation: string option
      Format: string
      Qualification: QualificationSlotDto list }

// ─── Mod Manifest DTO ───

[<CLIMutable>]
type ModManifestDto =
    { Name: string
      Author: string
      Version: string
      Priority: int
      Dependencies: string list
      Description: string
      MergeStrategy: string option }
