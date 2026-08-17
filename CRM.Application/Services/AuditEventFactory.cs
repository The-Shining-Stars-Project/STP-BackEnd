using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

/// <summary>
/// The pure half of audit logging: merging ambient context into an entry, sanitizing every
/// string that lands in the log, and the two rules the action filter would otherwise get
/// wrong (which route value is the entity id, and which status codes count as success).
///
/// It is static and dependency-free on purpose. CRM.Tests references only CRM.Application
/// and CRM.Infrastructure, so neither the CRM.API filter nor the CRM.Persistence writer can
/// be unit-tested directly. Keeping every bug-prone decision here means the untested
/// remainder is a thin adapter that extracts primitives and a single EF insert.
/// </summary>
public static class AuditEventFactory
{
    // Must match the column lengths in AuditEventConfiguration. A value the sanitizer
    // accepts must always fit the column, or a hostile input turns into a failed audit write.
    public const int ActionMaxLength = 64;
    public const int EntityTypeMaxLength = 64;
    public const int UserEmailMaxLength = 256;
    public const int UserRoleMaxLength = 32;
    public const int SummaryMaxLength = 512;
    public const int IpAddressMaxLength = 45;
    public const int UserAgentMaxLength = 512;

    /// <summary>
    /// Metadata has no column limit (nvarchar(max)), but an unbounded field reachable from
    /// client input is a way to bloat the table and bury real events. 4000 characters is far
    /// more than any event here produces.
    /// </summary>
    public const int MetadataMaxLength = 4000;

    /// <summary>
    /// Longest client-asserted forwarded chain kept once it reaches the metadata JSON. The
    /// capture site (HttpAuditContextAccessor) caps it too; this is the backstop for any other
    /// caller that populates <see cref="AuditContext.ClientAssertedIp"/>.
    /// </summary>
    public const int ClientAssertedIpMaxLength = 256;

    /// <summary>
    /// Builds the row to insert. Caller-supplied actor fields win; anything left null is
    /// filled from the ambient request context.
    /// </summary>
    public static AuditEvent Build(AuditEntry entry, AuditContext? ambient)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var email = NormalizeEmail(entry.UserEmail ?? ambient?.UserEmail);

