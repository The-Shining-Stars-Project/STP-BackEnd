using System.Linq.Expressions;
using CRM.Application.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;

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
    public FakeRepository<StaffMember> StaffRepo { get; } = new();
    public FakeRepository<CrmProgram> ProgramsRepo { get; } = new();
    public FakeRepository<User> UsersRepo { get; } = new();
    public List<StaffProgramAssignment> StaffProgramAssignments { get; } = new();

    public IRepository<Participant> Participants => ParticipantsRepo;
    public IRepository<StaffMember> Staff => StaffRepo;
    public IRepository<CrmProgram> Programs => ProgramsRepo;
    public IRepository<User> Users => UsersRepo;

    public IRepository<ObjectiveArea> ObjectiveAreas { get; } = new FakeRepository<ObjectiveArea>();
    public IRepository<SubSkill> SubSkills { get; } = new FakeRepository<SubSkill>();
    public IRepository<Game> Games { get; } = new FakeRepository<Game>();
    public IRepository<GameSubGoal> GameSubGoals { get; } = new FakeRepository<GameSubGoal>();
    public IRepository<Site> Sites { get; } = new FakeRepository<Site>();
    public IRepository<StarGroup> StarGroups { get; } = new FakeRepository<StarGroup>();
    public IRepository<RosterAssignment> RosterAssignments { get; } = new FakeRepository<RosterAssignment>();
    public IRepository<ParticipantArtsProfile> ParticipantArtsProfiles { get; } = new FakeRepository<ParticipantArtsProfile>();
    public IRepository<WeeklyDataEntry> WeeklyDataEntries { get; } = new FakeRepository<WeeklyDataEntry>();
    public IRepository<MonthlyProgressSnapshot> MonthlyProgressSnapshots { get; } = new FakeRepository<MonthlyProgressSnapshot>();
    public IRepository<WeeklyFocusSkill> WeeklyFocusSkills { get; } = new FakeRepository<WeeklyFocusSkill>();
    public IRepository<ScoreThreshold> ScoreThresholds { get; } = new FakeRepository<ScoreThreshold>();
    public IRepository<GoalBankEntry> GoalBankEntries { get; } = new FakeRepository<GoalBankEntry>();
    public IRepository<WeeklyNoteSelection> WeeklyNoteSelections { get; } = new FakeRepository<WeeklyNoteSelection>();
    public IRepository<MonthlySummary> MonthlySummaries { get; } = new FakeRepository<MonthlySummary>();
    public IRepository<GameIdea> GameIdeas { get; } = new FakeRepository<GameIdea>();
    public IRepository<AgeModification> AgeModifications { get; } = new FakeRepository<AgeModification>();
    public IRepository<PerStarPlan> PerStarPlans { get; } = new FakeRepository<PerStarPlan>();
    public IRepository<CalendarTheme> CalendarThemes { get; } = new FakeRepository<CalendarTheme>();
    public IRepository<KeyArtsDate> KeyArtsDates { get; } = new FakeRepository<KeyArtsDate>();
    public IRepository<AttendanceRecord> Attendance { get; } = new FakeRepository<AttendanceRecord>();
    public IRepository<AttendanceNote> AttendanceNotes { get; } = new FakeRepository<AttendanceNote>();
    public IRepository<Session> Sessions { get; } = new FakeRepository<Session>();
    public IRepository<CalendarEvent> CalendarEvents { get; } = new FakeRepository<CalendarEvent>();
    public IRepository<Project> Projects { get; } = new FakeRepository<Project>();
    public IRepository<ProjectTask> Tasks { get; } = new FakeRepository<ProjectTask>();
    public IRepository<Script> Scripts { get; } = new FakeRepository<Script>();
    public IRepository<OnboardingItem> OnboardingItems { get; } = new FakeRepository<OnboardingItem>();
    public IRepository<ChecklistTemplateItem> ChecklistTemplateItems { get; } = new FakeRepository<ChecklistTemplateItem>();
    public IRepository<RefreshToken> RefreshTokens { get; } = new FakeRepository<RefreshToken>();

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

    public Task<IReadOnlyList<ScriptProgram>> GetScriptProgramsAsync() =>
        Task.FromResult<IReadOnlyList<ScriptProgram>>(new List<ScriptProgram>());

    public Task ReplaceScriptProgramsAsync(Guid scriptId, IReadOnlyCollection<Guid> programIds) => Task.CompletedTask;

    public Task<int> SaveChangesAsync() => Task.FromResult(0);
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
