using CRM.Application.Interfaces;
using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CRM.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
        Participants = new GenericRepository<Participant>(db);
        DocumentRecords = new GenericRepository<DocumentRecord>(db);
        Volunteers = new GenericRepository<Volunteer>(db);
        Staff = new GenericRepository<StaffMember>(db);
        Programs = new GenericRepository<CrmProgram>(db);
        ObjectiveAreas = new GenericRepository<ObjectiveArea>(db);
        SubSkills = new GenericRepository<SubSkill>(db);
        Games = new GenericRepository<Game>(db);
        GameSubGoals = new GenericRepository<GameSubGoal>(db);
        Sites = new GenericRepository<Site>(db);
        StarGroups = new GenericRepository<StarGroup>(db);
        RosterAssignments = new GenericRepository<RosterAssignment>(db);
        ParticipantArtsProfiles = new GenericRepository<ParticipantArtsProfile>(db);
        WeeklyDataEntries = new GenericRepository<WeeklyDataEntry>(db);
        MonthlyProgressSnapshots = new GenericRepository<MonthlyProgressSnapshot>(db);
        WeeklyFocusSkills = new GenericRepository<WeeklyFocusSkill>(db);
        ScoreThresholds = new GenericRepository<ScoreThreshold>(db);
        GoalBankEntries = new GenericRepository<GoalBankEntry>(db);
        WeeklyNoteSelections = new GenericRepository<WeeklyNoteSelection>(db);
        MonthlySummaries = new GenericRepository<MonthlySummary>(db);
        GameIdeas = new GenericRepository<GameIdea>(db);
        AgeModifications = new GenericRepository<AgeModification>(db);
        PerStarPlans = new GenericRepository<PerStarPlan>(db);
        CalendarThemes = new GenericRepository<CalendarTheme>(db);
        KeyArtsDates = new GenericRepository<KeyArtsDate>(db);
        Attendance = new GenericRepository<AttendanceRecord>(db);
        EventSessions = new GenericRepository<EventSession>(db);
        EventAttendanceRecords = new GenericRepository<EventAttendanceRecord>(db);
        AttendanceNotes = new GenericRepository<AttendanceNote>(db);
        Sessions = new GenericRepository<Session>(db);
        CalendarEvents = new GenericRepository<CalendarEvent>(db);
        Projects = new GenericRepository<Project>(db);
        Tasks = new GenericRepository<ProjectTask>(db);
        Scripts = new GenericRepository<Script>(db);
        OnboardingItems = new GenericRepository<OnboardingItem>(db);
        ChecklistTemplateItems = new GenericRepository<ChecklistTemplateItem>(db);
        Users = new GenericRepository<User>(db);
        RefreshTokens = new GenericRepository<RefreshToken>(db);
        MfaChallenges = new GenericRepository<MfaChallenge>(db);
        MfaRecoveryCodes = new GenericRepository<MfaRecoveryCode>(db);
    }

    public IRepository<Participant> Participants { get; }
    public IRepository<DocumentRecord> DocumentRecords { get; }
    public IRepository<Volunteer> Volunteers { get; }
    public IRepository<StaffMember> Staff { get; }
    public IRepository<CrmProgram> Programs { get; }
    public IRepository<ObjectiveArea> ObjectiveAreas { get; }
    public IRepository<SubSkill> SubSkills { get; }
    public IRepository<Game> Games { get; }
    public IRepository<GameSubGoal> GameSubGoals { get; }
    public IRepository<Site> Sites { get; }
    public IRepository<StarGroup> StarGroups { get; }
    public IRepository<RosterAssignment> RosterAssignments { get; }
    public IRepository<ParticipantArtsProfile> ParticipantArtsProfiles { get; }
    public IRepository<WeeklyDataEntry> WeeklyDataEntries { get; }
    public IRepository<MonthlyProgressSnapshot> MonthlyProgressSnapshots { get; }
    public IRepository<WeeklyFocusSkill> WeeklyFocusSkills { get; }
    public IRepository<ScoreThreshold> ScoreThresholds { get; }
    public IRepository<GoalBankEntry> GoalBankEntries { get; }
    public IRepository<WeeklyNoteSelection> WeeklyNoteSelections { get; }
    public IRepository<MonthlySummary> MonthlySummaries { get; }
    public IRepository<GameIdea> GameIdeas { get; }
    public IRepository<AgeModification> AgeModifications { get; }
    public IRepository<PerStarPlan> PerStarPlans { get; }
    public IRepository<CalendarTheme> CalendarThemes { get; }
    public IRepository<KeyArtsDate> KeyArtsDates { get; }
    public IRepository<AttendanceRecord> Attendance { get; }
    public IRepository<EventSession> EventSessions { get; }
    public IRepository<EventAttendanceRecord> EventAttendanceRecords { get; }
    public IRepository<AttendanceNote> AttendanceNotes { get; }
    public IRepository<Session> Sessions { get; }
    public IRepository<CalendarEvent> CalendarEvents { get; }
    public IRepository<Project> Projects { get; }
    public IRepository<ProjectTask> Tasks { get; }
    public IRepository<Script> Scripts { get; }
    public IRepository<OnboardingItem> OnboardingItems { get; }
    public IRepository<ChecklistTemplateItem> ChecklistTemplateItems { get; }
    public IRepository<User> Users { get; }
    public IRepository<RefreshToken> RefreshTokens { get; }
    public IRepository<MfaChallenge> MfaChallenges { get; }
    public IRepository<MfaRecoveryCode> MfaRecoveryCodes { get; }

    public async Task<IReadOnlyList<StaffProgramAssignment>> GetStaffProgramAssignmentsAsync() =>
        await _db.Set<StaffProgramAssignment>().AsNoTracking().ToListAsync();

    public async Task AddStaffProgramAssignmentAsync(StaffProgramAssignment assignment) =>
        await _db.Set<StaffProgramAssignment>().AddAsync(assignment);

    public async Task RemoveStaffProgramAssignmentAsync(Guid staffMemberId, Guid programId)
    {
        var existing = await _db.Set<StaffProgramAssignment>()
            .FirstOrDefaultAsync(a => a.StaffMemberId == staffMemberId && a.ProgramId == programId);
        if (existing is not null) _db.Set<StaffProgramAssignment>().Remove(existing);
    }

    public async Task<IReadOnlyList<EventSessionSite>> GetEventSessionSitesAsync(Guid eventSessionId) =>
        await _db.Set<EventSessionSite>().AsNoTracking()
            .Where(s => s.EventSessionId == eventSessionId).ToListAsync();

    public async Task ReplaceEventSessionSitesAsync(Guid eventSessionId, IReadOnlyCollection<Guid> siteIds)
    {
        var existing = await _db.Set<EventSessionSite>()
            .Where(s => s.EventSessionId == eventSessionId).ToListAsync();
        _db.Set<EventSessionSite>().RemoveRange(existing);

        // Ignore ids that are not real sites, the same way ReplaceScriptProgramsAsync does —
        // a stale id from a client should not fail the whole save.
        var valid = await _db.Set<Site>().Where(s => siteIds.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        foreach (var siteId in valid.Distinct())
            _db.Set<EventSessionSite>().Add(new EventSessionSite { EventSessionId = eventSessionId, SiteId = siteId });
    }

    public async Task<IReadOnlyList<ScriptProgram>> GetScriptProgramsAsync() =>
        await _db.Set<ScriptProgram>().AsNoTracking().ToListAsync();

    public async Task ReplaceScriptProgramsAsync(Guid scriptId, IReadOnlyCollection<Guid> programIds)
    {
        var existing = await _db.Set<ScriptProgram>()
            .Where(sp => sp.ScriptId == scriptId)
            .ToListAsync();
        _db.Set<ScriptProgram>().RemoveRange(existing);

        // De-dupe and ignore any ids that aren't real programs (e.g. the virtual
        // "productions" tag has no backing program row).
        var validProgramIds = await _db.Set<CrmProgram>()
            .Where(p => programIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        foreach (var programId in validProgramIds.Distinct())
            _db.Set<ScriptProgram>().Add(new ScriptProgram { ScriptId = scriptId, ProgramId = programId });
    }

    public Task<int> SaveChangesAsync() => _db.SaveChangesAsync();
}
