using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using CRM.Application.Services;

namespace CRM.Tests;

/// <summary>
/// Covers the pure core of audit logging. This matters more than the line count suggests:
/// CRM.Tests references only CRM.Application and CRM.Infrastructure, so the CRM.API action
/// filter and the CRM.Persistence writer cannot be reached from here. Every decision with a
/// bug in it was pushed into <see cref="AuditEventFactory"/> precisely so it could be tested,
/// leaving a thin adapter and a single EF insert as the untested remainder.
/// </summary>
public class AuditEventFactoryTests
{
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid Subject = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    private static AuditContext Ambient() => new(
        UserId: Actor,
        UserEmail: "admin@example.org",
        UserRole: "Admin",
        IpAddress: "203.0.113.7",
        UserAgent: "Mozilla/5.0",
        ClientAssertedIp: "198.51.100.9");

    // ---------- ambient context merging ----------

    [Fact]
    public void Build_fills_null_actor_fields_from_ambient_context()
    {
        var row = AuditEventFactory.Build(new AuditEntry { Action = "participant.view" }, Ambient());

        Assert.Equal(Actor, row.UserId);
        Assert.Equal("admin@example.org", row.UserEmail);
        Assert.Equal("Admin", row.UserRole);
        Assert.Equal("203.0.113.7", row.IpAddress);
        Assert.Equal("Mozilla/5.0", row.UserAgent);
    }

    [Fact]
    public void Build_lets_caller_supplied_actor_fields_win_over_ambient()
    {
        // The admin-reset and failed-login cases depend on this: the actor is not the subject.
        var entry = new AuditEntry
        {
            Action = "auth.login",
            UserId = Subject,
            UserEmail = "someone.else@example.org",
            UserRole = "Staff",
        };

        var row = AuditEventFactory.Build(entry, Ambient());

        Assert.Equal(Subject, row.UserId);
        Assert.Equal("someone.else@example.org", row.UserEmail);
        Assert.Equal("Staff", row.UserRole);
    }

    [Fact]
    public void Build_works_with_no_ambient_context()
    {
        var row = AuditEventFactory.Build(new AuditEntry { Action = "auth.login" }, null);

        Assert.Null(row.UserId);
        Assert.Equal(string.Empty, row.UserEmail);
        Assert.Equal("auth.login", row.Action);
    }

    [Fact]
    public void Build_defaults_OccurredAt_to_now_but_honours_an_explicit_value()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var defaulted = AuditEventFactory.Build(new AuditEntry { Action = "a" }, null);
        Assert.InRange(defaulted.OccurredAt, before, DateTime.UtcNow.AddSeconds(1));

