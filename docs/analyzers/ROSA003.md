# ROSA003: Catch errors per pass in a background service loop

Rule of the collection: dependency-injection DI-016.

## Reported
The outermost `while`, `do`, `for` or `foreach` loop in an override of `BackgroundService.ExecuteAsync` whose body has no `try` statement with a `catch` for `Exception` (or a `catch` without a type). Since .NET 6 an unhandled exception in `ExecuteAsync` stops the whole application.

```csharp
// Reported: one failed run stops the web API as well
while (await timer.WaitForNextTickAsync(ct))
{
    await CleanupAsync(ct);
}
```

## Allowed
```csharp
while (await timer.WaitForNextTickAsync(ct))
{
    try
    {
        await CleanupAsync(ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        log.LogError(ex, "Cleanup run failed");
    }
}
```
The `try` may also sit inside a `using (...) { }` block, a `lock` or a plain inner block of the loop body, because those wrap the whole pass. A `try` inside an `if` does not count: it protects only one branch.

Statements next to the `try` in the loop body, such as `await Task.Delay(interval, ct)`, are allowed: cancellation at shutdown is meant to end the loop. Loops inside lambdas and local functions, and `ExecuteAsync` without a loop, are not reported.

## Fix
No automatic fix: what to log and whether a failure should end the service is a decision.
