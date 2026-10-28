using Funda.AgentRankings.Api.Contracts;
using Funda.AgentRankings.Listings;
using System.Globalization;

namespace Funda.AgentRankings.Api;

internal sealed class PropertyListingCollection
{
    private readonly Dictionary<string, Property> _properties = new(StringComparer.Ordinal);

    public void Add(FundaObject root, CancellationToken cancellationToken)
    {
        var pending = new Stack<FundaObject>();
        pending.Push(root);
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Id) || item.AgentId is <= 0)
            {
                throw new InvalidDataException("A property has an invalid listing ID or agent ID.");
            }

            var id = NormalizeId(item.Id);
            if (!_properties.TryGetValue(id, out var property))
            {
                property = new Property(id);
                _properties.Add(id, property);
            }
            property.Observe(item);

            var children = item.ChildrenObjects ?? [];
            for (var index = children.Length - 1; index >= 0; index--)
            {
                var child = children[index];
                if (child is null) { continue; }
                if (string.IsNullOrWhiteSpace(child.Id))
                {
                    throw new InvalidDataException("A child property has no listing ID.");
                }
                property.ChildIds.Add(NormalizeId(child.Id));
                pending.Push(child);
            }
        }
    }

    public (IReadOnlyList<Listing> Listings, ListingResolutionSummary Summary) Resolve(
        bool enableNameFallback, CancellationToken cancellationToken)
    {
        var listings = new List<Listing>();
        var agentNames = new Dictionary<int, string>();
        var nameLookup = enableNameFallback ? new AgentNameLookup() : null;
        var conflicts = 0;
        var inferred = 0;

        // Only direct, nonconflicting identities establish agent names and name matches.
        foreach (var property in _properties.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.ConflictingAgentIds || property.DirectAgentId is not { } agentId) { continue; }
            foreach (var name in property.DirectNames)
            {
                agentNames.TryAdd(agentId, name);
                nameLookup?.Observe(name, agentId);
            }
        }

        foreach (var property in _properties.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.ConflictingAgentIds) { conflicts++; continue; }
            if (property.DirectAgentId is { } agentId)
            {
                listings.Add(CreateListing(property, agentId, agentNames));
            }
        }

        foreach (var property in _properties.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.ConflictingAgentIds || property.DirectAgentId is not null) { continue; }
            var agentId = ResolveFromChildren(property);
            if (agentId is null && nameLookup is not null)
            {
                agentId = ResolveFromNames(property.Names, nameLookup);
            }
            if (agentId is { } resolvedId)
            {
                listings.Add(CreateListing(property, resolvedId, agentNames));
                inferred++;
            }
        }

        return (listings, new ListingResolutionSummary(_properties.Count, listings.Count, conflicts, inferred));
    }

    private int? ResolveFromChildren(Property property)
    {
        int? candidate = null;
        foreach (var childId in property.ChildIds)
        {
            var child = _properties[childId];
            if (child.ConflictingAgentIds) { return null; }
            if (child.DirectAgentId is not { } agentId) { continue; }
            if (candidate is not null && candidate != agentId) { return null; }
            candidate = agentId;
        }
        return candidate;
    }

    private static int? ResolveFromNames(IEnumerable<string> names, AgentNameLookup lookup)
    {
        int? candidate = null;
        foreach (var name in names)
        {
            if (!lookup.TryResolve(name, out var agentId)) { continue; }
            if (candidate is not null && candidate != agentId) { return null; }
            candidate = agentId;
        }
        return candidate;
    }

    private static Listing CreateListing(Property property, int agentId, Dictionary<int, string> agentNames)
    {
        var name = property.DirectNames.FirstOrDefault();
        if (name is null && !agentNames.TryGetValue(agentId, out name)) { name = property.Names.FirstOrDefault(); }
        name ??= "Agent " + agentId.ToString(CultureInfo.InvariantCulture);
        return new Listing(property.Id, agentId, name);
    }

    private static string NormalizeId(string id)
    {
        if (!Guid.TryParse(id, out var value)) { return id; }
        if (value == Guid.Empty) { throw new InvalidDataException("A property has an empty listing ID."); }
        return value.ToString("D");
    }

    private sealed class Property(string id)
    {
        public string Id { get; } = id;
        public int? DirectAgentId { get; private set; }
        public bool ConflictingAgentIds { get; private set; }
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DirectNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ChildIds { get; } = new(StringComparer.Ordinal);

        public void Observe(FundaObject item)
        {
            if (item.AgentId is { } agentId)
            {
                ConflictingAgentIds |= DirectAgentId is { } previous && previous != agentId;
                DirectAgentId ??= agentId;
                if (!string.IsNullOrWhiteSpace(item.AgentName)) { DirectNames.Add(item.AgentName.Trim()); }
            }
            if (!string.IsNullOrWhiteSpace(item.AgentName)) { Names.Add(item.AgentName.Trim()); }
        }
    }
}
