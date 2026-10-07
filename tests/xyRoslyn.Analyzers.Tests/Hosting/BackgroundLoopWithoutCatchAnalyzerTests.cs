using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.Hosting;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.Hosting.BackgroundLoopWithoutCatchAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.Hosting;

public sealed class BackgroundLoopWithoutCatchAnalyzerTests
{
    // Minimal stand-in for the real BackgroundService plus a helper for one pass of work.
    // The usings come first so that the test code appended after the stub can use them.
    private const string HostingStub = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.Extensions.Hosting
        {
            public abstract class BackgroundService
            {
                protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
            }
        }

        public static class Work
        {
            public static Task RunOnceAsync(CancellationToken ct) => Task.CompletedTask;
        }

        """;

    [Fact]
    public Task Reports_a_while_loop_without_try_in_ExecuteAsync() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    await Work.RunOnceAsync(ct);
                }
            }
        }
        """);

    [Theory]
    [InlineData("{|ROSA003:do|} { await Work.RunOnceAsync(ct); } while (!ct.IsCancellationRequested);")]
    [InlineData("{|ROSA003:for|} (var pass = 0; pass < 10; pass++) { await Work.RunOnceAsync(ct); }")]
    [InlineData("{|ROSA003:foreach|} (var queue in queues) { await Work.RunOnceAsync(ct); }")]
    [InlineData("await {|ROSA003:foreach|} (var message in messages) { await Work.RunOnceAsync(ct); }")]
    public Task Reports_the_loop_keyword_for_every_loop_kind(string loop) => Verify.VerifyAnalyzerAsync(HostingStub + $$"""
        public sealed class QueueService(List<string> queues, IAsyncEnumerable<string> messages) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {{loop}}
            }
        }
        """);

    [Fact]
    public Task Reports_the_loop_keyword_as_message_argument() => Verify.VerifyAnalyzerAsync(
        HostingStub + """
            public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken ct)
                {
                    {|#0:while|} (!ct.IsCancellationRequested)
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("while"));

    // A catch outside the loop ends the service after the first failed pass instead of continuing.
    [Fact]
    public Task Reports_a_loop_whose_try_sits_outside_the_loop() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                try
                {
                    {|ROSA003:while|} (!ct.IsCancellationRequested)
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        """);

    [Fact]
    public Task Reports_a_try_that_only_catches_OperationCanceledException() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Work.RunOnceAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Reports_a_try_with_finally_and_no_catch() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Work.RunOnceAsync(ct);
                    }
                    finally
                    {
                    }
                }
            }
        }
        """);

    // A try inside an if protects only one branch of the pass.
    [Fact]
    public Task Reports_a_try_that_sits_only_inside_an_if() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService(bool cleanupEnabled) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    if (cleanupEnabled)
                    {
                        try
                        {
                            await Work.RunOnceAsync(ct);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Reports_an_override_through_an_intermediate_base_class() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public abstract class PollingService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
        }

        public sealed class CleanupService : PollingService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    await Work.RunOnceAsync(ct);
                }
            }
        }
        """);

    // The inner loop must not be reported a second time.
    [Fact]
    public Task Reports_only_the_outer_loop_when_both_lack_a_catch() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class QueueService(List<string> queues) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                {|ROSA003:while|} (!ct.IsCancellationRequested)
                {
                    foreach (var queue in queues)
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_the_documented_allowed_loop() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Work.RunOnceAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }

                    await Task.Delay(TimeSpan.FromMinutes(5), ct);
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_catch_without_exception_type() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Work.RunOnceAsync(ct);
                    }
                    catch
                    {
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_try_after_a_using_declaration() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService(Func<IDisposable> createScope) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    using var scope = createScope();
                    try
                    {
                        await Work.RunOnceAsync(ct);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_try_inside_a_using_block() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService(Func<IDisposable> createScope) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    using (var scope = createScope())
                    {
                        try
                        {
                            await Work.RunOnceAsync(ct);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_try_inside_a_lock() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            private readonly object _gate = new();

            protected override Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        try
                        {
                            Work.RunOnceAsync(ct).Wait(ct);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                return Task.CompletedTask;
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_try_inside_a_plain_inner_block() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    {
                        try
                        {
                            await Work.RunOnceAsync(ct);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_an_inner_loop_inside_the_per_pass_try() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class QueueService(List<string> queues) : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var queue in queues)
                        {
                            await Work.RunOnceAsync(ct);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_loop_inside_a_lambda_in_ExecuteAsync() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken ct)
                => Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }, ct);
        }
        """);

    [Fact]
    public Task Does_not_report_a_loop_inside_a_local_function_in_ExecuteAsync() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken ct)
            {
                return RunAsync();

                async Task RunAsync()
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_loop_in_another_method_of_a_BackgroundService() => Verify.VerifyAnalyzerAsync(HostingStub + """
        public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
        {
            protected override Task ExecuteAsync(CancellationToken ct) => DrainAsync(ct);

            private static async Task DrainAsync(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    await Work.RunOnceAsync(ct);
                }
            }
        }
        """);

    // Also covers a project without Microsoft.Extensions.Hosting: no diagnostic and no AD0001.
    [Fact]
    public Task Does_not_report_ExecuteAsync_on_a_foreign_BackgroundService() => Verify.VerifyAnalyzerAsync("""
        using System.Threading;
        using System.Threading.Tasks;

        namespace Legacy.Hosting
        {
            public abstract class BackgroundService
            {
                protected abstract Task ExecuteAsync(CancellationToken stoppingToken);
            }

            public sealed class CleanupService : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken ct)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(1000, ct);
                    }
                }
            }
        }
        """);

    // Incomplete code: the analyzer keeps working on a loop with an undeclared condition (no AD0001).
    [Fact]
    public Task Reports_a_loop_whose_condition_does_not_compile() => Verify.VerifyAnalyzerAsync(
        HostingStub + """
            public sealed class CleanupService : Microsoft.Extensions.Hosting.BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken ct)
                {
                    {|ROSA003:while|} ({|#0:missing|})
                    {
                        await Work.RunOnceAsync(ct);
                    }
                }
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("missing"));

    // Against the real package, so the stub above cannot drift from the real BackgroundService unnoticed.
    [Fact]
    public Task Reports_a_while_loop_against_the_real_hosting_package() => new CSharpAnalyzerTest<BackgroundLoopWithoutCatchAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.Extensions.Hosting.Abstractions", "10.0.12")]),
        TestCode = """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Hosting;

            public sealed class CleanupService : BackgroundService
            {
                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    {|ROSA003:while|} (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
