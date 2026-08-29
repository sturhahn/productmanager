using System.ComponentModel.DataAnnotations;
using ProductManager.Domain.Common;
using ProductManager.Domain.Seam;

namespace ProductManager.Api.Contracts;

/// <summary>One unit of a feature's market meaning.</summary>
public sealed record FeatureDeltaDto(
    FeatureDeltaKind Kind,
    Guid? BusinessProcessId,
    CoverageTier? FromTier,
    CoverageTier? ToTier,
    Guid? ApproachId,
    Guid? SegmentId,
    double? Magnitude,
    string? Rationale);

public sealed record FeatureDto(
    Guid Id,
    Guid WorkspaceId,
    Guid? ProductId,
    string Name,
    string? Description,
    FeatureStatus Status,
    DateOnly? ShippedDate,
    IReadOnlyList<FeatureDeltaDto> Deltas);

public sealed record CreateFeatureRequest(
    [Required] Guid WorkspaceId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? ProductId);

public sealed record UpdateFeatureRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? ProductId,
    FeatureStatus? Status,
    DateOnly? ShippedDate);

/// <summary>
/// Adds a delta. Coverage deltas require <c>businessProcessId</c> and <c>toTier</c> (and must raise
/// the tier); other kinds require a <c>magnitude</c>.
/// </summary>
public sealed record AddFeatureDeltaRequest(
    FeatureDeltaKind Kind,
    Guid? BusinessProcessId,
    CoverageTier? FromTier,
    CoverageTier? ToTier,
    Guid? ApproachId,
    Guid? SegmentId,
    double? Magnitude,
    string? Rationale);

public static class SeamMapping
{
    public static FeatureDeltaDto ToDto(this FeatureDelta d) => new(
        d.Kind, d.BusinessProcessId, d.FromTier, d.ToTier, d.ApproachId, d.SegmentId, d.Magnitude, d.Rationale);

    public static FeatureDto ToDto(this Feature f) => new(
        f.Id, f.WorkspaceId, f.ProductId, f.Name, f.Description, f.Status, f.ShippedDate,
        f.Deltas.Select(ToDto).ToList());
}
