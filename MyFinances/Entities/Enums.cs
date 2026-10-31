using System.ComponentModel.DataAnnotations;

namespace MyFinances.Entities
{
    /// <summary>
    /// The currencies MyFinances keeps accounts in. Stored by code, so
    /// the database reads USD rather than 0.
    /// </summary>
    public enum Currency
    {
        USD,
        ZWG,
        ZAR,
        GBP,
        EUR
    }

    public enum AccountType
    {
        [Display(Name = "Bank account")]
        Bank,

        [Display(Name = "Mobile money")]
        MobileMoney,

        [Display(Name = "Cash wallet")]
        Cash,

        [Display(Name = "Savings account")]
        Savings,

        [Display(Name = "Investment account")]
        Investment,

        [Display(Name = "Other")]
        Other
    }

    public enum CategoryKind
    {
        Income,
        Expense
    }

    public enum TransactionType
    {
        Income,

        Expense,

        Transfer,

        /// <summary>
        /// Principal coming into an account from a debt: money borrowed,
        /// or a repayment from someone who owes you. Written only by the
        /// debt module; it moves the balance but is not income.
        /// </summary>
        [Display(Name = "Debt received")]
        DebtIn,

        /// <summary>
        /// Principal leaving an account for a debt: repaying what you
        /// owe, or lending money out. Not an expense.
        /// </summary>
        [Display(Name = "Debt paid")]
        DebtOut
    }

    public enum BudgetPeriod
    {
        Monthly,
        Quarterly,
        Yearly
    }

    public enum GoalStatus
    {
        Active,
        Achieved,
        Paused,
        Cancelled
    }

    public enum DebtDirection
    {
        [Display(Name = "Money I owe")]
        IOwe,

        [Display(Name = "Money owed to me")]
        OwedToMe
    }

    public enum DebtType
    {
        [Display(Name = "Bank loan")]
        BankLoan,

        [Display(Name = "Personal loan")]
        PersonalLoan,

        Mortgage,

        [Display(Name = "Credit card")]
        CreditCard,

        [Display(Name = "Hire purchase")]
        HirePurchase,

        [Display(Name = "Family or friend")]
        Informal,

        Other
    }

    public enum DebtStatus
    {
        Active,

        Settled,

        [Display(Name = "Written off")]
        WrittenOff
    }

    public enum Frequency
    {
        Weekly,
        Monthly,
        Quarterly,
        Yearly
    }

    public enum BillType
    {
        Rent,

        Utilities,

        Internet,

        Insurance,

        [Display(Name = "Software subscription")]
        Software,

        [Display(Name = "Loan repayment")]
        LoanRepayment,

        Other
    }

    public enum HoldingKind
    {
        Asset,
        Liability
    }

    public enum HoldingCategory
    {
        Property,

        Vehicle,

        Electronics,

        [Display(Name = "Business interest")]
        BusinessInterest,

        Investment,

        [Display(Name = "Other possession")]
        OtherPossession,

        Loan,

        [Display(Name = "Outstanding debt")]
        OutstandingDebt,

        [Display(Name = "Other obligation")]
        OtherObligation
    }

    public enum ThemePreference
    {
        Light,
        Dark,
        System
    }

    public enum DashboardPeriod
    {
        [Display(Name = "This month")]
        ThisMonth,

        [Display(Name = "Last month")]
        LastMonth,

        [Display(Name = "This quarter")]
        ThisQuarter,

        [Display(Name = "This year")]
        ThisYear,

        [Display(Name = "This financial year")]
        FinancialYear
    }
}
