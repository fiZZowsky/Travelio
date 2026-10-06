using Travelio.Domain;

namespace Travelio.Infrastructure;

/// <summary>Recommendations for identified venues take priority over transparent category estimates.</summary>
public sealed class VisitDurationEstimator
{
    private static readonly DateTimeOffset Reviewed = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    public static VisitDuration Estimate(string tourism, string historic, string leisure, string? wikidataId)
    {
        var official = wikidataId switch
        {
            "Q6373" => new VisitDuration(180, VisitDurationBasis.SourceRecommendation, "https://www.britishmuseum.org/visit/object-trails/three-hours-museum", Reviewed),
            "Q19675" => new VisitDuration(90, VisitDurationBasis.SourceRecommendation, "https://www.louvre.fr/en/explore/visitor-trails", Reviewed),
            "Q48435" => new VisitDuration(60, VisitDurationBasis.SourceRecommendation, "https://sagradafamilia.org/en/sagrada-familia-and-guided-tour?inheritRedirect=true", Reviewed),
            _ => null
        };
        if (official is not null) return official;
        var minutes = tourism switch
        {
            "museum" => 120, "zoo" => 180, "theme_park" => 300, "gallery" => 75,
            "viewpoint" => 30, "artwork" => 20,
            _ => historic == "castle" ? 120 : historic == "monument" ? 25 : leisure == "park" ? 60 : 60
        };
        return new(minutes, VisitDurationBasis.CategoryEstimate);
    }
}
