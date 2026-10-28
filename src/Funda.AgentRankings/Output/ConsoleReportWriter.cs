using Funda.AgentRankings.Rankings;
using Spectre.Console;
using System.Globalization;

namespace Funda.AgentRankings.Output;

public sealed class ConsoleReportWriter(TextWriter output)
{
    private readonly IAnsiConsole _console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(output),
        Ansi = AnsiSupport.No,
        Interactive = InteractionSupport.No
    });

    public void Write(string title, IReadOnlyList<AgentRanking> rankings)
    {
        _console.WriteLine(title);
        if (rankings.Count == 0)
        {
            _console.WriteLine("No rankable listings found.");
            _console.WriteLine();
            return;
        }

        var table = new Table().Border(TableBorder.Ascii)
            .AddColumn(new TableColumn("Rank").RightAligned())
            .AddColumn(new TableColumn("Agent ID").RightAligned())
            .AddColumn(new TableColumn("Listings").RightAligned())
            .AddColumn("Agent");
        for (var index = 0; index < rankings.Count; index++)
        {
            var agent = rankings[index];
            var name = string.Concat(agent.AgentName.Select(character => char.IsControl(character) ? ' ' : character));
            table.AddRow(
                new Text((index + 1).ToString(CultureInfo.InvariantCulture)),
                new Text(agent.AgentId.ToString(CultureInfo.InvariantCulture)),
                new Text(agent.ListingCount.ToString(CultureInfo.InvariantCulture)),
                new Text(name));
        }
        _console.Write(table);
        _console.WriteLine();
    }
}
