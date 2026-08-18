using CRM.Application.DTOs.Audit;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CRM.DataLoader;

/// <summary>
/// Loads the SIGNED-OFF review sheet into an empty Participants table, writing the audit
/// rows the application would have written had a person typed each record in.
///
/// Three decisions are worth stating, because each is a deliberate departure from how the
/// running application behaves:
///
/// 1. It does NOT call IAuditService. That writer is fail-open on a separate DbContext, so
///    a failed audit write never blocks a business write — the right trade for a teacher
///    halfway through attendance, and the wrong one here. A bulk load that half-commits its
///    audit trail is worse than one that does not run. So the rows are built with the same
///    AuditEventFactory the application uses (identical sanitising and length caps, so the
///    rows cannot drift from the real ones) and inserted in the SAME transaction as the
///    participants. Either every record and its audit row lands, or nothing does.
///
/// 2. It refuses to run against a non-empty Participants table. The attack review flagged
///    that "the table is empty" was stated as a human precondition; it is now asserted in
///    code, because the realistic trigger for a double-load is a partial failure, not
///    carelessness.
///
/// 3. It refuses on any unresolved review marker. Every judgement the review sheet flags —
///    anaphylaxis, status contradictions, shifted rows, ambiguous dates, missing programme —
///    must be settled by a person before a single row loads. The loader has no fallback
///    behaviour for them on purpose: a fallback is a guess, and the guesses are exactly what
///    made an automated import unsafe in the first place.
///
/// Usage:
///   dotnet run --project tools/CRM.DataLoader -- --csv "REVIEW.csv" --conn "&lt;conn&gt;"          (dry run)
///   dotnet run --project tools/CRM.DataLoader -- --csv "REVIEW.csv" --conn "&lt;conn&gt;" --commit
/// </summary>
public static class Program
{
    private const string ImportActor = "data-migration@theshiningstarsproject.org";

    /// <summary>Markers the review sheet uses for "a person still has to decide this".</summary>
    private static readonly string[] UnresolvedMarkers =
        ["REVIEW", "CONFLICT", "SHIFT", "MISSING"];

