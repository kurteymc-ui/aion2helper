namespace AionDPS;

/// <summary>
/// AION 2 Helper's copy of the meter. Everything that differs from the project it comes from (SkeeveAN/Aion-DPS-Meter, MIT,
/// see LICENSE and NOTICE.md) is set here: the name, the site it talks to, the folder it keeps its files in and where its updates
/// come from. The rest of the code stays as close to that project as possible, so its fixes (a game patch, a new feature) can be
/// taken over with a plain merge.
/// </summary>
public static class Brand
{
    public const string AppName = "A2HMeter";

    /// <summary>The website: links the meter opens.</summary>
    public const string Site = "https://aion2helper.com";

    /// <summary>The meter's API on the site. The client calls the same paths as the upstream client did on its own site
    /// (/api/uploads, /api/players/…, /api/schedule …), here under /meter.</summary>
    public const string Api = Site + "/meter";

    /// <summary>The folder under %APPDATA% for settings, sessions and logs (its own, so it can sit next to the upstream meter).</summary>
    public const string DataFolder = "A2HMeter";

    /// <summary>Where updates come from: full releases of this repository only (never test versions).</summary>
    public const string RepositoryUrl = "https://github.com/kurteymc-ui/aion2helper";
}
