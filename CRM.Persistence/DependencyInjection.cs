using CRM.Application.Interfaces;
using CRM.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                    // Azure SQL throttles and drops connections as a matter of course (#7).
                    // Without a retry policy those transients surface as 500s to a teacher
                    // halfway through taking attendance. Retries only ever cover errors
                    // Microsoft classes as transient, so this cannot mask a real failure.
                    //
                    // NOTE: an execution strategy refuses user-initiated transactions. If
                    // explicit transactions are ever added (see the UnitOfWork atomicity
                    // gap), wrap them in db.Database.CreateExecutionStrategy().ExecuteAsync
                    // rather than removing this.
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null);

                    // A query that has not answered in 30s is not going to; fail rather than
                    // hold the request thread open.
                    sqlOptions.CommandTimeout(30);
                }));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IStatsQueries, Queries.StatsQueries>();

        return services;
    }
}
