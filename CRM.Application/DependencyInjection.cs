using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IProgramAccessService, ProgramAccessService>();
        services.AddScoped<ITaxonomyService, TaxonomyService>();
        services.AddScoped<IGameService, GameService>();
        services.AddScoped<IRosterService, RosterService>();
        services.AddScoped<IEventAttendanceService, EventAttendanceService>();
        services.AddScoped<IProgressTrackingService, ProgressTrackingService>();
        services.AddScoped<IGoalBankService, GoalBankService>();
        services.AddScoped<ICohortRollUpService, CohortRollUpService>();
        services.AddScoped<IGameBacklogService, GameBacklogService>();
        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<IYearCalendarService, YearCalendarService>();
        services.AddScoped<IProgramService, ProgramService>();
        services.AddScoped<IParticipantService, ParticipantService>();
        services.AddScoped<IParticipantDocumentService, ParticipantDocumentService>();
        services.AddScoped<IParticipantImportService, ParticipantImportService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IVolunteerService, VolunteerService>();
        services.AddScoped<IArtsProfileService, ArtsProfileService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IScriptService, ScriptService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportsService, ReportsService>();

        return services;
    }
}
