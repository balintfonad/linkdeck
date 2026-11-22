namespace LinkDeck.Core.Config;

/// <summary>Resolves where LinkDeck keeps its files, including portable mode.</summary>
public sealed record AppPaths
{
    public required string DataDirectory { get; init; }

    /// <summary>Per-computer data (caches, logs, the confirmed-items list). Never the portable folder.</summary>
    public required string LocalDirectory { get; init; }

    public required bool IsPortable { get; init; }

    /// <summary>A <c>config.json</c> sits next to the executable but was ignored because other accounts can change that folder.</summary>
    public bool PortableConfigIgnored { get; init; }

    public string IconsDirectory => Path.Combine(LocalDirectory, "icons");

    public string LogsDirectory => Path.Combine(LocalDirectory, "logs");

    public string TrustPath => Path.Combine(LocalDirectory, "trust.dat");

    public string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public string StatePath => Path.Combine(DataDirectory, "state.json");

    /// <summary>
    /// Portable mode: when a <c>config.json</c> sits next to the executable, that directory holds config and state,
    /// but only if <paramref name="isPrivateFolder"/> confirms that no other account can change files there
    /// (otherwise another user could plant a config whose items run under this user's account).
    /// Otherwise config and state live in <paramref name="roamingAppData"/>\LinkDeck and the rest in <paramref name="localAppData"/>\LinkDeck.
    /// </summary>
    public static AppPaths Resolve(string exeDirectory, string roamingAppData, string localAppData, Func<string, bool> isPrivateFolder)
    {
        var hasPortableConfig = File.Exists(Path.Combine(exeDirectory, "config.json"));
        var portable = hasPortableConfig && isPrivateFolder(exeDirectory);
        return new AppPaths
        {
            DataDirectory = portable ? exeDirectory : Path.Combine(roamingAppData, "LinkDeck"),
            LocalDirectory = Path.Combine(localAppData, "LinkDeck"),
            IsPortable = portable,
            PortableConfigIgnored = hasPortableConfig && !portable,
        };
    }
}
