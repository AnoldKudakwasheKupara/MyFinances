using System.Text.RegularExpressions;

namespace MyFinances.Services
{
    public record SavedReceipt(string? StoredName, string? ContentType, string? Error);

    /// <summary>
    /// Keeps receipt files outside the web root, under App_Data, one
    /// folder per user, named by a random id rather than anything the
    /// uploader chose. Only PDFs and common images under 5 MB are
    /// taken, and the file's first bytes must match its type.
    /// </summary>
    public partial class ReceiptStorage
    {
        public const long MaxBytes = 5 * 1024 * 1024;

        private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46]]),
                [".jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
                [".jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
                [".png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
                [".webp"] = ("image/webp", [[0x52, 0x49, 0x46, 0x46]])
            };

        public static string Accept => string.Join(",", Allowed.Keys);

        private readonly string _root;

        public ReceiptStorage(IWebHostEnvironment environment)
            : this(Path.Combine(environment.ContentRootPath, "App_Data", "receipts"))
        {
        }

        public ReceiptStorage(string root)
        {
            _root = root;
        }

        public async Task<SavedReceipt> SaveAsync(IFormFile file, int userId)
        {
            if (file.Length == 0)
                return new SavedReceipt(null, null, "The receipt file is empty.");

            if (file.Length > MaxBytes)
                return new SavedReceipt(null, null, "Receipts can be up to 5 MB.");

            var extension = Path.GetExtension(file.FileName);

            if (!Allowed.TryGetValue(extension, out var kind))
                return new SavedReceipt(null, null, "Receipts can be a PDF, JPG, PNG or WebP file.");

            var header = new byte[8];

            await using (var peek = file.OpenReadStream())
            {
                var read = await peek.ReadAsync(header);

                var matches = kind.Signatures.Any(signature =>
                    read >= signature.Length && header.AsSpan(0, signature.Length).SequenceEqual(signature));

                if (!matches)
                    return new SavedReceipt(null, null, "That file does not look like the kind its name says it is.");
            }

            var stored = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var folder = Folder(userId);

            Directory.CreateDirectory(folder);

            await using (var target = File.Create(Path.Combine(folder, stored)))
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(target);
            }

            return new SavedReceipt(stored, kind.ContentType, null);
        }

        public Stream? Open(int userId, string storedName)
        {
            var path = PathOf(userId, storedName);

            return path != null && File.Exists(path)
                ? File.OpenRead(path)
                : null;
        }

        public void Delete(int userId, string storedName)
        {
            var path = PathOf(userId, storedName);

            if (path != null && File.Exists(path))
                File.Delete(path);
        }

        /// <summary>Removes every receipt a user has, when the account is closed.</summary>
        public void DeleteAll(int userId)
        {
            var folder = Folder(userId);

            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }

        public IEnumerable<string> List(int userId)
        {
            var folder = Folder(userId);

            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()
                : [];
        }

        private string Folder(int userId) => Path.Combine(_root, userId.ToString());

        /// <summary>
        /// Only names this class made are accepted, so a stored value
        /// can never point outside the user's own folder.
        /// </summary>
        private string? PathOf(int userId, string storedName) =>
            StoredName().IsMatch(storedName)
                ? Path.Combine(Folder(userId), storedName)
                : null;

        [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|jpg|jpeg|png|webp)$")]
        private static partial Regex StoredName();
    }
}
