namespace xyRoslyn.Analyzers.Tests.EfCore;

// Minimal stand-in for EF Core's query surface, shared by the ROSA004 and ROSA005 tests.
// The usings come first so that the test code appended after the stub can use them.
internal static class EfCoreStub
{
    public const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public virtual DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
            }

            public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
            {
                public abstract Type ElementType { get; }
                public abstract Expression Expression { get; }
                public abstract IQueryProvider Provider { get; }
                public abstract IEnumerator<TEntity> GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public static class EntityFrameworkQueryableExtensions
            {
                public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default)
                    => Task.FromResult(new List<TSource>());
                public static Task<TSource[]> ToArrayAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default)
                    => Task.FromResult(new TSource[0]);
                public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, CancellationToken cancellationToken = default)
                    => Task.FromResult(0);
                public static IQueryable<TEntity> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> navigationPropertyPath)
                    where TEntity : class => source;
                public static IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity> source)
                    where TEntity : class => source;
            }
        }

        public sealed class OrderLine
        {
            public int Id { get; set; }
        }

        public sealed class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public List<OrderLine> Lines { get; set; }
        }

        public class AppDbContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();
        }

        """;
}
