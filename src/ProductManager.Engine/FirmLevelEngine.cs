using System.Diagnostics;
using System.Numerics;

namespace ProductManager.Engine;

/// <summary>
/// Engine v1: firm-level choice Monte Carlo.
///
/// Firm state is vectorized, not an object graph: per MC path the engine keeps flat arrays
/// indexed by firm (segment index, sampled price sensitivity, consolidation preference, an
/// importance-weight matrix [firm * processCount + process]) and by (firm, product) pairs
/// (held-module bitmask ulong[firm * productCount + product], price level). Business-process
/// scopes, coverage, and module sets are 64-bit masks (hence the 64-process limit), so
/// consideration filtering and module bookkeeping are AND/OR/popcount operations.
///
/// Per monthly tick per path, per segment:
///   1. newFirms ~ Poisson(arrival * mult): append synthetic prospect firms (latents sampled
///      from segment distributions at creation).
///   2. new-business opportunities ~ Poisson(prospectCount * conversion * mult), drawn without
///      replacement from non-busy prospect firms.
///   3. re-evaluations ~ Binomial(clientCount, churnHazard * mult): a client firm puts one of
///      its existing relationships in play (incumbent stays without switching cost; the outside
///      option means dropping the provider — churn emerges from choice, it is not a direct rate).
///   4. cross-sell probes ~ Binomial(clientCount, crossSellRate * mult): an opportunity scoped
///      to processes the firm doesn't have covered.
/// Each opportunity: channel draw -> scope draw (broad vs clusters) -> consideration set
/// (pricing, channel carry, capacity, hard constraints, nonzero coverage) -> utilities
/// (importance x effective coverage - price - workarounds - switching cost + integration +
/// consolidation + brand + channel bias) -> logit draw incl. the outside option -> lognormal
/// sales-cycle delay -> the decision's effects (relationship create/extend/terminate, bookings,
/// one possible scope expansion) apply at the decision month. Choices are drawn at open and
/// applied at maturity (the firm is "busy" in between), a standard discrete-event simplification
/// of "mid-cycle".
///
/// Output trajectories use the exact v0 format (same 7 metrics, quantile assembly, NPV math via
/// shared internals); our-perspective semantics: Clients/pool/wins/churn/revenue all measure our
/// products. Deterministic per (input, seed): each path owns an independent RNG stream derived
/// from (seed, pathIndex); paths run in parallel but all cross-path reductions happen in a fixed
/// sequential order afterward.
/// </summary>
public static class FirmLevelEngine
{
    private const int MetricCount = EngineInternals.MetricCount;
    private const int MaxProcesses = 64;

    public static FirmLevelResult Run(FirmLevelInput input)
    {
        var model = StaticModel.Build(input); // validates
        var sw = Stopwatch.StartNew();

        int segCount = model.SegCount;
        int months = input.HorizonMonths;
        int paths = input.Paths;
        int chanSlots = model.ChannelCount + 1; // + implicit direct

        // Metric storage: [segment or total][metric][path * months + month].
        int slots = segCount + 1;
        var metrics = new double[slots][][];
        for (int s = 0; s < slots; s++)
        {
            metrics[s] = new double[MetricCount][];
            for (int m = 0; m < MetricCount; m++) metrics[s][m] = new double[paths * months];
        }

        var npvPerPath = new double[paths];

        // Per-path diagnostics, reduced sequentially after the parallel loop.
        var diagFunnel = new double[paths][]; // per path: seg * 6 (opened,qualified,won,lost,noDecision,stillOpen)
        var diagChannel = new double[paths][]; // per path: chan * 2 (opened, won)
        var diagScalars = new double[paths][]; // cycleSum, cycleCount, expansions, gapSum, gapCount, synthetic

        try
        {
            Parallel.For(
                0,
                paths,
                () => new PathState(model),
                (p, _, state) =>
                {
                    state.RunPath(p, input, metrics, npvPerPath);
                    diagFunnel[p] = state.ExportFunnel();
                    diagChannel[p] = state.ExportChannel();
                    diagScalars[p] = state.ExportScalars();
                    return state;
                },
                _ => { });
        }
        catch (AggregateException ae)
        {
            // Guard-rail failures (e.g. the MaxFlowMultiplier check) must surface as the same
            // ArgumentException the aggregate engine throws, not as an AggregateException.
            var inner = ae.Flatten().InnerExceptions
                .OfType<ArgumentException>()
                .FirstOrDefault();
            if (inner is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }

        // ---- Trajectory + NPV summaries (identical shape to v0) ----
        var segmentResults = new SegmentTrajectories[segCount];
        for (int s = 0; s < segCount; s++)
            segmentResults[s] = EngineInternals.Summarize(input.Segments[s].Id, metrics[s], paths, months);
        var total = EngineInternals.Summarize("total", metrics[segCount], paths, months);
        var npvSummary = EngineInternals.SummarizeScalar(npvPerPath);

        // ---- Diagnostics reduction (fixed order over paths for determinism) ----
        var funnelSum = new double[segCount * 6];
        var chanSum = new double[chanSlots * 2];
        double cycleSum = 0, cycleCount = 0, expansions = 0, gapMeanSum = 0, synthetic = 0;
        int gapPaths = 0;
        for (int p = 0; p < paths; p++)
        {
            var f = diagFunnel[p];
            for (int i = 0; i < funnelSum.Length; i++) funnelSum[i] += f[i];
            var c = diagChannel[p];
            for (int i = 0; i < chanSum.Length; i++) chanSum[i] += c[i];
            var d = diagScalars[p];
            cycleSum += d[0];
            cycleCount += d[1];
            expansions += d[2];
            if (d[4] > 0) { gapMeanSum += d[3] / d[4]; gapPaths++; }
            synthetic += d[5];
        }

        var segFunnels = new SegmentFunnel[segCount];
        for (int s = 0; s < segCount; s++)
        {
            double opened = funnelSum[s * 6 + 0] / paths;
            double won = funnelSum[s * 6 + 2] / paths;
            segFunnels[s] = new SegmentFunnel(
                input.Segments[s].Id,
                opened,
                funnelSum[s * 6 + 1] / paths,
                won,
                funnelSum[s * 6 + 3] / paths,
                funnelSum[s * 6 + 4] / paths,
                funnelSum[s * 6 + 5] / paths,
                opened > 0 ? won / opened : 0.0);
        }

        var chanFunnels = new ChannelFunnel[chanSlots];
        for (int c = 0; c < chanSlots; c++)
        {
            double opened = chanSum[c * 2] / paths;
            double won = chanSum[c * 2 + 1] / paths;
            string id = c < model.ChannelCount ? input.Channels[c].Id : "direct";
            chanFunnels[c] = new ChannelFunnel(id, opened, won, opened > 0 ? won / opened : 0.0);
        }

        var diagnostics = new FirmLevelDiagnostics(
            segFunnels,
            chanFunnels,
            cycleCount > 0 ? cycleSum / cycleCount : 0.0,
            expansions / paths,
            gapPaths > 0 ? gapMeanSum / gapPaths : 0.0,
            synthetic / paths);

        sw.Stop();
        var engineResult = new EngineResult(
            segmentResults, total, npvSummary, npvPerPath,
            paths, input.Seed, months, sw.ElapsedMilliseconds);
        return new FirmLevelResult(engineResult, diagnostics);
    }

    // =========================================================================================
    // Static model: everything shareable across paths, precomputed once.
    // =========================================================================================

    private sealed class StaticModel
    {
        internal FirmLevelInput Input = null!;
        internal int SegCount, ProcCount, ProdCount, ChannelCount, ClusterCount, Months;
        internal Dictionary<string, int> SegIndex = null!;
        internal ulong AllProcMask;
        internal int[] ClusterOfProc = null!;
        internal ulong[] ClusterMask = null!;

        // Per segment.
        internal SegModel[] Segs = null!;
        // Per (segment, product): [seg][prod].
        internal SegProd[][] Prods = null!;
        internal bool[] IsOurs = null!;
        // Integration quality [j * ProdCount + k].
        internal double[] Integration = null!;
        // Channel data.
        internal bool[][] ChannelCarries = null!; // [chanSlot][prod] (direct slot carries all)
        internal double[][] ChannelBias = null!;  // [chanSlot][prod]
        // Event multipliers [seg][flow * months + t].
        internal double[][] EventMult = null!;

        // Base firm roster + synthetic fill (deterministic).
        internal int BaseFirmCount;
        internal int[] BaseSegIdx = null!;
        internal ulong[] BaseHeld = null!;      // [firm * ProdCount + prod]
        internal double[] BasePriceLvl = null!; // [firm * ProdCount + prod]
        internal double[]?[] FixedImportance = null!; // per firm: fixed row (length ProcCount) or null => latent
        internal DistributionSpec?[] FirmPriceSens = null!; // null => segment default
        internal double[] FirmConsol = null!;
        internal int SyntheticFillCount;

        internal static StaticModel Build(FirmLevelInput input)
        {
            Validate(input);
            var m = new StaticModel
            {
                Input = input,
                SegCount = input.Segments.Count,
                ProcCount = input.Processes.Count,
                ProdCount = input.Products.Count,
                ChannelCount = input.Channels.Count,
                Months = input.HorizonMonths,
            };

            int P = m.ProcCount, J = m.ProdCount;
            m.AllProcMask = P == 64 ? ulong.MaxValue : (1UL << P) - 1UL;

            m.SegIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int s = 0; s < m.SegCount; s++) m.SegIndex[input.Segments[s].Id] = s;
            var procIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int p = 0; p < P; p++) procIndex[input.Processes[p].Id] = p;
            var prodIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < J; j++) prodIndex[input.Products[j].Id] = j;

