# xyRoslyn

Roslyn analyzers that check rules of the goldstandard best-practice collection at compile time.

## Install

```xml
<PackageReference Include="xyRoslyn" PrivateAssets="all" />
```

## Rules

| ID | Rule of the collection | Title |
|---|---|---|
| [ROSA001](docs/analyzers/ROSA001.md) | ef-core EF-011 | Save once per unit of work, not inside a foreach loop |

Every rule has a help page under `docs/analyzers/`. Severities are set per repository in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.ROSA001.severity = error
```

## License

GPL-3.0-or-later. The analyzers run inside the compiler as a development dependency and are not part of the build output of a consuming project.
