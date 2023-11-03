using System.Collections.Immutable;
using MongoDB.Driver;

namespace MongoDB.Migration;

/// <summary>
/// Computes a continioues path/trace of migration from one version to another.
/// </summary>
internal sealed class MigrationGraph
{
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByDownVersion;
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByUpVersion;
    private readonly long _startVersion;
    private readonly long _endVersion;
    private readonly ImmutableArray<MigrationExecutionDescriptor> _orderedMigrations;

    public MigrationGraph(ImmutableArray<MigrationExecutionDescriptor> migrations, long startVersion, long endVersion)
    {
        _orderedMigrations = migrations;
        _migrationByDownVersion = migrations
            .ToImmutableMap(m => m.DownVersion, m => new Node(m));
        _migrationByUpVersion = migrations
            .ToImmutableMap(m => m.UpVersion, m => NodesByDown(m.DownVersion).First(other => ReferenceEquals(other.Migration, m)));
        _startVersion = startVersion;
        _endVersion = endVersion;
    }

    /// <summary>
    /// Creates a <see cref="MigrationGraph"/> from an arbitrary sequence of migrations, starting at <see cref="MigrationExecutionDescriptor.DownVersion"/> greater or equal to <paramref name="currentVersion"/> and ending with <see cref="MigrationExecutionDescriptor.UpVersion"/> less then or equal to <paramref name="targetVersion"/> if specified.
    /// </summary>
    /// <param name="migrations">The sequence of migrations.</param>
    /// <param name="currentVersion">The minimum respected <see cref="MigrationExecutionDescriptor.DownVersion"/>.</param>
    /// <param name="targetVersion">The maximum respected <see cref="MigrationExecutionDescriptor.UpVersion"/>.</param>
    /// <returns></returns>
    public static MigrationGraph? CreateOrDefault(IEnumerable<MigrationExecutionDescriptor> migrations, long? currentVersion, long? targetVersion = null)
    {
        SortedList<long, MigrationExecutionDescriptor> orderedMigrations = [];
        foreach (var migration in migrations)
        {
            if ((currentVersion is { } c && c > migration.DownVersion)
                || (targetVersion is { } t && t < migration.UpVersion))
            {
                continue;
            }
            orderedMigrations.Add(migration.DownVersion, migration);
        }
        if (orderedMigrations.Count == 0)
        {
            return null;
        }
        return new(orderedMigrations.Values.ToImmutableArray(), migrations.First().DownVersion, migrations.Max(m => m.UpVersion));
    }

    private ImmutableArray<Node> NodesByDown(long version)
    {
        return _migrationByDownVersion.TryGetValue(version, out var nodes) ? nodes : ImmutableArray<Node>.Empty;
    }

    private ImmutableArray<Node> NodesByUp(long version)
    {
        return _migrationByUpVersion.TryGetValue(version, out var nodes) ? nodes : ImmutableArray<Node>.Empty;
    }

    /// <summary>
    /// Dijkstra algorithm.
    /// Trace linked list <see cref="Node.Previous"/>
    /// Distance to start <see cref="Node.Distance"/>
    /// </summary>
    private void TraceDistance()
    {
        PriorityQueue<Node, nuint> queue = new(_orderedMigrations.Length);
        Apply(node =>
        {
            node.IsVisited = false;
            node.Previous = null;
            if (node.Migration.DownVersion == _startVersion)
            {
                node.Distance = 0;
                queue.Enqueue(node, 0);
            }
            else
            {
                node.Distance = nuint.MaxValue;
            }
        });

        while (queue.TryDequeue(out var root, out var distance))
        {
            root.IsVisited = true;
            var nextDistance = distance + 1;
            foreach (var node in NodesByDown(root.Migration.UpVersion))
            {
                if (node.Distance <= nextDistance)
                {
                    continue;
                }
                node.Distance = nextDistance;
                node.Previous = root;
                if (!node.IsVisited)
                {
                    queue.Enqueue(node, node.Distance);
                }
            }
        }
    }

    private void EnsureTracePlausible()
    {
        var errors = ValidateTrace().ToArray();
        if (errors.Length == 0)
        {
            return;
        }
        if (errors.Length == 1)
        {
            throw errors[0];
        }
        throw new AggregateException($"Invalid migration set: no path from {_startVersion} to {_endVersion} exists", errors);

        IEnumerable<Exception> ValidateTrace()
        {
            // validate that the start and end nodes are connected
            if (!NodesByUp(_endVersion).Any(node => node.Previous is not null))
            {
                yield return new InvalidOperationException($"Invalid migration set: No path to the target version ({_endVersion}) exists with the available mirgrations.");
            }
            // validate that the start and end nodes are connected
            if (!NodesByDown(_startVersion).Any(node => node.IsVisited))
            {
                yield return new InvalidOperationException($"Invalid migration set: No path from the current version ({_startVersion}) exists with the available mirgrations.");
            }
        }
    }

    public IEnumerable<MigrationExecutionDescriptor> GetMigrationTrace()
    {
        TraceDistance();
        EnsureTracePlausible();
        Stack<MigrationExecutionDescriptor> trace = [];
        var downVersion = _endVersion;
        while (downVersion > _startVersion)
        {
            var closestNode = NodesByUp(downVersion)
                .Where(node => node.IsVisited)
                .MinBy(node => node.Distance);
            if (closestNode is null)
            {
                break;
            }
            trace.Push(closestNode.Migration);
            downVersion = closestNode.Migration.DownVersion;
        }
        if (downVersion != _startVersion)
        {
            throw new InvalidOperationException($"No path between the current version ({_startVersion}) and the intermediary version ({downVersion}) exists.");
        }
        return trace;
    }

    private void Apply(Action<Node> apply)
    {
        foreach (var nodes in _migrationByDownVersion.Values)
        {
            foreach (var node in nodes)
            {
                apply(node);
            }
        }
    }


    private sealed class Node(MigrationExecutionDescriptor migration)
    {
        public MigrationExecutionDescriptor Migration => migration;
        public nuint Distance { get; set; } = nuint.MaxValue;
        public bool IsVisited { get; set; }
        public Node? Previous { get; set; }
    }
}
