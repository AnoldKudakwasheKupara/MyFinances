namespace MyFinances.Services
{
    public class ServiceResult
    {
        public bool Found { get; private init; } = true;

        public bool Succeeded { get; private init; }

        public string? Message { get; private init; }

        public static ServiceResult Missing() =>
            new() { Found = false, Succeeded = false };

        public static ServiceResult Ok(string? message = null) =>
            new() { Succeeded = true, Message = message };

        public static ServiceResult Refused(string message) =>
            new() { Succeeded = false, Message = message };
    }
}
