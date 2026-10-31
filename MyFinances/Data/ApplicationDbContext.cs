using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyFinances.Entities;

namespace MyFinances.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
    {
        private readonly CurrentUser _currentUser;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            CurrentUser currentUser)
            : base(options)
        {
            _currentUser = currentUser;
        }

        /// <summary>
        /// Read by the query filters each time a query runs, so every
        /// owned table only ever shows the signed-in person's rows.
        /// </summary>
        public int? CurrentUserId => _currentUser.UserId;

        public DbSet<UserSettings> UserSettings => Set<UserSettings>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
        public DbSet<Transaction> Transactions => Set<Transaction>();
        public DbSet<Budget> Budgets => Set<Budget>();
        public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
        public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
        public DbSet<Debt> Debts => Set<Debt>();
        public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
        public DbSet<Bill> Bills => Set<Bill>();
        public DbSet<BillPayment> BillPayments => Set<BillPayment>();
        public DbSet<Holding> Holdings => Set<Holding>();
        public DbSet<HoldingValuation> HoldingValuations => Set<HoldingValuation>();

        protected override void ConfigureConventions(
            ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
            configurationBuilder.Properties<Currency>().HaveConversion<string>().HaveMaxLength(3);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(e =>
            {
                e.Property(u => u.FullName).HasMaxLength(100);
            });

            builder.Entity<UserSettings>(e =>
            {
                e.HasKey(s => s.UserId);
                e.HasOne(s => s.User)
                    .WithOne()
                    .HasForeignKey<UserSettings>(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.Property(s => s.DateFormat).HasMaxLength(20);
                e.Property(s => s.DefaultDashboardPeriod).HasConversion<string>().HasMaxLength(20);
                e.Property(s => s.Theme).HasConversion<string>().HasMaxLength(10);
            });

            builder.Entity<Account>(e =>
            {
                e.Property(a => a.Name).HasMaxLength(80);
                e.Property(a => a.Description).HasMaxLength(500);
                e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
                e.HasOne(a => a.User)
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(a => new { a.UserId, a.Name });
            });

            builder.Entity<Category>(e =>
            {
                e.Property(c => c.Name).HasMaxLength(60);
                e.Property(c => c.Icon).HasMaxLength(40);
                e.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
                e.HasIndex(c => new { c.UserId, c.Kind, c.Name }).IsUnique();
                Owned(e);
            });

            builder.Entity<ExchangeRate>(e =>
            {
                e.Property(r => r.Rate).HasPrecision(18, 8);
                e.HasIndex(r => new { r.UserId, r.FromCurrency, r.ToCurrency, r.EffectiveDate }).IsUnique();
                Owned(e);
            });

            builder.Entity<Transaction>(e =>
            {
                e.Property(t => t.Description).HasMaxLength(200);
                e.Property(t => t.Notes).HasMaxLength(1000);
                e.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
                e.Property(t => t.TransferRate).HasPrecision(18, 8);
                e.Property(t => t.ReportingRate).HasPrecision(18, 8);
                e.Property(t => t.ReceiptPath).HasMaxLength(100);
                e.Property(t => t.ReceiptFileName).HasMaxLength(255);
                e.Property(t => t.ReceiptContentType).HasMaxLength(100);

                // History must never lose its account or category; the
                // services refuse to delete one that is in use. NO ACTION
                // rather than RESTRICT so closing a whole account, which
                // removes everything in one statement, is still allowed.
                e.HasOne(t => t.Account)
                    .WithMany()
                    .HasForeignKey(t => t.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
                e.HasOne(t => t.ToAccount)
                    .WithMany()
                    .HasForeignKey(t => t.ToAccountId)
                    .OnDelete(DeleteBehavior.NoAction);
                e.HasOne(t => t.Category)
                    .WithMany()
                    .HasForeignKey(t => t.CategoryId)
                    .OnDelete(DeleteBehavior.NoAction);

                // Rows another module wrote go when that record goes.
                e.HasOne(t => t.BillPayment)
                    .WithMany(p => p.Transactions)
                    .HasForeignKey(t => t.BillPaymentId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(t => t.Debt)
                    .WithMany(d => d.Transactions)
                    .HasForeignKey(t => t.DebtId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(t => t.DebtPayment)
                    .WithMany(p => p.Transactions)
                    .HasForeignKey(t => t.DebtPaymentId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(t => t.GoalContribution)
                    .WithMany(c => c.Transactions)
                    .HasForeignKey(t => t.GoalContributionId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(t => new { t.UserId, t.Date });
                e.HasIndex(t => new { t.UserId, t.AccountId });
                e.HasIndex(t => new { t.UserId, t.CategoryId });
                Owned(e);
            });

            builder.Entity<Budget>(e =>
            {
                e.Property(b => b.Name).HasMaxLength(80);
                e.Property(b => b.Notes).HasMaxLength(500);
                e.Property(b => b.Period).HasConversion<string>().HasMaxLength(10);
                e.HasOne(b => b.Category)
                    .WithMany()
                    .HasForeignKey(b => b.CategoryId)
                    .OnDelete(DeleteBehavior.NoAction);
                Owned(e);
            });

            builder.Entity<SavingsGoal>(e =>
            {
                e.Property(g => g.Name).HasMaxLength(80);
                e.Property(g => g.Description).HasMaxLength(500);
                e.Property(g => g.Icon).HasMaxLength(40);
                e.Property(g => g.Status).HasConversion<string>().HasMaxLength(10);
                e.HasOne(g => g.Account)
                    .WithMany()
                    .HasForeignKey(g => g.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
                Owned(e);
            });

            builder.Entity<GoalContribution>(e =>
            {
                e.Property(c => c.Notes).HasMaxLength(500);
                e.HasOne(c => c.Goal)
                    .WithMany(g => g.Contributions)
                    .HasForeignKey(c => c.GoalId)
                    .OnDelete(DeleteBehavior.Cascade);
                Owned(e);
            });

            builder.Entity<Debt>(e =>
            {
                e.Property(d => d.Description).HasMaxLength(200);
                e.Property(d => d.Counterparty).HasMaxLength(100);
                e.Property(d => d.Notes).HasMaxLength(1000);
                e.Property(d => d.InterestRate).HasPrecision(7, 4);
                e.Property(d => d.Direction).HasConversion<string>().HasMaxLength(10);
                e.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
                e.Property(d => d.Status).HasConversion<string>().HasMaxLength(12);
                e.Property(d => d.RepaymentFrequency).HasConversion<string>().HasMaxLength(10);
                Owned(e);
            });

            builder.Entity<DebtPayment>(e =>
            {
                e.Property(p => p.Notes).HasMaxLength(500);
                e.HasOne(p => p.Debt)
                    .WithMany(d => d.Payments)
                    .HasForeignKey(p => p.DebtId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(p => p.Account)
                    .WithMany()
                    .HasForeignKey(p => p.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
                Owned(e);
            });

            builder.Entity<Bill>(e =>
            {
                e.Property(b => b.Name).HasMaxLength(80);
                e.Property(b => b.Notes).HasMaxLength(500);
                e.Property(b => b.Type).HasConversion<string>().HasMaxLength(20);
                e.Property(b => b.Frequency).HasConversion<string>().HasMaxLength(10);
                e.HasOne(b => b.Category)
                    .WithMany()
                    .HasForeignKey(b => b.CategoryId)
                    .OnDelete(DeleteBehavior.NoAction);
                e.HasOne(b => b.Account)
                    .WithMany()
                    .HasForeignKey(b => b.AccountId)
                    .OnDelete(DeleteBehavior.NoAction);
                e.HasIndex(b => new { b.UserId, b.NextDueDate });
                Owned(e);
            });

            builder.Entity<BillPayment>(e =>
            {
                e.Property(p => p.Notes).HasMaxLength(500);
                e.HasOne(p => p.Bill)
                    .WithMany(b => b.Payments)
                    .HasForeignKey(p => p.BillId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(p => new { p.BillId, p.DueDate }).IsUnique();
                Owned(e);
            });

            builder.Entity<Holding>(e =>
            {
                e.Property(h => h.Name).HasMaxLength(100);
                e.Property(h => h.Notes).HasMaxLength(1000);
                e.Property(h => h.Kind).HasConversion<string>().HasMaxLength(10);
                e.Property(h => h.Category).HasConversion<string>().HasMaxLength(20);
                Owned(e);
            });

            builder.Entity<HoldingValuation>(e =>
            {
                e.Property(v => v.Notes).HasMaxLength(500);
                e.HasOne(v => v.Holding)
                    .WithMany(h => h.Valuations)
                    .HasForeignKey(v => v.HoldingId)
                    .OnDelete(DeleteBehavior.Cascade);
                Owned(e);
            });

            builder.Entity<UserSettings>()
                .HasQueryFilter(s => s.UserId == CurrentUserId);
            builder.Entity<Account>()
                .HasQueryFilter(a => a.UserId == CurrentUserId);
        }

        /// <summary>
        /// Ties a table to its owner and hides everybody else's rows.
        /// </summary>
        private void Owned<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
            where T : class, IUserOwned
        {
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampAndGuard();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            StampAndGuard();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Fills in the owner of new rows and refuses to write a row for
        /// anyone but the current user, so a bug elsewhere cannot put
        /// one person's money in another's books.
        /// </summary>
        private void StampAndGuard()
        {
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                if (entry.Entity is IUserOwned owned)
                {
                    var userId = CurrentUserId ?? throw new InvalidOperationException(
                        "Financial records can only be changed for a signed-in user.");

                    if (entry.State == EntityState.Added && owned.UserId == 0)
                        owned.UserId = userId;

                    if (owned.UserId != userId)
                    {
                        throw new InvalidOperationException(
                            "A record belonging to another user cannot be changed.");
                    }
                }

                if (entry.State == EntityState.Deleted)
                    continue;

                var created = entry.Metadata.FindProperty("CreatedAt");
                var updated = entry.Metadata.FindProperty("UpdatedAt");

                if (entry.State == EntityState.Added && created != null)
                {
                    entry.Property("CreatedAt").CurrentValue = now;
                }

                if (updated != null)
                    entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }
}
