using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Fortnox.Console.Storage;

public sealed class AccountConfig
{
    /// <summary>The Fortnox integration (developer portal app) this account authenticates as.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The Fortnox tenant (DatabaseNumber). Known for every account once a token has been seen,
    /// since it is a claim in the access token; required for <c>client_credentials</c>.
    /// </summary>
    public string TenantId { get; set; } = "";

    /// <summary><c>client_credentials</c> (service account) or <c>refresh_token</c> (user consent).</summary>
    public string AuthMode { get; set; } = "";

    public string Scopes { get; set; } = "";

    /// <summary>The company name `companyinformation` reported when the account was added.</summary>
    public string Identity { get; set; } = "";

    /// <summary>The organization number `companyinformation` reported at that time.</summary>
    public string Organization { get; set; } = "";

    public string AddedAt { get; set; } = "";
}

public sealed class FortnoxConfig
{
    public int Version { get; set; } = 1;
    public Dictionary<string, AccountConfig> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Non-secret account metadata in a readable YAML file. The client secret and tokens never land
/// here - they go to <see cref="ISecretStore"/>.
/// </summary>
public static class ConfigStore
{
    public static string ConfigDir
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("FORTNOX_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fortnox-cli");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (OperatingSystem.IsMacOS())
                return Path.Combine(home, "Library", "Application Support", "fortnox-cli");

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg, "fortnox-cli");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDir, "config.yaml");
    public static string LockPath => Path.Combine(ConfigDir, ".lock");

    /// <summary>
    /// Held around a token refresh. Separate from <see cref="LockPath"/> because FileLock is not
    /// reentrant, and a refresh never needs to write the config.
    /// </summary>
    public static string TokenLockPath => Path.Combine(ConfigDir, ".token.lock");

    public static void EnsureDir()
    {
        var dir = ConfigDir;
        if (Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir);
    }

    public static FortnoxConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path)) return new FortnoxConfig();

        var yaml = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(yaml)) return new FortnoxConfig();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<FortnoxConfig>(yaml) ?? new FortnoxConfig();

        // Account lookup must be case-insensitive; YamlDotNet builds an ordinal dictionary.
        config.Accounts = new Dictionary<string, AccountConfig>(config.Accounts, StringComparer.OrdinalIgnoreCase);
        return config;
    }

    public static void Save(FortnoxConfig config)
    {
        EnsureDir();

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        AtomicWrite(ConfigPath, serializer.Serialize(config));
    }

    /// <summary>Write to a temp file in the same directory, then replace - never a partial config.</summary>
    public static void AtomicWrite(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        RestrictToOwner(temp);
        File.Move(temp, path, overwrite: true);
    }

    public static void AtomicWrite(string path, byte[] contents)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, contents);
        RestrictToOwner(temp);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>0600 on Unix. On Windows the DPAPI blob is already user-scoped.</summary>
    public static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = Directory.Exists(path)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception)
        {
            // Best effort - a filesystem that cannot express permissions is not a reason to fail.
        }
    }
}
