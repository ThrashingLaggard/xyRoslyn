# xyRoslyn

!!!WIP!!!

Custom Roslyn analyzers that check rules at compile time.

## Install

```xml
<PackageReference Include="xyRoslyn" PrivateAssets="all" />
```

## Rules

| ID | Rule of the collection | Title |
|---|---|---|
| [ROSA001](docs/analyzers/ROSA001.md) | ef-core EF-011 | Save once per unit of work, not inside a foreach loop |
| [ROSA002](docs/analyzers/ROSA002.md) | http-clients HTTP-017 | Escape variable values in URL paths (code fix) |
| [ROSA003](docs/analyzers/ROSA003.md) | dependency-injection DI-016 | Catch errors per pass in a background service loop |
| [ROSA004](docs/analyzers/ROSA004.md) | ef-core EF-002 | Filter, sort and count in the database |
| [ROSA005](docs/analyzers/ROSA005.md) | ef-core EF-006 | Page only after a unique sort order |
| [ROSA006](docs/analyzers/ROSA006.md) | mcp-server MCP-005 | Set every MCP tool annotation explicitly |
| [ROSA007](docs/analyzers/ROSA007.md) | configuration CFG-006 | Read configuration values instead of asking for the environment |

Severities are set per repository in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.ROSA001.severity = error
```

## License

GPL-3.0-or-later. The analyzers run inside the compiler as a development dependency and are not part of the build output of a consuming project.


## Author

https://github.com/ThrashingLaggard
