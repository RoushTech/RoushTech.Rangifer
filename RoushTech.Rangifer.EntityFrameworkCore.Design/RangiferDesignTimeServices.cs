namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

public sealed class RangiferDesignTimeServices : IDesignTimeServices
{
    // EF configures referenced design-time services before its own TryAdd registrations, so this one wins.
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection) =>
        serviceCollection.AddScoped<IMigrationsScaffolder, RangiferMigrationsScaffolder>();
}
