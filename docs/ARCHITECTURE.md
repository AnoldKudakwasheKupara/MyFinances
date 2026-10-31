# MyFinances — architecture and plan

MyFinances is a personal finance web application: accounts, income,
expenses, transfers, budgets, savings goals, debts, bills, assets and
reports, in one place.

It follows the approach of My Intellego (the `HrPortal` repository):
one ASP.NET Core MVC project on .NET 10, ASP.NET Identity, EF Core,
PostgreSQL, a Docker image hosted on Render from a `render.yaml`
blueprint, user secrets for local configuration, and migrations applied
on startup.

## Deviations from the original brief

| Brief | MyFinances | Why |
| --- | --- | --- |
| Microsoft SQL Server | PostgreSQL 16+ (Npgsql) | Same database setup and hosting as HrPortal: Render's managed PostgreSQL. EF Core keeps the code provider-neutral. |
| Excel export | CSV (opens in Excel) and PDF | CSV covers spreadsheets without another dependency; PDF uses QuestPDF as HrPortal does. |
| Email reminders | In-app reminders; email digest behind `Email:Enabled` | Email is off until a mail account is configured, as in HrPortal. |

## Solution layout

```
MyFinances.slnx
Dockerfile, render.yaml           Render blueprint (web service + PostgreSQL + disk)
docs/                             this document
MyFinances/                       the web application
  Controllers/                    thin MVC controllers
  Data/                           ApplicationDbContext, ApplicationUser, seeding
  Entities/                       EF Core entities and enums
  Migrations/
  Security/                       sign-in limits, ownership guard
  Services/                       all financial logic (ledger, balances, reports …)
  ViewModels/                     form and screen models
  Views/, Areas/Identity/         Razor views, restyled Identity pages
  wwwroot/                        css, js, vendored libraries (Bootstrap, Bootstrap Icons, Chart.js, jQuery Validation)
MyFinances.Tests/                 xUnit integration tests against a throwaway PostgreSQL database
```

Controllers never calculate money. They call services
(`LedgerService`, `BalanceService`, `CurrencyService`,
`BudgetService`, `GoalService`, `DebtService`, `BillService`,
`NetWorthService`, `DashboardService`, `ReportService`) which own the
rules below and are covered by tests.

## Ownership and isolation

Every financial row carries `UserId`. Two independent guards keep users apart:

1. **Global query filters.** `ApplicationDbContext` knows the signed-in
   user's id and filters every owned entity by it, so a forgotten `Where`
   cannot leak another user's data. Background work and seeding opt out
   explicitly with `IgnoreQueryFilters()`.
2. **Write guard.** `SaveChanges` refuses to insert or update an owned
   row whose `UserId` is not the signed-in user's.

Receipts are served only through a controller that loads the
transaction through the filtered context first.

## Database schema

Money is `numeric(18,2)`; exchange rates are `numeric(18,8)`. Audit
timestamps (`CreatedAt`, `UpdatedAt`) are UTC. Dates the user picks
(transaction date, due date) are stored as dates without a time.
Enums are stored as text so the database stays readable.

