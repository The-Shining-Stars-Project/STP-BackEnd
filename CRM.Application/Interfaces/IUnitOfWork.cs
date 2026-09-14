using CRM.Domain.Entities;

namespace CRM.Application.Interfaces;

// Deliberately NOT IDisposable: the DbContext is owned by the DI container (scoped),
// so the unit of work must not dispose it.
public interface IUnitOfWork
{
    IRepository<Participant> Participants { get; }
    IRepository<DocumentRecord> DocumentRecords { get; }
    IRepository<Volunteer> Volunteers { get; }
    IRepository<StaffMember> Staff { get; }
    IRepository<CrmProgram> Programs { get; }
    IRepository<ObjectiveArea> ObjectiveAreas { get; }
    IRepository<SubSkill> SubSkills { get; }
    IRepository<Game> Games { get; }
    IRepository<GameSubGoal> GameSubGoals { get; }
    IRepository<Site> Sites { get; }
    IRepository<StarGroup> StarGroups { get; }
    IRepository<RosterAssignment> RosterAssignments { get; }
    IRepository<ParticipantArtsProfile> ParticipantArtsProfiles { get; }
    IRepository<WeeklyDataEntry> WeeklyDataEntries { get; }
    IRepository<MonthlyProgressSnapshot> MonthlyProgressSnapshots { get; }
    IRepository<WeeklyFocusSkill> WeeklyFocusSkills { get; }
    IRepository<ScoreThreshold> ScoreThresholds { get; }
    IRepository<GoalBankEntry> GoalBankEntries { get; }
    IRepository<WeeklyNoteSelection> WeeklyNoteSelections { get; }
    IRepository<MonthlySummary> MonthlySummaries { get; }
    IRepository<GameIdea> GameIdeas { get; }
    IRepository<AgeModification> AgeModifications { get; }
    IRepository<PerStarPlan> PerStarPlans { get; }
    IRepository<CalendarTheme> CalendarThemes { get; }
    IRepository<KeyArtsDate> KeyArtsDates { get; }
    IRepository<AttendanceRecord> Attendance { get; }
    IRepository<EventSession> EventSessions { get; }
    IRepository<EventAttendanceRecord> EventAttendanceRecords { get; }
    IRepository<AttendanceNote> AttendanceNotes { get; }
    IRepository<Session> Sessions { get; }
    IRepository<CalendarEvent> CalendarEvents { get; }
    IRepository<Project> Projects { get; }
    IRepository<ProjectTask> Tasks { get; }
    IRepository<Script> Scripts { get; }
    IRepository<OnboardingItem> OnboardingItems { get; }
    IRepository<ChecklistTemplateItem> ChecklistTemplateItems { get; }
    IRepository<User> Users { get; }
    IRepository<RefreshToken> RefreshTokens { get; }
    IRepository<MfaChallenge> MfaChallenges { get; }
    IRepository<MfaRecoveryCode> MfaRecoveryCodes { get; }

    // AuditEvent is deliberately absent, and that is not an oversight to be tidied up later.
    // Every other entity gets a repository here, but routing audit writes through this unit
    // of work would enlist them in the caller's SaveChangesAsync — so a business operation
    // that rolled back would take its own audit trail down with it, and the interesting case
    // (the write that failed) would be the one that never got recorded. IAuditService owns
    // audit persistence on a separate DbContext and a separate transaction. Adding
    // IRepository<AuditEvent> here would also hand callers an update and delete path to an
    // append-only table.

    // StaffProgramAssignment has a composite PK and does not extend BaseEntity,
    // so it is exposed via dedicated methods rather than a generic repository.
    Task<IReadOnlyList<StaffProgramAssignment>> GetStaffProgramAssignmentsAsync();
    Task AddStaffProgramAssignmentAsync(StaffProgramAssignment assignment);
    Task RemoveStaffProgramAssignmentAsync(Guid staffMemberId, Guid programId);

    // ScriptProgram is a join entity with a composite PK (no BaseEntity), so it is
    // managed via dedicated methods rather than a generic repository.
    // EventSessionSite has a composite PK and no BaseEntity, so it is managed through
    // dedicated methods rather than a generic repository — the ScriptProgram pattern.
    Task<IReadOnlyList<EventSessionSite>> GetEventSessionSitesAsync(Guid eventSessionId);
    Task ReplaceEventSessionSitesAsync(Guid eventSessionId, IReadOnlyCollection<Guid> siteIds);

    // RosterAssignmentSite: composite PK, no BaseEntity — the EventSessionSite pattern.
    Task<IReadOnlyList<RosterAssignmentSite>> GetRosterAssignmentSitesAsync(IReadOnlyCollection<Guid> assignmentIds);
    Task ReplaceRosterAssignmentSitesAsync(Guid assignmentId, IReadOnlyCollection<Guid> siteIds);

    // CalendarEventSite: composite PK, no BaseEntity — the EventSessionSite pattern.
    Task<IReadOnlyList<CalendarEventSite>> GetCalendarEventSitesAsync(IReadOnlyCollection<Guid> eventIds);
    Task ReplaceCalendarEventSitesAsync(Guid eventId, IReadOnlyCollection<Guid> siteIds);

    Task<IReadOnlyList<ScriptProgram>> GetScriptProgramsAsync();
    Task ReplaceScriptProgramsAsync(Guid scriptId, IReadOnlyCollection<Guid> programIds);

    Task<int> SaveChangesAsync();
}
