using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CRM.Application.DTOs.Audit;
using CRM.Application.DTOs.Participants;
using CRM.Application.Exceptions;
using CRM.Application.Files;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <summary>
/// Self-serve replacement for the one-off data loader in tools/. Accepts the Stars export's
/// own column names (so export → fix → import round-trips) and the review sheet's names as
/// aliases, applies the loader's rules, and refuses to write anything while any row has a
/// problem. Rows are reported by their spreadsheet line number so a fix is a lookup, not a hunt.
/// </summary>
public class ParticipantImportService : IParticipantImportService
{
    /// <summary>A roster of a few hundred rows is tens of KB; 5 MB is a generous ceiling.</summary>
    public const long MaxCsvBytes = 5L * 1024 * 1024;

    private const int MaxReportRows = 2000;

    private static readonly string[] UnresolvedMarkers = { "REVIEW", "CONFLICT", "SHIFT", "MISSING" };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "yyyy/M/d", "d-MMM-yyyy", "MMM d, yyyy", "MMMM d, yyyy",
    };

    // canonical key → every header spelling accepted for it (case-insensitive, trimmed).
    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["name"]        = new[] { "Name", "Full name", "Star", "Student", "Participant" },
        ["first"]       = new[] { "First name", "First" },
        ["last"]        = new[] { "Last name", "Last" },
        ["dob"]         = new[] { "DOB", "Date of birth", "Birth date", "Birthdate" },
        ["birthYear"]   = new[] { "Birth year" },
        ["program"]     = new[] { "Program", "Primary program", "Programme" },
        ["secondary"]   = new[] { "Also enrolled in", "Secondary program", "Secondary programme" },
        ["status"]      = new[] { "Status" },
        ["start"]       = new[] { "Started", "Start date", "Start" },
        ["sc"]          = new[] { "Service coordinator", "SC", "Coordinator" },
        ["scEmail"]     = new[] { "Service coordinator email", "SC email", "Coordinator email" },
        ["scPhone"]     = new[] { "Service coordinator phone", "SC phone", "Coordinator phone" },
        ["guardian"]    = new[] { "Guardian", "Guardian name", "Parent", "Parent / guardian" },
        ["guardianPhone"] = new[] { "Guardian phone", "Parent phone", "Phone" },
        ["guardianEmail"] = new[] { "Guardian email", "Parent email", "Email" },
        ["referral"]    = new[] { "Referral source", "Referral", "Referred by" },
        ["shirt"]       = new[] { "T-shirt size", "Shirt", "Shirt size", "T-shirt" },
        ["pos"]         = new[] { "POS expiry", "POS exp", "POS expires", "Authorization expiry", "Authorization expires" },
        ["ipp"]         = new[] { "IPP expiry", "IPP exp", "IPP expires" },
        ["allergies"]   = new[] { "Allergies (* = anaphylactic)", "Allergies", "Allergy" },
        ["anaphylactic"] = new[] { "Anaphylactic?", "Anaphylactic" },
        ["areas"]       = new[] { "Areas of concern", "Concerns" },
        ["remind"]      = new[] { "Contact in Remind", "Remind", "Remind contact" },
        ["notes"]       = new[] { "Notes", "Intake notes", "Note" },
        ["flags"]       = new[] { "Review flags", "Flags" },
    };

    public IReadOnlyList<string> TemplateHeaders { get; } = new[]
    {
        "Name", "DOB", "Program", "Also enrolled in", "Status", "Started",
        "Service coordinator", "Service coordinator email", "Service coordinator phone",
        "Guardian", "Guardian phone", "Guardian email", "Referral source", "T-shirt size",
        "POS expiry", "IPP expiry", "Allergies (* = anaphylactic)", "Areas of concern",
        "Contact in Remind", "Notes",
    };

    private readonly IUnitOfWork _uow;
    private readonly IProgramAccessService _access;
    private readonly IAuditService _audit;
    private readonly IOrgClock _clock;

    public ParticipantImportService(IUnitOfWork uow, IProgramAccessService access, IAuditService audit, IOrgClock clock)
    {
        _uow = uow;
        _access = access;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ParticipantImportReportDto> ImportAsync(Guid userId, Stream csv, string fileName, bool commit, CancellationToken ct = default)
    {
        var report = new ParticipantImportReportDto { FileName = FileValidation.SanitizeFileName(fileName) };

        // ---- read + parse ---------------------------------------------------------
        using var buffer = new MemoryStream();
        await csv.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw new InvalidFileException("The uploaded file is empty.");
        if (buffer.Length > MaxCsvBytes) throw new InvalidFileException("That spreadsheet is over the 5 MB limit for an import.");
        var bytes = buffer.ToArray();
        report.SourceHash = Convert.ToHexString(SHA256.HashData(bytes))[..16];

        var rows = Csv.Parse(Encoding.UTF8.GetString(bytes));
        if (rows.Count < 2)
        {
            report.FileProblems.Add("The file has a header row but no data rows.");
            return report;
        }

        var header = rows[0].Select(h => h.Trim()).ToArray();
        var col = new Dictionary<string, int>();
        foreach (var (key, aliases) in Columns)
        {
            var i = Array.FindIndex(header, h => aliases.Any(a => string.Equals(a, h, StringComparison.OrdinalIgnoreCase)));
            if (i >= 0) col[key] = i;
        }

        var hasName = col.ContainsKey("name") || (col.ContainsKey("first") && col.ContainsKey("last"));
        if (!hasName) report.FileProblems.Add("No name column found — the sheet needs a 'Name' column (or 'First name' and 'Last name').");
        if (!col.ContainsKey("program")) report.FileProblems.Add("No 'Program' column found.");
        if (report.FileProblems.Count > 0) return report;

        // ---- reference data ---------------------------------------------------------
        var access = await _access.ForUserAsync(userId);
        var programs = await _uow.Programs.GetAllAsync(ct);
        var programByKey = new Dictionary<string, CrmProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in programs)
        {
            programByKey.TryAdd(p.Name.Trim(), p);
            programByKey.TryAdd(p.Slug.Trim(), p);
        }

        var existing = await _uow.Participants.GetAllAsync(ct);
        var existingByName = existing
            .GroupBy(p => NormalizeName(p.FullName))
            .ToDictionary(g => g.Key, g => g.ToList());
        var usedInitials = new HashSet<string>(existing.Select(p => p.Initials), StringComparer.OrdinalIgnoreCase);
        var seenNames = new Dictionary<string, int>(); // normalised name → first line

        // ---- validate every row before writing anything ------------------------------
        var staged = new List<(ParticipantImportRowDto Row, Participant Entity)>();
        var today = _clock.Today;

        for (var r = 1; r < rows.Count && r <= MaxReportRows; r++)
        {
            var raw = rows[r];
            string Cell(string key) => col.TryGetValue(key, out var i) && i < raw.Length ? raw[i].Trim() : string.Empty;

            var line = r + 1;
            var fullName = col.ContainsKey("name")
                ? Cell("name")
                : $"{Cell("first")} {Cell("last")}".Trim();
            fullName = CollapseSpaces(fullName);

            var row = new ParticipantImportRowDto { Line = line, Name = fullName, Program = Cell("program") };
            report.Rows.Add(row);
            var problems = row.Messages;

            // Unresolved review markers from the review sheet are a hard stop, as before.
            var flags = Cell("flags");
            if (UnresolvedMarkers.Any(m => flags.Contains(m, StringComparison.OrdinalIgnoreCase)))
                problems.Add($"Unresolved review flag(s): {flags}");

            if (fullName.Length == 0) { problems.Add("No name."); row.Status = "error"; continue; }

            // ---- program(s)
            CrmProgram? program = null;
            var programText = Cell("program");
            if (programText.Length == 0) problems.Add("Program is blank.");
            else if (!programByKey.TryGetValue(programText, out program))
                problems.Add($"Unknown program '{programText}'. Programs are: {string.Join(", ", programs.Select(p => p.Name))}.");
            else if (!access.CanAccess(program.Id))
                problems.Add($"You are not assigned to {program.Name}.");

            CrmProgram? secondary = null;
            var secondaryText = Cell("secondary");
            if (secondaryText.Length > 0)
            {
                if (!programByKey.TryGetValue(secondaryText, out secondary))
                    problems.Add($"Unknown secondary program '{secondaryText}'.");
                else if (program is not null && secondary.Id == program.Id)
                    problems.Add("Secondary program is the same as the primary.");
                else if (!access.CanAccess(secondary.Id))
                    problems.Add($"You are not assigned to {secondary.Name}.");
            }

            // ---- status: the export's labels, the enum names, or blank (= Active)
            var status = ParticipantStatus.Active;
            var statusText = Cell("status");
            if (statusText.Length > 0 && !TryParseStatus(statusText, out status))
                problems.Add($"Status '{statusText}' is not one of: {string.Join(", ", Enum.GetNames<ParticipantStatus>())}.");

            // ---- dates: never defaulted silently
            var startText = Cell("start");
            if (!TryDate(startText, out var start))
                problems.Add(startText.Length == 0
                    ? "Start date is missing — enter the date the star started (the app would otherwise stamp today)."
                    : $"Start date '{startText}' is not a date (use YYYY-MM-DD).");

            var dob = OptionalDate(Cell("dob"), "DOB", problems);
            var pos = OptionalDate(Cell("pos"), "POS expiry", problems);
            var ipp = OptionalDate(Cell("ipp"), "IPP expiry", problems);

            int? birthYear = dob?.Year;
            var birthYearText = Cell("birthYear");
            if (birthYear is null && birthYearText.Length > 0)
            {
                if (int.TryParse(birthYearText, out var by) && by is >= 1900 and <= 2100) birthYear = by;
                else problems.Add($"Birth year '{birthYearText}' is not a year.");
            }

            // ---- allergies: the export writes "peanuts *" for anaphylactic; the review sheet
            // has a separate Anaphylactic? column and an "(empty at source)" placeholder that is
            // provenance, not data.
            var allergies = Cell("allergies");
            var anaphylactic = false;
            if (allergies.EndsWith('*')) { anaphylactic = true; allergies = allergies.TrimEnd('*').Trim(); }
            var anaText = Cell("anaphylactic");
            if (anaText.Contains("REVIEW", StringComparison.OrdinalIgnoreCase))
                problems.Add("'Anaphylactic?' still reads REVIEW — must be Yes or blank.");
            else if (anaText.StartsWith('Y') || anaText.StartsWith('y') || anaText.Equals("true", StringComparison.OrdinalIgnoreCase))
                anaphylactic = true;
            var allergiesUnverified = allergies.Contains("(empty at source", StringComparison.OrdinalIgnoreCase);
            if (allergiesUnverified) allergies = string.Empty;
            if (allergies.Equals("none", StringComparison.OrdinalIgnoreCase) || allergies == "—" || allergies == "-") allergies = string.Empty;

            // ---- duplicates: against the database and within the sheet
            var key = NormalizeName(fullName);
            if (existingByName.TryGetValue(key, out var matches))
            {
                var sameChild = matches.Any(m => m.DateOfBirth is null || dob is null || m.DateOfBirth.Value.Date == dob.Value.Date);
                if (sameChild) problems.Add("A star with this name already exists — the import does not update existing records.");
            }
            if (seenNames.TryGetValue(key, out var firstLine)) problems.Add($"Duplicate of line {firstLine} in this sheet.");
            else seenNames[key] = line;

            var notes = string.Join(" | ", new[]
            {
                Cell("notes"),
                allergiesUnverified ? "Allergies: blank in the source spreadsheet at import — NOT verified with a guardian." : string.Empty,
            }.Where(x => x.Length > 0));

            var entity = new Participant
            {
                FullName = fullName,
                ProgramId = program?.Id ?? Guid.Empty,
                SecondaryProgramId = secondary?.Id,
                Status = status,
                StartDate = start == default ? today : start,
                BirthYear = birthYear,
                DateOfBirth = dob,
                AuthorizationExpiry = pos,
                IppExpiry = ipp,
                Allergies = NullIfBlank(allergies),
                AllergyAnaphylactic = anaphylactic,
                AreasOfConcern = NullIfBlank(Cell("areas")),
                GuardianName = NullIfBlank(Cell("guardian")),
                GuardianPhone = NullIfBlank(Cell("guardianPhone")),
                GuardianEmail = NullIfBlank(Cell("guardianEmail")),
                ReferralSource = NullIfBlank(Cell("referral")),
                ServiceCoordinator = NullIfBlank(Cell("sc")),
                ServiceCoordinatorEmail = NullIfBlank(Cell("scEmail")),
                ServiceCoordinatorPhone = NullIfBlank(Cell("scPhone")),
                ContactInRemind = NullIfBlank(Cell("remind")),
                TShirtSize = NullIfBlank(Cell("shirt")),
                IntakeNotes = NullIfBlank(notes),
            };

            // Fail loudly rather than truncate — a silently shortened guardian phone is a
            // number nobody can call.
            foreach (var (field, value, max) in new (string, string?, int)[]
            {
                ("Name", entity.FullName, 200), ("Guardian", entity.GuardianName, 200),
                ("Guardian phone", entity.GuardianPhone, 50), ("Guardian email", entity.GuardianEmail, 200),
                ("Service coordinator", entity.ServiceCoordinator, 200),
                ("Service coordinator email", entity.ServiceCoordinatorEmail, 200),
                ("Service coordinator phone", entity.ServiceCoordinatorPhone, 50),
                ("Referral source", entity.ReferralSource, 200), ("T-shirt size", entity.TShirtSize, 20),
                ("Allergies", entity.Allergies, 500), ("Areas of concern", entity.AreasOfConcern, 1000),
                ("Contact in Remind", entity.ContactInRemind, 300), ("Notes", entity.IntakeNotes, ParticipantLimits.IntakeNotesMax),
            })
                if (value is not null && value.Length > max)
                    problems.Add($"{field} is {value.Length} characters; the limit is {max}.");

            if (problems.Count > 0) { row.Status = "error"; continue; }

            entity.Initials = UniqueInitials(fullName, usedInitials);
            staged.Add((row, entity));
        }

        if (rows.Count - 1 > MaxReportRows)
            report.FileProblems.Add($"Only the first {MaxReportRows} rows were checked — split the sheet.");

        report.RowCount = rows.Count - 1;
        report.ReadyCount = staged.Count;
        report.ProblemCount = report.Rows.Count(x => x.Status == "error");

        if (!commit || report.ProblemCount > 0 || report.FileProblems.Count > 0 || staged.Count == 0)
            return report;

        // ---- commit: every row in one save, so a failure writes nothing ----------------
        foreach (var (_, entity) in staged)
            await _uow.Participants.AddAsync(entity);
        await _uow.SaveChangesAsync();

        foreach (var (row, entity) in staged)
        {
            row.Status = "created";
            row.ParticipantId = entity.Id;
            // One row per created star, like the loader wrote, so "where did this record come
            // from" has an answer. Best-effort by contract (IAuditService never throws).
            await _audit.RecordAsync(new AuditEntry
            {
                Action = "participant.import",
                EntityType = "Participant",
                EntityId = entity.Id,
                UserId = userId,
                Summary = $"Imported from {report.FileName} (line {row.Line})",
                Metadata = $"{{\"sourceHash\":\"{report.SourceHash}\",\"line\":{row.Line}}}",
            }, ct);
        }

        report.Committed = true;
        report.CreatedCount = staged.Count;
        return report;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static bool TryParseStatus(string text, out ParticipantStatus status)
    {
        var t = text.Trim().ToLowerInvariant();
        switch (t)
        {
            case "needs attention": status = ParticipantStatus.Attention; return true;
            case "auth pending": case "authorization pending": status = ParticipantStatus.AuthPending; return true;
            case "not interested": status = ParticipantStatus.NotInterested; return true;
        }
        return Enum.TryParse(text.Replace(" ", string.Empty), ignoreCase: true, out status);
    }

    private static bool TryDate(string text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static DateTime? OptionalDate(string text, string label, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "—" || text == "-") return null;
        if (TryDate(text, out var d)) return d;
        problems.Add($"{label} '{text}' is not a date (use YYYY-MM-DD).");
        return null;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string CollapseSpaces(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeName(string s) => CollapseSpaces(s).ToLowerInvariant();

    /// <summary>Ports the frontend's initialsOf: first + last initial, or two letters of a single name.</summary>
    private static string InitialsOf(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    /// <summary>Siblings collide on first+last initial; widen with more of the first name, inside the 5-char column.</summary>
    private static string UniqueInitials(string fullName, HashSet<string> used)
    {
        var baseInitials = InitialsOf(fullName);
        if (used.Add(baseInitials)) return baseInitials;

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : fullName;
        for (var extra = 2; extra <= 4 && extra <= firstName.Length; extra++)
        {
            var candidate = $"{firstName[..extra]}{parts[^1][0]}".ToUpperInvariant();
            if (candidate.Length <= 5 && used.Add(candidate)) return candidate;
        }
        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{baseInitials}{n}";
            if (candidate.Length <= 5 && used.Add(candidate)) return candidate;
        }
        return baseInitials;
    }
}