| Table | Key columns | Notes |
| --- | --- | --- |
| `AspNetUsers` | `FullName` | Identity, `int` keys |
| `UserSettings` | `UserId` (PK/FK), `ReportingCurrency`, `DateFormat`, `FinancialYearStartMonth`, `DefaultDashboardPeriod`, `BudgetWarningPercent`, `BudgetLimitPercent`, `Theme`, `BillReminderDays`, `EmailReminders` | one row per user |
| `Accounts` | `Name`, `Type`, `Currency`, `OpeningBalance`, `OpeningDate`, `Description`, `IsArchived` | balance is never stored |
| `Categories` | `Name`, `Kind` (Income/Expense), `Icon`, `IsActive` | defaults seeded per user at sign-up; unique (`UserId`, `Kind`, `Name`) |
| `ExchangeRates` | `FromCurrency`, `ToCurrency`, `Rate`, `EffectiveDate` | entered by hand; unique per pair and day |
| `Transactions` | `Date`, `Type`, `Description`, `Amount`, `Currency`, `AccountId`, `ToAccountId`, `ToAmount`, `TransferRate`, `CategoryId`, `Notes`, receipt columns, `ReportingCurrency`, `ReportingRate`, `ReportingAmount`, `ReportingRateDate`, `Source`, `CreatedAt`, `UpdatedAt` | the ledger |
| `Budgets` | `Name`, `CategoryId`, `Period`, `Amount`, `Currency`, `StartDate`, `EndDate` | |
| `SavingsGoals` | `Name`, `Description`, `TargetAmount`, `Currency`, `TargetDate`, `AccountId`, `Status` | |
| `GoalContributions` | `GoalId`, `Date`, `Amount` (negative = withdrawal), `TransactionId` | |
| `Debts` | `Direction` (IOwe/OwedToMe), `Description`, `Counterparty`, `Type`, `OriginalAmount`, `Currency`, `StartDate`, `DueDate`, `InterestRate`, `RepaymentFrequency`, `RepaymentAmount`, `Notes`, `Status` | outstanding balance is calculated |
| `DebtPayments` | `DebtId`, `Date`, `Principal`, `Interest`, `AccountId`, `Notes` | ledger rows point back to it |
| `Bills` | `Name`, `Type`, `Amount`, `Currency`, `CategoryId`, `AccountId`, `Frequency`, `NextDueDate`, `ReminderDays`, `IsActive`, `Notes` | |
| `BillPayments` | `BillId`, `DueDate`, `PaidDate`, `Amount`, `TransactionId` | unique (`BillId`, `DueDate`) — an occurrence can be paid once |
| `Assets` | `Kind` (Asset/Liability), `Name`, `Category`, `Currency`, `AcquiredOn`, `Notes` | |
| `AssetValuations` | `AssetId`, `Date`, `Value` | current value = latest valuation |

Foreign keys from the ledger to accounts and categories are `NO ACTION`
and the services refuse the delete: an account or category with history
cannot be deleted, only archived or deactivated. (`RESTRICT` would also
block closing a whole user account, which removes everything in one
statement.) Child rows (contributions, payments, valuations) cascade
from their parent, and their linked ledger rows are removed in the same
database transaction by the owning service.

Indexes: (`UserId`, `Date`) and (`UserId`, `AccountId`) and
(`UserId`, `CategoryId`) on `Transactions`; (`UserId`, `NextDueDate`) on
`Bills`; the unique keys above.

## Financial rules

**Account balance** = opening balance
\+ income \+ money received against debts \+ transfers in (`ToAmount`)
− expenses − money paid against debts − transfers out (`Amount`),
counting only ledger rows dated on or after the opening date. It is
always calculated, never stored, so editing or deleting a transaction
cannot leave a stale balance.

**Ledger types.** Users record `Income`, `Expense` and `Transfer`. Two
system types, `DebtIn` and `DebtOut`, are written only by the debt module
for principal: borrowing or being repaid brings money in; lending or
repaying takes it out. They move account balances but are neither income
nor expense.

**Transfers** are one row with a source and a destination account and are
never income or expense. Same currency: `ToAmount = Amount`. Different
currencies: the user must enter the amount received (or the rate); the
transfer stores both sides and the rate. A transfer to the same account
is refused.

**Currencies.** A transaction is always in its account's currency. The
original amount and currency are never changed by conversion. When the
currency differs from the reporting currency the ledger stores a
snapshot: reporting currency, rate, rate date and converted amount, using
the latest hand-entered rate on or before the transaction date (direct or
inverse pair). If there is no rate the snapshot is left empty: the row is
shown as **unconverted** and left out of reporting-currency totals, with a
warning that names how many rows and which currency. Adding a rate offers
to fill in rows that had none. Changing the reporting currency
recalculates every snapshot from the dated rates — never from today's rate
for old transactions.

