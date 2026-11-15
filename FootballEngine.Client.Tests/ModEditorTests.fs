module FootballEngine.Client.Tests.ModEditorTests

open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Data
open FootballEngine.ModEditorTypes
open FootballEngine.Settings

let private apply message state =
    UpdateModEditor.update message state |> fst

let private country code name =
    UpdateModEditor.createEmptyCountry code name

let private stateWithCountries countries selected =
    // HasDocument stays false so the update never writes the author's draft to disk.
    { initModEditorState with
        Document =
            { initModEditorState.Document with
                Countries = countries |> Map.ofList }
        SelectedCountryCode = selected }

let tests =
    testList
        "FootballEngine.Client.ModEditor"
        [ test "renaming a country rekeys the map and preserves selection" {
              let initial = stateWithCountries [ "ARG", country "ARG" "Argentina" ] (Some "ARG")

              let updated =
                  apply
                      (UpdateCountry(
                          "ARG",
                          fun data ->
                              { data with
                                  Country = { data.Country with Code = "AR1" } }
                      ))
                      initial

              Expect.isNone (updated.Document.Countries |> Map.tryFind "ARG") "old code is removed"
              Expect.isSome (updated.Document.Countries |> Map.tryFind "AR1") "new code is present"
              Expect.equal updated.SelectedCountryCode (Some "AR1") "selection follows the rename"
          }

          test "renaming to an existing country code is rejected without data loss" {
              let argentina = country "ARG" "Argentina"
              let brazil = country "BRA" "Brazil"
              let initial = stateWithCountries [ "ARG", argentina; "BRA", brazil ] (Some "ARG")

              let updated =
                  apply
                      (UpdateCountry(
                          "ARG",
                          fun data ->
                              { data with
                                  Country = { data.Country with Code = "BRA" } }
                      ))
                      initial

              Expect.equal
                  updated.Document.Countries
                  initial.Document.Countries
                  "duplicate code leaves the catalog intact"

              Expect.equal updated.SelectedCountryCode (Some "ARG") "duplicate code leaves selection intact"
          }

          test "undo restores the selected country after a rename" {
              let initial = stateWithCountries [ "ARG", country "ARG" "Argentina" ] (Some "ARG")

              let renamed =
                  apply
                      (UpdateCountry(
                          "ARG",
                          fun data ->
                              { data with
                                  Country = { data.Country with Code = "AR1" } }
                      ))
                      initial

              let restored = apply UndoMod renamed

              Expect.isSome (restored.Document.Countries |> Map.tryFind "ARG") "undo restores the original key"
              Expect.equal restored.SelectedCountryCode (Some "ARG") "undo restores the original selection"
          }

          test "editing after publishing marks the package as not published" {
              let exported =
                  { initModEditorState with
                      PublishStatus = Published("mods/x.femod", false)
                      IsDirty = false }

              let edited =
                  apply (UpdateManifest("name", fun manifest -> { manifest with Name = "Edited mod" })) exported

              Expect.equal edited.PublishStatus NotPublished "edited data needs a new publish"
              Expect.isTrue edited.IsDirty "edited data is dirty"
          }

          test "a pasted squad keeps valid rows and counts header and malformed rows as skipped" {
              let text =
                  "Name;Pos;Age;Rating\nJuan Uno\tGK\t31\t78\t80\tderecho\tURU\nPedro Dos; ST; 20; 66\nSin Puesto; XX; 22; 70\nTres, mc, 19, 120, 50"

              let players, skipped = UpdateModEditor.parseSquad text

              Expect.equal (players |> List.map _.Name) [ "Juan Uno"; "Pedro Dos"; "Tres" ] "valid rows in order"
              Expect.equal skipped 2 "header and unknown position are skipped"
              let keeper = players.[0]
              Expect.equal (keeper.Position, keeper.Age, keeper.Rating, keeper.Potential) ("GK", 31, 78, 80) "tab row"
              Expect.equal keeper.PreferredFoot (Some "Right") "Spanish foot name is understood"
              Expect.equal keeper.Nationality (Some "URU") "nationality code"
              Expect.equal players.[1].Potential 66 "missing potential defaults to the rating"

              Expect.equal
                  (players.[2].Position, players.[2].Rating, players.[2].Potential)
                  ("MC", 100, 100)
                  "rating clamps and potential never falls below it"
          }

          test "importing a pasted squad appends to the selected club in one undo step" {
              let withClub =
                  { country "ARG" "Argentina" with
                      Clubs = [ UpdateModEditor.createEmptyClub "River" 0 ] }

              let initial =
                  { stateWithCountries [ "ARG", withClub ] (Some "ARG") with
                      SquadPaste = "Uno; GK; 30; 70\nDos; ST; 21; 65" }

              let imported = apply (ImportSquad("ARG", 0)) initial

              Expect.equal (imported.Document.Countries["ARG"].Clubs.Head.Players.Length) 2 "both players added"
              Expect.equal imported.Past.Length 1 "one undo step"
              Expect.equal imported.SquadPaste "" "paste box clears after import"
              let undone = apply UndoMod imported
              Expect.isEmpty undone.Document.Countries["ARG"].Clubs.Head.Players "undo removes the import"
          }

          test "settings saved before version 2 move the old full-match default to key moments once" {
              let v1 =
                  { GlobalSettings.defaults with
                      Version = 1
                      MatchView =
                          { GlobalSettings.defaults.MatchView with
                              HighlightFilter = FootballEngine.Settings.All } }
                  |> GlobalSettings.toJson

              match GlobalSettings.fromJson v1 with
              | Error error -> failtestf "v1 settings must load: %s" error
              | Ok migrated ->
                  Expect.equal
                      migrated.MatchView.HighlightFilter
                      FootballEngine.Settings.KeyMoments
                      "old default migrates"

                  Expect.equal migrated.Version GlobalSettings.currentVersion "migrated settings carry the new version"

                  // A full match chosen after the migration is the player's choice and must survive a reload.
                  let chosen =
                      { migrated with
                          MatchView =
                              { migrated.MatchView with
                                  HighlightFilter = FootballEngine.Settings.All } }

                  match GlobalSettings.fromJson (GlobalSettings.toJson chosen) with
                  | Ok reloaded ->
                      Expect.equal reloaded.MatchView.HighlightFilter FootballEngine.Settings.All "explicit choice kept"
                  | Error error -> failtestf "v2 settings must load: %s" error
          }

          test "an overridden country keeps the code of the entity it replaces" {
              let initial =
                  let state = stateWithCountries [ "ARG", country "ARG" "Argentina" ] (Some "ARG")

                  { state with
                      Document =
                          { state.Document with
                              Sources =
                                  Map.ofList
                                      [ "ARG",
                                        { ModId = "builtin.argentina"
                                          ContentId = "builtin.argentina:country/arg" } ] } }

              let updated =
                  apply
                      (UpdateCountry(
                          "ARG",
                          fun data ->
                              { data with
                                  Country = { data.Country with Code = "AR1" } }
                      ))
                      initial

              Expect.equal
                  updated.Document.Countries
                  initial.Document.Countries
                  "the patch target cannot be renamed away"
          } ]
