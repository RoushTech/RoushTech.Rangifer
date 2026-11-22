# Thin EF Core migration designers

Every EF Core migration ships a `.Designer.cs` holding a complete copy of the model as it stood at that migration. Their total size grows with the number of migrations times the size of the model.

The designers can't simply be deleted, they're a needed part of EFC. Each migration's target model feeds SQL generation (SQLite table rebuilds, provider version checks, seed data), and `dotnet ef migrations remove` restores the snapshot from the previous migration's model.

EF already works out what changed since the previous migration, then saves the whole model anyway. Rangifer saves only what changed, and rebuilds the full model at runtime by replaying the chain, so EF gets the same model it would have read from the full designer.

On one production project, conversion made the designers 34x smaller. Up and Down SQL for every migration came out byte-identical, `migrations remove` wrote an identical snapshot, and the migrations project rebuilt in 19 s instead of 43 s.

A thin designer looks like this:

```csharp
[DbContext(typeof(BloggingContext))]
[Migration("20260301120000_AddTags")]
partial class AddTags
{
    internal static global::RoushTech.Rangifer.EntityFrameworkCore.ModelDelta RangiferDelta { get; } = CreateRangiferDelta();

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => RangiferDelta.Apply(modelBuilder);

    private static global::RoushTech.Rangifer.EntityFrameworkCore.ModelDelta CreateRangiferDelta()
    {
        var delta = new global::RoushTech.Rangifer.EntityFrameworkCore.ModelDelta("20260301120000_AddTags", "20260215090000_AddPosts", global::Blogging.Migrations.AddPosts.RangiferDelta);

        delta.Edit("1:Blogging.Post", 5, "3b0c2633")
            .Replace(3, static b => b.Property<string>("Title")
                .HasMaxLength(200)
                .HasColumnType("TEXT"));

        delta.SetEntity("1:Blogging.Tag", "1:Blogging.Post",
            static b => b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER"),
            static b => b.HasKey("Id"),
            static b => b.ToTable("Tags"));

        return delta;
    }
}
```

## Install

```bash
# The project that holds your migrations:
dotnet add package RoushTech.Rangifer.EntityFrameworkCore

# Your startup project, the one that already references Microsoft.EntityFrameworkCore.Design:
dotnet add package RoushTech.Rangifer.EntityFrameworkCore.Design
```

Reference the Design package with `PrivateAssets="all"`, the same way as `Microsoft.EntityFrameworkCore.Design`. It registers itself with `dotnet ef` through an assembly attribute added to executable projects, so no code is needed. EF loads design-time services from the startup project's output, which is why the Design package belongs there and not in a class library.

## Convert existing migrations

```bash
dotnet tool install --global RoushTech.Rangifer.Cli
rangifer ef convert --project path/to/MigrationsProject --dry-run
rangifer ef convert --project path/to/MigrationsProject
```

Every converted designer is checked against the original before it is written: the chain must rebuild the same blocks in the same order, or nothing is written. Commit the conversion on its own so it can be reviewed separately from schema changes.

We still recommend comparing the generated SQL once, before and after converting:

```bash
dotnet ef migrations script -o before-up.sql
dotnet ef migrations script <LatestMigration> 0 -o before-down.sql
# convert, rebuild, then generate after-up.sql and after-down.sql the same way and diff them
```

## After converting

- `dotnet ef migrations add` writes a thin designer whose parent is the previous migration. If the previous designer hasn't been converted yet, the new designer stores its whole model, and the next `rangifer ef convert` rebases it.
- `dotnet ef migrations remove` works unchanged.
- Squashing works as it does today. The first migration after a squash becomes the base of a new chain.

## Requirements

- EF Core 10 and C# migrations.
- `GenerateAssemblyInfo` must stay enabled in the startup project, because that's how the Design package registers itself. Otherwise add `[assembly: DesignTimeServicesReference("RoushTech.Rangifer.EntityFrameworkCore.Design.RangiferDesignTimeServices, RoushTech.Rangifer.EntityFrameworkCore.Design")]` yourself.
- Rangifer refuses designers it can't safely re-indent, such as multi-line string literals in model code. EF never generates those.
