using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MSA.Infrastrucute;

namespace MSA.IdentityService;

public sealed class IdentityDesignTimeDbContextFactory : BaseDesignTimeDbContextFactory<ApplicationDbContext>
{
    protected override string ConnectionStringName => "DefaultConnection";

    protected override void ConfigureDbContext(DbContextOptionsBuilder options)
    {
        options.UseOpenIddict();
    }
}
