using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.EfCore;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.EfCore.MaterializedBeforeQueryAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.EfCore;

public sealed class MaterializedBeforeQueryAnalyzerTests
{
    [Fact]
    public Task Reports_Where_after_ToList_on_a_DbSet() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public IEnumerable<Order> Open(AppDbContext db)
                => db.Orders.ToList().{|ROSA004:Where|}(o => o.Status == "Placed");
        }
        """);

    [Theory]
    [InlineData("{|ROSA004:Where|}(o => o.Status == \"Placed\")")]
    [InlineData("{|ROSA004:OrderBy|}(o => o.CreatedAt)")]
    [InlineData("{|ROSA004:OrderByDescending|}(o => o.CreatedAt)")]
    [InlineData("{|ROSA004:Count|}()")]
    [InlineData("{|ROSA004:LongCount|}()")]
    [InlineData("{|ROSA004:Any|}()")]
    public Task Reports_every_query_operator_after_ToList(string call) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderReport
        {
            public object Run(AppDbContext db) => db.Orders.ToList().{{call}};
        }
        """);

    [Theory]
    [InlineData("ToList")]
    [InlineData("ToArray")]
    [InlineData("AsEnumerable")]
    public Task Reports_Where_after_every_synchronous_materializer(string materializer) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderReport
        {
            public IEnumerable<Order> Open(AppDbContext db)
                => db.Orders.{{materializer}}().{|ROSA004:Where|}(o => o.Status == "Placed");
        }
        """);

    [Theory]
    [InlineData("db.Orders.ToList().{|ROSA004:Count|}")]
    [InlineData("db.Orders.ToArray().{|ROSA004:Length|}")]
    [InlineData("(await db.Orders.ToListAsync(ct)).{|ROSA004:Count|}")]
    [InlineData("(await db.Orders.ToArrayAsync(ct)).{|ROSA004:Length|}")]
    public Task Reports_the_Count_or_Length_property_of_a_materialized_query(string expression) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderReport
        {
            public async Task<int> CountAsync(AppDbContext db, CancellationToken ct)
            {
                await Task.Yield();
                return {{expression}};
            }
        }
        """);

    [Fact]
    public Task Reports_an_operator_after_an_awaited_ToListAsync() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public async Task<bool> AnyAsync(AppDbContext db, CancellationToken ct)
                => (await db.Orders.ToListAsync(ct)).{|ROSA004:Any|}();
        }
        """);

    // The source is an IQueryable<T> from Queryable.Where, not the DbSet itself.
    [Fact]
    public Task Reports_ToList_of_a_filtered_query() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public int Open(AppDbContext db, int customerId)
                => db.Orders.Where(o => o.CustomerId == customerId).ToList().{|ROSA004:Count|}();
        }
        """);

    [Fact]
    public Task Reports_the_materializer_and_the_operator_as_message_arguments() => Verify.VerifyAnalyzerAsync(
        EfCoreStub.Source + """
            public sealed class OrderReport
            {
                public IEnumerable<Order> Open(AppDbContext db)
                    => db.Orders.ToList().{|#0:Where|}(o => o.Status == "Placed");
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("ToList", "Where"));

    [Fact]
    public Task Does_not_report_operators_applied_to_the_query() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public async Task<int> RunAsync(AppDbContext db, CancellationToken ct)
            {
                var open = await db.Orders.Where(o => o.Status == "Placed").ToListAsync(ct);
                var count = await db.Orders.CountAsync(ct);
                return open.Count + count;
            }
        }
        """);

    [Fact]
    public Task Does_not_report_Select_after_AsEnumerable() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public IEnumerable<string> Labels(AppDbContext db)
                => db.Orders.AsEnumerable().Select(o => o.Status.ToUpperInvariant());
        }
        """);

    [Fact]
    public Task Does_not_report_ToList_of_an_in_memory_collection() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public IEnumerable<Order> Open(List<Order> orders)
                => orders.ToList().Where(o => o.Status == "Placed");
        }
        """);

    // Documented limit: a materialized result stored in a variable first is not followed.
    [Fact]
    public Task Does_not_report_a_materialized_result_stored_in_a_variable() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderReport
        {
            public IEnumerable<Order> Open(AppDbContext db)
            {
                var all = db.Orders.ToList();
                return all.Where(o => o.Status == "Placed");
            }
        }
        """);

    // An own ToListAsync with the same name is not EF Core's; the inner namespace binds it first.
    [Fact]
    public Task Does_not_report_an_awaited_ToListAsync_from_another_type() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        namespace Legacy.Data
        {
            public static class QueryExtensions
            {
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct)
                    => Task.FromResult(new List<T>());
            }

            public sealed class OrderReport
            {
                public async Task<int> CountAsync(AppDbContext db, CancellationToken ct)
                    => (await db.Orders.ToListAsync(ct)).Count;
            }
        }
        """);

    // Incomplete code: the analyzer keeps working next to an undeclared name (no AD0001).
    [Fact]
    public Task Reports_next_to_an_undeclared_name() => Verify.VerifyAnalyzerAsync(
        EfCoreStub.Source + """
            public sealed class OrderReport
            {
                public int Open(AppDbContext db)
                    => db.Orders.ToList().{|ROSA004:Count|}() + {|#0:missing|};
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("missing"));

    // Against the real package: next to .NET 10's System.Linq.AsyncEnumerable, ToListAsync must
    // still bind to EntityFrameworkQueryableExtensions, and the stub above cannot drift unnoticed.
    [Fact]
    public Task Reports_the_Count_of_an_awaited_ToListAsync_against_the_real_EF_Core_package() => new CSharpAnalyzerTest<MaterializedBeforeQueryAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.EntityFrameworkCore", "10.0.0")]),
        TestCode = """
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;

            public sealed class Order
            {
                public int Id { get; set; }
            }

            public sealed class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public sealed class OrderReport
            {
                public async Task<int> CountAsync(AppDbContext db, CancellationToken ct)
                    => (await db.Orders.ToListAsync(ct)).{|ROSA004:Count|};
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
