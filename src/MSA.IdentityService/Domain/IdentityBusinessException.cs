namespace MSA.IdentityService.Domain;

public class IdentityBusinessException(string message) : InvalidOperationException(message);

public sealed class IdentityResourceNotFoundException(string message) : KeyNotFoundException(message);

public sealed class IdentityIdempotencyConflictException(string message) : IdentityBusinessException(message);
