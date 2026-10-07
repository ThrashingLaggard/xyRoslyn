using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.EfCore;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.EfCore.PagingWithoutOrderByAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.EfCore;

public sealed class PagingWithoutOrderByAnalyzerTests
{
    // Only the first Skip or Take of a chain is reported; Take after Skip stays quiet.
    [Fact]
    public Task Reports_Skip_then_Take_once_at_Skip() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderPages
        {
            public Task<List<Order>> PageAsync(AppDbContext db, int id, CancellationToken ct)
                => db.Orders.Where(o => o.CustomerId == id).{|ROSA005:Skip|}(20).Take(20).ToListAsync(ct);
        }
        """);

    [Theory]
    [InlineData("db.Orders")]
    [InlineData("orders")]
    [InlineData("db.Set<Order>()")]
    public Task Reports_paging_on_every_DbSet_source(string source) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderPages
        {
            private readonly DbSet<Order> orders;

            public OrderPages(DbSet<Order> orders) => this.orders = orders;

            public IQueryable<Order> FirstPage(AppDbContext db) => {{source}}.{|ROSA005:Take|}(20);
        }
        """);

    [Fact]
    public Task Reports_through_Include_and_AsNoTracking() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderPages
        {
            public IQueryable<Order> FirstPage(AppDbContext db)
                => db.Orders.Include(o => o.Lines).AsNoTracking().{|ROSA005:Take|}(20);
        }
        """);

    [Fact]
    public Task Reports_the_operator_name_as_message_argument() => Verify.VerifyAnalyzerAsync(
        EfCoreStub.Source + """
            public sealed class OrderPages
            {
                public IQueryable<Order> FirstPage(AppDbContext db) => db.Orders.{|#0:Take|}(20);
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("Take"));

    [Theory]
    [InlineData("OrderBy(o => o.Id)")]
    [InlineData("OrderByDescending(o => o.CreatedAt)")]
    [InlineData("Order()")]
    [InlineData("OrderDescending()")]
    public Task Does_not_report_paging_after_ordering(string ordering) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderPages
        {
            public IQueryable<Order> SecondPage(AppDbContext db) => db.Orders.{{ordering}}.Skip(20).Take(20);
        }
        """);

    [Fact]
    public Task Does_not_report_the_documented_allowed_query() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderPages
        {
            public Task<List<Order>> PageAsync(AppDbContext db, int id, CancellationToken ct)
                => db.Orders.Where(o => o.CustomerId == id)
                    .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
                    .Skip(20).Take(20).ToListAsync(ct);
        }
        """);

    [Fact]
    public Task Does_not_report_ordering_before_AsNoTracking() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderPages
        {
            public IQueryable<Order> SecondPage(AppDbContext db)
                => db.Orders.OrderBy(o => o.Id).AsNoTracking().Skip(20);
        }
        """);

    // Documented limit: the order of a query held in a variable is unknown here.
    [Fact]
    public Task Does_not_report_a_query_from_a_local_variable() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public sealed class OrderPages
        {
            public IQueryable<Order> SecondPage(AppDbContext db)
            {
                var query = db.Orders.AsNoTracking();
                return query.Skip(20);
            }
        }
        """);

    // Documented limit: the chain is not followed into a method outside Queryable and EF Core.
    [Fact]
    public Task Does_not_report_a_query_through_an_unknown_extension_method() => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + """
        public static class OrderQueries
        {
            public static IQueryable<Order> Placed(this IQueryable<Order> orders) => orders.Where(o => o.Status == "Placed");
        }

        public sealed class OrderPages
        {
            public IQueryable<Order> SecondPage(AppDbContext db) => db.Orders.Placed().Skip(20);
        }
        """);

    [Theory]
    [InlineData("repository.Orders")]
    [InlineData("list.AsQueryable()")]
    public Task Does_not_report_paging_on_an_IQueryable_that_is_not_a_DbSet(string source) => Verify.VerifyAnalyzerAsync(EfCoreStub.Source + $$"""
        public sealed class OrderRepository
        {
            public IQueryable<Order> Orders { get; set; }
        }

        public sealed class OrderPages
        {
            public IQueryable<Order> SecondPage(OrderRepository repository, List<Order> list) => {{source}}.Skip(20);
        }
        """);

    // Incomplete code: the analyzer keeps working next to an undeclared name (no AD0001).
    [Fact]
    public Task Reports_next_to_an_undeclared_name() => Verify.VerifyAnalyzerAsync(
        EfCoreStub.Source + """
            public sealed class OrderPages
            {
                public IQueryable<Order> SecondPage(AppDbContext db)
                {
                    var pageSize = {|#0:settings|}.PageSize;
                    return db.Orders.{|ROSA005:Skip|}(20);
                }
            }
            """,
        DiagnosticResult.CompilerError("CS0103").WithLocation(0).WithArguments("settings"));

    // Against the real package: Include returns IIncludableQueryable, which the stub does not model.
    [Fact]
    public Task Reports_Skip_after_Include_against_the_real_EF_Core_package() => new CSharpAnalyzerTest<PagingWithoutOrderByAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("Microsoft.EntityFrameworkCore", "10.0.0")]),
        TestCode = """
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public sealed class OrderLine
            {
                public int Id { get; set; }
            }

            public sealed class Order
            {
                public int Id { get; set; }
                public List<OrderLine> Lines { get; set; } = [];
            }

            public sealed class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public sealed class OrderPages
            {
                public IQueryable<Order> SecondPage(AppDbContext db)
                    => db.Orders.Include(o => o.Lines).AsNoTracking().{|ROSA005:Skip|}(20);
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
