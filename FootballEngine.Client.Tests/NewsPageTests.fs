module FootballEngine.Client.Tests.NewsPageTests

open System
open Expecto
open FSharp.Reflection
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Pages
open FootballEngine.Routing

let private emptyState: GameState =
    { CurrentDate = DateTime(2026, 8, 1)
      Season = 2026
      TrainingWeeksApplied = 0
      Clubs = Map.empty
      Players = Map.empty
      Staff = Map.empty
      Competitions = Map.empty
      Countries = Map.empty
      DataLock = Unchecked.defaultof<_>
      UserClubId = 1
      UserStaffId = 0
      PrimaryCountry = "TST"
      Inbox = []
      NextInboxId = 1
      PendingNegotiations = Map.empty
      NextNegotiationId = 1
      ProfileCache = Map.empty
      PlayerExperiences = Map.empty
      TeamEmphasis = EmPossession
      SeasonHistory = []
      CareerHistory = []
      ExecutionSeed = 1UL
      MatchSeeds = Map.empty
      News = []
      NextNewsId = 1
      PulseProcessed = Set.empty
      PulseQueues = PulseQueues.empty
      JobOffers = []
      NextJobOfferId = 1
      UserStudy = []
      Market = MarketState.empty }

/// One sample per NewsKind case; the reflection count below fails the suite when a new case lacks a sample.
let private sampleKinds: NewsKind list =
    [ NewsKind.BigWin(1, 2, 5, 0)
      NewsKind.Upset(1, 2, 2, 1)
      NewsKind.Streak(1, StreakKind.Wins, 5)
      NewsKind.Streak(1, StreakKind.Unbeaten, 8)
      NewsKind.Streak(1, StreakKind.Winless, 6)
      NewsKind.Streak(1, StreakKind.Losses, 4)
      NewsKind.NewLeader(1, 1)
      NewsKind.Clasico(1, 2)
      NewsKind.HatTrick(1, 1, 3)
      NewsKind.ScoringRun(1, 4)
      NewsKind.BoardUnrest(1, 27)
      NewsKind.ManagerSacked(1, 1, 12)
      NewsKind.ManagerAppointed(1, 1)
      NewsKind.FansMood(1, FansMoodBand.Furious)
      NewsKind.FansMood(1, FansMoodBand.Euphoric)
      NewsKind.TransferRequest(1, 1, UnrestReason.LackOfMinutes)
      NewsKind.TransferRequest(1, 1, UnrestReason.ContractStalled)
      NewsKind.TransferRequest(1, 1, UnrestReason.BiggerClubInterest)
      NewsKind.ContractRunningOut(1, 1, 5)
      NewsKind.Milestone(1, MilestoneKind.Debut, 1)
      NewsKind.Milestone(1, MilestoneKind.Appearances, 100)
      NewsKind.Milestone(1, MilestoneKind.Goals, 50)
      NewsKind.Breakout(1, 1)
      NewsKind.Retirement(1, 37)
      NewsKind.Rumour(1, 1, 2)
      NewsKind.TransferDone(1, Some 1, 2, 12_500_000m)
      NewsKind.TransferDone(1, None, 2, 0m)
      NewsKind.Award(AwardKind.TopScorer, Some 1, None, 1)
      NewsKind.Award(AwardKind.ManagerOfSeason, None, Some 1, 1)
      NewsKind.Promotion(1, 1)
      NewsKind.Relegation(1, 1)
      NewsKind.Champion(1, 1) ]

let private item id kind scope importance : NewsItem =
    { Id = id
      Date = DateTime(2026, 8, 1)
      Kind = kind
      Importance = importance
      Scope = scope
      Clubs = [ 1 ]
      Players = [] }

let private withLocale locale f =
    let before = Localization.active ()
    Localization.setActive locale

    try
        f ()
    finally
        Localization.setActive before

let private caseName (kind: NewsKind) =
    (fst (FSharpValue.GetUnionFields(kind, typeof<NewsKind>))).Name

