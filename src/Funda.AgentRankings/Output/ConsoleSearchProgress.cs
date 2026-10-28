using Funda.AgentRankings.Listings;
using Spectre.Console;
using System.Globalization;

namespace Funda.AgentRankings.Output;

public sealed class ConsoleSearchProgress(TextWriter output, bool interactive) : IProgress<ListingSearchProgress>
{
    private readonly IAnsiConsole _console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(output),
        Ansi = interactive ? AnsiSupport.Detect : AnsiSupport.No,
        Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No
    });
    private readonly Dictionary<ListingSearch, ProgressTask> _tasks = [];
    private readonly Dictionary<ListingSearch, ListingResolutionSummary> _summaries = [];
    private ProgressContext? _context;

    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        WriteIntroduction();
        _summaries.Clear();
        try
        {
            return await _console.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
                .StartAsync(async context =>
                {
                    _context = context;
                    _tasks.Clear();
                    try
                    {
                        return await action();
                    }
                    finally
                    {
                        // Spectre does not draw live progress when output is redirected or ANSI is unavailable.
                        if (!_console.Profile.Capabilities.Interactive || !_console.Profile.Capabilities.Ansi)
                        {
                            foreach (var task in _tasks.Values)
                            {
                                _console.WriteLine($"{task.Description}: {task.Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%");
                            }
                        }
                        _context = null;
                        _tasks.Clear();
                    }
                });
        }
        finally
        {
            foreach (var (search, summary) in _summaries)
            {
                _console.WriteLine($"{GetDescription(search)}: {summary.RankedProperties}/{summary.UniqueProperties} properties ranked; {summary.UnresolvedProperties} unresolved; {summary.InferredProperties} inferred.\n");
                if (summary.ConflictingProperties > 0)
                {
                    _console.WriteLine($"  {summary.ConflictingProperties} unresolved properties have conflicting agent IDs. \n");
                }
            }
            _summaries.Clear();
        }
    }

    public void Report(ListingSearchProgress progress)
    {
        var context = _context ?? throw new InvalidOperationException("Search progress is not active.");
        if (!_tasks.TryGetValue(progress.Search, out var task))
        {
            task = context.AddTask(GetDescription(progress.Search));
            _tasks.Add(progress.Search, task);
        }

        task.Value = progress.Percentage;
        if (progress.Summary is { } summary) { _summaries[progress.Search] = summary; }
    }

    private static string GetDescription(ListingSearch search) => search switch
    {
        ListingSearch.Amsterdam => "Amsterdam",
        ListingSearch.AmsterdamWithGarden => "Amsterdam with a garden",
        _ => throw new ArgumentOutOfRangeException(nameof(search))
    };

    private void WriteIntroduction()
    {
        _console.Write(new Rule("[bold orange1]Funda Agent Rankings[/]") { Justification = Justify.Left });
        _console.WriteLine("Find the top 10 real estate agents in Amsterdam by listings for sale.");
        _console.WriteLine("Two searches: all properties, then properties with a garden.");
        _console.WriteLine("Results appear after both searches finish. Press Ctrl+C to cancel.");
        _console.WriteLine();
    }
}
