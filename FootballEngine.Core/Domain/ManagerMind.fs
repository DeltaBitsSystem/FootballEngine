namespace FootballEngine.Domain

open System

/// The label the user sees for a coach. Derived from traits, never stored, so editing a trait can never leave a
/// stale label behind.
type ManagerArchetype =
    | Copycat
    | Hothead
    | Strategist
    | Romantic
    | Gambler
    | Conservative
    | Pragmatist

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ManagerMind =

    /// A coach with no strong trait: what a hand-built staff member gets when nothing authored his personality.
    let neutral: ManagerMind =
        { Philosophy = CoachPhilosophy.Possession
          Boldness = 10
          Temperament = 10
          Pride = 10
          Imitation = 10
          Rivals = Map.empty
          LastPlan = None }

    let private clamp01 (v: float) = max 0.0 (min 1.0 v)

    /// How well he reads a match and a squad: current skill over the 200 ceiling.
    let reading (s: Staff) : float = clamp01 (float s.CurrentSkill / 200.0)

    /// How fast he picks up the user's habits: potential over the 200 ceiling.
    let learning (s: Staff) : float =
        clamp01 (float s.PotentialSkill / 200.0)

    /// Rules are ordered: the first that matches names the coach, so a trait that dominates his behaviour
    /// (copying, temper) outranks the milder ones.
    let archetypeOf (s: Staff) : ManagerArchetype =
        let m = s.Mind

        if m.Imitation >= 15 then
            Copycat
        elif m.Temperament >= 15 then
            Hothead
        elif s.Mental.Adaptability >= 14 && s.CurrentSkill >= 140 then
            Strategist
        elif
            s.Mental.Adaptability <= 7
            && (m.Philosophy = CoachPhilosophy.Possession
                || m.Philosophy = CoachPhilosophy.Pressing)
        then
            Romantic
        elif m.Boldness >= 15 then
            Gambler
        elif m.Boldness <= 6 || m.Philosophy = CoachPhilosophy.Fortress then
            Conservative
        else
            Pragmatist

    let private meeting (date: DateTime) (mine: int) (theirs: int) (previous: RivalRecord option) : RivalRecord =
        let before =
            previous
            |> Option.defaultValue
                { Played = 0
                  Won = 0
                  Drawn = 0
                  Lost = 0
                  GoalsFor = 0
                  GoalsAgainst = 0
                  LastMargin = 0
                  LastMet = date }

        { Played = before.Played + 1
          Won = before.Won + (if mine > theirs then 1 else 0)
          Drawn = before.Drawn + (if mine = theirs then 1 else 0)
          Lost = before.Lost + (if mine < theirs then 1 else 0)
          GoalsFor = before.GoalsFor + mine
          GoalsAgainst = before.GoalsAgainst + theirs
          LastMargin = mine - theirs
          LastMet = date }

    /// Both coaches remember the meeting from their own side. A coach never meets himself.
    let recordResult (date: DateTime) (a: Staff) (b: Staff) (goalsA: int) (goalsB: int) : Staff * Staff =
        if a.Id = b.Id then
            a, b
        else
            let remember (me: Staff) (other: Staff) (mine: int) (theirs: int) =
                { me with
                    Mind =
                        { me.Mind with
                            Rivals =
                                me.Mind.Rivals
                                |> Map.add other.Id (meeting date mine theirs (me.Mind.Rivals |> Map.tryFind other.Id)) } }

            remember a b goalsA goalsB, remember b a goalsB goalsA

    /// Rivalries nobody renewed fade, so a long save does not carry every coach ever met.
    let prune (cfg: FootballEngine.Core.Match.RivalManagerConfig) (now: DateTime) (s: Staff) : Staff =
        let limit = 365.25 * float cfg.RivalMemoryYears

        { s with
            Mind =
                { s.Mind with
                    Rivals =
                        s.Mind.Rivals
                        |> Map.filter (fun _ record -> (now - record.LastMet).TotalDays <= limit) } }

    /// One season of experience: a young coach closes part of the gap to his potential, an old one slips. The
    /// result never passes PA and never drops under the floor.
    let developSeason (cfg: FootballEngine.Core.Match.RivalManagerConfig) (now: DateTime) (s: Staff) : Staff =
        let age = Staff.age now s

        let next =
            if age < cfg.CoachPeakEndAge then
                let gap = s.PotentialSkill - s.CurrentSkill

                if gap <= 0 then
                    s.CurrentSkill
                else
                    s.CurrentSkill + max 1 (int (round (float gap * cfg.CoachGrowthRate)))
            else
                s.CurrentSkill - cfg.CoachDeclinePerYear

        { s with
            CurrentSkill = min s.PotentialSkill (max cfg.CoachSkillFloor next) }
