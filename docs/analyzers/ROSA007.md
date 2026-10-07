# ROSA007: Read configuration values instead of asking for the environment

Rule of the collection: configuration CFG-006.

## Reported
`IsDevelopment()`, `IsProduction()`, `IsStaging()`, `IsEnvironment(...)` or a read of `IHostEnvironment.EnvironmentName` outside composition code.

```csharp
// Reported: the service asks for the environment instead of getting a value
public decimal VatRate => env.IsProduction() ? 0.19m : 0m;
```

## Allowed
Composition code:
- the program entry point (top-level statements in `Program.cs`, or `Main`),
- every method with a parameter of type `IServiceCollection`, `IHostApplicationBuilder`, `IHostBuilder`, `IApplicationBuilder` or `IEndpointRouteBuilder`, which covers `Add{Feature}` and `Map{Feature}` extension methods.

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
```

Injecting `IHostEnvironment` for `ContentRootPath` is not reported.

## Fix
No automatic fix: the service gets a configuration value (`Ordering:VatRate`) through validated options (configuration step 5).
