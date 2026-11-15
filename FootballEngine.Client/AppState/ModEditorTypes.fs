namespace FootballEngine

open FootballEngine.Data

module ModEditorTypes =

    type ModEditorTab =
        | Start
        | Details
        | World
        | International
        | Publish

    type ModEditorSubTab =
        | General
        | Leagues
        | Clubs
        | Cups
        | NamePool

    /// A country copied from an installed package keeps the identity it overrides, so publishing
    /// replaces that entity through a patch instead of adding a second country with the same code.
    [<CLIMutable>]
    type CountrySource = { ModId: string; ContentId: string }

    /// Everything the author edits; the unit of undo, drafts and publishing.
    [<CLIMutable>]
    type EditorDocument =
        { Manifest: ModManifestDto
          Countries: Map<string, CountryDataDto>
          InternationalComps: InternationalCompDto list
          Sources: Map<string, CountrySource>
          CompetitionSources: Map<string, CountrySource> }

    type LibraryEntry =
        {
            Path: string
            ModId: string
            Title: string
            Version: string
            Author: string
            IsBuiltin: bool
            Countries: int
            Clubs: int
            Players: int
            Competitions: int
            /// Code and name of each country, so a single country can be imported without reopening the package.
            CountryNames: (string * string) list
        }

    type PublishStatus =
        | NotPublished
        | Published of path: string * enabled: bool
        | PublishFailed of string

    type HistoryItem =
        { Document: EditorDocument
          SelectedCountryCode: string option }

    type ModEditorState =
        {
            ActiveTab: ModEditorTab
            ActiveSubTab: ModEditorSubTab
            Document: EditorDocument
            HasDocument: bool
            SelectedCountryCode: string option
            SelectedClubIndex: int option
            SearchQuery: string
            PlayerQuery: string
            /// Text pasted from a spreadsheet, one player per line, waiting to be added to the selected club.
            SquadPaste: string
            SquadPasteReport: string option
            Library: LibraryEntry list
            LibraryError: string option
            HasDraft: bool
            Past: HistoryItem list
            Future: HistoryItem list
            /// Consecutive edits of the same field merge into one undo step; typing a name is one change, not one per key.
            LastEditKey: string option
            Errors: ModDiagnostic list
            Validated: bool
            IsDirty: bool
            PublishStatus: PublishStatus
        }

    let initManifest: ModManifestDto =
        { Id = "community.my-world"
          Name = "My World"
          Author = ""
          Version = "1.0.0"
          Priority = 100
          Dependencies = []
          Description = ""
          MergeStrategy = None }

    let emptyDocument: EditorDocument =
        { Manifest = initManifest
          Countries = Map.empty
          InternationalComps = []
          Sources = Map.empty
          CompetitionSources = Map.empty }

    let initModEditorState: ModEditorState =
        { ActiveTab = Start
          ActiveSubTab = General
          Document = emptyDocument
          HasDocument = false
          SelectedCountryCode = None
          SelectedClubIndex = None
          SearchQuery = ""
          PlayerQuery = ""
          SquadPaste = ""
          SquadPasteReport = None
          Library = []
          LibraryError = None
          HasDraft = false
          Past = []
          Future = []
          LastEditKey = None
          Errors = []
          Validated = false
          IsDirty = false
          PublishStatus = NotPublished }
