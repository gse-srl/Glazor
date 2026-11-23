using Microsoft.EntityFrameworkCore;

namespace Glazor.Tests.Models;

public partial class MGADbContext : DbContext
{
    public MGADbContext()
    {
    }

    public MGADbContext(DbContextOptions<MGADbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<MGASinistro> MGASinistro { get; set; }

    public virtual DbSet<MGAAttore> MGAAttore { get; set; }

    public virtual DbSet<MGAAdesione> MGAAdesione { get; set; }

    // SaveChanges() just call this
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var changedEntities = ChangeTracker.Entries();
        foreach (var changedEntity in changedEntities)
        {
            if (changedEntity.Entity is Entity entity)
            {
                switch (changedEntity.State)
                {
                    case EntityState.Added:
                        entity.OnBeforeInsert();
                        break;
                    case EntityState.Modified:
                        entity.OnBeforeUpdate();
                        break;
                    case EntityState.Deleted:
                        entity.OnBeforeDelete();
                        break;
                }
            }
        }
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
    {
        var changedEntities = ChangeTracker.Entries();
        foreach (var changedEntity in changedEntities)
        {
            if (changedEntity.Entity is Entity entity)
            {
                switch (changedEntity.State)
                {
                    case EntityState.Added:
                        entity.OnBeforeInsert();
                        break;
                    case EntityState.Modified:
                        entity.OnBeforeUpdate();
                        break;
                    case EntityState.Deleted:
                        entity.OnBeforeDelete();
                        break;
                }
            }
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
