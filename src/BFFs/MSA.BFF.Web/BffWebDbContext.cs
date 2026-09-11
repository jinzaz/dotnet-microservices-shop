using Microsoft.EntityFrameworkCore;

namespace MSA.BFF.Web;

public class BffWebDbContext : DbContext
{
    public BffWebDbContext(
        DbContextOptions<BffWebDbContext> options)
    : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bff_web");

        base.OnModelCreating(modelBuilder);

    }
}

