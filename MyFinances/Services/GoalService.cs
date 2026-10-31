using Microsoft.EntityFrameworkCore;
using MyFinances.Data;
using MyFinances.Entities;
using MyFinances.ViewModels;

namespace MyFinances.Services
{
    public record GoalProgress(SavingsGoal Goal, decimal Saved)
    {
        public decimal Remaining => Math.Max(0, Goal.TargetAmount - Saved);

        public decimal Percent => Goal.TargetAmount <= 0 ? 0 : Math.Round(Saved / Goal.TargetAmount * 100, 1);

        /// <summary>What to put away each month from now to reach the target on time.</summary>
        public decimal? MonthlyNeeded
        {
            get
            {
                if (Goal.TargetDate == null || Remaining <= 0)
                    return null;

                var today = UserPreferences.Today;
                var months = (Goal.TargetDate.Value.Year - today.Year) * 12 + Goal.TargetDate.Value.Month - today.Month;

                return months <= 0 ? Remaining : Money.Round(Remaining / months);
            }
        }
    }

    /// <summary>
    /// Savings goals and the money set aside for them. A contribution
    /// is never income. When it comes from another account into the
    /// goal's own account it is written to the ledger as a transfer;
    /// otherwise it only earmarks money already counted.
    /// </summary>
    public class GoalService
    {
        private readonly ApplicationDbContext _context;
        private readonly LedgerService _ledger;

        public GoalService(ApplicationDbContext context, LedgerService ledger)
        {
            _context = context;
            _ledger = ledger;
        }

        public async Task<List<GoalProgress>> ListAsync(bool activeOnly = false)
        {
            var goals = await _context.SavingsGoals
                .AsNoTracking()
                .Include(g => g.Account)
                .Where(g => !activeOnly || g.Status == GoalStatus.Active)
                .OrderBy(g => g.Status)
                .ThenBy(g => g.TargetDate)
                .ThenBy(g => g.Name)
                .ToListAsync();

            var saved = await _context.GoalContributions
                .GroupBy(c => c.GoalId)
                .Select(g => new { g.Key, Total = g.Sum(c => c.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Total);

            return goals.Select(g => new GoalProgress(g, saved.GetValueOrDefault(g.Id))).ToList();
        }

        public async Task<decimal> SavedAsync(int goalId) =>
            await _context.GoalContributions.Where(c => c.GoalId == goalId).SumAsync(c => (decimal?)c.Amount) ?? 0;

        public async Task<ServiceResult> SaveAsync(int? id, GoalFormViewModel form)
        {
            Account? account = null;

            if (form.AccountId != null)
            {
                account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == form.AccountId);

                if (account == null)
                    return ServiceResult.Refused("Pick one of your accounts.");

                if (account.Currency != form.Currency)
                    return ServiceResult.Refused($"{account.Name} is in {account.Currency}, so the goal must be in {account.Currency} too.");
            }

            SavingsGoal? goal;

            if (id == null)
            {
                goal = new SavingsGoal { Status = GoalStatus.Active };
                _context.SavingsGoals.Add(goal);
            }
            else
            {
                goal = await _context.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id);

                if (goal == null)
                    return ServiceResult.Missing();

                if (goal.Currency != form.Currency && await _context.GoalContributions.AnyAsync(c => c.GoalId == goal.Id))
                    return ServiceResult.Refused("The currency cannot change once money has been put towards the goal.");

                goal.Status = form.Status;
            }

            goal.Name = form.Name.Trim();
            goal.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
            goal.TargetAmount = Money.Round(form.TargetAmount);
            goal.Currency = form.Currency;
            goal.TargetDate = form.TargetDate;
            goal.AccountId = account?.Id;
            goal.Icon = GoalFormViewModel.Icons.Contains(form.Icon) ? form.Icon : "piggy-bank";

            await _context.SaveChangesAsync();

            form.Id = goal.Id;

            return ServiceResult.Ok(id == null ? $"{goal.Name} was created." : $"{goal.Name} was updated.");
        }

