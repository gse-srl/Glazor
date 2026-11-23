using Glazor.Tests.Models;
using Microsoft.EntityFrameworkCore;

public class MockMGADbContextFactory : IDbContextFactory<MGADbContext>
{
    private readonly DbContextOptions<MGADbContext> _options;

    public MockMGADbContextFactory(DbContextOptions<MGADbContext> options)
    {
        _options = options;
    }

    public MGADbContext CreateDbContext()
    {
        return new MGADbContext(_options);
    }

    public Task<MGADbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}