Point-in-time balances (accounts, net worth) convert at the latest rate on
or before the day being reported. Original-currency figures are always
shown beside converted totals.

**Categories.** Income needs an active income category, expense an active
expense category, transfers none. Deactivated categories keep their
history but are not offered for new entries.

**Budgets.** Spending = expenses in the budget's category inside the
period window (month, quarter or year from the start date, until the end
date). Expenses in another currency are converted with the rate on their
date; any that cannot be converted are listed. Status: under the warning
threshold (default 80%), warning, or over the limit (default 100%).

**Savings goals.** Saved = sum of contributions minus withdrawals. A
contribution is never income. If the money moves between accounts (from
an everyday account into the goal's savings account) the goal records a
transfer; otherwise it only earmarks money that is already counted.

**Debts.** Outstanding = original amount − principal repaid. A payment
splits principal from interest: interest is an expense (money I owe) or
income (owed to me) in the ledger; principal is `DebtIn`/`DebtOut`. A
payment can be recorded without touching an account when the money is
already in the ledger, so nothing is counted twice. Ledger rows created by
the debt, bill or goal modules are edited and deleted from that module.

**Bills.** Marking a bill paid records the occurrence at its due date
(unique, so it cannot be paid twice), advances the next due date by the
frequency, and optionally writes the expense to the ledger. Undoing the
latest payment removes it and its expense and restores the due date.

**Net worth** = account balances + assets + money owed to me
− money I owe − liabilities, all converted to the reporting currency.
Accounts and debts are counted from their own modules; the Assets &
Liabilities module is for everything else (a car, a house, a store
account not tracked as a debt), and its screens say so.

**Periods.** Income and expenses for a period use transaction dates. The
financial year starts in the month chosen in settings.

## Edge cases handled

- Editing a transaction's account, amount, type or date: balances follow
  automatically because they are derived.
- Changing an account's currency after it has transactions is refused.
- Archiving keeps history; deleting is allowed only for an account with
  no ledger rows, goals, bills or debt payments.
- Dates before an account's opening date are refused.
- Negative or zero amounts are refused; direction comes from the type.
- A bill paid early or late still settles its own due date.
- Removing an exchange rate does not alter snapshots already taken.
- Overpaying a debt is refused (principal above the outstanding balance).

## Principal screens

The shell is HrPortal's: a dark green sidebar, a light topbar with the
page title and breadcrumb, search, notifications, profile and a **Quick
add** button, rounded white cards on a warm off-white page, Montserrat,
and the same tokens — retuned to navy text with teal/green accents, plus
a dark theme.

- **Dashboard** — period picker; four summary cards with change versus the
  previous period; income vs expenses chart (monthly, quarterly, yearly);
  spending doughnut; account balances; recent transactions; budgets;
  goals; upcoming bills; period summary; quick actions; an empty state
  that walks a new user through adding an account and a first transaction.
- **Transactions** — filter bar (search, dates, account, category, type),
  sortable paged table, CSV export; a form whose fields change with the
  type.
- **Accounts** — cards per account with balance in its currency, detail
  page with history.
- **Budgets, Savings Goals, Debts & Loans, Bills** — list with progress,
  detail with history, record-payment dialogs.
- **Assets & Liabilities** — list with current value and a valuation
  history.
- **Reports** — one page per report, shared date filter, CSV and PDF.
- **Settings** — preferences, categories, exchange rates, data export.

## Implementation plan

Each step is its own commit, pushed as it lands.

1. **Foundation** — project, PostgreSQL, Identity with open registration,
   entities, first migration, layout and navigation, dashboard shell.
2. **Core** — accounts, categories, exchange rates, transactions (income,
   expense, transfer), search and filters, balances.
3. **Planning** — budgets, savings goals, debts, bills, in-app reminders.
4. **Analytics** — dashboard figures and charts, reports, exports,
   multi-currency summaries.
5. **Hardening and deployment** — tests, error handling, settings and data
   export, Docker and Render blueprint, README.
