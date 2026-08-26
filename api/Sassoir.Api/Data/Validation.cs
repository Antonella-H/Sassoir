using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sassoir.Api.Models;

namespace Sassoir.Api.Data
{
    public static partial class AdminEventValidator
    {
        public static Dictionary<string, string[]> Validate(AdminEventUpsertRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                errors["name"] = ["Event name is required."];
            }

            if (string.IsNullOrWhiteSpace(request.Slug))
            {
                errors["slug"] = ["Slug is required."];
            }
            else if (!SlugRegex().IsMatch(request.Slug))
            {
                errors["slug"] = ["Use lowercase letters, numbers, and hyphens only."];
            }

            if (!ColorIsValid(request.PrimaryColor)) errors["primaryColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.SecondaryColor)) errors["secondaryColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.BackgroundColor)) errors["backgroundColor"] = ["Use a valid hex color."];
            if (!ColorIsValid(request.TextColor)) errors["textColor"] = ["Use a valid hex color."];

            return errors;
        }

        private static bool ColorIsValid(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || HexColorRegex().IsMatch(value);
        }

        [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        private static partial Regex SlugRegex();

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private static partial Regex HexColorRegex();
    }

    public static class SearchNormalizer
    {
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var withoutAccents = new string(value
                .Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                .ToArray());

            return withoutAccents
                .Replace("\u0623", "\u0627")
                .Replace("\u0625", "\u0627")
                .Replace("\u0622", "\u0627")
                .Replace("\u0671", "\u0627")
                .Replace("\u0649", "\u064a")
                .Replace("\u0624", "\u0648")
                .Replace("\u0626", "\u064a")
                .Replace("\u0629", "\u0647")
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Aggregate(string.Empty, (current, part) => current.Length == 0 ? part : $"{current} {part}");
        }

        public static int Rank(Guest guest, string normalizedQuery)
        {
            var displayName = Normalize(guest.DisplayName);
            var aliases = guest.SearchAliases.Select(Normalize).ToArray();

            if (displayName == normalizedQuery) return 1;
            if (aliases.Contains(normalizedQuery)) return 2;
            if (displayName.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 3;
            if (aliases.Any(alias => alias.StartsWith(normalizedQuery, StringComparison.Ordinal))) return 4;
            if (displayName.Contains(normalizedQuery, StringComparison.Ordinal)) return 5;
            if (aliases.Any(alias => alias.Contains(normalizedQuery, StringComparison.Ordinal))) return 6;

            return 99;
        }
    }
}
