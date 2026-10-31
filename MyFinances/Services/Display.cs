using MyFinances.Entities;

namespace MyFinances.Services
{
    /// <summary>Icons and wording shared by the views.</summary>
    public static class Display
    {
        public static string AccountIcon(AccountType type) => type switch
        {
            AccountType.Bank => "bank",
            AccountType.MobileMoney => "phone",
            AccountType.Cash => "cash-stack",
            AccountType.Savings => "piggy-bank",
            AccountType.Investment => "graph-up-arrow",
            _ => "wallet2"
        };

        public static string AccountTone(AccountType type) => type switch
        {
            AccountType.Bank => "tone-blue",
            AccountType.MobileMoney => "tone-amber",
            AccountType.Cash => "tone-green",
            AccountType.Savings => "tone-teal",
            AccountType.Investment => "tone-teal",
            _ => "tone-slate"
        };

        public static string TypeIcon(TransactionType type) => type switch
        {
            TransactionType.Income => "arrow-down-left",
            TransactionType.Expense => "arrow-up-right",
            TransactionType.Transfer => "arrow-left-right",
            TransactionType.DebtIn => "box-arrow-in-down-left",
            _ => "box-arrow-up-right"
        };

        public static string TypeTone(TransactionType type) => type switch
        {
            TransactionType.Income or TransactionType.DebtIn => "tone-green",
            TransactionType.Expense or TransactionType.DebtOut => "tone-rose",
            _ => "tone-blue"
        };

        /// <summary>
        /// How a row changes the balance of the account it is seen from:
        /// positive for money arriving, negative for money leaving.
        /// Seen from nowhere in particular, a transfer shows what left.
        /// </summary>
        public static (decimal Amount, Currency Currency) Effect(Transaction t, int? accountId = null)
        {
            if (t.Type == TransactionType.Transfer)
            {
                if (accountId != null && t.ToAccountId == accountId && t.AccountId != accountId)
                    return (t.ToAmount ?? t.Amount, t.ToAccount?.Currency ?? t.Currency);

                return (accountId == null ? t.Amount : -t.Amount, t.Currency);
            }

            return t.Type is TransactionType.Income or TransactionType.DebtIn
                ? (t.Amount, t.Currency)
                : (-t.Amount, t.Currency);
        }

        public static string AmountClass(Transaction t, decimal effect) =>
            t.Type == TransactionType.Transfer
                ? "move"
                : effect >= 0 ? "in" : "out";
    }
}
