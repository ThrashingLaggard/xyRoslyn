# ROSA006: Set every MCP tool annotation explicitly

Rule of the collection: mcp-server MCP-005.

## Reported
An `[McpServerTool]` attribute that does not set all of `ReadOnly`, `Destructive`, `Idempotent` and `OpenWorld`. The message names the missing ones.

```csharp
// Reported: Destructive, Idempotent and OpenWorld are not set
[McpServerTool(Name = "get_order", ReadOnly = true)]
```

## Allowed
```csharp
[McpServerTool(Name = "get_order", Title = "Bestellung anzeigen",
    ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
```

## Fix
No automatic fix: the protocol defaults assume the worst case, and the right values depend on what the tool does. Whether the values are correct stays a review and test question (MCP-005 integration test).

## Limits
Tools registered as delegates (lambdas or local functions with the attribute) are not checked. A partial tool method is checked once, on its declaring part.
