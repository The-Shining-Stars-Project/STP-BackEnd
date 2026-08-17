using CRM.Application.DTOs.Audit;

namespace CRM.Tests;

/// <summary>
/// POST /api/audit/export is the one endpoint where a caller writes strings of their choosing
/// into the audit log. These tests pin the hardening that keeps it from being a log-forging
/// and log-flooding primitive — a cheap way to bury a real event under plausible noise.
/// </summary>
public class AuditExportValidationTests
{
    private static RecordExportDto Valid() => new()
    {
        ExportKind = "participant-roster",
        RowCount = 42,
        FileName = "stars.csv",
        Scope = "All active",
    };

    [Fact]
    public void Accepts_a_known_export_kind()
    {
        Assert.True(ExportAuditValidation.TryValidate(Valid(), out var validated, out var error));

        Assert.Null(error);
        Assert.Equal("participant-roster", validated.ExportKind);
        Assert.Equal(42, validated.RowCount);
        Assert.Equal("stars.csv", validated.FileName);
        Assert.Equal("All active", validated.Scope);
    }

    [Theory]
    [InlineData("reports-summary")]
    [InlineData("star-attendance")]
    [InlineData("participant-roster")]
    [InlineData("stars-detail")]
    [InlineData("staff-onboarding")]
    public void Accepts_every_kind_the_frontend_actually_exports(string kind)
    {
        var dto = Valid();
        dto.ExportKind = kind;

        Assert.True(ExportAuditValidation.TryValidate(dto, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("everything")]
    [InlineData("Participant-Roster")]     // whitelist is ordinal — case must match exactly
    [InlineData("participant-roster; DROP")]
    public void Rejects_anything_outside_the_whitelist(string kind)
    {
        var dto = Valid();
        dto.ExportKind = kind;

        Assert.False(ExportAuditValidation.TryValidate(dto, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejection_message_does_not_echo_the_submitted_value_back()
    {
        // Echoing unvalidated input into an error body is how a rejected value ends up
        // rendered somewhere it was never sanitized for.
        var dto = Valid();
        dto.ExportKind = "<script>alert(1)</script>";

        Assert.False(ExportAuditValidation.TryValidate(dto, out _, out var error));
        Assert.DoesNotContain("script", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_missing_body()
    {
        Assert.False(ExportAuditValidation.TryValidate(null, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    [InlineData(int.MaxValue, ExportAuditValidation.MaxRowCount)]
    public void Clamps_the_row_count_to_a_believable_range(int submitted, int expected)
    {
        var dto = Valid();
        dto.RowCount = submitted;

        Assert.True(ExportAuditValidation.TryValidate(dto, out var validated, out _));
        Assert.Equal(expected, validated.RowCount);
    }

    [Fact]
    public void Caps_the_filename_length()
    {
        var dto = Valid();
        dto.FileName = new string('x', 1000);

        Assert.True(ExportAuditValidation.TryValidate(dto, out var validated, out _));
        Assert.Equal(ExportAuditValidation.FileNameMaxLength, validated.FileName!.Length);
    }

    [Fact]
    public void Caps_the_scope_length()
    {
        var dto = Valid();
        dto.Scope = new string('y', 1000);

        Assert.True(ExportAuditValidation.TryValidate(dto, out var validated, out _));
        Assert.Equal(ExportAuditValidation.ScopeMaxLength, validated.Scope!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_optional_fields_become_null(string? value)
    {
        var dto = Valid();
        dto.FileName = value;
        dto.Scope = value;

        Assert.True(ExportAuditValidation.TryValidate(dto, out var validated, out _));
        Assert.Null(validated.FileName);
        Assert.Null(validated.Scope);
    }

    [Fact]
    public void Control_characters_in_the_filename_are_stripped_before_the_row_is_built()
    {
        // Validation caps length; AuditEventFactory.Sanitize — which every audit write goes
        // through — is what removes the newline that would forge a second log line.
        var dto = Valid();
        dto.FileName = "roster.csv\r\nadmin@example.org auth.login SUCCESS";

        Assert.True(ExportAuditValidation.TryValidate(dto, out var validated, out _));

        var cleaned = CRM.Application.Services.AuditEventFactory.Sanitize(validated.FileName, 128);
        Assert.NotNull(cleaned);
        Assert.DoesNotContain('\r', cleaned);
        Assert.DoesNotContain('\n', cleaned);
    }
}
