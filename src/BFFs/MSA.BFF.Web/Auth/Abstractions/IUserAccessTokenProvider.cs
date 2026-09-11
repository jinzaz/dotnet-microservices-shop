namespace MSA.BFF.Web.Auth.Abstractions;

public interface IUserAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(
    HttpContext context,
    CancellationToken cancellationToken = default);
}
