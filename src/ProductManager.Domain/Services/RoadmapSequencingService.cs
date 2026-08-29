using ProductManager.Domain.Execution;

namespace ProductManager.Domain.Services;

public enum SequencingViolationKind
{
    /// <summary>The dependent project's planned start precedes the dependency's planned end.</summary>
    StartsBeforeDependencyEnds,

    /// <summary>The project depends on a project that is not planned on this roadmap.</summary>
    DependencyNotOnRoadmap,

    /// <summary>The dependency graph contains a cycle involving this edge.</summary>
    DependencyCycle
}

/// <summary>A dependency-ordering problem on a roadmap.</summary>
public sealed record SequencingViolation(
    SequencingViolationKind Kind,
    Guid ProjectId,
    Guid DependsOnProjectId,
    string Description);

/// <summary>
/// Pure roadmap sequencing invariants: a project must not be planned to start before every
/// project it depends on is planned to end, dependencies must be on the roadmap, and the
/// dependency graph must be acyclic.
/// </summary>
public static class RoadmapSequencingService
{
    public static IReadOnlyList<SequencingViolation> DetectViolations(
        Roadmap roadmap,
        IEnumerable<Project> projects)
    {
        ArgumentNullException.ThrowIfNull(roadmap);
        ArgumentNullException.ThrowIfNull(projects);

        var projectList = projects.ToList();
        var itemByProject = roadmap.Items.ToDictionary(i => i.ProjectId);
        var plannedProjectIds = itemByProject.Keys.ToHashSet();

        var violations = new List<SequencingViolation>();

        // Only dependencies of projects that are actually planned on this roadmap matter.
        var plannedProjects = projectList.Where(p => plannedProjectIds.Contains(p.Id)).ToList();

        // Cycle detection over the dependency edges among planned projects.
        var edges = plannedProjects
            .SelectMany(p => p.Dependencies)
            .Where(d => plannedProjectIds.Contains(d.DependsOnProjectId))
            .ToList();
        var cyclicEdges = FindEdgesInCycles(plannedProjectIds, edges);

        foreach (var project in plannedProjects)
        {
            var item = itemByProject[project.Id];
            foreach (var dependency in project.Dependencies)
            {
                if (!itemByProject.TryGetValue(dependency.DependsOnProjectId, out var dependencyItem))
                {
                    violations.Add(new SequencingViolation(
                        SequencingViolationKind.DependencyNotOnRoadmap,
                        project.Id, dependency.DependsOnProjectId,
                        $"Project '{project.Name}' depends on a project that is not planned on this roadmap."));
                    continue;
                }

                if (cyclicEdges.Contains((dependency.ProjectId, dependency.DependsOnProjectId)))
                {
                    violations.Add(new SequencingViolation(
                        SequencingViolationKind.DependencyCycle,
                        project.Id, dependency.DependsOnProjectId,
                        $"Project '{project.Name}' is part of a dependency cycle."));
                    continue;
                }

                if (item.PlannedStart < dependencyItem.PlannedEnd)
                {
                    violations.Add(new SequencingViolation(
                        SequencingViolationKind.StartsBeforeDependencyEnds,
                        project.Id, dependency.DependsOnProjectId,
                        $"Project '{project.Name}' is planned to start {item.PlannedStart:yyyy-MM-dd}, " +
                        $"before its dependency is planned to end {dependencyItem.PlannedEnd:yyyy-MM-dd}."));
                }
            }
        }

        return violations;
    }

    /// <summary>Returns the set of directed edges that participate in at least one cycle.</summary>
    private static HashSet<(Guid From, Guid To)> FindEdgesInCycles(
        HashSet<Guid> nodes,
        List<ProjectDependency> edges)
    {
        // Iteratively strip nodes with no outgoing or no incoming edges; what remains is the
        // union of cycles (and paths between them). Edges among remaining nodes are cyclic-ish;
        // to be precise we keep only edges where both endpoints are in a strongly connected
        // component of size > 1 or a self-loop. Simple Tarjan SCC:
        var adjacency = nodes.ToDictionary(n => n, _ => new List<Guid>());
        foreach (var e in edges)
            if (adjacency.TryGetValue(e.ProjectId, out var list))
                list.Add(e.DependsOnProjectId);

        var index = 0;
        var indices = new Dictionary<Guid, int>();
        var lowLinks = new Dictionary<Guid, int>();
        var onStack = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        var componentOf = new Dictionary<Guid, int>();
        var componentSizes = new Dictionary<int, int>();
        var componentCounter = 0;

        foreach (var node in nodes)
            if (!indices.ContainsKey(node))
                StrongConnect(node);

        var cyclic = new HashSet<(Guid, Guid)>();
        foreach (var e in edges)
        {
            if (componentOf.TryGetValue(e.ProjectId, out var c1) &&
                componentOf.TryGetValue(e.DependsOnProjectId, out var c2) &&
                c1 == c2 && componentSizes[c1] > 1)
            {
                cyclic.Add((e.ProjectId, e.DependsOnProjectId));
            }
        }

        return cyclic;

        void StrongConnect(Guid v)
        {
            // Iterative Tarjan to avoid stack depth issues on long chains.
            var work = new Stack<(Guid Node, int EdgeIndex)>();
            work.Push((v, 0));
            while (work.Count > 0)
            {
                var (node, edgeIndex) = work.Pop();
                if (edgeIndex == 0)
                {
                    indices[node] = index;
                    lowLinks[node] = index;
                    index++;
                    stack.Push(node);
                    onStack.Add(node);
                }

                var recurse = false;
                var neighbors = adjacency[node];
                for (var i = edgeIndex; i < neighbors.Count; i++)
                {
                    var next = neighbors[i];
                    if (!indices.ContainsKey(next))
                    {
                        work.Push((node, i + 1));
                        work.Push((next, 0));
                        recurse = true;
                        break;
                    }

                    if (onStack.Contains(next))
                        lowLinks[node] = Math.Min(lowLinks[node], indices[next]);
                }

                if (recurse) continue;

                if (lowLinks[node] == indices[node])
                {
                    var componentId = componentCounter++;
                    var size = 0;
                    Guid member;
                    do
                    {
                        member = stack.Pop();
                        onStack.Remove(member);
                        componentOf[member] = componentId;
                        size++;
                    } while (member != node);

                    componentSizes[componentId] = size;
                }

                if (work.Count > 0)
                {
                    var (parent, _) = work.Peek();
                    lowLinks[parent] = Math.Min(lowLinks[parent], lowLinks[node]);
                }
            }
        }
    }
}
