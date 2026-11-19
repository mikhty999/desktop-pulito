using System.Globalization;
using System.Text;

namespace DesktopPulito.Domain;

/// <summary>Normalizzazione per la ricerca: minuscolo, senza accenti.</summary>
public static class SearchText
{
    public static string Normalize(string s)
    {
        string f = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new(f.Length);
        foreach (char c in f)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}

/// <summary>
/// Classifica gli elementi per una ricerca: inizio del nome &gt; inizio di una parola &gt; contenuto &gt; lettere in ordine.
/// A parità di punteggio vincono quelli aperti di recente, poi l'ordine alfabetico.
/// </summary>
public static class DesktopSearch
{
    public static double Score(DesktopItem item, string normalizedQuery)
    {
        string q = normalizedQuery;
        if (q.Length == 0) return 1;
        string k = item.Key;
        if (k.StartsWith(q, StringComparison.Ordinal)) return 4;
        int i = k.IndexOf(q, StringComparison.Ordinal);
        if (i > 0) return k[i - 1] is ' ' or '-' or '_' or '.' ? 3 : 2;
        int j = 0;
        foreach (char c in k) if (j < q.Length && c == q[j]) j++;
        return j == q.Length ? 0.5 : 0;
    }

    public static IReadOnlyList<DesktopItem> Rank(IEnumerable<DesktopItem> items, string query, RecentItems recent)
    {
        string q = SearchText.Normalize(query.Trim());
        return items
            .Select(it => (it, s: Score(it, q), r: recent.RankOf(it.Path)))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s)
            .ThenBy(x => x.r < 0 ? int.MaxValue : x.r)
            .ThenBy(x => x.it.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.it)
            .ToList();
    }
}

