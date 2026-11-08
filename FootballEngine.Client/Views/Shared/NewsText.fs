namespace FootballEngine

open FootballEngine.Domain

/// Turns structured news facts into prose. Core stores facts only; every word the player reads is chosen here.
module NewsText =

    /// Deterministic by Id so the same item reads the same on every redraw and in every replay of a save.
    let variantIndex (id: int) (count: int) : int = (abs (int64 id) % int64 count) |> int

    /// One phrasing: Spanish headline, Spanish body, English headline, English body.
    type private Variant = string * string * string * string

    let private missing = "—"

    let private clubName (gs: GameState) (id: ClubId) =
        gs.Clubs |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue missing

    let private playerName (gs: GameState) (id: PlayerId) =
        gs.Players |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue missing

    let private staffName (gs: GameState) (id: StaffId) =
        gs.Staff |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue missing

    let private competitionName (gs: GameState) (id: CompetitionId) =
        gs.Competitions
        |> Map.tryFind id
        |> Option.map _.Name
        |> Option.defaultValue missing

    let private es' (es: string) (en: string) =
        if Localization.active () = Locale.Es then es else en

    let private streakWhat (kind: StreakKind) =
        match kind with
        | StreakKind.Wins -> es' "victorias seguidas" "straight wins"
        | StreakKind.Unbeaten -> es' "partidos sin perder" "matches unbeaten"
        | StreakKind.Winless -> es' "partidos sin ganar" "matches without a win"
        | StreakKind.Losses -> es' "derrotas seguidas" "straight defeats"

    let private moodWord (band: FansMoodBand) =
        match band with
        | FansMoodBand.Furious -> es' "furiosa" "furious"
        | FansMoodBand.Restless -> es' "inquieta" "restless"
        | FansMoodBand.Content -> es' "conforme" "content"
        | FansMoodBand.Euphoric -> es' "eufórica" "euphoric"

    let private reasonWord (reason: UnrestReason) =
        match reason with
        | UnrestReason.LackOfMinutes -> es' "la falta de minutos" "a lack of playing time"
        | UnrestReason.ContractStalled -> es' "el contrato que no avanza" "his stalled contract talks"
        | UnrestReason.BiggerClubInterest -> es' "el interés de un club más grande" "interest from a bigger club"

    let private awardWord (kind: AwardKind) =
        match kind with
        | AwardKind.TopScorer -> es' "Goleador de la temporada" "Top scorer of the season"
        | AwardKind.PlayerOfSeason -> es' "Jugador de la temporada" "Player of the season"
        | AwardKind.YoungPlayer -> es' "Mejor jugador joven" "Best young player"
        | AwardKind.ManagerOfSeason -> es' "Entrenador de la temporada" "Manager of the season"

    let private variants (gs: GameState) (kind: NewsKind) : Variant list * (string * string) list =
        let club = clubName gs
        let player = playerName gs
        let n (value: int) = string value

        match kind with
        | NewsKind.BigWin(winner, loser, gf, ga) ->
            [ ("{winner} golea {gf}-{ga} a {loser}",
               "Una exhibición: {winner} se llevó el partido por {gf} a {ga} y {loser} no tuvo respuesta.",
               "{winner} thrash {loser} {gf}-{ga}",
               "A statement win: {winner} took the match {gf}-{ga} and {loser} had no answer.")
              ("Paliza de {winner}: {gf}-{ga} sobre {loser}",
               "{loser} salió goleado por {gf} a {ga} y tendrá que dar explicaciones.",
               "{winner} run riot: {gf}-{ga} over {loser}",
               "{loser} were beaten {gf}-{ga} and will have questions to answer.")
              ("{loser} cae {ga}-{gf} ante {winner}",
               "Un resultado abultado que pesa: {winner} fue muy superior durante todo el partido.",
               "{loser} crushed {ga}-{gf} by {winner}",
               "A heavy result: {winner} were far superior throughout.") ],
            [ "winner", club winner; "loser", club loser; "gf", n gf; "ga", n ga ]
        | NewsKind.Upset(underdog, favourite, gu, gf) ->
            [ ("Batacazo: {underdog} derrota a {favourite} por {gu}-{gf}",
               "Nadie lo esperaba. {underdog} dio la sorpresa frente a un {favourite} que llegaba como favorito.",
               "Shock: {underdog} beat {favourite} {gu}-{gf}",
               "Nobody saw it coming. {underdog} stunned a {favourite} side that arrived as favourites.")
              ("{underdog} sorprende a {favourite} ({gu}-{gf})",
               "El resultado más inesperado de la fecha: {favourite} se va con las manos vacías.",
               "{underdog} stun {favourite} ({gu}-{gf})",
               "The most unexpected result of the round: {favourite} go home empty-handed.") ],
            [ "underdog", club underdog
              "favourite", club favourite
              "gu", n gu
              "gf", n gf ]
        | NewsKind.Streak(c, kind, length) ->
            [ ("{club} suma {length} {what}",
               "La racha de {club} llega a {length} {what} y el vestuario lo nota.",
               "{club} reach {length} {what}",
               "{club}'s run has stretched to {length} {what} and the dressing room feels it.")
              ("Racha de {length} {what} para {club}",
               "{club} sostiene un momento que ya es noticia en toda la liga.",
               "A run of {length} {what} for {club}",
               "{club} are keeping up a spell that is now making headlines across the league.") ],
            [ "club", club c; "length", n length; "what", streakWhat kind ]
        | NewsKind.NewLeader(c, comp) ->
            [ ("{club} es el nuevo líder de {competition}",
               "{club} se queda con la punta y ahora los demás deben perseguirlo.",
               "{club} go top of {competition}",
               "{club} take the lead and the rest now have to chase them.")
              ("Cambio en la cima de {competition}: manda {club}",
               "Tras esta fecha, {club} encabeza la tabla.",
               "New leaders in {competition}: {club}",
               "After this round, {club} sit at the top of the table.") ],
            [ "club", club c; "competition", competitionName gs comp ]
        | NewsKind.Clasico(home, away) ->
            [ ("Se viene el clásico: {home} vs {away}",
               "Los dos rivales de siempre se miran de frente y la previa ya se siente en la ciudad.",
               "Derby day looms: {home} v {away}",
               "The old rivals face each other and the build-up is already felt around town.")
              ("{home} y {away}, un clásico para no perderse",
               "Hay mucho en juego y nadie quiere ceder terreno en este partido.",
               "{home} and {away}: a derby not to be missed",
               "There is a lot at stake and nobody wants to give an inch in this one.") ],
            [ "home", club home; "away", club away ]
        | NewsKind.HatTrick(p, c, goals) ->
            [ ("{player} marca {goals} goles para {club}",
               "Una tarde de ensueño: {player} se llevó la pelota a casa con {goals} tantos.",
               "{player} scores {goals} for {club}",
               "A dream afternoon: {player} took the match ball home with {goals} goals.")
              ("Tarde de {player}: {goals} goles con {club}",
               "{player} fue imparable y dejó su firma en el partido.",
               "{player} on fire: {goals} goals for {club}",
               "{player} was unstoppable and left his mark on the match.") ],
            [ "player", player p; "club", club c; "goals", n goals ]
        | NewsKind.ScoringRun(p, matches) ->
            [ ("{player} lleva goles en {matches} partidos seguidos",
               "El delantero está en racha y los rivales ya no saben cómo frenarlo.",
               "{player} has scored in {matches} games in a row",
               "The striker is in form and rivals no longer know how to stop him.")
              ("Racha goleadora: {player}, {matches} partidos convirtiendo",
               "{player} no para de marcar y se mete en la conversación por el premio al goleador.",
               "Scoring streak: {player}, {matches} matches on the mark",
               "{player} keeps on scoring and is entering the golden boot conversation.") ],
            [ "player", player p; "matches", n matches ]
        | NewsKind.BoardUnrest(c, confidence) ->
            [ ("La directiva de {club} pierde la paciencia",
               "La confianza en el entrenador cayó a {confidence}/100 y se habla de cambios si no mejoran los resultados.",
               "The {club} board is losing patience",
               "Confidence in the manager has fallen to {confidence}/100 and changes are being discussed if results do not improve.")
              ("Malestar en los despachos de {club}",
               "Con una confianza de {confidence}/100, el cargo del entrenador está bajo la lupa.",
               "Unrest in the {club} boardroom",
               "With confidence at {confidence}/100, the manager's job is under scrutiny.") ],
            [ "club", club c; "confidence", n confidence ]
        | NewsKind.ManagerSacked(c, manager, matches) ->
            [ ("{club} despide a {manager}",
               "El técnico dejó el cargo tras {matches} partidos al frente del equipo.",
               "{club} sack {manager}",
               "The manager leaves after {matches} matches in charge.")
              ("Se terminó el ciclo de {manager} en {club}",
               "Después de {matches} partidos, la directiva decidió cortar por lo sano.",
               "{manager} is out at {club}",
               "After {matches} matches, the board decided to cut its losses.") ],
            [ "club", club c; "manager", staffName gs manager; "matches", n matches ]
        | NewsKind.ManagerAppointed(c, manager) ->
            [ ("{manager} es el nuevo entrenador de {club}",
               "El club presentó a su nuevo técnico, que tendrá la misión de cambiar la dinámica.",
               "{manager} named new {club} manager",
               "The club unveiled its new boss, who is tasked with turning things around.")
              ("{club} confía en {manager}",
               "{manager} asume el banco con la ilusión de empezar una nueva etapa.",
               "{club} put their faith in {manager}",
               "{manager} takes the dugout hoping to start a new chapter.") ],
            [ "club", club c; "manager", staffName gs manager ]
        | NewsKind.FansMood(c, band) ->
            [ ("La hinchada de {club} está {mood}",
               "El clima en las tribunas se refleja en el estadio y en la recaudación del club.",
               "{club} supporters are {mood}",
               "The mood on the terraces shows in the stadium and in the club's income.")
              ("Los hinchas de {club} se muestran {mood}",
               "Lo que se dice en la calle y en las redes pesa cada vez más sobre el equipo.",
               "{club} fans are feeling {mood}",
               "What is said on the street and online weighs ever more on the team.") ],
            [ "club", club c; "mood", moodWord band ]
        | NewsKind.TransferRequest(p, c, reason) ->
            [ ("{player} pide salir de {club}",
               "El jugador comunicó que quiere marcharse por {reason}.",
               "{player} wants out of {club}",
               "The player has said he wants to leave because of {reason}.")
              ("{player} solicita su transferencia",
               "Según su entorno, {reason} lo empuja a buscar otro destino lejos de {club}.",
               "{player} requests a transfer",
               "According to his camp, {reason} is pushing him to look for a move away from {club}.") ],
            [ "player", player p; "club", club c; "reason", reasonWord reason ]
        | NewsKind.ContractRunningOut(p, c, months) ->
            [ ("El contrato de {player} vence en {months} meses",
               "{club} tiene poco tiempo para renovarlo antes de que quede libre.",
               "{player}'s contract expires in {months} months",
               "{club} have little time to renew him before he becomes a free agent.")
              ("{player} entra en la recta final de su contrato",
               "Quedan {months} meses y el futuro del jugador en {club} sigue abierto.",
               "{player} enters the final stretch of his deal",
               "{months} months remain and his future at {club} is still open.") ],
            [ "player", player p; "club", club c; "months", n months ]
        | NewsKind.Milestone(p, MilestoneKind.Debut, _) ->
            [ ("Debut de {player}",
               "{player} jugó su primer partido como profesional.",
               "{player} makes his debut",
               "{player} played his first professional match.")
              ("{player} estrena su carrera profesional",
               "Un día que no va a olvidar: {player} se estrenó en primera.",
               "{player} begins his professional career",
               "A day he will not forget: {player} made his bow.") ],
            [ "player", player p ]
        | NewsKind.Milestone(p, MilestoneKind.Appearances, value) ->
            [ ("{player} llega a los {value} partidos",
               "Un hito en su carrera: {player} alcanzó los {value} encuentros como profesional.",
               "{player} reaches {value} appearances",
               "A career landmark: {player} has now played {value} professional matches.")
              ("{value} partidos para {player}",
               "{player} suma una nueva marca de regularidad y trayectoria.",
               "{value} games for {player}",
               "{player} adds another mark of consistency and longevity.") ],
            [ "player", player p; "value", n value ]
        | NewsKind.Milestone(p, MilestoneKind.Goals, value) ->
            [ ("{player} alcanza los {value} goles",
               "Un número redondo en su carrera: {value} goles que ya son historia.",
               "{player} hits {value} career goals",
               "A round number in his career: {value} goals that are already history.")
              ("Gol {value} en la carrera de {player}",
               "{player} sigue sumando y escribe su nombre en los libros.",
               "Goal number {value} for {player}",
               "{player} keeps adding to the tally and writes his name in the books.") ],
            [ "player", player p; "value", n value ]
        | NewsKind.Breakout(p, c) ->
            [ ("{player} explota en {club}",
               "El joven está dando un salto de nivel y ya se habla de él en todo el país.",
               "{player} breaks through at {club}",
               "The youngster has stepped up a level and the whole country is talking about him.")
              ("La gran revelación: {player}",
               "{club} descubrió una joya y otros clubes ya tomaron nota.",
               "The big revelation: {player}",
               "{club} have found a gem and other clubs have taken note.") ],
            [ "player", player p; "club", club c ]
        | NewsKind.Retirement(p, age) ->
            [ ("{player} se retira a los {age} años",
               "Cuelga los botines un jugador que dejó huella en el fútbol.",
               "{player} retires at {age}",
               "A player who left his mark on the game hangs up his boots.")
              ("Adiós al fútbol: {player}, {age} años",
               "{player} anunció su retiro tras una larga carrera.",
               "Farewell to football: {player}, {age}",
               "{player} announced his retirement after a long career.") ],
            [ "player", player p; "age", n age ]
        | NewsKind.Rumour(p, from, interested) ->
            [ ("{interested} sigue de cerca a {player}",
               "Se dice que {interested} prepara una oferta por el jugador de {from}.",
               "{interested} keeping tabs on {player}",
               "It is said {interested} are preparing an offer for the {from} player.")
              ("Rumor: {player} podría dejar {from} para ir a {interested}",
               "Nada oficial todavía, pero las conversaciones habrían comenzado.",
               "Rumour: {player} could leave {from} for {interested}",
               "Nothing official yet, but talks are said to have begun.") ],
            [ "player", player p; "from", club from; "interested", club interested ]
        | NewsKind.TransferDone(p, from, toClub, fee) ->
            let fromName =
                from
                |> Option.map club
                |> Option.defaultValue (es' "el mercado libre" "free agency")

            let feeText =
                if fee <= 0m then
                    es' "gratis" "on a free"
                else
                    Formatters.money fee

            [ ("{player} ficha por {to} ({fee})",
               "{to} cerró la incorporación de {player}, que llega desde {from}.",
               "{player} joins {to} ({fee})",
               "{to} completed the signing of {player} from {from}.")
              ("{to} se refuerza con {player}",
               "La operación con {from} se cerró por {fee} y el jugador ya es oficialmente de {to}.",
               "{to} land {player}",
               "The deal with {from} was agreed at {fee} and the player is officially {to}'s.") ],
            [ "player", player p; "from", fromName; "to", club toClub; "fee", feeText ]
        | NewsKind.Award(kind, p, staff, comp) ->
            let winner =
                match p, staff with
                | Some pid, _ -> player pid
                | None, Some sid -> staffName gs sid
                | None, None -> missing

            [ ("{award}: {winner}",
               "{winner} se llevó el premio de {competition}.",
               "{award}: {winner}",
               "{winner} claimed the {competition} award.")
              ("{winner} gana el premio {award}",
               "El reconocimiento de {competition} fue para {winner}.",
               "{winner} wins {award}",
               "The {competition} recognition went to {winner}.") ],
            [ "award", awardWord kind
              "winner", winner
              "competition", competitionName gs comp ]
        | NewsKind.Promotion(c, comp) ->
            [ ("{club} asciende",
               "{club} logra el ascenso desde {competition} y volverá a jugar en una categoría superior.",
               "{club} promoted",
               "{club} earn promotion from {competition} and will play in a higher division.")
              ("Ascenso de {club}",
               "Festejo en {club}: la temporada en {competition} termina con premio.",
               "Promotion for {club}",
               "Celebrations at {club}: the {competition} season ends with a reward.") ],
            [ "club", club c; "competition", competitionName gs comp ]
        | NewsKind.Relegation(c, comp) ->
            [ ("{club} desciende",
               "{club} pierde la categoría en {competition} y deberá reconstruirse.",
               "{club} relegated",
               "{club} drop out of {competition} and will have to rebuild.")
              ("Descenso de {club}",
               "Un golpe duro: la temporada en {competition} termina con la caída de {club}.",
               "Relegation for {club}",
               "A heavy blow: the {competition} season ends with {club} going down.") ],
            [ "club", club c; "competition", competitionName gs comp ]
        | NewsKind.Champion(c, comp) ->
            [ ("{club} es campeón de {competition}",
               "{club} se consagra campeón y su gente sale a festejar.",
               "{club} are {competition} champions",
               "{club} are crowned champions and their fans take to the streets.")
              ("¡{club} se corona en {competition}!",
               "Una temporada para el recuerdo cierra con el título.",
               "{club} crowned {competition} champions!",
               "A season to remember ends with the title.") ],
            [ "club", club c; "competition", competitionName gs comp ]

    /// Headline and body in the active language.
    let render (gs: GameState) (item: NewsItem) : string * string =
        let options, parameters = variants gs item.Kind
        let (hEs, bEs, hEn, bEn) = options[variantIndex item.Id options.Length]
        let label = $"news:{item.Id}"

        Localization.resolveVariant label hEs hEn parameters, Localization.resolveVariant label bEs bEn parameters

    /// How many phrasings the kind has; exposed so tests can prove variation without reaching into templates.
    let variantCount (gs: GameState) (kind: NewsKind) : int = (variants gs kind |> fst).Length
