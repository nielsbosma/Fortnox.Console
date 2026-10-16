using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fortnox.Console.Infrastructure;

/// <summary>
/// Builds a request body from <c>--data</c> and <c>--set</c>.
///
/// <c>--data</c> is JSON inline, <c>@path</c> for a file, or <c>-</c> for stdin. <c>--set</c>
/// overrides single top-level fields on top of it, httpie-style: <c>Field=value</c> sends a
/// string, <c>Field:=json</c> sends the value as JSON (numbers, booleans, arrays). Strings are the
/// default because Fortnox ids such as CustomerNumber are strings that often look like numbers.
///
/// Fortnox expects the payload wrapped in the singular resource name (<c>{"Customer":{...}}</c>).
/// Either form is accepted: a body whose only key is the wrapper is sent as-is, anything else is
/// wrapped.
/// </summary>
public static class Body
{
    public static string Build(string? data, string[]? sets, string? wrapper)
    {
        JsonObject inner;
        var parsed = Parse(data);

        if (parsed is JsonObject obj && wrapper is not null && obj.Count == 1 &&
            obj.First().Key.Equals(wrapper, StringComparison.OrdinalIgnoreCase) && obj.First().Value is JsonObject wrapped)
            inner = (JsonObject)wrapped.DeepClone();
        else if (parsed is JsonObject plain)
            inner = (JsonObject)plain.DeepClone();
        else if (parsed is null)
            inner = new JsonObject();
        else
            throw FortnoxException.Invalid("--data must be a JSON object.");

        foreach (var set in sets ?? [])
        {
            var eq = set.IndexOf('=');
            if (eq <= 0)
                throw FortnoxException.Invalid($"--set expects Field=value or Field:=json, got '{set}'.");

            var json = set[eq - 1] == ':';
            var field = set[..(json ? eq - 1 : eq)].Trim();
            var value = set[(eq + 1)..];
            inner[field] = json ? JsonValueOf(field, value) : JsonValue.Create(value);
        }

        if (inner.Count == 0)
            throw FortnoxException.Invalid("Nothing to send.", "Pass --data '<json>' (or @file.json) and/or --set Field=value.");

        JsonNode body = wrapper is null ? inner : new JsonObject { [wrapper] = inner };
        return body.ToJsonString();
    }

    /// <summary>A raw body for the <c>api</c> passthrough: sent exactly as given, never wrapped.</summary>
    public static string? Raw(string? data) => Parse(data)?.ToJsonString();

    private static JsonNode? Parse(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;

        var text = data switch
        {
            "-" => System.Console.In.ReadToEnd(),
            _ when data.StartsWith('@') => File.ReadAllText(data[1..]),
            _ => data
        };

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw FortnoxException.Invalid($"--data is not valid JSON: {ex.Message}",
                "In PowerShell, put JSON in a file and pass --data @file.json to avoid quoting problems.");
        }
    }

    private static JsonNode? JsonValueOf(string field, string raw)
    {
        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw FortnoxException.Invalid($"--set {field}:= is not valid JSON: {ex.Message}",
                $"Use {field}=<value> to send a string.");
        }
    }
}
