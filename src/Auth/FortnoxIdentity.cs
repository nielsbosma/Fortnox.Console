using System.Text.Json;

namespace Fortnox.Console.Auth;

/// <summary>
/// Reads the two things worth remembering from <c>companyinformation</c>: the company name and
/// its organization number. Both are recorded when an account is added and shown whenever
/// accounts are listed - with several companies on one machine, the account name alone does not
/// say which books a command is about to change.
/// </summary>
public static class FortnoxIdentity
{
    public static string Describe(JsonDocument companyInformation) => Field(companyInformation, "CompanyName");

    public static string Organization(JsonDocument companyInformation) => Field(companyInformation, "OrganizationNumber");

    public static string TenantId(JsonDocument companyInformation) => Field(companyInformation, "DatabaseNumber");

    private static string Field(JsonDocument doc, string name)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return "";
        if (root.TryGetProperty("CompanyInformation", out var info)) root = info;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return "";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }
}
