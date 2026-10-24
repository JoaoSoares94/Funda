namespace Funda.AgentRankings.Tests.ObjectMothers;

internal static class FundaResponseJsonMother
{
    public static string CreateDefault() => """
        {
          "AccountStatus": 0, "EmailNotConfirmed": false, "ValidationFailed": false,
          "ValidationReport": null,
          "Metadata": { "ObjectType": "Koopwoningen" },
          "Objects": [{
            "Id": "listing-1", "MakelaarId": 42, "MakelaarNaam": "Example Agent",
            "AanmeldDatum": "/Date(1262300400000+0100)/",
            "ChildrenObjects": [{
              "Id": "child-1", "MakelaarId": 42, "MakelaarNaam": "Example Agent"
            }]
          }],
          "Paging": {
            "AantalPaginas": 226, "HuidigePagina": 1,
            "VolgendeUrl": "/~/koop/amsterdam/p2/", "VorigeUrl": null
          },
          "TotaalAantalObjecten": 5631
        }
        """;

    public static string CreateWithMissingObjects() =>
        CreateDefault().Replace("\"Objects\":", "\"UnexpectedObjects\":", StringComparison.Ordinal);

    public static string CreateWithEmptyResults() => """
        {
          "AccountStatus": 0, "EmailNotConfirmed": false, "ValidationFailed": false,
          "Objects": [], "Paging": { "AantalPaginas": 0, "HuidigePagina": 1 },
          "TotaalAantalObjecten": 0
        }
        """;
}