[<Tests>]
let tests =
    // Localization.active is process-global; tests that switch locale must not interleave with each other.
    testSequenced
    <| testList
        "News page"
        [ testCase "samples cover every NewsKind case"
          <| fun _ ->
              let covered = sampleKinds |> List.map caseName |> Set.ofList

              let all =
                  FSharpType.GetUnionCases typeof<NewsKind> |> Array.map _.Name |> Set.ofArray

              Expect.equal covered all "every NewsKind case has a text sample"

          testCase "NewsText renders a headline and body for every kind in both languages, every variant"
          <| fun _ ->
              for locale in [ Locale.Es; Locale.En ] do
                  withLocale locale (fun () ->
                      for kind in sampleKinds do
                          let count = NewsText.variantCount emptyState kind
                          Expect.isGreaterThanOrEqual count 2 $"{caseName kind} has at least two phrasings"

                          for id in 0 .. count - 1 do
                              let headline, body =
                                  NewsText.render emptyState (item id kind NewsScope.World NewsImportance.Minor)

                              Expect.isNotEmpty headline $"{caseName kind}/{locale}/{id} headline"
                              Expect.isNotEmpty body $"{caseName kind}/{locale}/{id} body"

                              Expect.isFalse
                                  (headline.Contains "{")
                                  $"{caseName kind}/{locale} headline has no placeholder"

                              Expect.isFalse (body.Contains "{") $"{caseName kind}/{locale} body has no placeholder")

          testCase "Spanish and English headlines differ"
          <| fun _ ->
              let kind = NewsKind.BigWin(1, 2, 5, 0)
              let it = item 1 kind NewsScope.World NewsImportance.Minor
              let es = withLocale Locale.Es (fun () -> NewsText.render emptyState it)
              let en = withLocale Locale.En (fun () -> NewsText.render emptyState it)
              Expect.notEqual es en "locale selects the phrasing"

          testCase "variant choice is deterministic by Id and cycles through the phrasings"
          <| fun _ ->
              let kind = NewsKind.BigWin(1, 2, 3, 0)
              let n = NewsText.variantCount emptyState kind

              let render id =
                  withLocale Locale.Es (fun () ->
                      NewsText.render emptyState (item id kind NewsScope.World NewsImportance.Minor))

              Expect.equal (render 7) (render 7) "same id, same text"
              Expect.equal (render 7) (render (7 + n)) "ids congruent modulo the variant count share a phrasing"
              Expect.notEqual (render 0) (render 1) "adjacent ids use different phrasings"
              Expect.equal (NewsText.variantIndex 7 n) (7 % n) "index is Id modulo count"

          testCase "transfer headline carries the formatted fee"
          <| fun _ ->
              let it =
                  item 0 (NewsKind.TransferDone(1, Some 1, 2, 12_500_000m)) NewsScope.World NewsImportance.Major

              let headline, body = withLocale Locale.En (fun () -> NewsText.render emptyState it)
              Expect.isTrue ((headline + body).Contains(Formatters.money 12_500_000m)) "fee shown"

          testCase "news filters select by scope"
          <| fun _ ->
              let items =
                  [ item 1 (NewsKind.Clasico(1, 2)) NewsScope.UserClub NewsImportance.Minor
                    item 2 (NewsKind.Clasico(1, 2)) NewsScope.UserLeague NewsImportance.Minor
                    item 3 (NewsKind.Clasico(1, 2)) NewsScope.World NewsImportance.Minor
                    item 4 (NewsKind.Clasico(1, 2)) NewsScope.UserClub NewsImportance.Minor ]

              let ids filter =
                  News.visible filter items |> List.map _.Id

              Expect.equal (ids NewsMyClub) [ 1; 4 ] "my club"
              Expect.equal (ids NewsMyLeague) [ 2 ] "my league"
              Expect.equal (ids NewsWorld) [ 3 ] "world"
              Expect.equal (ids NewsEverything) [ 1; 2; 3; 4 ] "everything"

          testCase "home headlines are the latest day's most important items"
          <| fun _ ->
              let day d id imp =
                  { item id (NewsKind.Clasico(1, 2)) NewsScope.World imp with
                      Date = DateTime(2026, 8, d) }

              let items =
                  [ day 2 5 NewsImportance.Minor
                    day 2 4 NewsImportance.Headline
                    day 2 3 NewsImportance.Major
                    day 2 2 NewsImportance.Notable
                    day 1 1 NewsImportance.Headline ]

              Expect.equal (News.topItems 3 items |> List.map _.Id) [ 4; 3; 2 ] "latest day, ranked by importance"
              Expect.isEmpty (News.topItems 3 []) "no news, no headlines"

          testCase "board gauges use the world pulse thresholds"
          <| fun _ ->
              let p = WorldPulseConfig.defaultWorldPulse

              Expect.equal
                  (fst (Board.confidenceZone (p.UltimatumThreshold - 1)))
                  TranslationKey.BoardZoneUltimatum
                  "ultimatum"

              Expect.equal
                  (fst (Board.confidenceZone p.UltimatumThreshold))
                  TranslationKey.BoardZoneWarning
                  "warning starts at ultimatum"

              Expect.equal (fst (Board.confidenceZone p.BoardWarningThreshold)) TranslationKey.BoardZoneSafe "safe"
              Expect.equal (fst (Board.fansBand (p.FansFuriousBelow - 1))) TranslationKey.FansBandFurious "furious"
              Expect.equal (fst (Board.fansBand p.FansFuriousBelow)) TranslationKey.FansBandRestless "restless"
              Expect.equal (fst (Board.fansBand p.FansRestlessBelow)) TranslationKey.FansBandContent "content"
              Expect.equal (fst (Board.fansBand p.FansEuphoricFrom)) TranslationKey.FansBandEuphoric "euphoric"

          testCase "News stays open without a club; the Board belongs to the club you manage"
          <| fun _ ->
              for route in [ Route.News; Route.Board ] do
                  Expect.equal (Route.fromPath (Route.toPath route)) (Some route) "round-trips"

              Expect.isTrue (Route.canNavigate false Route.News) "news while unemployed"
              Expect.isFalse (Route.canNavigate false Route.Board) "no board without a club"
              Expect.isTrue (Route.canNavigate true Route.Board) "board when employed" ]
