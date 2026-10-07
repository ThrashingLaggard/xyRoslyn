# ROSA005: Page only after a unique sort order

Rule of the collection: ef-core EF-006.

## Reported
`Skip` or `Take` on a query that starts at a `DbSet` (`db.Orders`, a `DbSet` field, `db.Set<Order>()`) without `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Order` or `OrderDescending` before it. Only the first `Skip` or `Take` of a chain is reported.

```csharp
// Reported: the database may return any 20 rows
var page = await db.Orders.Where(o => o.CustomerId == id).Skip(20).Take(20).ToListAsync(ct);
```

## Allowed
```csharp
var page = await db.Orders.Where(o => o.CustomerId == id)
    .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
    .Skip(20).Take(20).ToListAsync(ct);
```

## Fix
No automatic fix: the sort order is a decision.

## Limits
The chain is followed only through `Queryable` and EF Core extension methods (`Include`, `AsNoTracking`, ...). A query that comes from a variable or from another method is not reported, because its order is unknown at this point. Whether the sort key is unique (`ThenBy(x => x.Id)`) is not checked.
