namespace FootballEngine

open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.World.Agents

/// What the home page shows about the rival's head coach before a match. Pure: the text is resolved for a locale
/// passed in, so the same projection is testable in both languages without the UI.
module RivalCoachPresenter =

    type RecordVM =
        | FirstMeeting
        | Meetings of played: int * won: int * drawn: int * lost: int

    type RivalCoachVM =
        {
            CoachName: string
            Archetype: string
            Behaviour: string
            /// 1..5 stars from the coach's reading of the game.
            Stars: int
            RecordVs: RecordVM
            /// "4-3-3", "4-2-3-1 Wide": the shape he will field.
            Formation: string
            /// Why he picked that plan, in one sentence.
            PlanReason: string
        }

    /// "F4231" reads as "4-2-3-1": the shape a manager says out loud. Same rule as the Tactics page.
    let formationLabel (formation: Formation) =
        let raw = (sprintf "%A" formation).TrimStart('F')

        let digits =
            raw |> Seq.takeWhile System.Char.IsDigit |> Seq.map string |> String.concat "-"

        let suffix =
            raw |> Seq.skipWhile System.Char.IsDigit |> Seq.toArray |> System.String

        if suffix = "" then digits else $"{digits} {suffix}"

    let archetypeText (es: bool) (archetype: ManagerArchetype) : string * string =
        match archetype with
        | Copycat ->
            if es then
                "Copión", "Si le ganás con algo, te lo copia."
            else
                "Copycat", "Beat him with something and he copies it."
        | Hothead ->
            if es then
                "Temperamental", "Reacciona en caliente: cambios rápidos y dobles al entretiempo."
            else
                "Hothead", "Reacts in the heat of the moment: quick changes and double switches at half-time."
        | Strategist ->
            if es then
                "Estratega", "Lee el partido y te estudia: tu receta le dura poco."
            else
                "Strategist", "Reads the game and studies you: your recipe does not last long."
        | Romantic ->
            if es then
                "Romántico", "Juega a lo suyo pase lo que pase."
            else
                "Romantic", "Plays his own way no matter what."
        | Gambler ->
            if es then
                "Jugador", "Arriesga: sale a buscarlo temprano."
            else
                "Gambler", "Takes risks: goes looking for the game early."
        | Conservative ->
            if es then
                "Conservador", "Cuida el resultado y se cierra pronto."
            else
                "Conservative", "Protects the result and shuts up shop early."
        | Pragmatist ->
            if es then
                "Pragmático", "Elige lo que le sirve en cada partido."
            else
                "Pragmatist", "Picks whatever works for each match."

    /// 1..5 stars; a coach at CA 0 still shows one so the row never reads as missing data.
    let stars (coach: Staff) : int =
        1 + int (System.Math.Round(ManagerMind.reading coach * 4.0))

    let private philosophyText (es: bool) (philosophy: CoachPhilosophy) =
        match philosophy with
        | CoachPhilosophy.Possession -> if es then "posesión" else "possession"
        | CoachPhilosophy.Pressing -> if es then "presión alta" else "high press"
        | CoachPhilosophy.Counter -> if es then "contragolpe" else "counter-attack"
        | CoachPhilosophy.Direct -> if es then "juego directo" else "direct play"
        | CoachPhilosophy.Fortress -> if es then "bloque bajo" else "low block"

    let planReason (es: bool) (cfg: RivalManagerConfig) (coach: Staff) (user: Staff option) (note: GamePlanNote) =
        match note.Approach with
        | OwnIdentity ->
            let style = philosophyText es coach.Mind.Philosophy

            if es then
                $"Juega a lo suyo: {style}"
            else
                $"Plays his own game: {style}"
        | CounterRecipe formation ->
            let shape = formationLabel formation

            if es then
                $"Te estudió: va a neutralizar tu {shape}"
            else
                $"He studied you: he will neutralise your {shape}"
        | CopyRecipe formation ->
            let shape = formationLabel formation

            if es then
                $"Te copia el {shape} con el que le ganaste"
            else
                $"He copies the {shape} you beat him with"
        | Revenge ->
            let margin =
                user
                |> Option.bind (fun u -> Map.tryFind u.Id coach.Mind.Rivals)
                |> Option.map (fun r -> abs r.LastMargin)

            match margin with
            | Some goals when es -> $"Busca revancha: la última vez perdió por {goals}"
            | Some goals -> $"Out for revenge: he lost by {goals} last time"
            | None -> if es then "Busca revancha" else "Out for revenge"
        | Underdog ->
            let bold = coach.Mind.Boldness >= cfg.UnderdogBoldnessPivot

            if es then
                if bold then
                    "Se siente inferior pero se anima: va a presionarte arriba"
                else
                    "Se siente inferior: se cierra y sale de contra"
            elif bold then
                "He feels outmatched but goes for it: he will press you high"
            else
                "He feels outmatched: he will sit deep and hit on the break"

    /// None when the user has no next fixture, the rival has no head coach, or the coach cannot field a plan.
    let project (es: bool) (cfg: RivalManagerConfig) (gs: GameState) : RivalCoachVM option =
        GameState.getUserNextFixture gs
        |> Option.bind (fun (_, fixture) ->
            let rivalId =
                if fixture.HomeClubId = gs.UserClubId then
                    fixture.AwayClubId
                else
                    fixture.HomeClubId

            GameState.headCoach rivalId gs
            |> Option.map (fun coach ->
                let user = GameState.headCoach gs.UserClubId gs
                let archetype, behaviour = archetypeText es (ManagerMind.archetypeOf coach)

                let record =
                    user
                    |> Option.bind (fun u -> Map.tryFind u.Id coach.Mind.Rivals)
                    |> Option.filter (fun r -> r.Played > 0)
                    // The coach's record is from his side; the card speaks to the user.
                    |> Option.map (fun r -> Meetings(r.Played, r.Lost, r.Drawn, r.Won))
                    |> Option.defaultValue FirstMeeting

                let plan = GamePlanner.preview cfg gs fixture

                { CoachName = coach.Name
                  Archetype = archetype
                  Behaviour = behaviour
                  Stars = stars coach
                  RecordVs = record
                  Formation =
                    plan
                    |> Option.map (fun (_, note) -> formationLabel note.Formation)
                    |> Option.defaultValue "—"
                  PlanReason =
                    plan
                    |> Option.map (fun (_, note) -> planReason es cfg coach user note)
                    |> Option.defaultValue (
                        if es then
                            "Todavía no definió su once"
                        else
                            "He has not picked his XI yet"
                    ) }))
