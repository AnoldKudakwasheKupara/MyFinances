using System.Globalization;
using MyFinances.Entities;

namespace MyFinances.Services
{
    /// <summary>
    /// How amounts are written: the currency's own sign where it has a
    /// short one, its code where it does not, always two decimals.
    /// </summary>
    public static class Money
    {
        private static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;

        public static string Symbol(Currency currency) => currency switch
        {
            Currency.USD => "$",
            Currency.ZAR => "R",
            Currency.GBP => "£",
            Currency.EUR => "€",
            _ => currency + " "
        };

        public static string Name(Currency currency) => currency switch
        {
            Currency.USD => "US dollar",
            Currency.ZWG => "Zimbabwe Gold",
            Currency.ZAR => "South African rand",
            Currency.GBP => "British pound",
            Currency.EUR => "Euro",
            _ => currency.ToString()
        };

        public static string Format(decimal amount, Currency currency)
        {
            var sign = amount < 0 ? "-" : string.Empty;

            return $"{sign}{Symbol(currency)}{Math.Abs(amount).ToString("#,##0.00", Numbers)}";
        }

        /// <summary>With an explicit + or −, for amounts that move a balance.</summary>
        public static string Signed(decimal amount, Currency currency) =>
            amount > 0
                ? "+" + Format(amount, currency)
                : amount < 0
                    ? "−" + Format(-amount, currency)
                    : Format(0, currency);

        /// <summary>Short form for chart axes and tight spaces: $12.5k.</summary>
        public static string Compact(decimal amount, Currency currency)
        {
            var abs = Math.Abs(amount);
            var sign = amount < 0 ? "-" : string.Empty;

            var text = abs >= 1_000_000
                ? (abs / 1_000_000).ToString("0.#", Numbers) + "m"
                : abs >= 10_000
                    ? (abs / 1_000).ToString("0.#", Numbers) + "k"
                    : abs.ToString("#,##0", Numbers);

            return sign + Symbol(currency) + text;
        }

        public static decimal Round(decimal amount) =>
            Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }
}
