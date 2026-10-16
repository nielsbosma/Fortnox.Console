namespace Fortnox.Console.Infrastructure;

public static class ResourcePath
{
    /// <summary>
    /// Escapes an id for a URL path. A slash is kept as a separator, because some ids are
    /// compound - a voucher is <c>A/12</c>, i.e. <c>vouchers/A/12</c>.
    /// </summary>
    public static string Id(string id) =>
        string.Join("/", id.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    /// <summary>Appends <c>key=value</c> pairs, skipping empty values.</summary>
    public static string WithQuery(string path, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var parts = query
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
            .ToList();

        if (parts.Count == 0) return path;
        return path + (path.Contains('?') ? "&" : "?") + string.Join("&", parts);
    }

    /// <summary>Parses repeated <c>--query key=value</c> options.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> Pairs(string[]? query)
    {
        foreach (var q in query ?? [])
        {
            var eq = q.IndexOf('=');
            if (eq <= 0) throw FortnoxException.Invalid($"--query expects key=value, got '{q}'.");
            yield return new(q[..eq].Trim(), q[(eq + 1)..]);
        }
    }

    public static void RequireYes(bool yes, string what)
    {
        if (yes) return;
        throw FortnoxException.Invalid(
            $"{what} cannot be undone, so it needs --yes.",
            "Read the resource back first, confirm it is the right one in the right account, then re-run with --yes.");
    }
}
