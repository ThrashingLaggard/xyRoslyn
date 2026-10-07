namespace xyRoslyn.Analyzers;

/// <summary>All diagnostic IDs of this package. IDs are never reused, not even after a rule is removed.</summary>
public static class DiagnosticIds
{
    /// <summary>ef-core EF-011: SaveChanges inside a foreach loop.</summary>
    public const string SaveChangesInLoop = "ROSA001";
}
