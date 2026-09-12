namespace MSA.UserService.Domain;

public class UserBusinessException(string message) : InvalidOperationException(message);

public sealed class UserResourceNotFoundException(string message) : KeyNotFoundException(message);

public sealed class UserIdempotencyConflictException(string message) : UserBusinessException(message);
