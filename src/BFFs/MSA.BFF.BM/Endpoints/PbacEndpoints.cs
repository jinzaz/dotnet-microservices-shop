using MSA.BFF.BM.Authorization;

namespace MSA.BFF.BM.Endpoints;

internal static class PbacEndpoints
{
    internal static IEndpointRouteBuilder MapPbacEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/pbac")
            .RequireAuthorization()
            .WithTags("BM PBAC");

        group.MapGet("/me/permissions", GetMyPermissionsAsync)
            .WithName("GetMyBmPermissions")
            .WithSummary("Get the current BM user's effective permissions.")
            .Produces<EffectivePermissionsResponse>();

        group.MapGet("/permissions", (PbacPolicyStore store, CancellationToken cancellationToken) =>
                store.ListPermissionsAsync(cancellationToken))
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("ListBmPermissions")
            .WithSummary("List the fixed module/action permission catalog.");

        MapUsers(group);
        MapRoles(group);
        MapPolicies(group);
        return endpoints;
    }

    private static void MapUsers(RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users");
        users.MapGet("/", (BmDirectoryStore store, CancellationToken cancellationToken) =>
                store.ListUsersAsync(cancellationToken))
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("ListBmUsers");
        users.MapGet("/{identityUserId}", async (
                string identityUserId,
                BmDirectoryStore store,
                CancellationToken cancellationToken) =>
            {
                var user = await store.FindUserAsync(identityUserId, cancellationToken);
                return user is null ? Results.NotFound() : Results.Ok(user);
            })
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("GetBmUser");
        users.MapPost("/", CreateUserAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Create))
            .WithName("CreateBmUser")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapPut("/{identityUserId}", UpdateUserAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Edit))
            .WithName("UpdateBmUser")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        users.MapDelete("/{identityUserId}", DeleteUserAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Delete))
            .WithName("DeleteBmUser")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapRoles(RouteGroupBuilder group)
    {
        var roles = group.MapGroup("/roles");
        roles.MapGet("/", (BmDirectoryStore store, CancellationToken cancellationToken) =>
                store.ListRolesAsync(cancellationToken))
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("ListBmRoles");
        roles.MapGet("/{id:guid}", async (
                Guid id,
                BmDirectoryStore store,
                CancellationToken cancellationToken) =>
            {
                var role = await store.FindRoleAsync(id, cancellationToken);
                return role is null ? Results.NotFound() : Results.Ok(role);
            })
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("GetBmRole");
        roles.MapPost("/", CreateRoleAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Create))
            .WithName("CreateBmRole")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        roles.MapPut("/{id:guid}", UpdateRoleAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Edit))
            .WithName("UpdateBmRole")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        roles.MapDelete("/{id:guid}", DeleteRoleAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Delete))
            .WithName("DeleteBmRole")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapPolicies(RouteGroupBuilder group)
    {
        var policies = group.MapGroup("/policies");
        policies.MapGet("/", (PbacPolicyStore store, CancellationToken cancellationToken) =>
                store.ListPoliciesAsync(cancellationToken))
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("ListBmPbacPolicies");
        policies.MapGet("/{id:guid}", async (
                Guid id,
                PbacPolicyStore store,
                CancellationToken cancellationToken) =>
            {
                var policy = await store.FindPolicyAsync(id, cancellationToken);
                return policy is null ? Results.NotFound() : Results.Ok(policy);
            })
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Query))
            .WithName("GetBmPbacPolicy");
        policies.MapPost("/", CreatePolicyAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Create))
            .WithName("CreateBmPbacPolicy")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        policies.MapPut("/{id:guid}", UpdatePolicyAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Edit))
            .WithName("UpdateBmPbacPolicy")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        policies.MapDelete("/{id:guid}", DeletePolicyAsync)
            .RequireAuthorization(PbacDefaults.ManagementPolicy(PbacAction.Delete))
            .WithName("DeleteBmPbacPolicy")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetMyPermissionsAsync(
        HttpContext context,
        PbacAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        var subject = PbacAuthorizer.GetSubject(context.User);
        if (subject is null)
            return Results.Unauthorized();
        var permissions = await authorizer.ListEffectivePermissionCodesAsync(
            context.User,
            cancellationToken);
        return Results.Ok(new EffectivePermissionsResponse(subject, permissions));
    }

    private static async Task<IResult> CreateUserAsync(
        CreateBmUserRequest request,
        BmDirectoryStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidateUser(request.IdentityUserId, request.RoleIds);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.CreateUserAsync(
            request.IdentityUserId,
            request.IsActive,
            request.RoleIds,
            cancellationToken);
        return result.Status switch
        {
            PbacWriteStatus.Success => Results.Created(
                $"/bff/pbac/users/{Uri.EscapeDataString(result.Value!.IdentityUserId)}",
                result.Value),
            PbacWriteStatus.NotFound => Problem(StatusCodes.Status404NotFound, "A BM role was not found."),
            _ => Problem(StatusCodes.Status409Conflict, "The BM user already exists.")
        };
    }

    private static async Task<IResult> UpdateUserAsync(
        string identityUserId,
        UpdateBmUserRequest request,
        BmDirectoryStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidateUser(identityUserId, request.RoleIds);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.UpdateUserAsync(
            identityUserId,
            request.IsActive,
            request.RoleIds,
            cancellationToken);
        return result.Status switch
        {
            PbacWriteStatus.Success => Results.Ok(result.Value),
            PbacWriteStatus.NotFound => Problem(StatusCodes.Status404NotFound, "The BM user or a BM role was not found."),
            _ => Problem(StatusCodes.Status409Conflict, "The BM user could not be updated.")
        };
    }

    private static async Task<IResult> DeleteUserAsync(
        string identityUserId,
        BmDirectoryStore store,
        CancellationToken cancellationToken) =>
        await store.DeleteUserAsync(identityUserId, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();

    private static async Task<IResult> CreateRoleAsync(
        SaveBmRoleRequest request,
        BmDirectoryStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRole(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.CreateRoleAsync(
            request.Code,
            request.DisplayName,
            request.IsActive,
            cancellationToken);
        return result.Status == PbacWriteStatus.Success
            ? Results.Created($"/bff/pbac/roles/{result.Value!.Id}", result.Value)
            : Problem(StatusCodes.Status409Conflict, "The BM role code already exists.");
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid id,
        SaveBmRoleRequest request,
        BmDirectoryStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidateRole(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.UpdateRoleAsync(
            id,
            request.Code,
            request.DisplayName,
            request.IsActive,
            cancellationToken);
        return result.Status switch
        {
            PbacWriteStatus.Success => Results.Ok(result.Value),
            PbacWriteStatus.NotFound => Results.NotFound(),
            _ => Problem(StatusCodes.Status409Conflict, "The role is immutable or its code already exists.")
        };
    }

    private static async Task<IResult> DeleteRoleAsync(
        Guid id,
        BmDirectoryStore store,
        CancellationToken cancellationToken) =>
        (await store.DeleteRoleAsync(id, cancellationToken)) switch
        {
            PbacWriteStatus.Success => Results.NoContent(),
            PbacWriteStatus.NotFound => Results.NotFound(),
            _ => Problem(StatusCodes.Status409Conflict, "The system administrator role cannot be deleted.")
        };

    private static async Task<IResult> CreatePolicyAsync(
        SavePbacPolicyRequest request,
        PbacPolicyStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidatePolicy(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.CreatePolicyAsync(
            request.Name,
            request.Effect,
            request.PermissionIds,
            request.UserIds,
            request.RoleIds,
            cancellationToken);
        return result.Status switch
        {
            PbacWriteStatus.Success => Results.Created(
                $"/bff/pbac/policies/{result.Value!.Id}",
                result.Value),
            PbacWriteStatus.NotFound => Problem(StatusCodes.Status404NotFound, "A permission, BM user, or BM role was not found."),
            _ => Problem(StatusCodes.Status409Conflict, "The PBAC policy name already exists.")
        };
    }

    private static async Task<IResult> UpdatePolicyAsync(
        Guid id,
        SavePbacPolicyRequest request,
        PbacPolicyStore store,
        CancellationToken cancellationToken)
    {
        var errors = ValidatePolicy(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var result = await store.UpdatePolicyAsync(
            id,
            request.Name,
            request.Effect,
            request.PermissionIds,
            request.UserIds,
            request.RoleIds,
            cancellationToken);
        return result.Status switch
        {
            PbacWriteStatus.Success => Results.Ok(result.Value),
            PbacWriteStatus.NotFound => Problem(StatusCodes.Status404NotFound, "The policy or one of its assignments was not found."),
            _ => Problem(StatusCodes.Status409Conflict, "The policy is immutable or its name already exists.")
        };
    }

    private static async Task<IResult> DeletePolicyAsync(
        Guid id,
        PbacPolicyStore store,
        CancellationToken cancellationToken) =>
        (await store.DeletePolicyAsync(id, cancellationToken)) switch
        {
            PbacWriteStatus.Success => Results.NoContent(),
            PbacWriteStatus.NotFound => Results.NotFound(),
            _ => Problem(StatusCodes.Status409Conflict, "The system administrator policy cannot be deleted.")
        };

    private static Dictionary<string, string[]> ValidateUser(
        string? identityUserId,
        IReadOnlyCollection<Guid>? roleIds)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(identityUserId) || identityUserId.Length > 200)
            errors[nameof(identityUserId)] = ["IdentityUserId must contain 1 to 200 characters."];
        if (roleIds is null || roleIds.Contains(Guid.Empty))
            errors[nameof(roleIds)] = ["RoleIds cannot be null or contain an empty GUID."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateRole(SaveBmRoleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100)
            errors[nameof(request.Code)] = ["Code must contain 1 to 100 characters."];
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 120)
            errors[nameof(request.DisplayName)] = ["DisplayName must contain 1 to 120 characters."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidatePolicy(SavePbacPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
            errors[nameof(request.Name)] = ["Name must contain 1 to 120 characters."];
        if (!Enum.IsDefined(request.Effect))
            errors[nameof(request.Effect)] = ["Effect must be Allow or Deny."];
        if (request.PermissionIds is null || request.PermissionIds.Count == 0 || request.PermissionIds.Contains(Guid.Empty))
            errors[nameof(request.PermissionIds)] = ["PermissionIds must contain at least one non-empty GUID."];
        if (request.UserIds is null || request.UserIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200))
            errors[nameof(request.UserIds)] = ["UserIds cannot be null and each value must contain 1 to 200 characters."];
        if (request.RoleIds is null || request.RoleIds.Contains(Guid.Empty))
            errors[nameof(request.RoleIds)] = ["RoleIds cannot be null or contain an empty GUID."];
        return errors;
    }

    private static IResult Problem(int statusCode, string title) =>
        Results.Problem(statusCode: statusCode, title: title);
}

/// <summary>The current Identity subject and effective BM permission codes.</summary>
public sealed record EffectivePermissionsResponse(
    string IdentityUserId,
    IReadOnlyList<string> Permissions);

/// <summary>Creates the BM-side account linked to an Identity subject.</summary>
public sealed record CreateBmUserRequest(
    string IdentityUserId,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds);

/// <summary>Updates BM account state and BM role assignments.</summary>
public sealed record UpdateBmUserRequest(
    bool IsActive,
    IReadOnlyList<Guid> RoleIds);

/// <summary>Creates or updates a BM role.</summary>
public sealed record SaveBmRoleRequest(
    string Code,
    string DisplayName,
    bool IsActive);

/// <summary>Creates or updates a PBAC allow/deny policy and its assignments.</summary>
public sealed record SavePbacPolicyRequest(
    string Name,
    PbacEffect Effect,
    IReadOnlyList<Guid> PermissionIds,
    IReadOnlyList<string> UserIds,
    IReadOnlyList<Guid> RoleIds);
