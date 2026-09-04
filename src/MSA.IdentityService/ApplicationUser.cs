using Microsoft.AspNetCore.Identity;
namespace MSA.IdentityService;
public sealed class ApplicationUser:IdentityUser
{public string DisplayName{get;set;}=string.Empty;public bool IsActive{get;set;}=true;public DateTimeOffset CreatedOnUtc{get;set;}=DateTimeOffset.UtcNow;public DateTimeOffset? LastLoginOnUtc{get;set;}}
