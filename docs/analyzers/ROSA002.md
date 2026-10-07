# ROSA002: Escape variable values in URL paths

Rule of the collection: http-clients HTTP-017.

## Reported
An interpolation hole in the path part of the `requestUri` argument of `HttpClient` (`GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`, `GetStringAsync`, ...), of the `HttpClientJsonExtensions` (`GetFromJsonAsync`, `PostAsJsonAsync`, ...) or of `new HttpRequestMessage(method, requestUri)` for `SendAsync`, also inside `new Uri($"...")` and `new Uri(baseUri, $"...")`, that does not run through `Uri.EscapeDataString`.

```csharp
// Reported: an ID "a/b" requests shipments/a/b
using var response = await http.GetAsync($"shipments/{shipmentId}", ct);
```

## Allowed
- `Uri.EscapeDataString(value)` in the hole.
- Integers (`int`, `long`, ... and their nullable forms) and `Guid`: their text cannot contain `/`, `?` or `#`.
- Constants.
- A hole at the very start of a complete request URI: that is a base address, not a path segment. In `new Uri(baseUri, $"{segment}/...")` the first hole is a path segment and is reported. Base addresses belong in `HttpClient.BaseAddress` (http-clients HTTP-003).
- Holes after the first `?` or `#`: query and fragment are not part of this rule.

## Fix
The code fix wraps the value in `Uri.EscapeDataString`:

```csharp
using var response = await http.GetAsync($"shipments/{Uri.EscapeDataString(shipmentId)}", ct);
```

Without `using System;` it writes `System.Uri.EscapeDataString(...)`. It is offered only for strings that the compiler knows are not null (`Uri.EscapeDataString(null)` throws) and for holes without alignment or format. In code without nullable reference types (`<Nullable>enable</Nullable>` missing) the compiler knows nothing about null, so the fix is not offered there. Other cases are fixed by hand.

## Limits
A URL built in a variable first (`var url = $"..."; http.GetAsync(url)`) or with `+` is not checked.
