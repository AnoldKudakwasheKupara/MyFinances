namespace MyFinances.Entities
{
    /// <summary>
    /// A limit on one expense category that repeats every period from
    /// <see cref="StartDate"/> until <see cref="EndDate"/>, if any.
    /// </summary>
    public class Budget : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public int CategoryId { get; set; }

        public Category? Category { get; set; }

        public BudgetPeriod Period { get; set; }

        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class SavingsGoal : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal TargetAmount { get; set; }

        public Currency Currency { get; set; }

        public DateOnly? TargetDate { get; set; }

        /// <summary>
        /// Where the money for this goal is kept, if it has its own
        /// account. Contributions from elsewhere become transfers into it.
        /// </summary>
        public int? AccountId { get; set; }

        public Account? Account { get; set; }

        public GoalStatus Status { get; set; }

        public string Icon { get; set; } = "piggy-bank";

        public List<GoalContribution> Contributions { get; set; } = new();

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Money put towards a goal, or taken back when negative. It is
    /// never income: either it earmarks money already counted in an
    /// account, or the goal records a transfer between accounts.
    /// </summary>
    public class GoalContribution : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int GoalId { get; set; }

        public SavingsGoal? Goal { get; set; }

        public DateOnly Date { get; set; }

        public decimal Amount { get; set; }

        public string? Notes { get; set; }

        public List<Transaction> Transactions { get; set; } = new();

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Money borrowed or lent. What is still outstanding is the
    /// original amount less the principal of every payment, worked out
    /// by <see cref="Services.DebtService"/> rather than kept here.
    /// </summary>
    public class Debt : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public DebtDirection Direction { get; set; }

        public string Description { get; set; } = string.Empty;

        public string Counterparty { get; set; } = string.Empty;

        public DebtType Type { get; set; }

        public decimal OriginalAmount { get; set; }

        public Currency Currency { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly? DueDate { get; set; }

        /// <summary>Yearly interest as a percentage, such as 12.5.</summary>
        public decimal? InterestRate { get; set; }

        public Frequency? RepaymentFrequency { get; set; }

        public decimal? RepaymentAmount { get; set; }

        public string? Notes { get; set; }

        public DebtStatus Status { get; set; }

        public List<DebtPayment> Payments { get; set; } = new();

        public List<Transaction> Transactions { get; set; } = new();

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// A repayment made or received. Principal reduces the debt;
    /// interest is a cost (or earnings) of its own. When an account is
    /// given, the payment is written to the ledger too; without one it
    /// is recorded here only, for money already in the ledger.
    /// </summary>
    public class DebtPayment : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int DebtId { get; set; }

        public Debt? Debt { get; set; }

        public DateOnly Date { get; set; }

        public decimal Principal { get; set; }

        public decimal Interest { get; set; }

        public int? AccountId { get; set; }

        public Account? Account { get; set; }

        public string? Notes { get; set; }

        public List<Transaction> Transactions { get; set; } = new();

        public DateTime CreatedAt { get; set; }

        public decimal Total => Principal + Interest;
    }

    public class Bill : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public BillType Type { get; set; }

        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        public int CategoryId { get; set; }

        public Category? Category { get; set; }

        /// <summary>The account it is usually paid from.</summary>
        public int? AccountId { get; set; }

        public Account? Account { get; set; }

        public Frequency Frequency { get; set; }

        /// <summary>The earliest occurrence not yet paid.</summary>
        public DateOnly NextDueDate { get; set; }

        /// <summary>How many days ahead to remind; 0 for no reminder.</summary>
        public int ReminderDays { get; set; } = 3;

        public bool IsActive { get; set; } = true;

        public string? Notes { get; set; }

        public List<BillPayment> Payments { get; set; } = new();

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// One occurrence of a bill, paid. The due date it settles is
    /// unique per bill, so the same occurrence cannot be paid twice.
    /// </summary>
    public class BillPayment : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int BillId { get; set; }

        public Bill? Bill { get; set; }

        public DateOnly DueDate { get; set; }

        public DateOnly PaidDate { get; set; }

        public decimal Amount { get; set; }

        public string? Notes { get; set; }

        public List<Transaction> Transactions { get; set; } = new();

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Something owned or owed that is not already an account or a
    /// tracked debt: a house, a car, a store account. Its value is the
    /// latest of its valuations.
    /// </summary>
    public class Holding : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public HoldingKind Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        public HoldingCategory Category { get; set; }

        public Currency Currency { get; set; }

        public DateOnly? AcquiredOn { get; set; }

        public string? Notes { get; set; }

        /// <summary>Sold or settled: kept for history, left out of net worth.</summary>
        public bool IsClosed { get; set; }

        public List<HoldingValuation> Valuations { get; set; } = new();

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public class HoldingValuation : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int HoldingId { get; set; }

        public Holding? Holding { get; set; }

        public DateOnly Date { get; set; }

        public decimal Value { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
