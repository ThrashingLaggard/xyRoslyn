# ROSA004: Filter, sort and count in the database

Rule of the collection: ef-core EF-002.

## Reported
`Where`, `OrderBy`, `OrderByDescending`, `Count`, `LongCount`, `Any` or the property `Count`/`Length` applied directly to `ToList()`, `ToArray()` or `AsEnumerable()` of an `IQueryable`, or to an awaited `ToListAsync()`/`ToArrayAsync()`.

```csharp
// Reported: loads every order, then filters in memory
var open = db.Orders.ToList().Where(o => o.Status == OrderStatus.Placed);
var count = (await db.Orders.ToListAsync(ct)).Count;
```

## Allowed
```csharp
var open = await db.Orders.Where(o => o.Status == OrderStatus.Placed).ToListAsync(ct);
var count = await db.Orders.CountAsync(ct);
```
`Select` and other operators after `AsEnumerable()` are not reported.

## Fix
No automatic fix: moving an operator into the query can make it untranslatable, which only shows at run time. A deliberate client-side filter after `AsEnumerable()` (for example a .NET method the database cannot run) is suppressed at the method with a reason.

## Limits
A materialized result stored in a variable first is not followed. Null-conditional forms such as `db.Orders.ToList()?.Count` are not reported.
