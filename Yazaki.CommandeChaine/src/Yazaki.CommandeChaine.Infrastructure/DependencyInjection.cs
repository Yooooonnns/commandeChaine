using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCommandeChaineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Data Source=commandechaine.db";

        services.AddDbContext<CommandeChaineDbContext>(options =>
        {
            options.UseSqlite(connectionString);
        });

        return services;
    }
}
