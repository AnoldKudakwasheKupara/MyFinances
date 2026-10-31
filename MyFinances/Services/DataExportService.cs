using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MyFinances.Data;

namespace MyFinances.Services
{
    /// <summary>
    /// Everything a person has recorded, in one zip: the records as
    /// JSON, the ledger as CSV for a spreadsheet, and the receipt files.
    /// It is their own copy and a backup they control.
    /// </summary>
    public class DataExportService
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ApplicationDbContext _context;
        private readonly TransactionSearch _search;
        private readonly ReceiptStorage _receipts;
        private readonly CurrentUser _currentUser;

        public DataExportService(
            ApplicationDbContext context,
            TransactionSearch search,
            ReceiptStorage receipts,
            CurrentUser currentUser)
        {
            _context = context;
            _search = search;
            _receipts = receipts;
            _currentUser = currentUser;
        }

        public async Task<byte[]> ZipAsync()
        {
            var userId = _currentUser.RequiredUserId;

            var user = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FullName, u.Email, u.PhoneNumber, u.CreatedAt })
                .FirstAsync();

            var data = new
            {
                ExportedAt = DateTime.UtcNow,
                Format = "MyFinances export, version 1",
                User = user,
                Settings = await _context.UserSettings.AsNoTracking().FirstOrDefaultAsync(),
                Accounts = await _context.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(),
                Categories = await _context.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(),
                ExchangeRates = await _context.ExchangeRates.AsNoTracking().OrderBy(r => r.Id).ToListAsync(),
                Transactions = await _context.Transactions.AsNoTracking().OrderBy(t => t.Id).ToListAsync(),
                Budgets = await _context.Budgets.AsNoTracking().OrderBy(b => b.Id).ToListAsync(),
                SavingsGoals = await _context.SavingsGoals.AsNoTracking().Include(g => g.Contributions).OrderBy(g => g.Id).ToListAsync(),
                Debts = await _context.Debts.AsNoTracking().Include(d => d.Payments).OrderBy(d => d.Id).ToListAsync(),
                Bills = await _context.Bills.AsNoTracking().Include(b => b.Payments).OrderBy(b => b.Id).ToListAsync(),
                AssetsAndLiabilities = await _context.Holdings.AsNoTracking().Include(h => h.Valuations).OrderBy(h => h.Id).ToListAsync()
            };

            using var buffer = new MemoryStream();

            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var json = zip.CreateEntry("myfinances.json", CompressionLevel.Optimal);
                await using (var stream = json.Open())
                    await JsonSerializer.SerializeAsync(stream, data, Json);

                var csv = zip.CreateEntry("transactions.csv", CompressionLevel.Optimal);
                await using (var stream = csv.Open())
                    await stream.WriteAsync(await _search.CsvAsync(new TransactionFilter { Asc = true }));

                foreach (var name in _receipts.List(userId))
                {
                    await using var source = _receipts.Open(userId, name);

                    if (source == null)
                        continue;

                    var entry = zip.CreateEntry("receipts/" + name, CompressionLevel.NoCompression);
                    await using var target = entry.Open();
                    await source.CopyToAsync(target);
                }
            }

            return buffer.ToArray();
        }
    }
}
