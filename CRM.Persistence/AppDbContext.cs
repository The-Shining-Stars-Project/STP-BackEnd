using CRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CRM.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CrmProgram> Programs { get; set; }
    public DbSet<ObjectiveArea> ObjectiveAreas { get; set; }
    public DbSet<SubSkill> SubSkills { get; set; }
    public DbSet<Game> Games { get; set; }
    public DbSet<GameSubGoal> GameSubGoals { get; set; }
    public DbSet<Site> Sites { get; set; }
    public DbSet<StarGroup> StarGroups { get; set; }
    public DbSet<RosterAssignment> RosterAssignments { get; set; }
    public DbSet<ParticipantArtsProfile> ParticipantArtsProfiles { get; set; }
    public DbSet<WeeklyDataEntry> WeeklyDataEntries { get; set; }
    public DbSet<MonthlyProgressSnapshot> MonthlyProgressSnapshots { get; set; }
    public DbSet<WeeklyFocusSkill> WeeklyFocusSkills { get; set; }
    public DbSet<ScoreThreshold> ScoreThresholds { get; set; }
    public DbSet<GoalBankEntry> GoalBankEntries { get; set; }
    public DbSet<WeeklyNoteSelection> WeeklyNoteSelections { get; set; }
    public DbSet<MonthlySummary> MonthlySummaries { get; set; }
    public DbSet<GameIdea> GameIdeas { get; set; }
    public DbSet<AgeModification> AgeModifications { get; set; }
    public DbSet<PerStarPlan> PerStarPlans { get; set; }
    public DbSet<CalendarTheme> CalendarThemes { get; set; }
    public DbSet<KeyArtsDate> KeyArtsDates { get; set; }
    public DbSet<Participant> Participants { get; set; }
    public DbSet<Volunteer> Volunteers { get; set; }
    public DbSet<StaffMember> Staff { get; set; }
    public DbSet<StaffProgramAssignment> StaffProgramAssignments { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
    public DbSet<EventSession> EventSessions { get; set; }
    public DbSet<EventAttendanceRecord> EventAttendanceRecords { get; set; }
    public DbSet<AttendanceNote> AttendanceNotes { get; set; }
    public DbSet<CalendarEvent> CalendarEvents { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<ProjectTask> Tasks { get; set; }
    public DbSet<Script> Scripts { get; set; }
    public DbSet<ScriptProgram> ScriptPrograms { get; set; }
    public DbSet<DocumentRecord> DocumentRecords { get; set; }
    public DbSet<OnboardingItem> OnboardingItems { get; set; }
    public DbSet<ChecklistTemplateItem> ChecklistTemplateItems { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<MfaChallenge> MfaChallenges { get; set; }
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes { get; set; }

    /// <summary>
    /// Append-only security log. Written through AuditService on its own DI scope (its own
    /// context and transaction), never through the request's unit of work — see IUnitOfWork.
    /// </summary>
    public DbSet<AuditEvent> AuditEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Domain.Common.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