        return new AuditEvent
        {
            OccurredAt = entry.OccurredAt ?? DateTime.UtcNow,
            UserId = entry.UserId ?? ambient?.UserId,
            // Required column. An event with no identifiable actor (a failed login against a
            // blank email) still deserves a row, so fall back to empty rather than dropping it.
            UserEmail = Sanitize(email, UserEmailMaxLength) ?? string.Empty,
            UserRole = Sanitize(entry.UserRole ?? ambient?.UserRole, UserRoleMaxLength),
            Action = Sanitize(entry.Action, ActionMaxLength) ?? string.Empty,
            EntityType = Sanitize(entry.EntityType, EntityTypeMaxLength),
            EntityId = entry.EntityId,
            Summary = Sanitize(entry.Summary, SummaryMaxLength),
            IpAddress = Sanitize(entry.IpAddress ?? ambient?.IpAddress, IpAddressMaxLength),
            UserAgent = Sanitize(entry.UserAgent ?? ambient?.UserAgent, UserAgentMaxLength),
            Succeeded = entry.Succeeded,
            Metadata = BuildMetadata(entry.Metadata, ambient?.ClientAssertedIp),
        };
    }

    /// <summary>
    /// Produces the metadata column: the caller's JSON, plus the client-asserted forwarded
    /// address folded in under its own key when one was captured.
    ///
    /// The forwarded address rides in metadata rather than in IpAddress so the untrusted value
    /// can never be mistaken for the one the server observed. Both are useful; only one of them
    /// is evidence.
    ///
    /// Built with JsonObject rather than string concatenation, and capped BEFORE the merge
    /// rather than after. Splicing text and truncating the result put the cut inside a JSON
    /// string literal whenever the merged value was long, leaving stored metadata that no
    /// parser — the audit viewer's included — would accept. Composing nodes means a truncation
    /// can never land mid-token, and every value written here is escaped by the serializer.
    /// </summary>
    public static string? BuildMetadata(string? metadata, string? clientAssertedIp)
    {
        var cleanedIp = Sanitize(clientAssertedIp, ClientAssertedIpMaxLength);
        var cleaned = Sanitize(metadata, MetadataMaxLength);

        if (cleanedIp is null) return cleaned;

        JsonObject merged;
        try
        {
            // Every caller serializes an object; anything else is a bug elsewhere, so keep it
            // verbatim under a known key rather than dropping it or corrupting the result.
            merged = cleaned is null
                ? []
                : JsonNode.Parse(cleaned) as JsonObject ?? new JsonObject { ["detail"] = cleaned };
        }
        catch (JsonException)
        {
            merged = new JsonObject { ["detail"] = cleaned };
        }

        merged["clientAssertedIp"] = cleanedIp;

        // Deliberately NOT truncated again. Both inputs are already capped, so the result is
        // bounded at a little over MetadataMaxLength, and the column is nvarchar(max) — there
        // is nothing here to protect against. Re-truncating the assembled JSON is the exact
        // mistake this method exists to prevent.
        return merged.ToJsonString();
    }

    /// <summary>
    /// Removes CR, LF, and every other control character, then truncates. Applied to every
    /// string field without exception.
    ///
    /// This is not cosmetic. The submitted email on a failed login and the filename on an
    /// export are raw attacker input that end up in an audit viewer and in whatever log
    /// shipper the client eventually points at this table. Newlines are how you forge a
    /// second, convincing-looking log line inside the first one.
    /// </summary>
    public static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) return null;

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    /// <summary>
    /// Lower-cases and trims, matching the normalization AuthService.LoginAsync applies
    /// before looking a user up. Without this, filtering the audit log by email would miss
    /// rows whose actor typed their address with different capitalization.
    /// </summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Works out which record an audited action touched, in strict precedence order:
    /// an explicitly named route key, then "id", then the first route value whose name ends
    /// in "Id", then — for creates, which have no route id — the Id of the returned object.
    /// Anything unresolvable yields null; this never throws.
    /// </summary>
    public static Guid? ResolveEntityId(
        IDictionary<string, object?>? routeValues,
        string? explicitKey,
        object? resultValue)
    {
        if (routeValues is not null)
        {
            if (!string.IsNullOrWhiteSpace(explicitKey)
                && routeValues.TryGetValue(explicitKey, out var explicitValue)
                && TryParseGuid(explicitValue, out var fromExplicit))
                return fromExplicit;

            if (routeValues.TryGetValue("id", out var idValue) && TryParseGuid(idValue, out var fromId))
                return fromId;

            // Future-proofs {participantId}-style routes. Ordered by key so the answer does
            // not depend on dictionary enumeration order when a route has several.
            foreach (var pair in routeValues.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pair.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                    && TryParseGuid(pair.Value, out var fromSuffix))
                    return fromSuffix;
            }
        }

        // A POST has no route id — the new record's id exists only in the response body.
        return IdFromResult(resultValue);
    }

    private static Guid? IdFromResult(object? resultValue)
    {
        if (resultValue is null) return null;

        var property = resultValue.GetType()
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (property is null || property.PropertyType != typeof(Guid)) return null;

        try
        {
            return property.GetValue(resultValue) is Guid id && id != Guid.Empty ? id : null;
        }
        catch (TargetInvocationException)
        {
            // A property getter that throws must not take the request down with it.
            return null;
        }
    }

    private static bool TryParseGuid(object? value, out Guid result) =>
        Guid.TryParse(value?.ToString(), out result) && result != Guid.Empty;

    /// <summary>
    /// 2xx is success, everything else is not. A null status means the result never set one,
    /// which MVC treats as 200.
    /// </summary>
    public static bool SucceededFrom(int? statusCode)
    {
        var code = statusCode ?? 200;
        return code is >= 200 and < 300;
    }

    /// <summary>Longest route value copied into metadata. Route values are caller input.</summary>
    public const int RouteMetadataValueMaxLength = 128;

    /// <summary>
    /// The metadata an audited HTTP request produces: the status code when the request did not
    /// succeed (or the exception type when it died before producing one), plus any route values
    /// the endpoint asked to have recorded beside the entity id. Returns null when there is
    /// nothing worth writing — an empty object on every successful row is noise in a table that
    /// only grows.
    ///
    /// <paramref name="exceptionType"/> is a TYPE NAME, never a message: a DbUpdateException's
    /// message can carry the row data that caused it, and that data is what this system exists
    /// to protect.
    /// </summary>
    public static string? RequestMetadata(
        int? statusCode,
        IDictionary<string, object?>? routeValues,
        IReadOnlyList<string>? metadataRouteKeys,
        string? exceptionType = null)
    {
        var fields = new JsonObject();

        if (exceptionType is not null) fields["exception"] = Sanitize(exceptionType, ActionMaxLength);
        else if (!SucceededFrom(statusCode)) fields["statusCode"] = statusCode ?? 200;

        if (metadataRouteKeys is not null && routeValues is not null)
        {
            foreach (var key in metadataRouteKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (!routeValues.TryGetValue(key, out var value)) continue;

                var text = Sanitize(value?.ToString(), RouteMetadataValueMaxLength);
                if (text is not null) fields[key] = text;
            }
        }

        return fields.Count == 0 ? null : fields.ToJsonString();
    }
}
