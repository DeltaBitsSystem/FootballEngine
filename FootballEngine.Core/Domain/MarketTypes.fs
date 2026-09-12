namespace FootballEngine.Domain

type ContractOption =
    | PlayerOption of System.DateTime * decimal
    | ClubOption of System.DateTime * decimal
    | BuyOption of System.DateTime * decimal

type ContractClause =
    | ReleaseClause of decimal
    | SellOnPercentage of decimal
    | RecallFromLoan of System.DateTime

type MarketContract =
    { Id: ContractId
      Employer: ClubId
      Player: PlayerId
      Starts: System.DateTime
      Ends: System.DateTime
      Salary: decimal
      Bonuses: decimal
      Role: ContractRole
      MinutesPromised: int
      Options: ContractOption list
      Clauses: ContractClause list }

type LoanAgreement =
    { Player: PlayerId
      Origin: ClubId
      Destination: ClubId
      Starts: System.DateTime
      Ends: System.DateTime
      WageShareOrigin: decimal
      WageShareDestination: decimal
      BuyOption: decimal option
      RecallDate: System.DateTime option }

type RegistrationStatus =
    | Unregistered
    | Eligible
    | Registered
    | RegistrationBlocked of string

type TransferOperationStatus =
    | Draft
    | Submitted
    | Countered
    | Agreed
    | PendingRegistration
    | Completed
    | Rejected of string
    | Withdrawn
    | Expired

type TransferReason =
    | TransferWindowClosed
    | InsufficientFunds
    | WageCapacityExceeded
    | PlayerRejectedMinutes
    | PlayerRejectedProject
    | SellerRejectedPrice
    | PlayerAlreadyCommitted
    | RegistrationInvalid of string
    | InvalidOperation of string

type TransferOperation =
    { Id: TransferOperationId
      Player: PlayerId
      Buyer: ClubId
      Seller: ClubId option
      Fee: decimal
      Salary: decimal
      ContractYears: int
      Role: ContractRole
      MinutesPromised: int
      Status: TransferOperationStatus
      Reason: TransferReason option
      Created: System.DateTime
      Updated: System.DateTime }

type MarketEvent =
    | ScoutAssigned of ScoutingAssignmentId * string
    | ReportReady of ScoutingAssignmentId * string
    | OfferCountered of TransferOperationId * string
    | ContractRenewed of ContractId * string
    | TransferCompleted of TransferOperationId * string
    | PlayerPromoted of PlayerId * string
    | LoanEnded of PlayerId * string
    | RegistrationRejected of TransferOperationId * string

type MarketState =
    { Operations: Map<TransferOperationId, TransferOperation>
      Contracts: Map<ContractId, MarketContract>
      Loans: Map<PlayerId, LoanAgreement>
      Assignments: Map<ScoutingAssignmentId, ScoutingAssignment>
      Knowledge: Map<PlayerId, PlayerKnowledge>
      Shortlists: Map<ClubId, PlayerId list>
      Registrations: Map<PlayerId, RegistrationStatus>
      Events: MarketEvent list
      NextOperationId: int
      NextContractId: int }

module MarketState =
    let empty =
        { Operations = Map.empty
          Contracts = Map.empty
          Loans = Map.empty
          Assignments = Map.empty
          Knowledge = Map.empty
          Shortlists = Map.empty
          Registrations = Map.empty
          Events = []
          NextOperationId = 1
          NextContractId = 1 }
