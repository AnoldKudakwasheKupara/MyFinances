# MyFinances

A personal finance web app: accounts, income, expenses, transfers,
budgets, savings goals, debts, bills, assets and reports, in every
currency you use. ASP.NET Core MVC on .NET 10 with ASP.NET Identity and
EF Core on PostgreSQL (Neon), hosted on Render — set up the same way as My
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
- **Accounts for anyone** — open sign-up, with a password or with
  Google, GitHub, Facebook or X; every person sees only their own
  records.

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

## Hosting: Render + Neon

Both are free and need no card:

- **[Neon](https://neon.tech)** holds the PostgreSQL database. Its free
  plan does not expire; the database pauses after five idle minutes and
  wakes on the next request in about a second. It holds 0.5 GB.
- **Render** runs the app from the [`Dockerfile`](Dockerfile), as
  described in [`render.yaml`](render.yaml). The free web service sleeps
  after 15 minutes without visits; the next visit takes about a minute.

Setting it up once:

1. In Neon, create a project in **AWS Europe (Frankfurt)**, next to
   Render's Frankfurt region. On the project dashboard choose
   **Connect**, turn **Connection pooling off** (the app pools its own
   connections and runs migrations), and copy the connection string,
   `postgresql://…?sslmode=require…`.
2. In Render choose **New → Blueprint**, pick this repository, and paste
   that string as `DATABASE_URL` when asked. Every push to `master`
   redeploys; the first start creates all the tables.

The sign-in keys are kept in the database, so restarts do not sign
anyone out. Render ends HTTPS at its proxy; the blueprint sets
`ForwardedHeaders__Enabled` so the app reads the forwarded headers, and
cookies are HTTPS-only. Dates follow Harare time whatever zone the
server is in.

There is no disk on the free web service, so **uploaded receipts are
lost** whenever it restarts or redeploys; everything else is in the
database. For receipts that last, move the service to a paid plan and
give it a disk:

```yaml
    plan: starter
    disk:
      name: app-data
      mountPath: /app/App_Data
      sizeGB: 1
```

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

### Signing in with Google, GitHub, Facebook or X

The sign-in and sign-up pages offer a button for each provider whose
keys are set, and none for the others. For each one, register an app
with the provider, give it the callback address below (your own
address, if it differs), and put its two keys under the service's
**Environment** tab:

| Provider | Where to register | Callback address | Variables |
| --- | --- | --- | --- |
| Google | [Google Cloud Console](https://console.cloud.google.com/apis/credentials) → Create credentials → OAuth client ID → Web application | `https://myfinances-rm5j.onrender.com/signin-google` | `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` |
| GitHub | [GitHub → Settings → Developer settings → OAuth Apps](https://github.com/settings/developers) | `https://myfinances-rm5j.onrender.com/signin-github` | `Authentication__GitHub__ClientId`, `Authentication__GitHub__ClientSecret` |
| Facebook | [Meta for Developers](https://developers.facebook.com/apps) → Create app → Facebook Login | `https://myfinances-rm5j.onrender.com/signin-facebook` | `Authentication__Facebook__ClientId` (App ID), `Authentication__Facebook__ClientSecret` (App secret) |
| X | [X Developer Portal](https://developer.x.com/en/portal/dashboard) → project app → User authentication settings, OAuth 2.0, type *Web App* | `https://myfinances-rm5j.onrender.com/signin-twitter` | `Authentication__X__ClientId`, `Authentication__X__ClientSecret` (the OAuth 2.0 client keys) |

Saving the variables restarts the service and the button appears.
Locally, set the same keys with `dotnet user-secrets set
"Authentication:Google:ClientId" "…"` and use `http://localhost:5180`
in the callback address.

The first time someone signs in with a provider, they confirm their
name, email and main currency and get an account with no password. X
does not share email addresses, so people coming from X type theirs.
An email that already has an account is not joined to the provider
automatically, since that would hand the account to whoever controls
the provider login: its owner signs in with their password and links
the provider under *My profile → External logins*.

## Backups and recovery

- **Database.** Neon can restore the database to an earlier moment
  within its history window (short on the free plan) from the project's
  **Restore** page. For
  your own copy, take one from any machine with the PostgreSQL client:

  ```bash
  pg_dump --format=custom --no-owner --file=myfinances.dump "<Neon connection string>"
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
