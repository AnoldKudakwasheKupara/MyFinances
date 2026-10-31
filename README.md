# MyFinances

A personal finance web app: accounts, income, expenses, transfers,
budgets, savings goals, debts, bills, assets and reports, in every
currency you use. ASP.NET Core MVC on .NET 10 with ASP.NET Identity and
EF Core on PostgreSQL, hosted on Render — set up the same way as My
Intellego (`HrPortal`).

The architecture, schema and money rules are in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## What it does

- **Dashboard** — available balance, income, expenses and net worth with
  change on the previous period; income vs expenses by month, quarter
  or year; a spending doughnut; balances; recent transactions; budgets;
  goals; bills due; a period summary; quick actions.
- **Transactions** — income, expenses and transfers (including between
  currencies) with receipts, search, filters, sorting, paging and CSV
  export.
- **Accounts** — bank, mobile money, cash, savings, investment. Balances
  are always worked out from the opening balance and the ledger.
- **Budgets, savings goals, debts & loans, bills** — with warnings,
  progress, principal/interest split, pay-once bills and reminders.
- **Assets & liabilities** — valuations over time and net worth.
- **Reports** — ten reports, each exportable to CSV and PDF with exactly
  the figures shown.
- **Settings** — main currency, date format, financial year, budget
  thresholds, theme, reminders, categories, exchange rates and a full
  data download.
- **Accounts for anyone** — open sign-up; every person sees only their
  own records.

## First run on a new machine

You need the .NET 10 SDK and PostgreSQL 16 or later, with a login that
may create databases. Create one once, as the `postgres` superuser:

```sql
CREATE ROLE myfinances_dev LOGIN CREATEDB PASSWORD 'choose-a-password';
```

(On the original development machine the existing `hrportal_dev` login
is used instead; any login with `CREATEDB` works.)

Put the connection string in user secrets, which live outside the
repository — a password must never go in `appsettings.json`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=MyFinances;Username=myfinances_dev;Password=choose-a-password" --project MyFinances/MyFinances.csproj
```

Run it:

```bash
dotnet run --project MyFinances/MyFinances.csproj --launch-profile http
```

and open <http://localhost:5180>. On startup the app applies any pending
migrations, which **creates the `MyFinances` database if it does not
exist**. Choose **Create an account** to sign up; your account starts
with the default income and expense categories in the currency you
pick.

### If the database will not open

The startup log names the database and host it tried. Check that
PostgreSQL is running, that the login exists and has `CREATEDB`, and
that the password matches. `ConnectionStrings__DefaultConnection` as an
environment variable works in place of user secrets, and so does a
`postgres://user:password@host:5432/database` address in `DATABASE_URL`.

## Tests

```bash
dotnet test
```

The tests create a throwaway database next to yours (named
`myfinances_test_…`), apply the migrations, run, and drop it. They use
the same user secret as the app, or `MYFINANCES_TEST_DB` if set. Your
own data is never touched.

## Database changes

`dotnet-ef` is pinned in the repository's tool manifest:

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project MyFinances/MyFinances.csproj
```

The migration is applied automatically the next time the app starts.

## Hosting on Render

[`render.yaml`](render.yaml) describes everything: a Docker web service
in Frankfurt built from the [`Dockerfile`](Dockerfile) and a PostgreSQL
database whose address reaches the app as `DATABASE_URL`. The sign-in
keys are kept in the database, so restarts do not sign anyone out.

Both run on Render's **free** plans, which have limits:

- The web service sleeps after 15 minutes without visits; the next
  visit takes about a minute to wake it.
- There is no disk, so **uploaded receipts are lost** whenever the
  service restarts or redeploys. Everything else is in the database.
- A free database expires 30 days after it is created unless it is
  upgraded, and has no backups.

To move to paid plans, set `plan` to e.g. `starter` on the service and
`basic-256mb` on the database, and give the service a disk:

```yaml
    disk:
      name: app-data
      mountPath: /app/App_Data
      sizeGB: 5
```

In the Render dashboard choose **New → Blueprint**, pick this
repository, and apply it. Every push to `master` redeploys. Render ends
HTTPS at its proxy; the app reads the forwarded headers and marks its
cookies HTTPS-only.

Settings you may want under the service's **Environment** tab:

| Variable | Purpose |
| --- | --- |
| `Registration__Enabled` | `false` closes sign-up to new people (default `true`) |
| `Email__Enabled` | `true` turns on email; sign-up then asks people to confirm their address |
| `Email__Delivery` | `Smtp`, `Graph` (Microsoft 365) or `File` |
| `Email__FromAddress`, `Email__FromName` | the sender |
| `Email__Host`, `Email__Port`, `Email__UserName`, `Email__Password` | for SMTP |
| `Email__TenantId`, `Email__ClientId`, `Email__ClientSecret` | for Microsoft Graph, as set up for My Intellego |
| `Email__PortalBaseUrl` | the public address, used in links in emails |

Until email is enabled, password reset links cannot be sent, accounts
need no confirmation, and reminders are shown in the app only.

## Backups and recovery

- **Database.** Render's paid PostgreSQL plans take daily backups with
  point-in-time recovery; restore from the database's **Recovery** tab.
  For your own copy, take one from any machine with the PostgreSQL client:

  ```bash
  pg_dump --format=custom --no-owner --file=myfinances.dump "<External Database URL from Render>"
  ```

  and restore into an empty database with
  `pg_restore --no-owner --dbname="<database URL>" myfinances.dump`.
- **Files.** Receipts live under `App_Data/receipts`. On the free plan
  that folder is wiped on every restart; with a disk, Render snapshots
  it daily.
- **Each person's own copy.** *Settings → Download all my data* gives a
  zip with every record as JSON, the ledger as CSV and all receipts.

## Security

- Every financial table carries its owner. Query filters hide other
  people's rows and saving a row for anyone else is refused, on top of
  the checks in each service.
- All pages need a sign-in except the sign-in pages themselves; forms
  carry antiforgery tokens; sign-in, sign-up and reset are rate limited;
  five wrong passwords lock an account for 15 minutes; idle sessions end
  after 15 minutes. Two-factor sign-in is available under *My profile*.
- Receipts are PDF, JPG, PNG or WebP up to 5 MB, checked by their first
  bytes, stored outside the web root under random names and only served
  to their owner.
- Responses carry a content security policy and refuse framing.
- No secrets are in the repository: connection strings and mail
  credentials come from user secrets or environment variables.

## Limitations

- Exchange rates are typed in by hand; there is no rate feed. Totals
  leave out amounts with no rate and say so.
- Exports are CSV (which opens in Excel) and PDF; there is no `.xlsx`.
- Bills advance by calendar months, so a bill due on the 31st moves to
  the last day of shorter months and stays there.
- A savings goal contribution can only move money between accounts in
  the goal's currency; record the exchange as a transfer first.
- Reminder emails are a once-a-day digest; there are no push or SMS
  notifications.
