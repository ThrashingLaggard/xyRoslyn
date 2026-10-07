namespace xyRoslyn.Analyzers;

/// <summary>All diagnostic IDs of this package. IDs are never reused, not even after a rule is removed.</summary>
public static class DiagnosticIds
{
    /// <summary>ef-core EF-011: SaveChanges inside a foreach loop.</summary>
    public const string SaveChangesInLoop = "ROSA001";

    /// <summary>http-clients HTTP-017: variable value in a URL path without Uri.EscapeDataString.</summary>
    public const string UnescapedUrlPathValue = "ROSA002";

    /// <summary>dependency-injection DI-016: BackgroundService loop without a catch per pass.</summary>
    public const string BackgroundLoopWithoutCatch = "ROSA003";

    /// <summary>ef-core EF-002: query materialized before filtering, sorting or counting.</summary>
    public const string MaterializedBeforeQuery = "ROSA004";

    /// <summary>ef-core EF-006: Skip or Take without OrderBy.</summary>
    public const string PagingWithoutOrderBy = "ROSA005";

    /// <summary>mcp-server MCP-005: MCP tool without explicit annotations.</summary>
    public const string McpToolAnnotationsMissing = "ROSA006";

    /// <summary>configuration CFG-006: environment query outside composition code.</summary>
    public const string EnvironmentQueryInBusinessCode = "ROSA007";
}
