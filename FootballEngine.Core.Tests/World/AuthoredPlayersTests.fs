namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation

module AuthoredPlayersTests =
    let private countryJson (northPlayers: string) =
        let playersField =
            if String.IsNullOrEmpty northPlayers then
                ""
            else
                $""","Players":[{northPlayers}]"""

        $"""{{"id":"test.world:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"test.world:country/tst.name","domain":{{"Country":{{"Code":"TST","Name":"Testland","Confederation":"UEFA"}},"Names":{{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]}},"LeagueNames":["Test League"],"LeagueRules":[{{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}}],"Clubs":[{{"Name":"North FC","LeagueLevel":0,"Reputation":7000{playersField}}},{{"Name":"South FC","LeagueLevel":0,"Reputation":6500}},{{"Name":"East FC","LeagueLevel":0,"Reputation":null}},{{"Name":"West FC","LeagueLevel":0,"Reputation":null}}],"Cups":[{{"Name":"Test Cup","Format":"SK:SingleLeg:ExtraTimeThenPenalties"}}]}}}}"""

    let private competitionJson =
        """{"id":"test.world:competition/test-cup","kind":"competition","schema":1,"nameKey":"test.world:competition/test-cup.name","domain":{"Name":"Test International","Confederation":"UEFA","Format":"GTK:2;2;1;3;1;GD,GS,H2H~TwoLegs:ExtraTimeThenPenalties","Qualification":[{"Kind":"LeaguePosition","Level":0,"FromPos":1,"ToPos":2,"CompetitionName":null,"Slots":null},{"Kind":"CupWinner","Level":null,"FromPos":null,"ToPos":null,"CompetitionName":"Test Cup","Slots":null}]}}"""

    let private resolve (northPlayers: string) : ResolvedDataSet =
        let package: ModPackage =
            { Manifest =
                { Id = ModId "test.world"
                  Version = "1.0.0"
                  Dependencies = []
                  Priority = 0
                  Before = []
                  After = [] }
              PackageSha256 = "sha-test-world"
              SportsContent =
                Map.ofList
                    [ ContentId "test.world:country/tst", countryJson northPlayers
                      ContentId "test.world:competition/test-cup", competitionJson ]
              Translations = Map.empty
              Assets = Map.empty
              Patches = [] }

        match
            ModResolution.resolve
                [ { RelativePath = "test-world.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> failtestf "test dataset did not resolve: %A" error

    let private startDate = DateTime(2034, 3, 17, 9, 30, 0, DateTimeKind.Utc)

    let private generate northPlayers =
        match WorldGen.generateNewGame (resolve northPlayers) 0xC02UL startDate "TST" "Test Manager" [] with
        | Ok value -> value
        | Error error -> failtestf "generation failed: %A" error

    let private authoredJson =
        """{"Name":"Ada Keeper","Position":"GK","Age":30,"Rating":80,"Potential":80,"PreferredFoot":null,"Nationality":null},{"Name":"Rio Striker","Position":"ST","Age":22,"Rating":70,"Potential":90,"PreferredFoot":"Left","Nationality":"ARG"}"""

    let private squadOf (state: GameState) (clubName: string) =
        let club =
            state.Clubs
            |> Map.toList
            |> List.map snd
            |> List.find (fun c -> c.Name = clubName)

        club, club.PlayerIds |> List.map (fun id -> state.Players[id])

    let private minimumPerPosition =
        [ GK, 3
          DC, 5
          DL, 3
          DR, 3
          WBL, 2
          WBR, 2
          DM, 3
          MC, 5
          ML, 2
          MR, 2
          AML, 3
          AMR, 3
          AMC, 3
          ST, 5 ]

    let private invalidDiagnostics (players: string) =
        let package: ModPackage =
            { Manifest =
                { Id = ModId "test.world"
                  Version = "1.0.0"
                  Dependencies = []
                  Priority = 0
                  Before = []
                  After = [] }
              PackageSha256 = "sha-test-world"
              SportsContent =
                Map.ofList
                    [ ContentId "test.world:country/tst", countryJson players
                      ContentId "test.world:competition/test-cup", competitionJson ]
              Translations = Map.empty
              Assets = Map.empty
              Patches = [] }

        match
            ModResolution.resolve
                [ { RelativePath = "test-world.femod"
                    Package = package } ]
        with
        | Error error -> error.Diagnostics
        | Ok resolved ->
            match ResolvedWorldData.materialize resolved with
            | Error error -> error.Diagnostics
            | Ok _ -> []

    let private expectDiagnostic code path (players: string) =
        let diagnostics = invalidDiagnostics players

        Expect.isTrue
            (diagnostics |> List.exists (fun d -> d.Code = code && d.JsonPath = Some path))
            $"expected {code} at {path}, got %A{diagnostics |> List.map (fun d -> d.Code, d.JsonPath)}"

    let private player extra =
        $"""{{"Name":"P","Position":"GK","Age":25,"Rating":50,"Potential":60{extra}}}"""

    /// Golden recorded from the generator before authored players existed; any RNG-order change for Players = [] breaks it.
    /// Birthdays are hashed as invariant ISO text because %A follows the machine culture and CI runs under C.UTF-8.
    let private noPlayersGoldenHash =
        "E01B11542E19173E1D9685341182188537D332D209B65DB805619975FF40EA62"

    let authoredPlayersTests =
        testList
            "Authored players"
            [ testCase "authored players appear at their club with exact name, position, age, skills and nationality"
              <| fun _ ->
                  let state = generate authoredJson
                  let club, squad = squadOf state "North FC"
                  let keeper = squad |> List.find (fun p -> p.Name = "Ada Keeper")
                  let striker = squad |> List.find (fun p -> p.Name = "Rio Striker")

                  Expect.equal keeper.Position GK "keeper position"
                  Expect.equal (keeper.Birthday.Year) (startDate.Year - 30) "keeper age by birthday year"
                  Expect.equal keeper.CurrentSkill 160 "rating 80 doubles to engine scale"
                  Expect.equal keeper.PotentialSkill 160 "potential 80 doubles"
                  Expect.equal keeper.Nationality "TST" "defaults to the country code"
                  Expect.equal striker.Position ST "striker position"
                  Expect.equal striker.Birthday.Year (startDate.Year - 22) "striker age"
                  Expect.equal striker.CurrentSkill 140 "rating 70 doubles"
                  Expect.equal striker.PotentialSkill 180 "potential 90 doubles"
                  Expect.equal striker.PreferredFoot Left "explicit foot kept"
                  Expect.equal striker.Nationality "ARG" "explicit nationality kept"
                  Expect.equal club.PlayerIds.Head keeper.Id "authored players come first in file order"

              testCase "the squad still covers the minimum per position and generates one fewer authored position"
              <| fun _ ->
                  let _, authoredSquad = squadOf (generate authoredJson) "North FC"

                  for position, minimum in minimumPerPosition do
                      let count =
                          authoredSquad |> List.filter (fun p -> p.Position = position) |> List.length

                      Expect.isGreaterThanOrEqual count minimum $"{position} below minimum"

                  let _, plainSquad = squadOf (generate "") "North FC"

                  let generatedGoalkeepers (squad: Player list) =
                      squad
                      |> List.filter (fun p -> p.Position = GK && not (p.Name.StartsWith "Ada"))
                      |> List.length

                  Expect.equal
                      (generatedGoalkeepers authoredSquad)
                      (generatedGoalkeepers plainSquad - 1)
                      "one generated goalkeeper slot is replaced by the authored one"

              testCase "unknown position is reported at the position path"
              <| fun _ ->
                  expectDiagnostic
                      "MOD-CONTENT-FIELD"
                      "/domain/Clubs/0/Players/0/Position"
                      """{"Name":"P","Position":"XX","Age":25,"Rating":50,"Potential":60}"""

              testCase "age outside 15..45 is reported"
              <| fun _ ->
                  expectDiagnostic
                      "MOD-CONTENT-FIELD"
                      "/domain/Clubs/0/Players/0/Age"
                      """{"Name":"P","Position":"GK","Age":12,"Rating":50,"Potential":60}"""

              testCase "rating of zero is reported"
              <| fun _ ->
                  expectDiagnostic
                      "MOD-CONTENT-FIELD"
                      "/domain/Clubs/0/Players/0/Rating"
                      """{"Name":"P","Position":"GK","Age":25,"Rating":0,"Potential":60}"""

              testCase "potential below rating is reported"
              <| fun _ ->
                  expectDiagnostic
                      "MOD-CONTENT-FIELD"
                      "/domain/Clubs/0/Players/0/Potential"
                      """{"Name":"P","Position":"GK","Age":25,"Rating":50,"Potential":40}"""

              testCase "empty name and unknown foot are reported"
              <| fun _ ->
                  expectDiagnostic
                      "MOD-CONTENT-FIELD"
                      "/domain/Clubs/0/Players/0/PreferredFoot"
                      (player ",\"PreferredFoot\":\"Middle\"")

                  let diagnostics =
                      invalidDiagnostics """{"Name":"","Position":"GK","Age":25,"Rating":50,"Potential":60}"""

                  Expect.isNonEmpty diagnostics "empty name must be rejected"

              testCase "unknown key is rejected with its path and does not crash"
              <| fun _ ->
                  let diagnostics = invalidDiagnostics (player ",\"Foo\":1")

                  Expect.isTrue
                      (diagnostics
                       |> List.exists (fun d -> d.JsonPath = Some "/domain/Clubs/0/Players/0/Foo"))
                      $"unknown property path missing: %A{diagnostics |> List.map (fun d -> d.Code, d.JsonPath)}"

              testCase "clubs without Players generate exactly the pre-feature world"
              <| fun _ ->
                  let state = generate ""

                  let text =
                      state.Players
                      |> Map.toList
                      |> List.map (fun (id, p) ->
                          sprintf
                              "%A|%s|%s|%d|%d|%A|%A|%A"
                              id
                              p.Name
                              (p.Birthday.ToString("O", Globalization.CultureInfo.InvariantCulture))
                              p.CurrentSkill
                              p.PotentialSkill
                              p.PreferredFoot
                              p.Position
                              p.Technical)
                      |> String.concat "\n"

                  let hash =
                      Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes text)
                      |> Convert.ToHexString

                  Expect.equal hash noPlayersGoldenHash "world generation changed for clubs without authored players" ]
