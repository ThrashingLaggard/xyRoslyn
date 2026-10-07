# ROSA001: Save once per unit of work, not inside a foreach loop

Rule of the collection: ef-core EF-011.

## Reported
`SaveChanges` or `SaveChangesAsync` of an EF Core `DbContext`, including overrides and `new` methods with these names in an own context, inside the body of a `foreach` or `await foreach` loop.

```csharp
// Reported: one round trip and one transaction per line
foreach (var line in lines)
{
    db.OrderLines.Add(line);
    await db.SaveChangesAsync(ct);
}
```

## Allowed
- `SaveChanges` after the loop.
- `while` and `do` loops, for example a worker that handles one unit of work per pass or a retry after `DbUpdateConcurrencyException`.
- `for` loops, because they are often retry counters.
- Calls inside a lambda or local function that sits inside the loop, because the analyzer cannot know when they run. A loop inside a lambda is checked like any other loop.
- Calls through an interface such as `IApplicationDbContext.SaveChangesAsync`, because the analyzer cannot know which implementation runs. The collection injects the context class directly (dependency-injection).

## Fix
Add or change all entities inside the loop and call `SaveChangesAsync` once after it:

```csharp
db.OrderLines.AddRange(lines);
await db.SaveChangesAsync(ct);
```

There is no automatic code fix: moving the save changes what stays stored when an error occurs, and that is a decision.

## Deliberate exceptions
Saving in chunks during a very large import is a decision, not an accident. Suppress it at the method with a reason:

```csharp
[SuppressMessage("Performance", "ROSA001:Save once per unit of work, not inside a foreach loop",
    Justification = "Import of up to 2 million rows; saving every 1000 rows keeps the change tracker small.")]
```
