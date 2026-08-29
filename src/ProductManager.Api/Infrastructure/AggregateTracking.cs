using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace ProductManager.Api.Infrastructure;

/// <summary>
/// The domain assigns entity identities in its own constructors, so a child object that EF first
/// meets through a navigation already has a populated key — and EF reads a populated key as
/// "this row already exists", issuing an UPDATE that touches nothing. These helpers tell EF which
/// children an aggregate operation actually created.
/// </summary>
public static class AggregateTracking
{
    /// <summary>
    /// Runs <paramref name="mutate"/> on an aggregate and marks as inserts whatever children it
    /// added to <paramref name="children"/>. Removals still ride on EF's own orphan detection.
    /// </summary>
    public static void MutateChildren<TChild>(
        this DbContext db,
        Func<IReadOnlyCollection<TChild>> children,
        Action mutate)
        where TChild : class
    {
        var before = new HashSet<TChild>(children(), ReferenceComparer<TChild>.Instance);
        mutate();

        foreach (var child in children())
            if (!before.Contains(child))
                db.Add(child);
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
