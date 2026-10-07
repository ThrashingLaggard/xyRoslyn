using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.EfCore;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.EfCore.SaveChangesInLoopAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.EfCore;

public sealed class SaveChangesInLoopAnalyzerTests
{
    // Minimal stand-in for the real DbContext; all four overloads are virtual, as in EF Core.
    // The usings come first so that the test code appended after the stub can use them.
    private const string DbContextStub = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public virtual int SaveChanges() => 0;
                public virtual int SaveChanges(bool acceptAllChangesOnSuccess) => 0;
                public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
                public virtual Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
        }

        public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
        }

        """;

    [Fact]
    public Task Reports_SaveChangesAsync_inside_a_foreach_body() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public async Task ImportAsync(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    await db.{|ROSA001:SaveChangesAsync|}(ct);
                }
            }
        }
        """);

    [Theory]
    [InlineData("db.{|ROSA001:SaveChanges|}()")]
    [InlineData("db.{|ROSA001:SaveChanges|}(true)")]
    [InlineData("db.{|ROSA001:SaveChangesAsync|}(ct)")]
    [InlineData("db.{|ROSA001:SaveChangesAsync|}(true, ct)")]
    public Task Reports_every_SaveChanges_overload_inside_a_foreach(string call) => Verify.VerifyAnalyzerAsync(DbContextStub + $$"""
        public sealed class Importer
        {
            public void Import(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    _ = {{call}};
                }
            }
        }
        """);

    [Fact]
    public Task Reports_an_override_in_an_own_context() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class AuditedDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                => base.SaveChangesAsync(cancellationToken);
        }

        public sealed class Importer
        {
            public async Task ImportAsync(AuditedDbContext db, IEnumerable<int> lines, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    await db.{|ROSA001:SaveChangesAsync|}(ct);
                }
            }
        }
        """);

    [Fact]
    public Task Reports_a_null_conditional_call() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public async Task ImportAsync(AppDbContext? db, IEnumerable<int> lines, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    await (db?.{|ROSA001:SaveChangesAsync|}(ct) ?? Task.FromResult(0));
                }
            }
        }
        """);

    [Fact]
    public Task Reports_an_unqualified_call_inside_the_own_context() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class ImportDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public void Import(IEnumerable<int> lines)
            {
                foreach (var line in lines)
                {
                    {|ROSA001:SaveChanges|}();
                }
            }
        }
        """);

    [Fact]
    public Task Reports_inside_await_foreach() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public async Task ImportAsync(AppDbContext db, IAsyncEnumerable<int> lines, CancellationToken ct)
            {
                await foreach (var line in lines)
                {
                    await db.{|ROSA001:SaveChangesAsync|}(ct);
                }
            }
        }
        """);

    [Fact]
    public Task Reports_when_nested_under_if_and_while_inside_a_foreach() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public void Import(AppDbContext db, IEnumerable<int> lines, bool retry)
            {
                foreach (var line in lines)
                {
                    if (line > 0)
                    {
                        while (retry)
                        {
                            db.{|ROSA001:SaveChanges|}();
                            retry = false;
                        }
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Reports_a_foreach_inside_a_while() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Worker
        {
            public void Run(AppDbContext db, Queue<List<int>> batches)
            {
                while (batches.Count > 0)
                {
                    foreach (var line in batches.Dequeue())
                    {
                        db.{|ROSA001:SaveChanges|}();
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Reports_a_foreach_inside_a_lambda() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public static class Retry
        {
            public static Task RunAsync(System.Func<Task> operation) => operation();
        }

        public sealed class Importer
        {
            public Task ImportAsync(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
                => Retry.RunAsync(async () =>
                {
                    foreach (var line in lines)
                    {
                        await db.{|ROSA001:SaveChangesAsync|}(ct);
                    }
                });
        }
        """);

    // The outer loop must not report the same call a second time.
    [Fact]
    public Task Reports_nested_foreach_once() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public void Import(AppDbContext db, IEnumerable<List<int>> orders)
            {
                foreach (var order in orders)
                {
                    foreach (var line in order)
                    {
                        db.{|ROSA001:SaveChanges|}();
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Reports_the_method_name_as_message_argument() => Verify.VerifyAnalyzerAsync(
        DbContextStub + """
            public sealed class Importer
            {
                public async Task ImportAsync(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
                {
                    foreach (var line in lines)
                    {
                        await db.{|#0:SaveChangesAsync|}(ct);
                    }
                }
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("SaveChangesAsync"));

    [Fact]
    public Task Does_not_report_a_call_after_the_foreach() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public async Task ImportAsync(AppDbContext db, IEnumerable<int> lines, List<int> added, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    added.Add(line);
                }

                await db.SaveChangesAsync(ct);
            }
        }
        """);

    // Worker loops, concurrency retry loops and retry counters save once per pass on purpose.
    [Theory]
    [InlineData("while (retry) { db.SaveChanges(); retry = false; }")]
    [InlineData("do { db.SaveChanges(); retry = false; } while (retry);")]
    [InlineData("for (var attempt = 0; attempt < 3; attempt++) { db.SaveChanges(); }")]
    public Task Does_not_report_inside_while_do_or_for(string loop) => Verify.VerifyAnalyzerAsync(DbContextStub + $$"""
        public sealed class Worker
        {
            public void Run(AppDbContext db, bool retry)
            {
                {{loop}}
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_call_in_the_foreach_collection_expression() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public void Import(AppDbContext db, List<int> added)
            {
                foreach (var id in Ids(db.SaveChanges()))
                {
                    added.Add(id);
                }
            }

            private static int[] Ids(int count) => new int[count];
        }
        """);

    [Fact]
    public Task Does_not_report_inside_a_lambda_in_a_foreach() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public static class Retry
        {
            public static Task RunAsync(System.Func<Task> operation) => operation();
        }

        public sealed class Importer
        {
            public async Task ImportAsync(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
            {
                foreach (var line in lines)
                {
                    await Retry.RunAsync(async () => await db.SaveChangesAsync(ct));
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_inside_a_local_function_in_a_foreach() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class Importer
        {
            public void Import(AppDbContext db, IEnumerable<int> lines, List<System.Action> later)
            {
                foreach (var line in lines)
                {
                    later.Add(Save);

                    void Save() => db.SaveChanges();
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_a_same_named_method_on_a_foreign_DbContext() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        namespace Legacy.Data
        {
            public class DbContext
            {
                public int SaveChanges() => 0;
            }

            public sealed class Importer
            {
                public void Import(DbContext db, IEnumerable<int> lines)
                {
                    foreach (var line in lines)
                    {
                        db.SaveChanges();
                    }
                }
            }
        }
        """);

    [Fact]
    public Task Does_not_report_without_EF_Core() => Verify.VerifyAnalyzerAsync("""
        using System.Collections.Generic;

        public class DbContext
        {
            public int SaveChanges() => 0;
        }

        public sealed class Importer
        {
            public void Import(DbContext db, IEnumerable<int> lines)
            {
                foreach (var line in lines)
                {
                    db.SaveChanges();
                }
            }
        }
        """);

    // A hiding SaveChanges on an own context still saves through DbContext: one round trip per pass.
    [Fact]
    public Task Reports_a_new_method_hiding_DbContext_SaveChanges() => Verify.VerifyAnalyzerAsync(DbContextStub + """
        public sealed class AuditedDbContext : Microsoft.EntityFrameworkCore.DbContext
        {
            public new int SaveChanges() => base.SaveChanges();
        }

        public sealed class Importer
        {
            public void Import(AuditedDbContext db, IEnumerable<int> lines)
            {
                foreach (var line in lines)
                {
                    db.{|ROSA001:SaveChanges|}();
                }
            }
        }
        """);

    // Incomplete code: the analyzer keeps working on a loop over an undeclared collection (no AD0001).
    [Fact]
    public Task Reports_in_a_foreach_over_an_undeclared_collection() => Verify.VerifyAnalyzerAsync(
        DbContextStub + """
            public sealed class Importer
            {
                public void Import(AppDbContext db)
                {
                    foreach (var line in {|#0:missing|})
                    {
                        db.{|ROSA001:SaveChanges|}();
                    }
                }
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("missing"));

    // Against the real package, so the stub above cannot drift from the real DbContext unnoticed.
    [Fact]
    public Task Reports_SaveChangesAsync_against_the_real_EF_Core_package() => new CSharpAnalyzerTest<SaveChangesInLoopAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.EntityFrameworkCore", "10.0.0")]),
        TestCode = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            public sealed class AppDbContext : DbContext
            {
                public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                    => base.SaveChangesAsync(cancellationToken);
            }

            public sealed class Importer
            {
                public async Task ImportAsync(AppDbContext db, IEnumerable<int> lines, CancellationToken ct)
                {
                    foreach (var line in lines)
                    {
                        await db.{|ROSA001:SaveChangesAsync|}(ct);
                    }
                }
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
