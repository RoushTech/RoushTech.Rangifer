namespace RoushTech.Rangifer.Tests;

using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using RoushTech.Rangifer.Tests.Models;

internal static class GeneratedDesigners
{
    public const string Namespace = "Rangifer.Generated";

    public static string MigrationName(int version) => $"Version{version}";

    public static string MigrationId(int version) => $"2026010100000{version}_{MigrationName(version)}";

    public static string Full(int version, string @namespace = Namespace)
    {
        using var context = new VersionedContext(version);
        using var services = CreateDesignTimeServices(context);
        var generator = services.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select("C#");
        return generator.GenerateMetadata(
            @namespace,
            typeof(VersionedContext),
            MigrationName(version),
            MigrationId(version),
            context.GetService<IDesignTimeModel>().Model);
    }

    public static ServiceProvider CreateDesignTimeServices(VersionedContext context, IDesignTimeServices? extra = null)
    {
        var services = new ServiceCollection();
        services.AddDbContextDesignTimeServices(context);

        // Provider design-time services live in EF's internal namespaces, so this registers what they would:
        // the provider's runtime services plus the relational annotation generator.
        services.AddEntityFrameworkSqlite();
        new EntityFrameworkRelationalDesignServicesBuilder(services)
            .TryAdd<IAnnotationCodeGenerator, AnnotationCodeGenerator>()
            .TryAddCoreServices();
        extra?.ConfigureDesignTimeServices(services);
        services.AddEntityFrameworkDesignTimeServices();
        return services.BuildServiceProvider();
    }

    public static string MigrationClass(int version, string @namespace = Namespace) => $$"""
        namespace {{@namespace}}
        {
            public partial class {{MigrationName(version)}} : Microsoft.EntityFrameworkCore.Migrations.Migration
            {
                protected override void Up(Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder migrationBuilder)
                {
                }
            }
        }
        """;
}

internal sealed class TemporaryProject : IDisposable
{
    public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("rangifer-").FullName;

    public string DesignerPath(int version) =>
        Path.Combine(Directory, "Migrations", $"{GeneratedDesigners.MigrationId(version)}.Designer.cs");

    public string WriteDesigner(int version, string text)
    {
        var path = DesignerPath(version);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
