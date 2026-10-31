namespace MyFinances.Entities
{
    /// <summary>
    /// One line of the ledger. <see cref="Amount"/> is always positive
    /// and in the account's currency; the type says which way it goes.
    ///
    /// A transfer is a single row with a source and a destination, so
    /// it can never be mistaken for income or spending. Between
    /// currencies it keeps both sides: <see cref="Amount"/> left the
    /// source and <see cref="ToAmount"/> arrived.
    ///
    /// The reporting columns are a snapshot of the conversion made when
    /// the row was saved, so old months keep the rate of their day.
    /// They are empty when no rate was available.
    /// </summary>
    public class Transaction : IUserOwned
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public DateOnly Date { get; set; }

        public TransactionType Type { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public Currency Currency { get; set; }

        public int AccountId { get; set; }

        public Account? Account { get; set; }

        public int? ToAccountId { get; set; }

        public Account? ToAccount { get; set; }

        public decimal? ToAmount { get; set; }

        /// <summary>Units of the destination currency per unit sent.</summary>
        public decimal? TransferRate { get; set; }

        public int? CategoryId { get; set; }

        public Category? Category { get; set; }

        public string? Notes { get; set; }

        /// <summary>The stored file's name under the receipts folder.</summary>
        public string? ReceiptPath { get; set; }

        public string? ReceiptFileName { get; set; }

        public string? ReceiptContentType { get; set; }

        public Currency? ReportingCurrency { get; set; }

        public decimal? ReportingRate { get; set; }

        public decimal? ReportingAmount { get; set; }

        public DateOnly? ReportingRateDate { get; set; }

        public int? BillPaymentId { get; set; }

        public BillPayment? BillPayment { get; set; }

        public int? DebtId { get; set; }

        public Debt? Debt { get; set; }

        public int? DebtPaymentId { get; set; }

        public DebtPayment? DebtPayment { get; set; }

        public int? GoalContributionId { get; set; }

        public GoalContribution? GoalContribution { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Rows written by bills, debts or goals are changed from there,
        /// so the two records cannot drift apart.
        /// </summary>
        public bool IsLinked =>
            BillPaymentId != null ||
            DebtId != null ||
            DebtPaymentId != null ||
            GoalContributionId != null;
    }
}
