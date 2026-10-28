namespace Funda.AgentRankings.Api;

internal sealed class AgentNameLookup
{
    private readonly Dictionary<string, int?> _agentIds = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(string name, int agentId)
    {
        name = name.Trim();
        if (_agentIds.TryGetValue(name, out var knownId) && knownId != agentId)
        {
            _agentIds[name] = null; // More than one ID uses this name.
            return;
        }
        _agentIds[name] = agentId;
    }

    public bool TryResolve(string? name, out int agentId)
    {
        agentId = 0;
        if (string.IsNullOrWhiteSpace(name) ||
            !_agentIds.TryGetValue(name.Trim(), out var knownId) || knownId is null)
        {
            return false;
        }
        agentId = knownId.Value;
        return true;
    }
}
