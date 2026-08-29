using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ProductManager.Api.Infrastructure;

/// <summary>One page of a list endpoint's results.</summary>
/// <typeparam name="T">The item DTO type.</typeparam>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Standard pagination query parameters for list endpoints.</summary>
public sealed class PageQuery
{
    public const int MaxPageSize = 200;

    [FromQuery(Name = "page")]
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [FromQuery(Name = "pageSize")]
    [Range(1, MaxPageSize)]
    public int PageSize { get; set; } = 50;

    /// <summary>Rows to skip for this page. A method, not a property, so it stays out of model binding.</summary>
    public int Skip() => (Page - 1) * PageSize;
}

public static class QueryablePagingExtensions
{
    public static async Task<PagedResponse<TDto>> ToPagedResponseAsync<TEntity, TDto>(
        this IQueryable<TEntity> query,
        PageQuery page,
        Func<TEntity, TDto> map,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip()).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<TDto>(items.Select(map).ToList(), page.Page, page.PageSize, total);
    }
}
