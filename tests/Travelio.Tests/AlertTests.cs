using System.Xml.Linq;
using Travelio.Application;
using Travelio.Infrastructure;
namespace Travelio.Tests;

public sealed class AlertTests
{
    [Fact]
    public void Feed_filters_inactive_faraway_events_and_untrusted_links()
    {
        var xml = XDocument.Parse("""
            <rss xmlns:g="http://www.gdacs.org" xmlns:p="http://www.georss.org/georss"><channel>
            <item><g:iscurrent>true</g:iscurrent><g:iso3>PRT</g:iso3><g:eventtype>FL</g:eventtype><g:alertlevel>Orange</g:alertlevel><link>https://www.gdacs.org/report.aspx?eventid=1</link></item>
            <item><g:iscurrent>false</g:iscurrent><g:iso3>PRT</g:iso3><link>https://www.gdacs.org/old</link></item>
            <item><g:iscurrent>true</g:iscurrent><g:iso3>JPN</g:iso3><p:point>35 135</p:point><link>https://www.gdacs.org/far</link></item>
            <item><g:iscurrent>true</g:iscurrent><g:iso3>PRT</g:iso3><link>https://evil.example/report</link></item>
            </channel></rss>
            """);
        var alert = Assert.Single(GdacsAlertProvider.SelectAlerts(xml, new DemoDestinationCatalog().Get("lisbon"), DateTimeOffset.UtcNow));
        Assert.Equal("orange", alert.Severity);
        Assert.StartsWith("Powódź", alert.Title);
        Assert.True(alert.IsVerified);
    }
    [Fact]
    public void Empty_feed_does_not_claim_destination_is_safe()
    {
        var result = GdacsAlertProvider.SelectAlerts(XDocument.Parse("<rss><channel/></rss>"),
            new DemoDestinationCatalog().Get("rome"), DateTimeOffset.UtcNow);
        Assert.Contains("nie obejmuje wszystkich", Assert.Single(result).Description);
    }
}

