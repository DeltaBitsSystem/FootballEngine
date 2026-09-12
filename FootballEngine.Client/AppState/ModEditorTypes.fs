namespace FootballEngine

open FootballEngine.Data

module ModEditorTypes =

    type ModEditorTab =
        | Overview
        | Countries
        | International
        | GlobalNames
        | Validation
        | Export

    type ModEditorSubTab =
        | General
        | Leagues
        | Clubs
        | Cups
        | NamePool

    type HistoryItem =
        { ModManifest: ModManifestDto
          Countries: Map<string, CountryDataDto>
          InternationalComps: InternationalCompDto list
          SelectedCountryCode: string option }

    type ExportStatus =
        | NotExported
        | Exported
        | ExportFailed of string

    type ModEditorState =
        { ActiveTab: ModEditorTab
          ActiveSubTab: ModEditorSubTab

          // Data being edited
          Manifest: ModManifestDto
          Countries: Map<string, CountryDataDto>
          InternationalComps: InternationalCompDto list

          // UI State
          SelectedCountryCode: string option
          SearchQuery: string

          // History for Undo/Redo
          Past: HistoryItem list
          Future: HistoryItem list

          // Validation
          Errors: ModDiagnostic list
          IsDirty: bool
          ExportStatus: ExportStatus }

    let initManifest: ModManifestDto =
        { Id = "community.my-new-mod"
          Name = "My New Mod"
          Author = "Anonymous"
          Version = "1.0.0"
          Priority = 100
          Dependencies = []
          Description = ""
          MergeStrategy = Some "Append" }

    let initModEditorState: ModEditorState =
        { ActiveTab = Overview
          ActiveSubTab = General
          Manifest = initManifest
          Countries = Map.empty
          InternationalComps = []
          SelectedCountryCode = None
          SearchQuery = ""
          Past = []
          Future = []
          Errors = []
          IsDirty = false
          ExportStatus = NotExported }
