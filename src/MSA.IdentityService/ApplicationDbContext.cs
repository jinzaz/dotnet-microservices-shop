using Microsoft.AspNetCore.Identity.EntityFrameworkCore;using Microsoft.EntityFrameworkCore;
namespace MSA.IdentityService;
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options):IdentityDbContext<ApplicationUser>(options);
