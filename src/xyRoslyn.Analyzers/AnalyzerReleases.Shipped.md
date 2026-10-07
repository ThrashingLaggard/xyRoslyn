; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ROSA001 | Performance | Warning | SaveChangesInLoopAnalyzer
ROSA002 | Security | Warning | UnescapedUrlPathValueAnalyzer
ROSA003 | Reliability | Warning | BackgroundLoopWithoutCatchAnalyzer
ROSA004 | Performance | Warning | MaterializedBeforeQueryAnalyzer
ROSA005 | Reliability | Warning | PagingWithoutOrderByAnalyzer
ROSA006 | Design | Warning | McpToolAnnotationsMissingAnalyzer
ROSA007 | Design | Warning | EnvironmentQueryInBusinessCodeAnalyzer
