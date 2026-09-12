module FootballEngine.Client.Tests.ModEditorTests

open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Data
open FootballEngine.ModEditorTypes

let private apply message state =
    UpdateModEditor.update message state |> fst

let private country code name =
    UpdateModEditor.createEmptyCountry code name

let private stateWithCountries countries selected =
    { initModEditorState with
        Countries = countries |> Map.ofList
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

              Expect.isNone (updated.Countries |> Map.tryFind "ARG") "old code is removed"
              Expect.isSome (updated.Countries |> Map.tryFind "AR1") "new code is present"
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

              Expect.equal updated.Countries initial.Countries "duplicate code leaves the catalog intact"
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

              Expect.isSome (restored.Countries |> Map.tryFind "ARG") "undo restores the original key"
              Expect.equal restored.SelectedCountryCode (Some "ARG") "undo restores the original selection"
          }

          test "editing after an export marks the package as not exported" {
              let exported =
                  { initModEditorState with
                      ExportStatus = Exported
                      IsDirty = false }

              let edited =
                  apply (UpdateManifest(fun manifest -> { manifest with Name = "Edited mod" })) exported

              Expect.equal edited.ExportStatus NotExported "edited data needs a new export"
              Expect.isTrue edited.IsDirty "edited data is dirty"
          } ]
