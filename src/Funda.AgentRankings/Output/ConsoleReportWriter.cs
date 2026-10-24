using Funda.AgentRankings.Rankings;

namespace Funda.AgentRankings.Output;

public sealed class ConsoleReportWriter(TextWriter output)
{
    public void Write(string title, IReadOnlyList<AgentRanking> rankings)
    {
        output.WriteLine(title);
        if (rankings.Count == 0)
        {
            output.WriteLine("No listings found.");
            output.WriteLine();
            return;
        }

        output.WriteLine($"{"Rank",4}  {"Agent ID",10}  {"Listings",8}  Agent");
        for (var index = 0; index < rankings.Count; index++)
        {
            var agent = rankings[index];
            var name = string.Concat(agent.AgentName.Select(character => char.IsControl(character) ? ' ' : character));
            output.WriteLine($"{index + 1,4}  {agent.AgentId,10}  {agent.ListingCount,8}  {name}");
        }
        output.WriteLine();
    }
}