            // Clusters.
            m.ClusterOfProc = new int[P];
            var clusterIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var clusterMasks = new List<ulong>();
            for (int p = 0; p < P; p++)
            {
                string? cid = input.Processes[p].ClusterId;
                int c;
                if (cid is null) { c = clusterMasks.Count; clusterMasks.Add(0); }
                else if (!clusterIds.TryGetValue(cid, out c)) { c = clusterMasks.Count; clusterIds[cid] = c; clusterMasks.Add(0); }
                m.ClusterOfProc[p] = c;
                clusterMasks[c] |= 1UL << p;
            }
            m.ClusterMask = clusterMasks.ToArray();
            m.ClusterCount = clusterMasks.Count;

            m.IsOurs = new bool[J];
            for (int j = 0; j < J; j++) m.IsOurs[j] = input.Products[j].IsOurs;

            // Integration matrix (symmetric max).
            m.Integration = new double[J * J];
            for (int j = 0; j < J; j++)
            {
                foreach (var edge in input.Products[j].Integrations)
                {
                    int k = prodIndex[edge.OtherProductId];
                    if (k == j) continue;
                    m.Integration[j * J + k] = Math.Max(m.Integration[j * J + k], edge.Quality);
                    m.Integration[k * J + j] = Math.Max(m.Integration[k * J + j], edge.Quality);
                }
            }