    public static async Task<int> Main(string[] args)
    {
        var csvPath = Arg(args, "--csv");
        var conn = Arg(args, "--conn") ?? Environment.GetEnvironmentVariable("CRM_CONN");
        var commit = args.Contains("--commit");

        if (csvPath is null || conn is null)
        {
            Console.Error.WriteLine(
                "usage: --csv <signed-off review sheet as .csv> --conn <connection string> [--commit]");
            return 2;
        }
        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"csv not found: {csvPath}");
            return 2;
        }

        // The workbook hash goes into the batch audit row, so a second run against a
        // different file — or the same one — is detectable after the fact.
        var bytes = await File.ReadAllBytesAsync(csvPath);
        var sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];

        var rows = Csv.Parse(System.Text.Encoding.UTF8.GetString(bytes));
        if (rows.Count < 2) { Console.Error.WriteLine("csv has no data rows"); return 2; }

        var header = rows[0].Select(h => h.Trim()).ToArray();
        int Col(string name)
        {
            var i = Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            if (i < 0) throw new InvalidOperationException($"review sheet is missing the '{name}' column");
            return i;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn, o =>
            {
                // Mirrors AddPersistenceServices so the load runs against the same client
                // behaviour production uses.
                o.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                o.CommandTimeout(120);
            })
            .Options;

        await using var db = new AppDbContext(options);

        // ---- gate 1: the table must be empty TO COMMIT ----------------------------
        // Only fatal on the commit path. A dry run writes nothing, and a reviewer wants to
        // validate their sheet long before the production database exists — refusing to
        // check their work until the table is empty would just push them to skip the check.
        var existing = await db.Participants.CountAsync();
        if (existing > 0)
        {
            if (commit)
            {
                Console.Error.WriteLine(
                    $"REFUSING: Participants already holds {existing} row(s). This loader only ever runs " +
                    "against an empty table. If a previous run failed partway, clear the table and re-run.");
                return 1;
            }
            Console.WriteLine(
                $"note: Participants already holds {existing} row(s). Validating the sheet anyway; " +
                "a --commit run against this database would refuse.");
        }

        var programBySlug = await db.Programs.ToDictionaryAsync(
            p => p.Slug, p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (programBySlug.Count == 0)
        {
            Console.Error.WriteLine("REFUSING: no programmes exist yet. Run migrations/seed first.");
            return 1;
        }

        // ---- parse + validate every row before writing anything ------------------
        var problems = new List<string>();
        var staged = new List<Participant>();
        var usedInitials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(string name)
            {
                var i = Col(name);
                return i < row.Length ? row[i].Trim() : string.Empty;
            }
            var line = r + 1; // 1-based, matching what the reviewer sees in Excel

            var flags = Cell("Review flags");
            var last = Cell("Last name");
            var first = Cell("First name");
            var label = string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first)
                ? $"row {line}" : $"row {line} ({last}, {first})".Trim();

            // gate 3: nothing unresolved may load.
            var unresolved = UnresolvedMarkers
                .Where(m => flags.Contains(m, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (unresolved.Count > 0)
                problems.Add($"{label}: unresolved review flag(s) [{flags}] — a person must settle these first");

            if (Cell("Anaphylactic?").Contains("REVIEW", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{label}: 'Anaphylactic?' still reads REVIEW — must be YES or blank");

            // The "(empty at source)" placeholder is INFORMATIONAL, not a blocker. It marks
            // "nobody has checked" as distinct from "checked, none" — worth saying loudly on
            // the review sheet, worth carrying onto the record, and actively harmful as a
            // load gate: 108 identical blockers is a wall a reviewer clears without reading,
            // which destroys the very signal it exists to preserve. So the loader strips it,
            // stores nothing in Allergies, and records the provenance in the notes instead.
            var allergies = Cell("Allergies");
            var allergiesUnverified = allergies.Contains("(empty at source", StringComparison.OrdinalIgnoreCase);
            if (allergiesUnverified) allergies = string.Empty;

            var fullName = $"{first} {last}".Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            { problems.Add($"{label}: no name"); continue; }

            // programme
            var slug = Cell("Program");
            if (string.IsNullOrWhiteSpace(slug) || !programBySlug.TryGetValue(slug, out var programId))
            { problems.Add($"{label}: programme '{slug}' is blank or unknown"); continue; }

            Guid? secondaryId = null;
            var slug2 = Cell("Secondary program");
            if (!string.IsNullOrWhiteSpace(slug2))
            {
                if (!programBySlug.TryGetValue(slug2, out var sid))
                { problems.Add($"{label}: secondary programme '{slug2}' unknown"); continue; }
                secondaryId = sid;
            }

            // status
            var statusText = Cell("Status").Replace(" ", "");
            if (!Enum.TryParse<ParticipantStatus>(statusText, ignoreCase: true, out var status))
            { problems.Add($"{label}: status '{Cell("Status")}' is not one of {string.Join('/', Enum.GetNames<ParticipantStatus>())}"); continue; }

            // dates — never defaulted silently
            if (!TryDate(Cell("Start date"), out var start))
            { problems.Add($"{label}: start date '{Cell("Start date")}' is missing or not YYYY-MM-DD — the app would silently stamp today"); continue; }
            DateTime? dob = TryDate(Cell("DOB"), out var d1) ? d1 : null;
            DateTime? pos = TryDate(Cell("POS exp"), out var d2) ? d2 : null;
            DateTime? ipp = TryDate(Cell("IPP exp"), out var d3) ? d3 : null;
            foreach (var (col, raw) in new[] { ("DOB", Cell("DOB")), ("POS exp", Cell("POS exp")), ("IPP exp", Cell("IPP exp")) })
                if (!string.IsNullOrWhiteSpace(raw) && !TryDate(raw, out _))
                    problems.Add($"{label}: {col} '{raw}' is not YYYY-MM-DD");

            var p = new Participant
            {
                FullName = fullName,
                Initials = UniqueInitials(fullName, usedInitials),
                ProgramId = programId,
                SecondaryProgramId = secondaryId,
                Status = status,
                StartDate = start,
                DateOfBirth = dob,
                AuthorizationExpiry = pos,
                IppExpiry = ipp,
                Allergies = NullIfBlank(allergies),
                AllergyAnaphylactic = Cell("Anaphylactic?")
                    .StartsWith("Y", StringComparison.OrdinalIgnoreCase),
                AreasOfConcern = NullIfBlank(Cell("Areas of concern")),
                GuardianName = NullIfBlank(Cell("Guardian")),
                GuardianPhone = NullIfBlank(Cell("Guardian phone")),
                GuardianEmail = NullIfBlank(Cell("Guardian email")),
                ServiceCoordinator = NullIfBlank(Cell("Service coordinator")),
                TShirtSize = NullIfBlank(Cell("Shirt")),
                IntakeNotes = NullIfBlank(string.Join(" | ", new[]
                {
                    Cell("Notes"),
                    allergiesUnverified
                        ? "Allergies: blank in the source spreadsheet at import — NOT verified with a guardian."
                        : string.Empty,
                }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            };

            // gate: lengths. Fail loudly rather than truncate — a silently shortened
            // guardian phone is a number nobody can call.
            foreach (var (field, value, max) in new (string, string?, int)[]
            {
                ("FullName", p.FullName, 200), ("Initials", p.Initials, 5),
                ("GuardianName", p.GuardianName, 200), ("GuardianPhone", p.GuardianPhone, 50),
                ("GuardianEmail", p.GuardianEmail, 200), ("ServiceCoordinator", p.ServiceCoordinator, 200),
                ("TShirtSize", p.TShirtSize, 20), ("Allergies", p.Allergies, 500),
                ("AreasOfConcern", p.AreasOfConcern, 1000), ("IntakeNotes", p.IntakeNotes, 2000),
            })
                if (value is not null && value.Length > max)
                    problems.Add($"{label}: {field} is {value.Length} chars, limit {max} — shorten it in the review sheet");

            staged.Add(p);
        }

        // ---- report ---------------------------------------------------------------
        Console.WriteLine($"review sheet : {Path.GetFileName(csvPath)}  (sha256 {sourceHash})");
        Console.WriteLine($"data rows    : {rows.Count - 1}");
        Console.WriteLine($"ready to load: {staged.Count}");
        Console.WriteLine($"problems     : {problems.Count}");
        if (problems.Count > 0)
        {
            Console.WriteLine();
            foreach (var pr in problems.Take(100)) Console.WriteLine("  - " + pr);
            if (problems.Count > 100) Console.WriteLine($"  ... and {problems.Count - 40} more");
            Console.WriteLine();
            Console.Error.WriteLine("REFUSING to load. Fix the review sheet and re-run. Nothing was written.");
            return 1;
        }

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("DRY RUN — nothing written. Re-run with --commit to load.");
            return 0;
        }

        // ---- load: participants + their audit rows, one transaction ---------------
        // EnableRetryOnFailure installs an execution strategy, and an execution strategy
        // refuses a user-initiated transaction unless the whole unit is run through it.
        // This is the pattern the Persistence DI comment points at.
        var strategy = db.Database.CreateExecutionStrategy();
        var loadedAt = DateTime.UtcNow;

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Re-assert emptiness inside the transaction. The count above was outside it,
            // and this is the check that actually protects against a concurrent second run.
            if (await db.Participants.CountAsync() > 0)
                throw new InvalidOperationException("Participants became non-empty; aborting.");

            db.Participants.AddRange(staged);

            foreach (var p in staged)
            {
                db.AuditEvents.Add(AuditEventFactory.Build(new AuditEntry
                {
                    Action = "participant.import",
                    EntityType = "Participant",
                    EntityId = p.Id,
                    UserEmail = ImportActor,
                    UserRole = "System",
                    Succeeded = true,
                    Summary = $"Imported from reviewed migration sheet: {p.FullName}",
                    OccurredAt = loadedAt,
                    Metadata = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        source = Path.GetFileName(csvPath),
                        sourceHash,
                        reviewed = true,
                    }),
                }, ambient: null));
            }

            // One batch row so the log answers "where did all this data come from" in a
            // single entry, not only 128 individual ones.
            db.AuditEvents.Add(AuditEventFactory.Build(new AuditEntry
            {
                Action = "participant.import.batch",
                EntityType = "Participant",
                UserEmail = ImportActor,
                UserRole = "System",
                Succeeded = true,
                Summary = $"Bulk import of {staged.Count} reviewed participant records",
                OccurredAt = loadedAt,
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    source = Path.GetFileName(csvPath),
                    sourceHash,
                    recordCount = staged.Count,
                }),
            }, ambient: null));

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });

        Console.WriteLine();
        Console.WriteLine($"LOADED {staged.Count} participants and {staged.Count + 1} audit rows in one transaction.");
        Console.WriteLine("Verify in the app: /audit filtered to action 'participant.import'.");
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Strict YYYY-MM-DD only. Anything else is a review-sheet problem, not a parse puzzle.</summary>
    private static bool TryDate(string s, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out value);
    }

    /// <summary>
    /// Port of the frontend's initialsOf (lib/format.ts), plus collision handling it does
    /// not need and this does: siblings share a surname, and Initials is only 5 characters.
    /// </summary>
    internal static string InitialsOf(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    private static string UniqueInitials(string fullName, HashSet<string> used)
    {
        var baseInitials = InitialsOf(fullName);
        if (used.Add(baseInitials)) return baseInitials;

        // Siblings collide on first+last initial. Widen with more of the first name,
        // staying inside the 5-character column.
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : fullName;
        for (var extra = 2; extra <= 4 && extra <= firstName.Length; extra++)
        {
            var candidate = ($"{firstName[..extra]}{parts[^1][0]}").ToUpperInvariant();
            if (candidate.Length <= 5 && used.Add(candidate)) return candidate;
        }
        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{baseInitials}{n}";
            if (candidate.Length <= 5 && used.Add(candidate)) return candidate;
        }
        throw new InvalidOperationException($"could not derive unique initials for {fullName}");
    }
}
