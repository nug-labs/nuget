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
        var queryNoSpaces = NormalizeNoSpaces(name);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        return strains.FirstOrDefault(strain =>
        {
            var strainName = Normalize(strain.Name);
            if (strainName == query || NormalizeNoSpaces(strainName) == queryNoSpaces)
            {
                return true;
            }

            return strain.Akas.Any(alias =>
            {
                var normalizedAlias = Normalize(alias);
                return normalizedAlias == query || NormalizeNoSpaces(normalizedAlias) == queryNoSpaces;
            });
        });
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
        var normalizedNoSpaces = NormalizeNoSpaces(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return strains.ToList();
        }

        return strains
            .Where(strain =>
            {
                var strainName = Normalize(strain.Name);
                if (strainName.Contains(normalized, StringComparison.Ordinal) ||
                    NormalizeNoSpaces(strainName).Contains(normalizedNoSpaces, StringComparison.Ordinal))
                {
                    return true;
                }

                return strain.Akas.Any(alias =>
                {
                    var normalizedAlias = Normalize(alias);
                    return normalizedAlias.Contains(normalized, StringComparison.Ordinal) ||
                           NormalizeNoSpaces(normalizedAlias)
                               .Contains(normalizedNoSpaces, StringComparison.Ordinal);
                });
            })
            .ToList();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeNoSpaces(string value) =>
        new(Normalize(value).Where(c => !char.IsWhiteSpace(c)).ToArray());
}
