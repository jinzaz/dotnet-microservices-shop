using MSA.BFF.BM.Authorization;
using MSA.BFF.BM.ObjectStorage;

namespace MSA.BFF.BM.Endpoints;

internal static class ObjectStorageEndpoints
{
    internal static IEndpointRouteBuilder MapObjectStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/object-storage")
            .RequireAuthorization()
            .WithTags("BM Object Storage");

        group.MapPost("/catalog-media/upload-url", (
                CreateObjectUploadUrlRequest request,
                S3ObjectStorage storage) => CreateUploadUrl(storage, ObjectStorageArea.CatalogMedia, request))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("catalog.products.edit"))
            .WithName("CreateCatalogMediaUploadUrl")
            .WithSummary("Create a short-lived RustFS upload URL for public catalog media.")
            .Produces<ObjectUploadUrlResponse>()
            .ProducesValidationProblem();

        group.MapPost("/refund-evidence/upload-url", (
                CreateObjectUploadUrlRequest request,
                S3ObjectStorage storage) => CreateUploadUrl(storage, ObjectStorageArea.RefundEvidence, request))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("payment.transactions.edit"))
            .WithName("CreateRefundEvidenceUploadUrl")
            .WithSummary("Create a short-lived RustFS upload URL for private refund evidence.")
            .Produces<ObjectUploadUrlResponse>()
            .ProducesValidationProblem();

        group.MapPost("/refund-evidence/read-url", (
                CreatePrivateObjectReadUrlRequest request,
                S3ObjectStorage storage) =>
            {
                return storage.TryCreateRefundReadUrl(request.ObjectKey, out var response)
                    ? Results.Ok(response)
                    : Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ObjectKey)] = ["ObjectKey is not a valid refund-evidence key."]
                    });
            })
            .RequireAuthorization(PbacDefaults.PermissionPolicy("payment.transactions.query"))
            .WithName("CreateRefundEvidenceReadUrl")
            .WithSummary("Create a short-lived read URL for private refund evidence.")
            .Produces<PrivateObjectReadUrlResponse>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static IResult CreateUploadUrl(
        S3ObjectStorage storage,
        ObjectStorageArea area,
        CreateObjectUploadUrlRequest request) =>
        storage.TryCreateUploadUrl(area, request, out var response, out var errors)
            ? Results.Ok(response)
            : Results.ValidationProblem(errors);
}
