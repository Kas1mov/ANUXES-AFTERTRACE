namespace ANUXES.Aftertrace.Models;

/// <summary>Counts of each supported change type.</summary>
public sealed class ChangeSummary
{
    public int Added { get; init; }
    public int Modified { get; init; }
    public int Deleted { get; init; }
    public int Moved { get; init; }
    public int Renamed { get; init; }
    public int Total => Added + Modified + Deleted + Moved + Renamed;

    public int GetCount(FileChangeType type) => type switch
    {
        FileChangeType.Added => Added,
        FileChangeType.Modified => Modified,
        FileChangeType.Deleted => Deleted,
        FileChangeType.Moved => Moved,
        FileChangeType.Renamed => Renamed,
        _ => 0
    };
}
