using CRM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace CRM.Infrastructure.Time;

/// <summary>Where the organisation's programs actually run. Bound from the "OrgTime" section.</summary>
public class OrgTimeSettings
{
    public const string SectionName = "OrgTime";

    /// <summary>
    /// IANA id ("America/Los_Angeles") or Windows id ("Pacific Standard Time") — .NET 6+
    /// accepts either on every platform, so one value works for local macOS development,
    /// the windows-latest CI runner, and Azure App Service alike.
    /// Defaults to Pacific: the programs run in Modesto and Manteca, California.
    /// </summary>
    public string TimeZone { get; set; } = "America/Los_Angeles";
}

/// <inheritdoc cref="IOrgClock"/>
public class OrgClock : IOrgClock
{
    private readonly TimeZoneInfo _timeZone;

    public OrgClock(IOptions<OrgTimeSettings> settings)
    {
        var id = settings.Value.TimeZone;
        try
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Fail at startup with a message that says what to do, rather than silently
            // falling back to UTC and reintroducing the bug this class exists to fix.
            throw new InvalidOperationException(
                $"OrgTime:TimeZone '{id}' is not a timezone this machine knows. Use an IANA id "
                + "such as 'America/Los_Angeles'.", ex);
        }
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);

    public DateTime Today => Now.Date;
}
