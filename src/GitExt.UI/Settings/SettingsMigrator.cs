using System.Text.Json.Nodes;

namespace GitExt.UI.Settings;

/// <summary>
/// A single step moving from one schema version to the next.
/// </summary>
/// <remarks>
/// Migrations run over the raw <see cref="JsonObject"/>, <b>before the typed model</b>. The reason is
/// simple: if a field was renamed, the typed model cannot read it anyway — the migration has to happen
/// at the one layer where it is still readable.
/// </remarks>
internal interface ISettingsMigration
{
    /// <summary>This migration's <b>input</b> version; its output is <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    void Apply(JsonObject root);
}

/// <summary>
/// Moves the settings file from the version it was read at to <see cref="AppSettings.CurrentVersion"/>
/// (P08-T14).
/// </summary>
internal sealed class SettingsMigrator
{
    /// <summary>
    /// The registered migrations. <b>Empty for now</b> — we are on the first schema version.
    /// </summary>
    /// <remarks>
    /// Being empty does not mean the mechanism does not work: it is verified with fake migrations
    /// injected in the tests. A step will be added here at the first real schema change; having to
    /// write the mechanism on that day would mean verifying both the migration and the infrastructure
    /// at once.
    /// </remarks>
    private static readonly ISettingsMigration[] Registered = [];

    private readonly IReadOnlyList<ISettingsMigration> _migrations;
    private readonly int _targetVersion;

    public SettingsMigrator()
        : this(Registered, AppSettings.CurrentVersion)
    {
    }

    internal SettingsMigrator(IReadOnlyList<ISettingsMigration> migrations, int targetVersion)
    {
        _migrations = migrations;
        _targetVersion = targetVersion;
    }

    /// <summary>
    /// Applies the migrations in order and updates the version field in the result.
    /// </summary>
    /// <returns>
    /// The migrated root when the file can be read; <see langword="null"/> when the file comes
    /// <b>from the future</b> (newer than us).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A file from the future is not read.</b> Reading a schema we do not know by guesswork and
    /// writing over it would mean <b>silently corrupting</b> the settings the user made in the newer
    /// version. In that case we run with the defaults and the file is <b>not touched at all</b>.
    /// </para>
    /// </remarks>
    public JsonObject? Migrate(JsonObject root)
    {
        int version = ReadVersion(root);

        if (version > _targetVersion)
        {
            return null;
        }

        while (version < _targetVersion)
        {
            ISettingsMigration? step = _migrations.FirstOrDefault(m => m.FromVersion == version);

            if (step is null)
            {
                // A step in between is missing: carrying on with what we have would mean pretending to
                // have made a transformation that does not exist. We fall back to the defaults.
                return null;
            }

            step.Apply(root);
            version++;
        }

        root["version"] = _targetVersion;

        return root;
    }

    /// <summary>
    /// Reads the version field.
    /// </summary>
    /// <remarks>
    /// When the field is absent or not a number, <b>1</b> is assumed: the only file format without a
    /// version field is the first version.
    /// </remarks>
    private static int ReadVersion(JsonObject root) =>
        root.TryGetPropertyValue("version", out JsonNode? node)
        && node is JsonValue value
        && value.TryGetValue(out int parsed)
            ? parsed
            : 1;
}
