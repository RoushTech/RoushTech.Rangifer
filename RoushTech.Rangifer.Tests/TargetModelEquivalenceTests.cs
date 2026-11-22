namespace RoushTech.Rangifer.Tests;

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;
using RoushTech.Rangifer.EntityFrameworkCore.Design;
using RoushTech.Rangifer.Tests.Models;

public class TargetModelEquivalenceTests
{
    private const string FullNamespace = "Rangifer.Generated.Full";
    private const string ThinNamespace = "Rangifer.Generated.Thin";

    [Test]
    public void ThinDesigners_BuildTheSameTargetModelsAsFullDesigners()
    {
        using var project = new TemporaryProject();
        var sources = new List<string>();
        foreach (var version in VersionedContext.Versions)
        {
            sources.Add(GeneratedDesigners.Full(version, FullNamespace));
            sources.Add(GeneratedDesigners.MigrationClass(version, FullNamespace));
            sources.Add(GeneratedDesigners.MigrationClass(version, ThinNamespace));
            project.WriteDesigner(version, GeneratedDesigners.Full(version, ThinNamespace));
        }

        ThinDesignerConverter.Convert(DesignerSet.Scan(project.Directory), null, dryRun: false, _ => { });
        sources.AddRange(VersionedContext.Versions.Select(version => File.ReadAllText(project.DesignerPath(version))));

        var assembly = Compile(sources);

        Assert.Multiple(() =>
        {
            foreach (var version in VersionedContext.Versions)
            {
                Assert.That(
                    DebugView(assembly, ThinNamespace, version),
                    Is.EqualTo(DebugView(assembly, FullNamespace, version)),
                    $"version {version}");
            }
        });
    }

    [Test]
    public void ModelDelta_RejectsParentWithAnotherMigrationId() =>
        Assert.That(() => new ModelDelta("2_Child", "1_Parent", new ModelDelta("0_Other")), Throws.InvalidOperationException);

    [Test]
    public void ModelDelta_NamesTheMigrationWhenItDoesNotApply()
    {
        var delta = new ModelDelta("2_Child", "1_Parent", new ModelDelta("1_Parent"));
        delta.Remove("missing");

        Assert.That(
            () => delta.Apply(new Microsoft.EntityFrameworkCore.ModelBuilder()),
            Throws.InvalidOperationException.With.Message.Contains("2_Child"));
    }

    private static string DebugView(Assembly assembly, string @namespace, int version)
    {
        var type = assembly.GetType($"{@namespace}.{GeneratedDesigners.MigrationName(version)}")
            ?? throw new InvalidOperationException($"Generated migration {version} was not compiled.");
        var migration = Activator.CreateInstance(type) as Migration
            ?? throw new InvalidOperationException($"{type} is not a migration.");
        return migration.TargetModel.ToDebugString(MetadataDebugStringOptions.LongDefault);
    }

    private static Assembly Compile(IEnumerable<string> sources)
    {
        var platform = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty).Split(Path.PathSeparator);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0).Select(assembly => assembly.Location);
        var references = platform
            .Concat(loaded)
            .Append(typeof(ModelDelta).Assembly.Location)
            .Append(typeof(VersionedContext).Assembly.Location)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            $"RangiferGenerated{Guid.NewGuid():N}",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        }

        return Assembly.Load(stream.ToArray());
    }
}
