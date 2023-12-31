using System.Collections.Immutable;
using MongoDB.Driver;

namespace MongoDB.Migration.Core;

/// <summary>
/// Computes a continioues path/trace of migration from one version to another.
/// </summary>
internal sealed class MigrationGraph
{
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByDownVersion;
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByUpVersion;
    private readonly long _startVersion;
    private readonly long _endVersion;
    private readonly ImmutableArray<MigrationDescriptor> _orderedMigrations;
    private readonly bool _allowBacktracking;

    public MigrationGraph(ImmutableArray<MigrationDescriptor> migrations, long startVersion, long endVersion, bool allowBacktracking)
    {
        _orderedMigrations = migrations;
        _migrationByDownVersion = migrations
            .ToImmutableMap(m => m.DownVersion, m => new Node(m));
        _migrationByUpVersion = migrations
            .ToImmutableMap(m => m.UpVersion, m => NodesByDown(m.DownVersion).First(other => ReferenceEquals(other.Migration, m)));
        _startVersion = startVersion;
        _endVersion = endVersion;
        _allowBacktracking = allowBacktracking;
    }

    /// <summary>
    /// Creates a <see cref="MigrationGraph"/> from an arbitrary sequence of migrations, starting at <see cref="MigrationDescriptor.DownVersion"/> greater or equal to <paramref name="currentVersion"/> and ending with <see cref="MigrationDescriptor.UpVersion"/> less then or equal to <paramref name="targetVersion"/> if specified.
    /// </summary>
    /// <param name="migrations">The sequence of migrations.</param>
    /// <param name="currentVersion">The minimum respected <see cref="MigrationDescriptor.DownVersion"/>.</param>
    /// <param name="targetVersion">The maximum respected <see cref="MigrationDescriptor.UpVersion"/>.</param>
    /// <param name="allowBacktracking">Allows downgrades in the path.</param>
    /// <returns></returns>
    public static MigrationGraph? CreateOrDefault(IEnumerable<MigrationDescriptor> migrations, long? currentVersion, long? targetVersion = null, bool allowBacktracking = false)
    {
        var orderedMigrations = migrations
            .Where(m
                => !(
                    (currentVersion is { } c && c > m.DownVersion)
                    || (targetVersion is { } t && t < m.UpVersion)
                )
            )
            .OrderBy(m => m.DownVersion)
            .ToImmutableArray();
        if (orderedMigrations.IsDefaultOrEmpty)
        {
            return null;
        }

        return new(orderedMigrations, orderedMigrations.First().DownVersion, orderedMigrations.Max(m => m.UpVersion), allowBacktracking);
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
        PriorityQueue<Node, long> queue = new(_orderedMigrations.Length);
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
                node.Distance = long.MaxValue;
            }
        });

        while (queue.TryDequeue(out var root, out var distance))
        {
            root.IsVisited = true;
            var nextDistance = distance + 1;
            var neighbours = NodesByDown(root.Migration.UpVersion).Concat(_allowBacktracking ? NodesByUp(root.Migration.UpVersion) : ImmutableArray<Node>.Empty);
            foreach (var node in neighbours)
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
            if (!NodesByUp(_endVersion).Any(node => node.Previous is not null || node.Migration.DownVersion == _startVersion))
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

    public IEnumerable<MigrationDescriptor> GetMigrationTrace()
    {
        TraceDistance();
        EnsureTracePlausible();
        Stack<MigrationDescriptor> trace = [];
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


    private sealed class Node(MigrationDescriptor migration)
    {
        public MigrationDescriptor Migration => migration;
        public long Distance { get; set; } = long.MaxValue;
        public bool IsVisited { get; set; }
        public Node? Previous { get; set; }
    }
}
