using System.Text.Json;
using ProductManager.Api.Contracts;
using ProductManager.Domain.Common;
using ProductManager.Domain.Facts;

namespace ProductManager.Api.Simulation;

/// <summary>
/// Reads observed covariate history back out of the bi-temporal facts ledger. Observations are
/// stored as ordinary <see cref="FactEvent"/>s — kind <c>CovariateObserved</c>, subject the
/// covariate, payload <c>{"value": …}</c> — so history and calibration evidence share one
/// append-only ledger instead of a parallel table that could disagree with it.
/// </summary>
public static class CovariateHistoryReader
{
    /// <summary>True when this fact is a covariate reading for the given covariate.</summary>
    public static bool IsObservationOf(FactEvent fact, Guid covariateId) =>
        fact.Kind == FactEventKind.CovariateObserved
        && fact.SubjectType == FactMapping.CovariateSubjectType
        && fact.SubjectId == covariateId;

    /// <summary>
    /// Collapses raw facts to one value per (covariate, effective date): the latest *recorded*
    /// answer wins, which is what "what we knew as of D" means when a reading was later corrected.
    /// Facts whose payload carries no numeric <c>value</c> are skipped rather than guessed at.
    /// </summary>
    public static List<CovariateObservation> ToObservations(IEnumerable<FactEvent> facts)
    {
        var latest = new Dictionary<(Guid Covariate, DateOnly Effective), (DateTimeOffset Recorded, double Value)>();

        foreach (var fact in facts)
        {
            if (fact.Kind != FactEventKind.CovariateObserved) continue;
            if (fact.SubjectType != FactMapping.CovariateSubjectType || fact.SubjectId is null) continue;
            if (!TryReadValue(fact.PayloadJson, out double value)) continue;

            var key = (fact.SubjectId.Value, fact.EffectiveDate);
            if (latest.TryGetValue(key, out var existing) && existing.Recorded >= fact.RecordedDate) continue;
            latest[key] = (fact.RecordedDate, value);
        }

        return latest
            .Select(kv => new CovariateObservation(kv.Key.Covariate, kv.Key.Effective, kv.Value.Value))
            .OrderBy(o => o.CovariateId).ThenBy(o => o.EffectiveDate)
            .ToList();
    }

    /// <summary>Pulls the numeric <c>value</c> out of a fact payload. False when it isn't there.</summary>
    public static bool TryReadValue(string? payloadJson, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty(FactMapping.ValuePayloadProperty, out var property)) return false;
            return property.TryGetDouble(out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
