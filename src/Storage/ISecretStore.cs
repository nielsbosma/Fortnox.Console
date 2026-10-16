namespace Fortnox.Console.Storage;

/// <summary>
/// Anything bearer-shaped. Each account stores one entry, <c>account:{name}</c>, holding its
/// <see cref="AccountSecrets"/> as JSON.
/// </summary>
public interface ISecretStore
{
    /// <summary>Backend name, as reported by <c>fortnox accounts list</c>.</summary>
    string Name { get; }

    string? Get(string key);
    void Set(string key, string value);
    void Delete(string key);
}

public static class SecretKeys
{
    public static string Account(string name) => "account:" + name.ToLowerInvariant();
}