        public async Task<ServiceResult> ContributeAsync(int goalId, ContributionFormViewModel form)
        {
            var goal = await _context.SavingsGoals.Include(g => g.Account).FirstOrDefaultAsync(g => g.Id == goalId);

            if (goal == null)
                return ServiceResult.Missing();

            if (form.Amount <= 0)
                return ServiceResult.Refused("Enter an amount above zero.");

            var amount = Money.Round(form.Amount);
            var saved = await SavedAsync(goalId);

            if (form.Withdraw && amount > saved)
                return ServiceResult.Refused($"Only {Money.Format(saved, goal.Currency)} has been put towards this goal.");

            TransactionInput? move = null;

            if (form.OtherAccountId != null)
            {
                if (goal.AccountId == null)
                    return ServiceResult.Refused("Link the goal to the account its money is kept in before moving money into it.");

                if (form.OtherAccountId == goal.AccountId)
                    return ServiceResult.Refused("Pick a different account from the goal's own.");

                var other = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == form.OtherAccountId);

                if (other == null)
                    return ServiceResult.Refused("Pick one of your accounts.");

                if (other.Currency != goal.Currency)
                {
                    return ServiceResult.Refused(
                        $"{other.Name} is in {other.Currency} and the goal is in {goal.Currency}. " +
                        "Record the exchange as a transfer first, then add the contribution.");
                }

                move = new TransactionInput
                {
                    Type = TransactionType.Transfer,
                    Date = form.Date,
                    Amount = amount,
                    AccountId = form.Withdraw ? goal.AccountId.Value : other.Id,
                    ToAccountId = form.Withdraw ? other.Id : goal.AccountId.Value,
                    Description = (form.Withdraw ? "From goal: " : "To goal: ") + goal.Name,
                    Notes = form.Notes
                };
            }

            LedgerResult? transfer = null;

            if (move != null)
            {
                transfer = await _ledger.BuildAsync(move);

                if (!transfer.Succeeded)
                    return ServiceResult.Refused(transfer.Error!);
            }

            var contribution = new GoalContribution
            {
                GoalId = goal.Id,
                Date = form.Date,
                Amount = form.Withdraw ? -amount : amount,
                Notes = string.IsNullOrWhiteSpace(form.Notes) ? null : form.Notes.Trim()
            };

            if (transfer?.Transaction != null)
                contribution.Transactions.Add(transfer.Transaction);

            _context.GoalContributions.Add(contribution);

            var newTotal = saved + contribution.Amount;

            if (goal.Status == GoalStatus.Active && newTotal >= goal.TargetAmount)
                goal.Status = GoalStatus.Achieved;
            else if (goal.Status == GoalStatus.Achieved && newTotal < goal.TargetAmount)
                goal.Status = GoalStatus.Active;

            // The contribution and its transfer are one change.
            await _context.SaveChangesAsync();

            var message = form.Withdraw
                ? $"{Money.Format(amount, goal.Currency)} taken from {goal.Name}."
                : $"{Money.Format(amount, goal.Currency)} put towards {goal.Name}.";

            if (goal.Status == GoalStatus.Achieved && !form.Withdraw)
                message += " Goal reached!";

            return ServiceResult.Ok(message);
        }

        public async Task<ServiceResult> RemoveContributionAsync(int goalId, int contributionId)
        {
            var contribution = await _context.GoalContributions
                .Include(c => c.Transactions)
                .Include(c => c.Goal)
                .FirstOrDefaultAsync(c => c.Id == contributionId && c.GoalId == goalId);

            if (contribution == null)
                return ServiceResult.Missing();

            _context.Transactions.RemoveRange(contribution.Transactions);
            _context.GoalContributions.Remove(contribution);

            var goal = contribution.Goal!;
            var remaining = await SavedAsync(goalId) - contribution.Amount;

            if (goal.Status == GoalStatus.Achieved && remaining < goal.TargetAmount)
                goal.Status = GoalStatus.Active;

            await _context.SaveChangesAsync();

            return ServiceResult.Ok("The entry was removed" + (contribution.Transactions.Any() ? ", with its transfer." : "."));
        }

        public async Task<ServiceResult> DeleteAsync(int id)
        {
            var goal = await _context.SavingsGoals
                .Include(g => g.Contributions).ThenInclude(c => c.Transactions)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (goal == null)
                return ServiceResult.Missing();

            if (goal.Contributions.Any(c => c.Transactions.Any()))
            {
                return ServiceResult.Refused(
                    "Money was moved between accounts for this goal. Remove those entries first, or mark the goal " +
                    "cancelled to keep the history.");
            }

            _context.SavingsGoals.Remove(goal);
            await _context.SaveChangesAsync();

            return ServiceResult.Ok($"{goal.Name} was deleted.");
        }
    }
}
