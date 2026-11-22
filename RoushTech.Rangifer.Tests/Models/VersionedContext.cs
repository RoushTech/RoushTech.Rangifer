namespace RoushTech.Rangifer.Tests.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

public class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Address? Address { get; set; }

    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }

    public Blog Blog { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }
}

public class FeaturedPost : Post
{
    public int Rank { get; set; }
}

public class Tag
{
    public int Id { get; set; }

    public string Label { get; set; } = string.Empty;
}

public class Address
{
    public string Street { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}

// One model per version, so consecutive versions exercise added, changed, moved and removed blocks.
public sealed class VersionedContext(int version) : DbContext
{
    public static readonly IReadOnlyList<int> Versions = [1, 2, 3, 4];

    public int Version { get; } = version;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCacheKeyFactory, VersionedModelCacheKeyFactory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>(blog =>
        {
            if (Version >= 4)
            {
                blog.Property(b => b.Name).HasMaxLength(100);
            }

            if (Version >= 3)
            {
                blog.OwnsOne(b => b.Address);
            }
            else
            {
                blog.Ignore(b => b.Address);
            }
        });

        modelBuilder.Entity<Post>(post =>
        {
            if (Version >= 2)
            {
                post.Property(p => p.Title).HasMaxLength(200);
            }
            else
            {
                post.Ignore(p => p.Body);
            }

            if (Version >= 3)
            {
                post.HasIndex(p => p.Title);
            }
        });

        if (Version is 2 or 4)
        {
            modelBuilder.Entity<Tag>(tag =>
            {
                if (Version == 4)
                {
                    tag.ToTable("Labels");
                }
            });
        }

        if (Version >= 3)
        {
            modelBuilder.Entity<FeaturedPost>();
        }
    }

    private sealed class VersionedModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), context is VersionedContext versioned ? versioned.Version : 0, designTime);
    }
}
