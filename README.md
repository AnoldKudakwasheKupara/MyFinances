# MyFinances

A personal finance web app: accounts, income, expenses, transfers,
budgets, savings goals, debts, bills, assets and reports, in every
currency you use. ASP.NET Core MVC on .NET 10 with ASP.NET Identity and
EF Core on Microsoft SQL Server, hosted on MonsterASP.NET.

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

You need the .NET 10 SDK and SQL Server. SQL Server Express LocalDB,
which comes with Visual Studio, is enough: `appsettings.json` already
points at it with Windows sign-in, so there is no password to set.

Run it:

```bash
dotnet run --project MyFinances/MyFinances.csproj --launch-profile http
```

and open <http://localhost:5180>. On startup the app applies any pending
migrations, which **creates the `MyFinances` database if it does not
exist**. Choose **Create an account** to sign up; your account starts
with the default income and expense categories in the currency you
pick.

To use another SQL Server, put its connection string in user secrets,
which live outside the repository, so a password never goes in
`appsettings.json`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=MyFinances;User Id=myfinances;Password=choose-a-password;TrustServerCertificate=true" --project MyFinances/MyFinances.csproj
```

### If the database will not open

The startup log names the database and server it tried. Check that SQL
Server (or LocalDB: `sqllocaldb info`) is running and that the login
can reach it and, locally, create databases.
`ConnectionStrings__DefaultConnection` as an environment variable works
in place of user secrets.

## Tests

```bash
dotnet test
```

The tests create a throwaway database next to yours (named
`myfinances_test_…`), apply the migrations, run, and drop it. They use
the same connection as the app, or `MYFINANCES_TEST_DB` if set. Your
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

## Hosting on MonsterASP.NET

MonsterASP.NET runs the site under IIS on Windows with its own SQL
Server database. The free plan is enough: one site, one database,
256 MB of memory, a `*.runasp.net` address, no card needed.

One-off setup, in the [Control Panel](https://admin.monsterasp.net):

1. **Create the website** and an **MSSQL database**. Open the database
   and copy its connection string (the *local* one: the site and the
   database talk inside MonsterASP's network).
2. On the website, under **Scripting → Environment variables**, add
   `ConnectionStrings__DefaultConnection` with that connection string,
   then restart the site. It overrides the LocalDB default in
   `appsettings.json` and never goes in the repository.
3. On the website's **Deploy** page, switch **WebDeploy** to *Enabled*.
   From the *Visual Studio access* card, add four secrets to the GitHub
   repository (*Settings → Secrets and variables → Actions*):

   | Secret | Value |
   | --- | --- |
   | `WEBSITE_NAME` | the site name, `siteXXXX` |
   | `SERVER_COMPUTER_NAME` | the server, `https://siteXXXX.siteasp.net:8172` |
   | `SERVER_USERNAME` | the login |
   | `SERVER_PASSWORD` | the Web Deploy password |

From then on [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml)
publishes every push to `master` (or run it by hand from the *Actions*
tab). The first start creates all the tables. Until the secrets exist,
the workflow only checks that the site builds.

The sign-in keys are kept in the database, so redeploys do not sign
anyone out, and the deploy skips `App_Data`, so receipts stay put. Dates
follow Harare time whatever zone the server is in.

Settings you may want, as environment variables in the same place:

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

- **Database.** Back it up from the database's page in the Control
  Panel. For your own copy, enable remote access there and use SQL
  Server Management Studio (*Tasks → Export Data-tier Application*
  makes a `.bacpac`) or `sqlpackage /Action:Export` with the *remote*
  connection string.
- **Files.** Receipts live under `App_Data/receipts` on the site;
  download that folder over FTP to keep a copy.
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
