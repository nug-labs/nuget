using NugLabs.Models;

namespace NugLabs.Search;

/// <summary>
/// Search helpers for exact and partial local strain lookups.
/// </summary>
public static class StrainSearch
{
    /// <summary>
    /// Performs an exact, case-insensitive lookup against strain names and aliases.
    /// </summary>
    /// <param name="strains">Dataset to search.</param>
    /// <param name="name">Strain name or alias to resolve.</param>
    /// <returns>The matching strain, or <c>null</c> when none exists.</returns>
    public static Strain? GetStrain(IEnumerable<Strain> strains, string name)
    {
        var query = Normalize(name);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        return strains.FirstOrDefault(strain =>
            Normalize(strain.Name) == query ||
            strain.Akas.Any(alias => Normalize(alias) == query));
    }

    /// <summary>
    /// Performs a case-insensitive partial search against strain names and aliases.
    /// </summary>
    /// <param name="strains">Dataset to search.</param>
    /// <param name="query">Partial query to match.</param>
    /// <returns>All matching strains.</returns>
    public static IReadOnlyList<Strain> SearchStrains(IEnumerable<Strain> strains, string query)
    {
        var normalized = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return strains.ToList();
        }

        return strains
            .Where(strain =>
                Normalize(strain.Name).Contains(normalized, StringComparison.Ordinal) ||
                strain.Akas.Any(alias => Normalize(alias).Contains(normalized, StringComparison.Ordinal)))
            .ToList();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
