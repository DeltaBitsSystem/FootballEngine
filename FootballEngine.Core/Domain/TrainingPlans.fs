namespace FootballEngine.Domain

/// Player-dependent training helpers. Lives after Player.fs: the DUs live in
/// Training.fs, the logic that reads a Player lives here.
module TrainingPlans =

    let isRehab (p: Player) : bool =
        match p.Status with
        | Injured _ -> true
        | _ -> false

    /// The plan that actually runs: injuries override anything on file.
    let effectivePlan (p: Player) : IndividualPlan option =
        if isRehab p then None else Some p.TrainingPlan

    let defaultPlanFor (position: Position) (age: int) : IndividualPlan =
        if age <= 20 then
            Develop(
                match position with
                | GK -> AttributeGroup.Goalkeeping
                | ST -> AttributeGroup.Finishing
                | AMC
                | MC
                | AML
                | AMR -> AttributeGroup.Creation
                | DC
                | DL
                | DR
                | DM -> AttributeGroup.Defending
                | _ -> AttributeGroup.Physical
            )
        else
            Perform

    let groupsFor (position: Position) : AttributeGroup list =
        let gkOnly =
            match position with
            | GK -> [ AttributeGroup.Goalkeeping ]
            | _ -> []

        [ AttributeGroup.Finishing
          AttributeGroup.Creation
          AttributeGroup.Technique
          AttributeGroup.Defending
          AttributeGroup.Physical
          AttributeGroup.Mental ]
        @ gkOnly
