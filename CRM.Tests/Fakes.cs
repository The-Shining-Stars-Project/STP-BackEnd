using System.Linq.Expressions;
using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using CRM.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace CRM.Tests;

/// <summary>
/// List-backed <see cref="IRepository{T}"/>. Enough to drive the services under test
/// without a database — the SQL Server <c>rowversion</c> columns have no SQLite equivalent,
/// and the scoping rules under test are pure in-process logic.
/// </summary>
internal sealed class FakeRepository<T> : IRepository<T> where T : BaseEntity
{
    public List<T> Items { get; } = new();

    public Task<T?> GetByIdAsync(Guid id) =>
        Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<T>>(Items.ToList());

    public Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<T>>(
            predicate is null ? Items.ToList() : Items.Where(predicate.Compile()).ToList());

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(predicate.Compile()));

    public Task<T> AddAsync(T entity)
    {
        Items.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(T entity) => Task.CompletedTask;

    public Task DeleteAsync(T entity)
    {
        Items.Remove(entity);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IUnitOfWork"/>. Expose the repositories you need to seed.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public FakeRepository<Participant> ParticipantsRepo { get; } = new();
    public FakeRepository<DocumentRecord> DocumentRecordsRepo { get; } = new();
    public FakeRepository<StaffMember> StaffRepo { get; } = new();
    public FakeRepository<CrmProgram> ProgramsRepo { get; } = new();
    public FakeRepository<User> UsersRepo { get; } = new();
    public FakeRepository<PerStarPlan> PerStarPlansRepo { get; } = new();
    public FakeRepository<RefreshToken> RefreshTokensRepo { get; } = new();
    public FakeRepository<MfaChallenge> MfaChallengesRepo { get; } = new();
    public FakeRepository<MfaRecoveryCode> MfaRecoveryCodesRepo { get; } = new();
    public List<StaffProgramAssignment> StaffProgramAssignments { get; } = new();

    public IRepository<Participant> Participants => ParticipantsRepo;
    public IRepository<DocumentRecord> DocumentRecords => DocumentRecordsRepo;
    public IRepository<StaffMember> Staff => StaffRepo;
    public IRepository<CrmProgram> Programs => ProgramsRepo;
    public IRepository<User> Users => UsersRepo;
    public IRepository<PerStarPlan> PerStarPlans => PerStarPlansRepo;
    public IRepository<RefreshToken> RefreshTokens => RefreshTokensRepo;
    public IRepository<MfaChallenge> MfaChallenges => MfaChallengesRepo;
    public IRepository<MfaRecoveryCode> MfaRecoveryCodes => MfaRecoveryCodesRepo;

    public IRepository<Volunteer> Volunteers { get; } = new FakeRepository<Volunteer>();
    public IRepository<ObjectiveArea> ObjectiveAreas { get; } = new FakeRepository<ObjectiveArea>();
    public FakeRepository<SubSkill> SubSkillsRepo { get; } = new();
    public IRepository<SubSkill> SubSkills => SubSkillsRepo;
    public IRepository<Game> Games { get; } = new FakeRepository<Game>();
    public IRepository<GameSubGoal> GameSubGoals { get; } = new FakeRepository<GameSubGoal>();
    public FakeRepository<Site> SitesRepo { get; } = new();
    public FakeRepository<RosterAssignment> RosterAssignmentsRepo { get; } = new();
    public IRepository<Site> Sites => SitesRepo;
    public IRepository<StarGroup> StarGroups { get; } = new FakeRepository<StarGroup>();
    public IRepository<RosterAssignment> RosterAssignments => RosterAssignmentsRepo;
    public IRepository<ParticipantArtsProfile> ParticipantArtsProfiles { get; } = new FakeRepository<ParticipantArtsProfile>();
    public IRepository<WeeklyDataEntry> WeeklyDataEntries { get; } = new FakeRepository<WeeklyDataEntry>();
    public FakeRepository<MonthlyProgressSnapshot> MonthlyProgressSnapshotsRepo { get; } = new();
    public IRepository<MonthlyProgressSnapshot> MonthlyProgressSnapshots => MonthlyProgressSnapshotsRepo;
    public IRepository<WeeklyFocusSkill> WeeklyFocusSkills { get; } = new FakeRepository<WeeklyFocusSkill>();
    public FakeRepository<ScoreThreshold> ScoreThresholdsRepo { get; } = new();
    public IRepository<ScoreThreshold> ScoreThresholds => ScoreThresholdsRepo;
    public IRepository<GoalBankEntry> GoalBankEntries { get; } = new FakeRepository<GoalBankEntry>();
    public IRepository<WeeklyNoteSelection> WeeklyNoteSelections { get; } = new FakeRepository<WeeklyNoteSelection>();
    public IRepository<MonthlySummary> MonthlySummaries { get; } = new FakeRepository<MonthlySummary>();
    public IRepository<GameIdea> GameIdeas { get; } = new FakeRepository<GameIdea>();
    public IRepository<AgeModification> AgeModifications { get; } = new FakeRepository<AgeModification>();
    public IRepository<CalendarTheme> CalendarThemes { get; } = new FakeRepository<CalendarTheme>();
    public IRepository<KeyArtsDate> KeyArtsDates { get; } = new FakeRepository<KeyArtsDate>();
    public FakeRepository<AttendanceRecord> AttendanceRepo { get; } = new();
    public IRepository<AttendanceRecord> Attendance => AttendanceRepo;
    public FakeRepository<EventSession> EventSessionsRepo { get; } = new();
    public IRepository<EventSession> EventSessions => EventSessionsRepo;
    public FakeRepository<EventAttendanceRecord> EventAttendanceRecordsRepo { get; } = new();
    public IRepository<EventAttendanceRecord> EventAttendanceRecords => EventAttendanceRecordsRepo;
    public IRepository<AttendanceNote> AttendanceNotes { get; } = new FakeRepository<AttendanceNote>();
    public IRepository<Session> Sessions { get; } = new FakeRepository<Session>();
    public IRepository<CalendarEvent> CalendarEvents { get; } = new FakeRepository<CalendarEvent>();
    public IRepository<Project> Projects { get; } = new FakeRepository<Project>();
    public IRepository<ProjectTask> Tasks { get; } = new FakeRepository<ProjectTask>();
    public IRepository<Script> Scripts { get; } = new FakeRepository<Script>();
    public IRepository<OnboardingItem> OnboardingItems { get; } = new FakeRepository<OnboardingItem>();
    public IRepository<ChecklistTemplateItem> ChecklistTemplateItems { get; } = new FakeRepository<ChecklistTemplateItem>();

    public Task<IReadOnlyList<StaffProgramAssignment>> GetStaffProgramAssignmentsAsync() =>
        Task.FromResult<IReadOnlyList<StaffProgramAssignment>>(StaffProgramAssignments.ToList());

    public Task AddStaffProgramAssignmentAsync(StaffProgramAssignment assignment)
    {
        StaffProgramAssignments.Add(assignment);
        return Task.CompletedTask;
    }

    public Task RemoveStaffProgramAssignmentAsync(Guid staffMemberId, Guid programId)
    {
        StaffProgramAssignments.RemoveAll(a => a.StaffMemberId == staffMemberId && a.ProgramId == programId);
        return Task.CompletedTask;
    }

    public List<CalendarEventSite> CalendarEventSites { get; } = new();

    public Task<IReadOnlyList<CalendarEventSite>> GetCalendarEventSitesAsync(IReadOnlyCollection<Guid> eventIds) =>
        Task.FromResult<IReadOnlyList<CalendarEventSite>>(
            CalendarEventSites.Where(s => eventIds.Contains(s.CalendarEventId)).ToList());

    public Task ReplaceCalendarEventSitesAsync(Guid eventId, IReadOnlyCollection<Guid> siteIds)
    {
        CalendarEventSites.RemoveAll(s => s.CalendarEventId == eventId);
        foreach (var siteId in siteIds.Distinct())
            CalendarEventSites.Add(new CalendarEventSite { CalendarEventId = eventId, SiteId = siteId });
        return Task.CompletedTask;
    }

    /// <summary>Roster site pairings, in memory. Composite PK, so no generic repository.</summary>
    public List<RosterAssignmentSite> RosterAssignmentSites { get; } = new();

    public Task<IReadOnlyList<RosterAssignmentSite>> GetRosterAssignmentSitesAsync(IReadOnlyCollection<Guid> assignmentIds) =>
        Task.FromResult<IReadOnlyList<RosterAssignmentSite>>(
            RosterAssignmentSites.Where(s => assignmentIds.Contains(s.RosterAssignmentId)).ToList());

    public Task ReplaceRosterAssignmentSitesAsync(Guid assignmentId, IReadOnlyCollection<Guid> siteIds)
    {
        RosterAssignmentSites.RemoveAll(s => s.RosterAssignmentId == assignmentId);
        foreach (var siteId in siteIds.Distinct())
            RosterAssignmentSites.Add(new RosterAssignmentSite { RosterAssignmentId = assignmentId, SiteId = siteId });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScriptProgram>> GetScriptProgramsAsync() =>
        Task.FromResult<IReadOnlyList<ScriptProgram>>(new List<ScriptProgram>());

    public Task ReplaceScriptProgramsAsync(Guid scriptId, IReadOnlyCollection<Guid> programIds) => Task.CompletedTask;

    /// <summary>Event site pairings, in memory. Composite PK, so no generic repository.</summary>
    public List<EventSessionSite> EventSessionSites { get; } = new();

    public Task<IReadOnlyList<EventSessionSite>> GetEventSessionSitesAsync(Guid eventSessionId) =>
        Task.FromResult<IReadOnlyList<EventSessionSite>>(
            EventSessionSites.Where(s => s.EventSessionId == eventSessionId).ToList());

    public Task ReplaceEventSessionSitesAsync(Guid eventSessionId, IReadOnlyCollection<Guid> siteIds)
    {
        EventSessionSites.RemoveAll(s => s.EventSessionId == eventSessionId);
        foreach (var siteId in siteIds.Distinct())
            EventSessionSites.Add(new EventSessionSite { EventSessionId = eventSessionId, SiteId = siteId });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set to make the next (and every subsequent) SaveChangesAsync throw this, standing in for
    /// the DbUpdateConcurrencyException two admins editing the same user row really produce.
    /// EF types are not referenced here, so tests pass whatever exception they want to see
    /// recorded — only its TYPE NAME reaches the audit row.
    /// </summary>
    public Exception? SaveException { get; set; }

    public Task<int> SaveChangesAsync() =>
        SaveException is null ? Task.FromResult(0) : Task.FromException<int>(SaveException);
}

/// <summary>A clock frozen at a fixed local date, so date-dependent behaviour is deterministic.</summary>
internal sealed class FakeOrgClock : IOrgClock
{
    public FakeOrgClock(DateTime? today = null) => Today = (today ?? new DateTime(2026, 7, 15)).Date;

    public DateTime Today { get; }
    public DateTime Now => Today.AddHours(17);
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Captures the audit entries a service under test produced, so tests can assert on what was
/// recorded. Mirrors the real service's central promise — it never throws — so a test can
/// also verify that callers do not depend on it succeeding.
/// </summary>
internal sealed class FakeAuditService : IAuditService
{
    public List<AuditEntry> Entries { get; } = new();

    /// <summary>
    /// Set to make every write DROP its entry, the way a database outage looks from the
    /// caller's side. Named for what it does: the real AuditService catches its own exceptions
    /// and logs them, so a caller sees a successful-looking Task and no row. It does not throw,
    /// and making it throw would not pin the contract either — it would only prove that
    /// AuthService, which has no try/catch around its audit calls, propagates whatever it is
    /// given. The contract belongs to AuditService, and pinning it there needs a test that can
    /// reach CRM.Persistence, which this project deliberately does not reference.
    /// </summary>
    public bool FailSilently { get; set; }

    public Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        if (FailSilently) return Task.CompletedTask;

        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public IEnumerable<AuditEntry> WithAction(string action) =>
        Entries.Where(e => e.Action == action);

    public AuditEntry Single(string action) => WithAction(action).Single();
}

/// <summary>Ambient request context with fixed values, or none at all.</summary>
internal sealed class FakeAuditContextAccessor : IAuditContextAccessor
{
    public FakeAuditContextAccessor(AuditContext? current = null) => Current = current;

    public AuditContext? Current { get; set; }
}

/// <summary>Issues a predictable token — the auth tests never inspect its contents.</summary>
internal sealed class FakeTokenService : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateToken(User user, StaffRole? staffRole = null) =>
        ($"token-for-{user.Id}", DateTime.UtcNow.AddHours(1));
}

/// <summary>
/// Reversible stand-in for PBKDF2: "hash" is the password with a marker prefix. Keeps the
/// auth tests fast and deterministic; PasswordHasherTests covers the real algorithm.
/// </summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public (string Hash, string Salt) HashPassword(string password) => ($"hashed:{password}", "salt");

    public bool VerifyPassword(string password, string hash, string salt) => hash == $"hashed:{password}";
}

/// <summary>
/// The real TotpService and MfaSecretProtector, wired for tests.
///
/// These two are NOT faked. Both are pure — no database, no clock they don't take as an
/// argument — and they are the parts of the MFA flow most worth exercising for real: a fake
/// TOTP validator would happily pass tests that the RFC would fail.
/// </summary>
internal static class TestMfa
{
    /// <summary>Base64 of 32 bytes. Not the production placeholder; nothing here is a secret.</summary>
    private const string EncryptionKey = "dGVzdC1vbmx5LWFlcy1rZXktMzItYnl0ZXMtLi4uLi4=";

    public static MfaSettings Settings => new()
    {
        EncryptionKey = EncryptionKey,
        Required = true,
        Issuer = "Shining Stars CRM",
    };

    public static TotpService Totp() => new(Options.Create(Settings));

    public static MfaSecretProtector Protector() => new(Options.Create(Settings));

    /// <summary>The code an authenticator would show for this secret at this instant.</summary>
    public static string CodeAt(byte[] secret, DateTimeOffset when) =>
        TotpService.ComputeCode(secret, TotpService.ComputeStep(when.ToUnixTimeSeconds()), 6);
}

/// <summary>No attendance history — the scoping tests don't assert on attendance percentages.</summary>
internal sealed class FakeStatsQueries : IStatsQueries
{
    public Task<IReadOnlyList<ParticipantAttendanceAgg>> GetParticipantAttendanceAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ParticipantAttendanceAgg>>(new List<ParticipantAttendanceAgg>());

    public Task<AttendanceStatusTotals> GetAttendanceStatusTotalsAsync(CancellationToken ct = default) =>
        Task.FromResult(new AttendanceStatusTotals(0, 0, 0, 0));

    public Task<IReadOnlyDictionary<Guid, int>> GetSessionCountByProgramAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());

    public Task<IReadOnlyDictionary<Guid, NextSessionStub>> GetNextSessionByProgramAsync(
        DateTime fromUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, NextSessionStub>>(new Dictionary<Guid, NextSessionStub>());
}