        var fixedTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var explicitTime = AuditEventFactory.Build(
            new AuditEntry { Action = "a", OccurredAt = fixedTime }, null);
        Assert.Equal(fixedTime, explicitTime.OccurredAt);
    }

    // ---------- email normalization ----------

    [Theory]
    [InlineData("  Admin@Example.ORG ", "admin@example.org")]
    [InlineData("USER@X.COM", "user@x.com")]
    public void Build_normalizes_email_the_same_way_login_does(string submitted, string expected)
    {
        // AuthService.LoginAsync lower-cases and trims before looking a user up. If audit rows
        // stored the raw form, filtering the log by email would silently miss them.
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "auth.login", UserEmail = submitted }, null);

        Assert.Equal(expected, row.UserEmail);
    }

    // ---------- sanitizing ----------

    [Fact]
    public void Sanitize_strips_carriage_returns_and_newlines()
    {
        // Log forging: a newline in an attacker-supplied field is how a second, entirely
        // fabricated log line gets written inside the first one.
        var forged = "victim@example.org\r\n2026-01-01 admin@example.org auth.login SUCCESS";

        var cleaned = AuditEventFactory.Sanitize(forged, 256);

        Assert.NotNull(cleaned);
        Assert.DoesNotContain('\r', cleaned);
        Assert.DoesNotContain('\n', cleaned);
    }

    [Fact]
    public void Sanitize_strips_other_control_characters()
    {
        var cleaned = AuditEventFactory.Sanitize("ab\0c\ad\te", 64);
        Assert.Equal("abcde", cleaned);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Sanitize_returns_null_for_nothing_worth_recording(string? value)
    {
        Assert.Null(AuditEventFactory.Sanitize(value, 64));
    }

    [Fact]
    public void Sanitize_truncates_to_the_cap()
    {
        var cleaned = AuditEventFactory.Sanitize(new string('x', 500), 64);
        Assert.Equal(64, cleaned!.Length);
    }

    [Fact]
    public void Build_truncates_every_string_field_to_its_column_length()
    {
        // These caps must match AuditEventConfiguration, or a hostile user-agent string turns
        // into a failed audit write — losing exactly the event most worth keeping.
        var overlong = new string('x', 5000);
        var entry = new AuditEntry
        {
            Action = overlong,
            EntityType = overlong,
            UserEmail = overlong,
            UserRole = overlong,
            Summary = overlong,
            IpAddress = overlong,
            UserAgent = overlong,
            Metadata = overlong,
        };

        var row = AuditEventFactory.Build(entry, null);

        Assert.Equal(AuditEventFactory.ActionMaxLength, row.Action.Length);
        Assert.Equal(AuditEventFactory.EntityTypeMaxLength, row.EntityType!.Length);
        Assert.Equal(AuditEventFactory.UserEmailMaxLength, row.UserEmail.Length);
        Assert.Equal(AuditEventFactory.UserRoleMaxLength, row.UserRole!.Length);
        Assert.Equal(AuditEventFactory.SummaryMaxLength, row.Summary!.Length);
        Assert.Equal(AuditEventFactory.IpAddressMaxLength, row.IpAddress!.Length);
        Assert.Equal(AuditEventFactory.UserAgentMaxLength, row.UserAgent!.Length);
        Assert.Equal(AuditEventFactory.MetadataMaxLength, row.Metadata!.Length);
    }

    [Fact]
    public void Build_keeps_a_row_even_when_the_actor_is_unidentifiable()
    {
        // A failed login against a blank email still deserves a row; UserEmail is a required
        // column, so it falls back to empty rather than the write being dropped.
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "auth.login", UserEmail = "   ", Succeeded = false }, null);

        Assert.Equal(string.Empty, row.UserEmail);
        Assert.False(row.Succeeded);
    }

    // ---------- entity id resolution ----------

    [Fact]
    public void ResolveEntityId_prefers_the_explicitly_named_route_key()
    {
        var routes = new Dictionary<string, object?> { ["id"] = Actor, ["itemId"] = Subject };

        Assert.Equal(Subject, AuditEventFactory.ResolveEntityId(routes, "itemId", null));
    }

    [Fact]
    public void ResolveEntityId_falls_back_to_id_when_no_explicit_key_is_given()
    {
        var routes = new Dictionary<string, object?> { ["id"] = Actor, ["itemId"] = Subject };

        Assert.Equal(Actor, AuditEventFactory.ResolveEntityId(routes, null, null));
    }

    [Fact]
    public void ResolveEntityId_falls_back_to_id_when_the_explicit_key_is_absent()
    {
        var routes = new Dictionary<string, object?> { ["id"] = Actor };

        Assert.Equal(Actor, AuditEventFactory.ResolveEntityId(routes, "nope", null));
    }

    [Fact]
    public void ResolveEntityId_accepts_any_route_value_whose_name_ends_in_Id()
    {
        var routes = new Dictionary<string, object?> { ["participantId"] = Subject };

        Assert.Equal(Subject, AuditEventFactory.ResolveEntityId(routes, null, null));
    }

    [Fact]
    public void ResolveEntityId_reads_the_new_records_id_off_the_response_for_creates()
    {
        // A POST has no route id — the id of what was just created exists only in the body.
        var created = new CreatedThing { Id = Subject };

        Assert.Equal(Subject, AuditEventFactory.ResolveEntityId(
            new Dictionary<string, object?>(), null, created));
    }

    [Fact]
    public void ResolveEntityId_prefers_a_route_id_over_the_response_body()
    {
        var routes = new Dictionary<string, object?> { ["id"] = Actor };
        var body = new CreatedThing { Id = Subject };

        Assert.Equal(Actor, AuditEventFactory.ResolveEntityId(routes, null, body));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void ResolveEntityId_returns_null_rather_than_throwing_on_junk(string value)
    {
        var routes = new Dictionary<string, object?> { ["id"] = value };

        Assert.Null(AuditEventFactory.ResolveEntityId(routes, null, null));
    }

    [Fact]
    public void ResolveEntityId_returns_null_when_there_is_nothing_to_resolve()
    {
        Assert.Null(AuditEventFactory.ResolveEntityId(null, null, null));
        Assert.Null(AuditEventFactory.ResolveEntityId(new Dictionary<string, object?>(), null, null));
        Assert.Null(AuditEventFactory.ResolveEntityId(new Dictionary<string, object?>(), null, "a string"));
    }

    [Fact]
    public void ResolveEntityId_ignores_an_empty_guid()
    {
        // Guid.Empty is what an unset id looks like; recording it would imply a real record.
        var routes = new Dictionary<string, object?> { ["id"] = Guid.Empty };

        Assert.Null(AuditEventFactory.ResolveEntityId(routes, null, null));
    }

    private sealed class CreatedThing
    {
        public Guid Id { get; init; }
    }

    // ---------- status code mapping ----------

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(null, true)]   // no status set — MVC treats that as 200
    [InlineData(300, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(409, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    public void SucceededFrom_treats_only_2xx_as_success(int? statusCode, bool expected)
    {
        Assert.Equal(expected, AuditEventFactory.SucceededFrom(statusCode));
    }

    // ---------- the client-asserted forwarded address ----------
    //
    // X-Forwarded-For is the only audit field fed straight from a request header, it is
    // reachable without authenticating (a failed login writes a row), and Kestrel accepts a
    // header of roughly 32 KB. It used to be spliced into the metadata JSON as text and the
    // RESULT truncated, so a long value left the cut inside a string literal — unparseable
    // metadata, and ~8 KB written per request into a table that has no purge path.

    private static AuditContext WithForwardedFor(string? xff) => new(
        UserId: Actor, UserEmail: "admin@example.org", UserRole: "Admin",
        IpAddress: "203.0.113.7", UserAgent: "Mozilla/5.0", ClientAssertedIp: xff);

    [Fact]
    public void A_huge_client_asserted_address_still_leaves_parseable_metadata()
    {
        var flood = new string('9', 10_000);

        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "auth.login", Metadata = """{"reason":"bad password"}""" },
            WithForwardedFor(flood));

        // Parses at all — this is the assertion that used to fail.
        using var parsed = System.Text.Json.JsonDocument.Parse(row.Metadata!);
        Assert.Equal("bad password", parsed.RootElement.GetProperty("reason").GetString());
        Assert.Equal(
            AuditEventFactory.ClientAssertedIpMaxLength,
            parsed.RootElement.GetProperty("clientAssertedIp").GetString()!.Length);
    }

    [Fact]
    public void A_huge_client_asserted_address_is_capped_even_with_no_other_metadata()
    {
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "participant.list" },
            WithForwardedFor(new string('a', 10_000)));

        using var parsed = System.Text.Json.JsonDocument.Parse(row.Metadata!);
        Assert.Equal(
            AuditEventFactory.ClientAssertedIpMaxLength,
            parsed.RootElement.GetProperty("clientAssertedIp").GetString()!.Length);
    }

    [Fact]
    public void A_forwarded_address_is_folded_into_existing_metadata_without_disturbing_it()
    {
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "participant.view", Metadata = """{"statusCode":403}""" },
            WithForwardedFor("198.51.100.9, 203.0.113.7"));

        using var parsed = System.Text.Json.JsonDocument.Parse(row.Metadata!);
        Assert.Equal(403, parsed.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("198.51.100.9, 203.0.113.7", parsed.RootElement.GetProperty("clientAssertedIp").GetString());
    }

    [Fact]
    public void Metadata_that_is_not_a_json_object_is_kept_verbatim_rather_than_corrupted()
    {
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "participant.view", Metadata = "not json at all" },
            WithForwardedFor("198.51.100.9"));

        using var parsed = System.Text.Json.JsonDocument.Parse(row.Metadata!);
        Assert.Equal("not json at all", parsed.RootElement.GetProperty("detail").GetString());
        Assert.Equal("198.51.100.9", parsed.RootElement.GetProperty("clientAssertedIp").GetString());
    }

    [Fact]
    public void A_control_character_in_the_forwarded_header_cannot_forge_a_log_line()
    {
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "auth.login" },
            WithForwardedFor("198.51.100.9\r\nadmin@example.org auth.login SUCCESS"));

        using var parsed = System.Text.Json.JsonDocument.Parse(row.Metadata!);
        var value = parsed.RootElement.GetProperty("clientAssertedIp").GetString()!;
        Assert.DoesNotContain('\r', value);
        Assert.DoesNotContain('\n', value);
    }

    [Fact]
    public void No_forwarded_address_leaves_metadata_exactly_as_the_caller_wrote_it()
    {
        var row = AuditEventFactory.Build(
            new AuditEntry { Action = "auth.login", Metadata = """{"reason":"no such user"}""" },
            WithForwardedFor(null));

        Assert.Equal("""{"reason":"no such user"}""", row.Metadata);
    }

    // ---------- request metadata ----------

    [Fact]
    public void RequestMetadata_is_null_when_a_successful_request_has_nothing_to_add()
    {
        Assert.Null(AuditEventFactory.RequestMetadata(204, new Dictionary<string, object?>(), null));
    }

    [Fact]
    public void RequestMetadata_records_the_status_of_a_refused_request()
    {
        // The whole point of moving recording out of the action stage: 401/403/400 responses
        // the action never produced still land in the log with their status.
        var json = AuditEventFactory.RequestMetadata(403, new Dictionary<string, object?>(), null);

        using var parsed = System.Text.Json.JsonDocument.Parse(json!);
        Assert.Equal(403, parsed.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public void RequestMetadata_copies_the_named_route_values()
    {
        // Granting a staff account access to a program: EntityId is the account, and without
        // this the row would not say access to WHAT.
        var routes = new Dictionary<string, object?>
        {
            ["id"] = "0f9b6a10-1111-2222-3333-444444444444",
            ["staffId"] = Subject.ToString(),
        };

        var json = AuditEventFactory.RequestMetadata(204, routes, ["id"]);

        using var parsed = System.Text.Json.JsonDocument.Parse(json!);
        Assert.Equal("0f9b6a10-1111-2222-3333-444444444444", parsed.RootElement.GetProperty("id").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("statusCode", out _));
    }

    [Fact]
    public void RequestMetadata_records_an_exception_type_and_never_a_message()
    {
        var json = AuditEventFactory.RequestMetadata(null, null, null, "DbUpdateConcurrencyException");

        using var parsed = System.Text.Json.JsonDocument.Parse(json!);
        Assert.Equal("DbUpdateConcurrencyException", parsed.RootElement.GetProperty("exception").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("statusCode", out _));
    }

    [Fact]
    public void RequestMetadata_caps_and_sanitizes_a_hostile_route_value()
    {
        var routes = new Dictionary<string, object?> { ["slug"] = new string('x', 500) + "\r\nforged" };

        var json = AuditEventFactory.RequestMetadata(200, routes, ["slug"]);

        using var parsed = System.Text.Json.JsonDocument.Parse(json!);
        var value = parsed.RootElement.GetProperty("slug").GetString()!;
        Assert.Equal(AuditEventFactory.RouteMetadataValueMaxLength, value.Length);
        Assert.DoesNotContain('\n', value);
    }
}