            // Segments and (segment, product) tables.
            m.Segs = new SegModel[m.SegCount];
            m.Prods = new SegProd[m.SegCount][];
            for (int s = 0; s < m.SegCount; s++)
            {
                var seg = input.Segments[s];
                var sm = new SegModel
                {
                    ImportanceSpec = new DistributionSpec?[P],
                    IsHard = new bool[P],
                    ReqCov = new double[P],
                    WorkCost = new double[P],
                };
                foreach (var need in seg.ProcessNeeds)
                {
                    int p = procIndex[need.ProcessId];
                    sm.ImportanceSpec[p] = need.ImportanceWeight;
                    sm.IsHard[p] = need.IsHard;
                    sm.ReqCov[p] = need.RequiredCoverage;
                    sm.WorkCost[p] = need.WorkaroundCost;
                }

                // Scoping.
                foreach (var sc in input.Scoping)
                {
                    if (!string.Equals(sc.SegmentId, seg.Id, StringComparison.Ordinal)) continue;
                    sm.HasScoping = true;
                    sm.BroadProb = sc.BroadScopeProbability;
                    sm.ScopeCountSpec = sc.ScopeClusterCount;
                }

                // Sales cycles.
                sm.ProductCycle = new (bool Has, double Mu, double Sigma)[J];
                foreach (var cyc in input.Choice.SalesCycles)
                {
                    if (!string.Equals(cyc.SegmentId, seg.Id, StringComparison.Ordinal)) continue;
                    if (cyc.ProductId is null) { sm.HasDefaultCycle = true; sm.CycleMu = cyc.Mu; sm.CycleSigma = cyc.Sigma; }
                    else
                    {
                        int j = prodIndex[cyc.ProductId];
                        sm.ProductCycle[j] = (true, cyc.Mu, cyc.Sigma);
                    }
                }

                // Approach mix (normalized).
                double mixSum = 0;
                foreach (var a in seg.ApproachMix) mixSum += a.Share;
                var mix = new List<(string? Id, double Share)>();
                if (mixSum > 0)
                    foreach (var a in seg.ApproachMix) mix.Add((a.ApproachId, a.Share / mixSum));
                else
                    mix.Add((null, 1.0));

                // Channel origination table for this segment: weight = reach x originationShare
                // per listed channel, NOT renormalized when the sum is below 1 — the residual
                // 1 - sum originates through the implicit direct channel (no bias, full product
                // visibility). A sum above 1 is normalized down proportionally (zero direct); a
                // sum of exactly 1 also leaves zero direct.
                var chanW = new List<(int Chan, double W)>();
                for (int c = 0; c < m.ChannelCount; c++)
                {
                    var ch = input.Channels[c];
                    double reach = 0, orig = 0;
                    foreach (var r in ch.SegmentReach)
                        if (string.Equals(r.SegmentId, seg.Id, StringComparison.Ordinal)) reach = r.Value;
                    foreach (var o in ch.OriginationShare)
                        if (string.Equals(o.SegmentId, seg.Id, StringComparison.Ordinal)) orig = o.Value;
                    double w = reach * orig;
                    if (w > 0) chanW.Add((c, w));
                }
                double wSum = 0;
                foreach (var (_, w) in chanW) wSum += w;
                double norm = wSum > 1.0 ? wSum : 1.0;
                sm.ChannelCum = new (int Chan, double Cum)[chanW.Count];
                double cum = 0;
                for (int i = 0; i < chanW.Count; i++)
                {
                    cum += chanW[i].W / norm;
                    // Guard against floating-point shortfall: when the declared weights fully
                    // cover the segment, the last channel must absorb any residual mass.
                    double bound = i == chanW.Count - 1 && wSum >= 1.0 ? double.PositiveInfinity : cum;
                    sm.ChannelCum[i] = (chanW[i].Chan, bound);
                }

                m.Segs[s] = sm;

                // (segment, product) tables.
                m.Prods[s] = new SegProd[J];
                for (int j = 0; j < J; j++)
                {
                    var prod = input.Products[j];
                    var sp = new SegProd();
                    foreach (var price in prod.Pricing)
                    {
                        if (!string.Equals(price.SegmentId, seg.Id, StringComparison.Ordinal)) continue;
                        sp.Priced = true;
                        sp.Arpu = price.ArpuPerProcessPerMonth;
                        sp.ImplFee = price.ImplementationFee;
                    }
                    foreach (var b in prod.Brand)
                        if (string.Equals(b.SegmentId, seg.Id, StringComparison.Ordinal)) sp.Brand = b.Bias;

                    BuildCoverage(m, sm, sp, prod, seg.Id, mix, procIndex);

                    m.Prods[s][j] = sp;
                }

                // Flow parameters (v0 ParameterSpec reuse).
                var covIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int c = 0; c < input.Covariates.Count; c++) covIndex[input.Covariates[c].Id] = c;
                foreach (var param in input.Parameters)
                {
                    if (!string.Equals(param.SegmentId, seg.Id, StringComparison.Ordinal)) continue;
                    var link = param.Covariate is null
                        ? CovLink.None
                        : new CovLink(covIndex[param.Covariate.CovariateId], param.Covariate.Beta);
                    switch (param.FlowKind)
                    {
                        case FlowKind.NewFirmArrival: sm.ArrivalSpec = param.Distribution; sm.ArrivalCov = link; break;
                        case FlowKind.ConversionToMarket: sm.ConversionSpec = param.Distribution; sm.ConversionCov = link; break;
                        case FlowKind.ChurnHazard: sm.ChurnSpec = param.Distribution; sm.ChurnCov = link; break;
                        case FlowKind.CrossSellRate when param.ModuleId is null:
                            sm.CrossSpec = param.Distribution; sm.CrossCov = link; break;
                        // WinRate and per-module CrossSellRate have no firm-level meaning: win
                        // probability emerges from utilities; modules are scope-derived.
                    }
                }
            }

            // Channels (+ implicit direct slot at index ChannelCount).
            int chanSlots = m.ChannelCount + 1;
            m.ChannelCarries = new bool[chanSlots][];
            m.ChannelBias = new double[chanSlots][];
            for (int c = 0; c < chanSlots; c++)
            {
                m.ChannelCarries[c] = new bool[J];
                m.ChannelBias[c] = new double[J];
                if (c == m.ChannelCount)
                {
                    Array.Fill(m.ChannelCarries[c], true);
                    continue;
                }
                var ch = input.Channels[c];
                if (ch.CarriesProducts is null) Array.Fill(m.ChannelCarries[c], true);
                else foreach (var pid in ch.CarriesProducts) m.ChannelCarries[c][prodIndex[pid]] = true;
                foreach (var b in ch.ProductBias) m.ChannelBias[c][prodIndex[b.ProductId]] = b.Bias;
            }

            m.EventMult = EngineInternals.BuildEventMultipliers(input.Events, m.SegIndex, m.SegCount, m.Months);

            // ---- Base firm roster + deterministic synthetic fill ----
            var segIdx = new List<int>();
            var held = new List<ulong[]>();
            var priceLvl = new List<double[]>();
            var fixedImp = new List<double[]?>();
            var priceSens = new List<DistributionSpec?>();
            var consol = new List<double>();
            var isFillSynthetic = new List<bool>();

            foreach (var firm in input.Firms)
            {
                int s = m.SegIndex[firm.SegmentId];
                segIdx.Add(s);
                var h = new ulong[J];
                var pl = new double[J];
                foreach (var rel in firm.Relationships)
                {
                    int j = prodIndex[rel.ProductId];
                    ulong mask = 0;
                    foreach (var pid in rel.ProcessIds) mask |= 1UL << procIndex[pid];
                    h[j] |= mask;
                    pl[j] = rel.PriceLevel;
                }
                held.Add(h);
                priceLvl.Add(pl);
                if (firm.ImportanceWeights is null) fixedImp.Add(null);
                else
                {
                    var row = new double[P];
                    foreach (var w in firm.ImportanceWeights) row[procIndex[w.ProcessId]] = w.Weight;
                    fixedImp.Add(row);
                }
                priceSens.Add(firm.PriceSensitivity);
                consol.Add(firm.ConsolidationPreference ?? input.Segments[s].ConsolidationPreference);
                isFillSynthetic.Add(firm.IsSynthetic);
            }

            int fillCount = 0;
            for (int s = 0; s < m.SegCount; s++)
            {
                var seg = input.Segments[s];
                // Prospect fill.
                int rosterProspects = 0;
                for (int f = 0; f < input.Firms.Count; f++)
                    if (segIdx[f] == s && f < input.Firms.Count && input.Firms[f].Relationships.Count == 0)
                        rosterProspects++;
                int fillP = Math.Max(0, (int)Math.Round(seg.InitialProspectPool) - rosterProspects);
                for (int i = 0; i < fillP; i++)
                {
                    segIdx.Add(s);
                    held.Add(new ulong[J]);
                    priceLvl.Add(new double[J]);
                    fixedImp.Add(null);
                    priceSens.Add(null);
                    consol.Add(seg.ConsolidationPreference);
                    isFillSynthetic.Add(true);
                    fillCount++;
                }
                // Client fill per target product.
                foreach (var target in seg.InitialClientTargets)
                {
                    int j = prodIndex[target.ProductId];
                    int rosterHolding = 0;
                    for (int f = 0; f < input.Firms.Count; f++)
                        if (segIdx[f] == s && held[f][j] != 0)
                            rosterHolding++;
                    int fillC = Math.Max(0, target.Count - rosterHolding);
                    ulong modules = m.Prods[s][j].Epochs[0].CovMask; // month-0 coverage
                    for (int i = 0; i < fillC; i++)
                    {
                        segIdx.Add(s);
                        var h = new ulong[J];
                        h[j] = modules;
                        var pl = new double[J];
                        pl[j] = 1.0;
                        held.Add(h);
                        priceLvl.Add(pl);
                        fixedImp.Add(null);
                        priceSens.Add(null);
                        consol.Add(seg.ConsolidationPreference);
                        isFillSynthetic.Add(true);
                        fillCount++;
                    }
                }
            }

            int N0 = segIdx.Count;
            m.BaseFirmCount = N0;
            m.SyntheticFillCount = fillCount;
            m.BaseSegIdx = segIdx.ToArray();
            m.BaseHeld = new ulong[N0 * J];
            m.BasePriceLvl = new double[N0 * J];
            for (int f = 0; f < N0; f++)
            {
                Array.Copy(held[f], 0, m.BaseHeld, f * J, J);
                Array.Copy(priceLvl[f], 0, m.BasePriceLvl, f * J, J);
            }
            m.FixedImportance = fixedImp.ToArray();
            m.FirmPriceSens = priceSens.ToArray();
            m.FirmConsol = consol.ToArray();
            return m;
        }

        private static void Validate(FirmLevelInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Segments is null || input.Segments.Count == 0)
                throw new ArgumentException("At least one segment is required.", nameof(input));
            if (input.HorizonMonths < 1)
                throw new ArgumentException("HorizonMonths must be >= 1.", nameof(input));
            if (input.Paths < 1)
                throw new ArgumentException("Paths must be >= 1.", nameof(input));
            if (input.Processes is null || input.Processes.Count == 0)
                throw new ArgumentException("At least one business process is required.", nameof(input));
            if (input.Processes.Count > MaxProcesses)
                throw new ArgumentException(
                    $"The firm-level engine supports at most {MaxProcesses} business processes " +
                    $"(scopes are 64-bit masks); got {input.Processes.Count}. Collapse sub-processes " +
                    "into their parents or split the market.", nameof(input));
            if (input.Choice is null)
                throw new ArgumentException("Choice parameters are required.", nameof(input));
            if (!(input.Choice.PriceScale > 0))
                throw new ArgumentException("Choice.PriceScale must be > 0.", nameof(input));

            var segIds = RequireUnique(input.Segments.Select(s => s.Id), "segment", nameof(input));
            var procIds = RequireUnique(input.Processes.Select(p => p.Id), "process", nameof(input));
            var prodIds = RequireUnique(input.Products.Select(p => p.Id), "product", nameof(input));
            var chanIds = RequireUnique(input.Channels.Select(c => c.Id), "channel", nameof(input));
            RequireUnique(input.Firms.Select(f => f.Id), "firm", nameof(input));

            foreach (var firm in input.Firms)
            {
                if (!segIds.Contains(firm.SegmentId))
                    throw new ArgumentException(
                        $"Firm '{firm.Id}' references unknown segment '{firm.SegmentId}'. " +
                        "Every firm must belong to a declared segment.", nameof(input));
                foreach (var rel in firm.Relationships)
                {
                    if (!prodIds.Contains(rel.ProductId))
                        throw new ArgumentException(
                            $"Firm '{firm.Id}' has a relationship with unknown product '{rel.ProductId}'.",
                            nameof(input));
                    foreach (var pid in rel.ProcessIds)
                        if (!procIds.Contains(pid))
                            throw new ArgumentException(
                                $"Firm '{firm.Id}' relationship with '{rel.ProductId}' holds unknown " +
                                $"process '{pid}'.", nameof(input));
                }
                foreach (var w in firm.ImportanceWeights ?? [])
                    if (!procIds.Contains(w.ProcessId))
                        throw new ArgumentException(
                            $"Firm '{firm.Id}' has an importance weight for unknown process '{w.ProcessId}'.",
                            nameof(input));
            }

            foreach (var prod in input.Products)
            {
                foreach (var cell in prod.Coverage)
                {
                    if (!procIds.Contains(cell.ProcessId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has coverage for unknown process '{cell.ProcessId}'.",
                            nameof(input));
                    if (cell.SegmentId is not null && !segIds.Contains(cell.SegmentId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has a coverage cell scoped to unknown segment " +
                            $"'{cell.SegmentId}'.", nameof(input));
                }
                foreach (var step in prod.CoverageSteps ?? [])
                {
                    if (!procIds.Contains(step.ProcessId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has a coverage step for unknown process " +
                            $"'{step.ProcessId}'.", nameof(input));
                    if (step.SegmentId is not null && !segIds.Contains(step.SegmentId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has a coverage step scoped to unknown segment " +
                            $"'{step.SegmentId}'.", nameof(input));
                    if (step.Month < 0)
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has a coverage step with negative month " +
                            $"{step.Month}; months are 0-based simulation months.", nameof(input));
                }
                foreach (var price in prod.Pricing)
                    if (!segIds.Contains(price.SegmentId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has pricing for unknown segment '{price.SegmentId}'.",
                            nameof(input));
                foreach (var edge in prod.Integrations)
                    if (!prodIds.Contains(edge.OtherProductId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has an integration with unknown product " +
                            $"'{edge.OtherProductId}'.", nameof(input));
                foreach (var b in prod.Brand)
                    if (!segIds.Contains(b.SegmentId))
                        throw new ArgumentException(
                            $"Product '{prod.Id}' has a brand bias for unknown segment '{b.SegmentId}'.",
                            nameof(input));
            }

            foreach (var ch in input.Channels)
            {
                foreach (var r in ch.SegmentReach)
                    if (!segIds.Contains(r.SegmentId))
                        throw new ArgumentException(
                            $"Channel '{ch.Id}' has reach into unknown segment '{r.SegmentId}'.", nameof(input));
                foreach (var o in ch.OriginationShare)
                    if (!segIds.Contains(o.SegmentId))
                        throw new ArgumentException(
                            $"Channel '{ch.Id}' has origination share for unknown segment '{o.SegmentId}'.",
                            nameof(input));
                foreach (var b in ch.ProductBias)
                    if (!prodIds.Contains(b.ProductId))
                        throw new ArgumentException(
                            $"Channel '{ch.Id}' has a bias for unknown product '{b.ProductId}'.", nameof(input));
                foreach (var pid in ch.CarriesProducts ?? [])
                    if (!prodIds.Contains(pid))
                        throw new ArgumentException(
                            $"Channel '{ch.Id}' carries unknown product '{pid}'.", nameof(input));
            }

            foreach (var seg in input.Segments)
            {
                foreach (var need in seg.ProcessNeeds)
                    if (!procIds.Contains(need.ProcessId))
                        throw new ArgumentException(
                            $"Segment '{seg.Id}' has a need for unknown process '{need.ProcessId}'.",
                            nameof(input));
                foreach (var target in seg.InitialClientTargets)
                    if (!prodIds.Contains(target.ProductId))
                        throw new ArgumentException(
                            $"Segment '{seg.Id}' has an initial client target for unknown product " +
                            $"'{target.ProductId}'.", nameof(input));
            }

            foreach (var sc in input.Scoping)
                if (!segIds.Contains(sc.SegmentId))
                    throw new ArgumentException(
                        $"Opportunity scoping references unknown segment '{sc.SegmentId}'.", nameof(input));

            foreach (var cyc in input.Choice.SalesCycles)
            {
                if (!segIds.Contains(cyc.SegmentId))
                    throw new ArgumentException(
                        $"Sales-cycle spec references unknown segment '{cyc.SegmentId}'.", nameof(input));
                if (cyc.ProductId is not null && !prodIds.Contains(cyc.ProductId))
                    throw new ArgumentException(
                        $"Sales-cycle spec references unknown product '{cyc.ProductId}'.", nameof(input));
            }

            foreach (var param in input.Parameters)
            {
                if (param.ModuleId is not null && param.FlowKind != FlowKind.CrossSellRate)
                    throw new ArgumentException(
                        $"Parameter ({param.SegmentId}, {param.FlowKind}) has a moduleId but only " +
                        "CrossSellRate parameters may.", nameof(input));
                if (param.Covariate is not null
                    && input.Covariates.All(c => !string.Equals(c.Id, param.Covariate.CovariateId, StringComparison.Ordinal)))
                    throw new ArgumentException(
                        $"Parameter ({param.SegmentId}, {param.FlowKind}) links unknown covariate " +
                        $"'{param.Covariate.CovariateId}'.", nameof(input));
            }

            EngineInternals.ValidateCovariates(input.Covariates, input.Parameters, input.HorizonMonths, nameof(input));
        }

        private static HashSet<string> RequireUnique(IEnumerable<string> ids, string kind, string paramName)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id))
                    throw new ArgumentException($"Every {kind} id must be non-empty.", paramName);
                if (!set.Add(id))
                    throw new ArgumentException($"Duplicate {kind} id '{id}'.", paramName);
            }
            return set;
        }

        /// <summary>One (process, approach?, segment-specific?) coverage cell over time. Base
        /// scores and steps are resolved INDEPENDENTLY through the precedence chain (see
        /// <see cref="BuildCoverage"/>): <see cref="TryBase"/> answers only for base
        /// <see cref="CoverageCell"/>s, <see cref="TryStep"/> only for the latest applicable
        /// <see cref="CoverageStep"/> (step month &lt;= t). A cell missing one of the two lets
        /// resolution of that half fall through to the more generic cells, so a segment-scoped
        /// base cell no longer masks a more generic cell's roadmap steps.</summary>
        private sealed class TimelineCell
        {
            internal bool HasBase;
            internal double BaseScore;
            internal List<(int Month, double Score)>? Steps; // sorted by month, stable

            internal bool TryBase(out double score)
            {
                score = BaseScore;
                return HasBase;
            }

            internal bool TryStep(int t, out double score)
            {
                score = 0.0;
                bool has = false;
                if (Steps is not null)
                {
                    foreach (var (month, s) in Steps)
                    {
                        if (month > t) break;
                        score = s;
                        has = true;
                    }
                }
                return has;
            }
        }

        /// <summary>
        /// Builds the coverage-derived data of one (segment, product): capacity mask plus one
        /// <see cref="CovEpoch"/> per span of months with constant resolved coverage. Cells and
        /// steps scoped to another segment are ignored entirely. The effective score per
        /// (process, approach in the segment's mix, month t) is
        /// <c>max(resolvedBase, resolvedStep(t))</c>: base cells and the latest applicable
        /// <see cref="CoverageStep"/> (step month &lt;= t) each resolve INDEPENDENTLY through
        /// the exact-then-generic precedence chain, approach first — (approach, segment) &gt;
        /// (approach, generic) &gt; (default, segment) &gt; (default, generic) — and the larger
        /// of the two wins. A segment-scoped base cell therefore shadows the generic BASE score
        /// but never a more generic cell's steps; a scoped step still beats a generic step for
        /// its segment; and steps never lower effective coverage. Hard-constraint qualification
        /// and every coverage-derived mask (CovMask, HardFailMask, soft costs, cluster
        /// adjacency) are built from this effective score. Coverage is evaluated as of the
        /// opportunity's open month, so epochs split only at step months — products without
        /// applicable steps get a single epoch and pay nothing per month.
        /// </summary>
        private static void BuildCoverage(
            StaticModel m,
            SegModel sm,
            SegProd sp,
            FirmLevelProductInput prod,
            string segId,
            List<(string? Id, double Share)> mix,
            Dictionary<string, int> procIndex)
        {
            int P = m.ProcCount;

            // Timeline cells keyed by (process, approach, segment-specific), restricted to cells
            // applicable to this segment. Later list entries win within the same key.
            var cells = new Dictionary<(int Proc, string? Approach, bool SegSpecific), TimelineCell>();
            foreach (var cell in prod.Coverage)
            {
                if (cell.SegmentId is not null && !string.Equals(cell.SegmentId, segId, StringComparison.Ordinal))
                    continue;
                var key = (procIndex[cell.ProcessId], cell.ApproachId, cell.SegmentId is not null);
                if (!cells.TryGetValue(key, out var tc)) cells[key] = tc = new TimelineCell();
                tc.HasBase = true;
                tc.BaseScore = cell.Score;
            }
            foreach (var step in prod.CoverageSteps ?? [])
            {
                if (step.SegmentId is not null && !string.Equals(step.SegmentId, segId, StringComparison.Ordinal))
                    continue;
                var key = (procIndex[step.ProcessId], step.ApproachId, step.SegmentId is not null);
                if (!cells.TryGetValue(key, out var tc)) cells[key] = tc = new TimelineCell();
                (tc.Steps ??= []).Add((step.Month, step.Score));
            }
            foreach (var tc in cells.Values)
                if (tc.Steps is { Count: > 1 })
                    tc.Steps = tc.Steps.OrderBy(s => s.Month).ToList(); // stable: list-order ties, later wins

            // Capacity never steps: it comes from base cells only, with segment shadowing at the
            // (process, approach) key. Any applicable cell without capacity gates its process.
            var capCells = new Dictionary<(int Proc, string? Approach), CoverageCell>();
            foreach (var cell in prod.Coverage)
            {
                if (cell.SegmentId is not null && !string.Equals(cell.SegmentId, segId, StringComparison.Ordinal))
                    continue;
                var key = (procIndex[cell.ProcessId], cell.ApproachId);
                if (!capCells.TryGetValue(key, out var existing)
                    || existing.SegmentId is null || cell.SegmentId is not null)
                    capCells[key] = cell;
            }
            foreach (var cell in capCells.Values)
                if (!cell.HasCapacity)
                    sp.NoCapacityMask |= 1UL << procIndex[cell.ProcessId];

            // Epoch boundaries: month 0 plus every step month that can fall inside the horizon.
            var changeMonths = new SortedSet<int>();
            foreach (var tc in cells.Values)
            {
                if (tc.Steps is null) continue;
                foreach (var (month, _) in tc.Steps)
                    if (month > 0 && month < m.Months)
                        changeMonths.Add(month);
            }
            var starts = new int[1 + changeMonths.Count];
            int e = 1;
            foreach (int month in changeMonths) starts[e++] = month;

            // Effective score = max(resolvedBase, resolvedStep(t)). Base cells and steps are
            // resolved independently through the same precedence chain, so a segment-scoped base
            // cell does not mask a more generic cell's steps (and a scoped step still beats a
            // generic step for its segment). Steps never lower effective coverage: a resolved
            // step below the resolved base loses the max.
            bool ResolveBase(int p, string? aid, out double score)
            {
                if (aid is not null)
                {
                    if (cells.TryGetValue((p, aid, true), out var c) && c.TryBase(out score)) return true;
                    if (cells.TryGetValue((p, aid, false), out c) && c.TryBase(out score)) return true;
                }
                if (cells.TryGetValue((p, null, true), out var g) && g.TryBase(out score)) return true;
                if (cells.TryGetValue((p, null, false), out g) && g.TryBase(out score)) return true;
                score = 0.0;
                return false;
            }

            bool ResolveStep(int p, string? aid, int t, out double score)
            {
                if (aid is not null)
                {
                    if (cells.TryGetValue((p, aid, true), out var c) && c.TryStep(t, out score)) return true;
                    if (cells.TryGetValue((p, aid, false), out c) && c.TryStep(t, out score)) return true;
                }
                if (cells.TryGetValue((p, null, true), out var g) && g.TryStep(t, out score)) return true;
                if (cells.TryGetValue((p, null, false), out g) && g.TryStep(t, out score)) return true;
                score = 0.0;
                return false;
            }

            double ResolveScore(int p, string? aid, int t)
            {
                bool hasBase = ResolveBase(p, aid, out double baseScore);
                bool hasStep = ResolveStep(p, aid, t, out double stepScore);
                if (hasBase && hasStep) return Math.Max(baseScore, stepScore);
                if (hasBase) return baseScore;
                return hasStep ? stepScore : 0.0;
            }

            sp.Epochs = new CovEpoch[starts.Length];
            for (int i = 0; i < starts.Length; i++)
            {
                int t = starts[i];
                var ep = new CovEpoch { Eff = new double[P], SoftCost = new double[P] };
                for (int p = 0; p < P; p++)
                {
                    double eff = 0;
                    foreach (var (aid, share) in mix)
                        eff += share * ResolveScore(p, aid, t);
                    ep.Eff[p] = eff;
                    if (eff > 0) ep.CovMask |= 1UL << p;
                    if (sm.ReqCov[p] > 0 && eff < sm.ReqCov[p])
                    {
                        if (sm.IsHard[p]) ep.HardFailMask |= 1UL << p;
                        else ep.SoftCost[p] = sm.WorkCost[p];
                    }
                    else if (sm.ReqCov[p] <= 0 && sm.WorkCost[p] > 0 && eff <= 0 && !sm.IsHard[p])
                    {
                        // A soft need with a workaround cost but no explicit threshold: not
                        // covering the process at all still incurs the workaround cost.
                        ep.SoftCost[p] = sm.WorkCost[p];
                    }
                }

                // Mean effective coverage per cluster (for expansion adjacency).
                ep.ClusterEff = new double[m.ClusterCount];
                for (int c = 0; c < m.ClusterCount; c++)
                {
                    ulong mask = m.ClusterMask[c];
                    double sum = 0;
                    int n = 0;
                    for (int p = 0; p < P; p++)
                        if ((mask & (1UL << p)) != 0) { sum += ep.Eff[p]; n++; }
                    ep.ClusterEff[c] = n > 0 ? sum / n : 0.0;
                }
                sp.Epochs[i] = ep;
            }

            if (starts.Length > 1)
            {
                sp.EpochOfMonth = new int[m.Months];
                int cur = 0;
                for (int t = 0; t < m.Months; t++)
                {
                    while (cur + 1 < starts.Length && starts[cur + 1] <= t) cur++;
                    sp.EpochOfMonth[t] = cur;
                }
            }
        }
    }

    private sealed class SegModel
    {
        internal DistributionSpec?[] ImportanceSpec = null!;
        internal bool[] IsHard = null!;
        internal double[] ReqCov = null!;
        internal double[] WorkCost = null!;
        internal bool HasScoping;
        internal double BroadProb;
        internal DistributionSpec? ScopeCountSpec;
        internal bool HasDefaultCycle;
        internal double CycleMu, CycleSigma;
        internal (bool Has, double Mu, double Sigma)[] ProductCycle = null!;
        internal (int Chan, double Cum)[] ChannelCum = null!;
        internal DistributionSpec ArrivalSpec = DistributionSpec.Point(0);
        internal DistributionSpec ConversionSpec = DistributionSpec.Point(0);
        internal DistributionSpec ChurnSpec = DistributionSpec.Point(0);
        internal DistributionSpec CrossSpec = DistributionSpec.Point(0);
        internal CovLink ArrivalCov = CovLink.None;
        internal CovLink ConversionCov = CovLink.None;
        internal CovLink ChurnCov = CovLink.None;
        internal CovLink CrossCov = CovLink.None;
    }

    /// <summary>Per-(segment, product) static data. Coverage-derived values live in
    /// <see cref="Epochs"/>: one snapshot per span of months with constant coverage (a single
    /// epoch unless CoverageSteps actually change this segment's resolved cells — the common
    /// case pays one null check, no per-month arrays).</summary>
    private sealed class SegProd
    {
        internal bool Priced;
        internal double Arpu, ImplFee, Brand;
        internal ulong NoCapacityMask;
        internal CovEpoch[] Epochs = null!;   // Epochs[0] applies from month 0
        internal int[]? EpochOfMonth;         // null when Epochs.Length == 1

        internal CovEpoch At(int t) => EpochOfMonth is null ? Epochs[0] : Epochs[EpochOfMonth[t]];
    }

    /// <summary>Coverage-derived values of one (segment, product) over one span of months.</summary>
    private sealed class CovEpoch
    {
        internal double[] Eff = null!;
        internal double[] SoftCost = null!;
        internal ulong CovMask, HardFailMask;
        internal double[] ClusterEff = null!;
    }

    private enum OppKind : byte { NewBusiness, ReEval, CrossSell }

    private struct Pending
    {
        internal int Firm, Seg, Chan, Winner, Incumbent, Delay;
        internal ulong Scope, ExpMask, Modules;
        internal OppKind Kind;
        internal bool HadOurCandidate;
        internal double WinnerU, BestOurU;
    }

    // =========================================================================================
    // Per-path mutable state (one instance per worker thread, reused across its paths).
    // =========================================================================================

    private sealed class PathState
    {
        private readonly StaticModel _m;
        private readonly int _procCount, _prodCount, _segCount, _months, _chanSlots;

        // Firm arrays (grow on synthetic arrivals).
        private int _capacity, _firmCount;
        private int[] _segIdx;
        private double[] _priceSens;
        private double[] _consol;
        private double[] _importance; // [firm * procCount + p]
        private ulong[] _held;        // [firm * prodCount + j]
        private double[] _priceLvl;   // [firm * prodCount + j]
        private ulong[] _heldUnion;
        private int[] _relCount;
        private int[] _ourRelCount;
        private double[] _ourMrr;
        private bool[] _busy;

        // Segment counters.
        private readonly int[] _prospectCount;
        private readonly int[] _ourClientCount;
        private readonly double[] _segOurMrr;

        // Monthly accumulators.
        private readonly double[] _monthWins, _monthChurn, _monthBookings, _monthImplFees;
        private readonly double[] _totalMargin;

        // Pending decision queue by month.
        private readonly List<Pending>[] _pending;

        // Scratch.
        private readonly List<int>[] _prospects, _clients;
        private readonly int[] _candProd;
        private readonly double[] _candU;
        private readonly int[] _clusterScratch;
        private readonly int[] _relScratch;
        private readonly double[] _covValues;

        // Diagnostics.
        private readonly double[] _funnel;   // seg * 6
        private readonly double[] _chanDiag; // chanSlot * 2
        private double _cycleSum, _cycleCount, _expansions, _gapSum, _gapCount, _synthetic;

        private EngineRandom _rng = null!;

        internal PathState(StaticModel m)
        {
            _m = m;
            _procCount = m.ProcCount;
            _prodCount = m.ProdCount;
            _segCount = m.SegCount;
            _months = m.Months;
            _chanSlots = m.ChannelCount + 1;

            _capacity = Math.Max(64, m.BaseFirmCount + 64);
            _segIdx = new int[_capacity];
            _priceSens = new double[_capacity];
            _consol = new double[_capacity];
            _importance = new double[_capacity * _procCount];
            _held = new ulong[_capacity * _prodCount];
            _priceLvl = new double[_capacity * _prodCount];
            _heldUnion = new ulong[_capacity];
            _relCount = new int[_capacity];
            _ourRelCount = new int[_capacity];
            _ourMrr = new double[_capacity];
            _busy = new bool[_capacity];

            _prospectCount = new int[_segCount];
            _ourClientCount = new int[_segCount];
            _segOurMrr = new double[_segCount];
            _monthWins = new double[_segCount];
            _monthChurn = new double[_segCount];
            _monthBookings = new double[_segCount];
            _monthImplFees = new double[_segCount];
            _totalMargin = new double[_months];
            _pending = new List<Pending>[_months];
            for (int t = 0; t < _months; t++) _pending[t] = [];
            _prospects = new List<int>[_segCount];
            _clients = new List<int>[_segCount];
            for (int s = 0; s < _segCount; s++) { _prospects[s] = []; _clients[s] = []; }
            _candProd = new int[_prodCount];
            _candU = new double[_prodCount];
            _clusterScratch = new int[m.ClusterCount];
            _relScratch = new int[_prodCount];
            _covValues = new double[m.Input.Covariates.Count * _months];
            _funnel = new double[_segCount * 6];
            _chanDiag = new double[_chanSlots * 2];
        }

        private void EnsureCapacity(int needed)
        {
            if (needed <= _capacity) return;
            int cap = Math.Max(_capacity, 64);
            while (cap < needed) cap *= 2;
            Array.Resize(ref _segIdx, cap);
            Array.Resize(ref _priceSens, cap);
            Array.Resize(ref _consol, cap);
            Array.Resize(ref _importance, cap * _procCount);
            Array.Resize(ref _held, cap * _prodCount);
            Array.Resize(ref _priceLvl, cap * _prodCount);
            Array.Resize(ref _heldUnion, cap);
            Array.Resize(ref _relCount, cap);
            Array.Resize(ref _ourRelCount, cap);
            Array.Resize(ref _ourMrr, cap);
            Array.Resize(ref _busy, cap);
            _capacity = cap;
        }

        internal double[] ExportFunnel() => (double[])_funnel.Clone();
        internal double[] ExportChannel() => (double[])_chanDiag.Clone();
        internal double[] ExportScalars()
            => [_cycleSum, _cycleCount, _expansions, _gapSum, _gapCount, _synthetic];

        internal void RunPath(int p, FirmLevelInput input, double[][][] metrics, double[] npvPerPath)
        {
            var m = _m;
            _rng = new EngineRandom(input.Seed, p);
            ResetState();

            // Latent attributes for base firms, in firm order (stable draw sequence).
            for (int f = 0; f < m.BaseFirmCount; f++) SampleLatents(f, m.BaseSegIdx[f], m.FixedImportance[f],
                m.FirmPriceSens[f], m.FirmConsol[f]);

            EngineInternals.ResolveCovariatePaths(input.Covariates, _months, _rng, _covValues);

            // Sample flow rates once per path (parameter uncertainty), segments in input order.
            var arrival = new double[_segCount];
            var conversion = new double[_segCount];
            var churn = new double[_segCount];
            var cross = new double[_segCount];
            for (int s = 0; s < _segCount; s++)
            {
                var sm = m.Segs[s];
                arrival[s] = Math.Max(0.0, _rng.Sample(sm.ArrivalSpec));
                conversion[s] = Math.Max(0.0, _rng.Sample(sm.ConversionSpec));
                churn[s] = Math.Max(0.0, _rng.Sample(sm.ChurnSpec));
                cross[s] = Math.Max(0.0, _rng.Sample(sm.CrossSpec));
            }

            for (int t = 0; t < _months; t++)
            {
                Array.Clear(_monthWins);
                Array.Clear(_monthChurn);
                Array.Clear(_monthBookings);
                Array.Clear(_monthImplFees);

                // 1) Synthetic new-firm arrivals.
                for (int s = 0; s < _segCount; s++)
                {
                    var sm = m.Segs[s];
                    var seg = input.Segments[s];
                    double mult = EngineInternals.CheckedMultiplier(
                        EngineInternals.CovariateMultiplier(sm.ArrivalCov, _covValues, _months, t)
                        * m.EventMult[s][(int)FlowKind.NewFirmArrival * _months + t],
                        seg.Id, FlowKind.NewFirmArrival, t, null);
                    long n = _rng.NextPoisson(Math.Max(0.0, arrival[s] * mult));
                    for (long i = 0; i < n; i++)
                    {
                        EnsureCapacity(_firmCount + 1);
                        int f = _firmCount++;
                        _segIdx[f] = s;
                        Array.Clear(_held, f * _prodCount, _prodCount);
                        Array.Clear(_priceLvl, f * _prodCount, _prodCount);
                        _heldUnion[f] = 0;
                        _relCount[f] = 0;
                        _ourRelCount[f] = 0;
                        _ourMrr[f] = 0;
                        _busy[f] = false;
                        SampleLatents(f, s, null, null, seg.ConsolidationPreference);
                        _prospectCount[s]++;
                        _synthetic++;
                    }
                }

                // 2) Bucket non-busy firms by segment and state.
                for (int s = 0; s < _segCount; s++) { _prospects[s].Clear(); _clients[s].Clear(); }
                for (int f = 0; f < _firmCount; f++)
                {
                    if (_busy[f]) continue;
                    if (_relCount[f] == 0) _prospects[_segIdx[f]].Add(f);
                    else _clients[_segIdx[f]].Add(f);
                }

                // 3) Open opportunities per segment: conversions, re-evaluations, cross-sell probes.
                for (int s = 0; s < _segCount; s++)
                {
                    var sm = m.Segs[s];
                    var seg = input.Segments[s];

                    double convMult = EngineInternals.CheckedMultiplier(
                        EngineInternals.CovariateMultiplier(sm.ConversionCov, _covValues, _months, t)
                        * m.EventMult[s][(int)FlowKind.ConversionToMarket * _months + t],
                        seg.Id, FlowKind.ConversionToMarket, t, null);
                    var prospects = _prospects[s];
                    long nConv = _rng.NextPoisson(prospects.Count * conversion[s] * convMult);
                    if (nConv > prospects.Count) nConv = prospects.Count;
                    for (long i = 0; i < nConv; i++)
                    {
                        int f = PickDistinct(prospects, (int)i);
                        OpenOpportunity(f, s, t, OppKind.NewBusiness, -1, 0);
                    }

                    double churnMult = EngineInternals.CheckedMultiplier(
                        EngineInternals.CovariateMultiplier(sm.ChurnCov, _covValues, _months, t)
                        * m.EventMult[s][(int)FlowKind.ChurnHazard * _months + t],
                        seg.Id, FlowKind.ChurnHazard, t, null);
                    var clients = _clients[s];
                    long nReEval = _rng.NextBinomial(clients.Count, EngineInternals.Clamp01(churn[s] * churnMult));
                    for (long i = 0; i < nReEval; i++)
                    {
                        int f = PickDistinct(clients, (int)i);
                        if (_busy[f]) continue;
                        // Pick one existing relationship uniformly.
                        int nRel = 0;
                        for (int j = 0; j < _prodCount; j++)
                            if (_held[f * _prodCount + j] != 0) _relScratch[nRel++] = j;
                        if (nRel == 0) continue;
                        int incumbent = _relScratch[(int)(_rng.NextUniform() * nRel) % nRel];
                        OpenOpportunity(f, s, t, OppKind.ReEval, incumbent, _held[f * _prodCount + incumbent]);
                    }

                    double crossMult = EngineInternals.CheckedMultiplier(
                        EngineInternals.CovariateMultiplier(sm.CrossCov, _covValues, _months, t)
                        * m.EventMult[s][(int)FlowKind.CrossSellRate * _months + t],
                        seg.Id, FlowKind.CrossSellRate, t, null);
                    long nCross = _rng.NextBinomial(clients.Count, EngineInternals.Clamp01(cross[s] * crossMult));
                    for (long i = 0; i < nCross; i++)
                    {
                        int f = clients[(int)(_rng.NextUniform() * clients.Count) % clients.Count];
                        if (_busy[f]) continue;
                        if ((m.AllProcMask & ~_heldUnion[f]) == 0) continue; // fully covered
                        OpenOpportunity(f, s, t, OppKind.CrossSell, -1, 0);
                    }
                }

                // 4) Apply decisions maturing this month (includes zero-delay opens from step 3).
                var due = _pending[t];
                for (int i = 0; i < due.Count; i++) ApplyDecision(due[i], input);
                due.Clear();

                // 5) Record month-end metrics.
                double marginTotal = 0;
                for (int s = 0; s < _segCount; s++)
                {
                    var costs = input.Segments[s].CostRates;
                    double mrr = _segOurMrr[s];
                    double margin = mrr + _monthImplFees[s]
                                    - (_ourClientCount[s] * (costs.ServicingPerClient + costs.InfraPerClient)
                                       + costs.InfraFixed + costs.RdDragPerMonth);
                    int idx = p * _months + t;
                    metrics[s][0][idx] = _ourClientCount[s];
                    metrics[s][1][idx] = _prospectCount[s];
                    metrics[s][2][idx] = _monthWins[s];
                    metrics[s][3][idx] = _monthChurn[s];
                    metrics[s][4][idx] = _monthBookings[s];
                    metrics[s][5][idx] = 12.0 * mrr;
                    metrics[s][6][idx] = margin;
                    marginTotal += margin;
                }
                int totalSlot = _segCount;
                for (int mi = 0; mi < MetricCount; mi++)
                {
                    double sum = 0;
                    int idx = p * _months + t;
                    for (int s = 0; s < _segCount; s++) sum += metrics[s][mi][idx];
                    metrics[totalSlot][mi][idx] = sum;
                }
                _totalMargin[t] = marginTotal;
            }

            npvPerPath[p] = EngineInternals.NpvForPath(
                _totalMargin, _months, input.DiscountRateAnnual, input.TerminalGrowthAnnual);
        }

        private void ResetState()
        {
            var m = _m;
            EnsureCapacity(Math.Max(1, m.BaseFirmCount));
            _firmCount = m.BaseFirmCount;
            Array.Copy(m.BaseSegIdx, _segIdx, m.BaseFirmCount);
            Array.Copy(m.BaseHeld, _held, m.BaseFirmCount * _prodCount);
            Array.Copy(m.BasePriceLvl, _priceLvl, m.BaseFirmCount * _prodCount);
            Array.Clear(_busy, 0, _capacity);
            Array.Clear(_prospectCount);
            Array.Clear(_ourClientCount);
            Array.Clear(_segOurMrr);
            Array.Clear(_totalMargin);
            for (int t = 0; t < _months; t++) _pending[t].Clear();
            Array.Clear(_funnel);
            Array.Clear(_chanDiag);
            _cycleSum = _cycleCount = _expansions = _gapSum = _gapCount = 0;
            _synthetic = m.SyntheticFillCount;

            for (int f = 0; f < m.BaseFirmCount; f++)
            {
                int s = _segIdx[f];
                ulong union = 0;
                int rel = 0, ourRel = 0;
                double mrr = 0;
                for (int j = 0; j < _prodCount; j++)
                {
                    ulong mask = _held[f * _prodCount + j];
                    if (mask == 0) continue;
                    union |= mask;
                    rel++;
                    if (m.IsOurs[j])
                    {
                        ourRel++;
                        mrr += BitOperations.PopCount(mask) * m.Prods[s][j].Arpu * _priceLvl[f * _prodCount + j];
                    }
                }
                _heldUnion[f] = union;
                _relCount[f] = rel;
                _ourRelCount[f] = ourRel;
                _ourMrr[f] = mrr;
                if (rel == 0) _prospectCount[s]++;
                if (ourRel > 0) { _ourClientCount[s]++; _segOurMrr[s] += mrr; }
            }
        }

        private void SampleLatents(int f, int s, double[]? fixedImp, DistributionSpec? priceSpec, double consol)
        {
            var sm = _m.Segs[s];
            int baseIdx = f * _procCount;
            if (fixedImp is not null)
            {
                Array.Copy(fixedImp, 0, _importance, baseIdx, _procCount);
            }
            else
            {
                for (int p = 0; p < _procCount; p++)
                {
                    var spec = sm.ImportanceSpec[p];
                    _importance[baseIdx + p] = spec is null ? 0.0 : Math.Max(0.0, _rng.Sample(spec));
                }
            }
            _priceSens[f] = _rng.Sample(priceSpec ?? _m.Input.Segments[s].PriceSensitivity);
            _consol[f] = consol;
        }

        /// <summary>Partial Fisher-Yates: swaps a uniformly chosen element from [i, n) into
        /// position i and returns it, so successive calls pick distinct elements.</summary>
        private int PickDistinct(List<int> list, int i)
        {
            int n = list.Count;
            int k = i + (int)(_rng.NextUniform() * (n - i)) % Math.Max(1, n - i);
            (list[i], list[k]) = (list[k], list[i]);
            return list[i];
        }

        private void OpenOpportunity(int f, int s, int t, OppKind kind, int incumbent, ulong reEvalScope)
        {
            var m = _m;
            var sm = m.Segs[s];
            _busy[f] = true;

            // Channel. A uniform beyond the last cumulative weight falls into the residual
            // 1 - sum(reach x originationShare): the implicit direct channel.
            int chan = m.ChannelCount; // implicit direct
            if (sm.ChannelCum.Length > 0)
            {
                double u = _rng.NextUniform();
                foreach (var (c, cumW) in sm.ChannelCum)
                {
                    if (u <= cumW) { chan = c; break; }
                }
            }

            // Scope.
            ulong scope;
            if (kind == OppKind.ReEval)
            {
                scope = reEvalScope;
            }
            else
            {
                ulong avail = kind == OppKind.CrossSell ? m.AllProcMask & ~_heldUnion[f] : m.AllProcMask;
                scope = DrawScope(sm, avail);
                if (scope == 0) { _busy[f] = false; return; }
            }
            int scopeSize = BitOperations.PopCount(scope);

            // Consideration set + utilities.
            var prods = m.Prods[s];
            var carries = m.ChannelCarries[chan];
            var bias = m.ChannelBias[chan];
            int nCand = 0;
            bool hadOur = false;
            double bestOurU = double.NegativeInfinity;
            int impBase = f * _procCount;
            ulong firmUnion = _heldUnion[f];
            for (int j = 0; j < _prodCount; j++)
            {
                var sp = prods[j];
                if (!sp.Priced || !carries[j]) continue;
                var cov = sp.At(t); // coverage as of the opportunity's open month
                ulong ownHeld = _held[f * _prodCount + j] & scope;
                if ((sp.NoCapacityMask & scope) != 0 && ownHeld == 0) continue; // capacity gate (incumbents exempt)
                if ((cov.HardFailMask & scope) != 0) continue;                  // hard-constraint disqualification
                ulong covered = cov.CovMask & scope;
                if (covered == 0) continue;                                     // nothing to sell

                double covUtil = 0, soft = 0;
                ulong rest = scope;
                while (rest != 0)
                {
                    int p = BitOperations.TrailingZeroCount(rest);
                    rest &= rest - 1;
                    covUtil += _importance[impBase + p] * cov.Eff[p];
                    soft += cov.SoftCost[p];
                }

                ulong otherHeld = (firmUnion & ~_held[f * _prodCount + j]) & scope;
                double switching = m.Input.Choice.SwitchingCostBase
                                   * BitOperations.PopCount(otherHeld) / (double)scopeSize;

                double integ = 0;
                int stackN = 0;
                for (int k = 0; k < _prodCount; k++)
                {
                    if (k == j || _held[f * _prodCount + k] == 0) continue;
                    integ += m.Integration[j * _prodCount + k];
                    stackN++;
                }
                if (stackN > 0) integ = m.Input.Choice.IntegrationWeight * integ / stackN;

                double consolTerm = _consol[f] * BitOperations.PopCount(covered) / (double)scopeSize;
                double price = BitOperations.PopCount(covered) * sp.Arpu;

                double u = covUtil
                           - m.Input.Choice.WorkaroundCostWeight * soft
                           - _priceSens[f] * price / m.Input.Choice.PriceScale
                           - switching
                           + integ
                           + consolTerm
                           + sp.Brand
                           + bias[j];
                _candProd[nCand] = j;
                _candU[nCand] = u;
                nCand++;
                if (m.IsOurs[j]) { hadOur = true; if (u > bestOurU) bestOurU = u; }
            }

            // Logit draw over candidates + the outside option (Gumbel-noise equivalent).
            double maxU = m.Input.Choice.NoDecisionUtility;
            for (int i = 0; i < nCand; i++) if (_candU[i] > maxU) maxU = _candU[i];
            double sumExp = Math.Exp(m.Input.Choice.NoDecisionUtility - maxU);
            for (int i = 0; i < nCand; i++) sumExp += Math.Exp(_candU[i] - maxU);
            double r = _rng.NextUniform() * sumExp;
            int winner = -1;
            double winnerU = m.Input.Choice.NoDecisionUtility;
            double acc = Math.Exp(m.Input.Choice.NoDecisionUtility - maxU);
            if (r > acc)
            {
                for (int i = 0; i < nCand; i++)
                {
                    acc += Math.Exp(_candU[i] - maxU);
                    if (r <= acc || i == nCand - 1) { winner = _candProd[i]; winnerU = _candU[i]; break; }
                }
            }

            // Sales-cycle delay: winner's product override, else segment default, else instant.
            int delay = 0;
            {
                bool has = false;
                double mu = 0, sigma = 0;
                if (winner >= 0 && sm.ProductCycle[winner].Has)
                    (has, mu, sigma) = (true, sm.ProductCycle[winner].Mu, sm.ProductCycle[winner].Sigma);
                else if (sm.HasDefaultCycle) (has, mu, sigma) = (true, sm.CycleMu, sm.CycleSigma);
                if (has)
                {
                    double months = Math.Exp(mu + sigma * _rng.NextGaussian());
                    delay = (int)Math.Clamp(Math.Round(months), 0.0, 10000.0);
                }
            }

            // One scope-expansion check (drawn at open, applied at decision): probability =
            // expansionPropensity x the winner's coverage advantage on the best adjacent cluster.
            ulong expMask = 0;
            if (winner >= 0 && m.Input.Choice.ExpansionPropensity > 0)
            {
                double u = _rng.NextUniform();
                var wcov = prods[winner].At(t);
                double bestAdv = 0;
                ulong bestMask = 0;
                for (int c = 0; c < m.ClusterCount; c++)
                {
                    ulong adjacent = m.ClusterMask[c] & ~scope & ~firmUnion;
                    if (adjacent == 0 || (adjacent & wcov.CovMask) == 0) continue;
                    double otherBest = 0;
                    for (int i = 0; i < nCand; i++)
                    {
                        int j = _candProd[i];
                        if (j == winner) continue;
                        double e = prods[j].At(t).ClusterEff[c];
                        if (e > otherBest) otherBest = e;
                    }
                    double adv = wcov.ClusterEff[c] - otherBest;
                    if (adv > bestAdv) { bestAdv = adv; bestMask = adjacent; }
                }
                if (bestMask != 0 && u < EngineInternals.Clamp01(m.Input.Choice.ExpansionPropensity * bestAdv))
                    expMask = bestMask;
            }

            // Modules the winner would take, resolved against the coverage in force at OPEN (the
            // month the choice is drawn); a coverage step between open and decision does not
            // retroactively change an already-drawn deal.
            ulong wonModules = winner >= 0 ? (scope | expMask) & prods[winner].At(t).CovMask : 0;

            // Funnel: opened / qualified; channel opened.
            _funnel[s * 6 + 0]++;
            if (hadOur) _funnel[s * 6 + 1]++;
            _chanDiag[chan * 2]++;

            int decisionMonth = t + delay;
            if (decisionMonth >= _months)
            {
                _funnel[s * 6 + 5]++; // still open at horizon end (firm stays busy)
                return;
            }
            _pending[decisionMonth].Add(new Pending
            {
                Firm = f, Seg = s, Chan = chan, Winner = winner, Incumbent = incumbent, Delay = delay,
                Scope = scope, ExpMask = expMask, Modules = wonModules, Kind = kind,
                HadOurCandidate = hadOur, WinnerU = winnerU, BestOurU = bestOurU,
            });
        }

        private ulong DrawScope(SegModel sm, ulong avail)
        {
            if (avail == 0) return 0;
            if (!sm.HasScoping) return avail;
            if (_rng.NextUniform() < sm.BroadProb) return avail;

            var m = _m;
            int nEligible = 0;
            for (int c = 0; c < m.ClusterCount; c++)
                if ((m.ClusterMask[c] & avail) != 0) _clusterScratch[nEligible++] = c;
            if (nEligible == 0) return avail;

            int k = Math.Max(1, (int)Math.Round(_rng.Sample(sm.ScopeCountSpec!)));
            if (k > nEligible) k = nEligible;
            ulong scope = 0;
            for (int i = 0; i < k; i++)
            {
                int pick = i + (int)(_rng.NextUniform() * (nEligible - i)) % Math.Max(1, nEligible - i);
                (_clusterScratch[i], _clusterScratch[pick]) = (_clusterScratch[pick], _clusterScratch[i]);
                scope |= m.ClusterMask[_clusterScratch[i]] & avail;
            }
            return scope;
        }

        private void ApplyDecision(in Pending opp, FirmLevelInput input)
        {
            var m = _m;
            int f = opp.Firm, s = opp.Seg;
            _busy[f] = false;
            _cycleSum += opp.Delay;
            _cycleCount++;

            if (opp.Winner < 0)
            {
                // Outside option. For a re-evaluation this means dropping the incumbent (churn);
                // otherwise nothing happens.
                if (opp.Kind == OppKind.ReEval)
                    RemoveModules(f, s, opp.Incumbent, _held[f * _prodCount + opp.Incumbent] & opp.Scope);
                _funnel[s * 6 + 4]++;
                return;
            }

            int j = opp.Winner;
            var sp = m.Prods[s][j];
            ulong modules = opp.Modules; // resolved at open against that month's coverage

            bool stay = opp.Kind == OppKind.ReEval && j == opp.Incumbent;
            if (!stay)
            {
                // The winner displaces other providers on the won modules.
                for (int k = 0; k < _prodCount; k++)
                {
                    if (k == j) continue;
                    ulong lost = _held[f * _prodCount + k] & modules;
                    if (lost != 0) RemoveModules(f, s, k, lost);
                }
            }

            ulong newModules = modules & ~_held[f * _prodCount + j];
            if (newModules != 0)
            {
                AddModules(f, s, j, newModules);
                if ((opp.ExpMask & newModules) != 0) _expansions++;
                if (m.IsOurs[j])
                {
                    double arr = BitOperations.PopCount(newModules) * sp.Arpu * 12.0;
                    _monthBookings[s] += arr + sp.ImplFee;
                    _monthImplFees[s] += sp.ImplFee;
                }
            }

            if (m.IsOurs[j])
            {
                _funnel[s * 6 + 2]++;
                _chanDiag[opp.Chan * 2 + 1]++;
            }
            else
            {
                _funnel[s * 6 + 3]++;
                if (opp.HadOurCandidate) { _gapSum += opp.WinnerU - opp.BestOurU; _gapCount++; }
            }
        }

        private void AddModules(int f, int s, int j, ulong newModules)
        {
            var m = _m;
            bool wasOur = _ourRelCount[f] > 0;
            bool wasProspect = _relCount[f] == 0;
            int idx = f * _prodCount + j;
            bool newRelationship = _held[idx] == 0;
            if (newRelationship) _priceLvl[idx] = 1.0; // list price for new relationships
            _held[idx] |= newModules;
            _heldUnion[f] |= newModules;
            if (newRelationship)
            {
                _relCount[f]++;
                if (m.IsOurs[j]) _ourRelCount[f]++;
            }
            if (m.IsOurs[j])
            {
                double delta = BitOperations.PopCount(newModules) * m.Prods[s][j].Arpu * _priceLvl[idx];
                _ourMrr[f] += delta;
                _segOurMrr[s] += delta;
            }
            if (wasProspect) _prospectCount[s]--;
            if (!wasOur && _ourRelCount[f] > 0)
            {
                _ourClientCount[s]++;
                _monthWins[s]++;
            }
        }

        private void RemoveModules(int f, int s, int j, ulong lostModules)
        {
            if (lostModules == 0) return;
            var m = _m;
            bool wasOur = _ourRelCount[f] > 0;
            int idx = f * _prodCount + j;
            _held[idx] &= ~lostModules;
            if (m.IsOurs[j])
            {
                double delta = BitOperations.PopCount(lostModules) * m.Prods[s][j].Arpu * _priceLvl[idx];
                _ourMrr[f] -= delta;
                _segOurMrr[s] -= delta;
            }
            if (_held[idx] == 0)
            {
                _relCount[f]--;
                if (m.IsOurs[j]) _ourRelCount[f]--;
            }
            // Recompute the union (cheap: prodCount is small).
            ulong union = 0;
            for (int k = 0; k < _prodCount; k++) union |= _held[f * _prodCount + k];
            _heldUnion[f] = union;
            if (_relCount[f] == 0) _prospectCount[s]++;
            if (wasOur && _ourRelCount[f] == 0)
            {
                _ourClientCount[s]--;
                _monthChurn[s]++;
            }
        }
    }
}
