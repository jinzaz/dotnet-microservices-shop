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

        group.MapGet("/catalog-media/policy", () => TypedResults.Ok(S3ObjectStorage.GetUploadPolicy(ObjectStorageArea.CatalogMedia)))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("catalog.products.edit"))
            .WithName("GetCatalogMediaUploadPolicy")
            .WithSummary("Get accepted catalog media types and size limits for client-side validation.");

        group.MapPost("/catalog-media/upload-url", (
                CreateObjectUploadUrlRequest request,
                S3ObjectStorage storage) => CreateUploadUrl(storage, ObjectStorageArea.CatalogMedia, request))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("catalog.products.edit"))
            .WithName("CreateCatalogMediaUploadUrl")
            .WithSummary("Create a short-lived RustFS upload URL for public catalog media.")
            .Produces<ObjectUploadUrlResponse>()
            .ProducesValidationProblem();

        group.MapPost("/catalog-media/verify", (
                VerifyObjectUploadRequest request,
                S3ObjectStorage storage,
                CancellationToken cancellationToken) =>
                VerifyUploadAsync(storage, ObjectStorageArea.CatalogMedia, request, cancellationToken))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("catalog.products.edit"))
            .WithName("VerifyCatalogMediaUpload")
            .WithSummary("Confirm a direct upload completed before its public URL is written to Catalog.")
            .Produces<VerifiedObjectResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/catalog-media/discard", async (
                DiscardObjectRequest request,
                S3ObjectStorage storage,
                CancellationToken cancellationToken) =>
                await storage.TryDiscardCatalogMediaAsync(request, cancellationToken)
                    ? Results.NoContent()
                    : Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.ObjectKey)] = ["Only catalog media uploaded through BM can be discarded."]
                    }))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("catalog.products.edit"))
            .WithName("DiscardCatalogMedia")
            .WithSummary("Delete an orphaned or removed catalog media object; repeated calls succeed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapPost("/refund-evidence/upload-url", (
                CreateObjectUploadUrlRequest request,
                S3ObjectStorage storage) => CreateUploadUrl(storage, ObjectStorageArea.RefundEvidence, request))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("payment.transactions.edit"))
            .WithName("CreateRefundEvidenceUploadUrl")
            .WithSummary("Create a short-lived RustFS upload URL for private refund evidence.")
            .Produces<ObjectUploadUrlResponse>()
            .ProducesValidationProblem();

        group.MapPost("/refund-evidence/verify", (
                VerifyObjectUploadRequest request,
                S3ObjectStorage storage,
                CancellationToken cancellationToken) =>
                VerifyUploadAsync(storage, ObjectStorageArea.RefundEvidence, request, cancellationToken))
            .RequireAuthorization(PbacDefaults.PermissionPolicy("payment.transactions.edit"))
            .WithName("VerifyRefundEvidenceUpload")
            .WithSummary("Confirm a private refund-evidence upload completed.")
            .Produces<VerifiedObjectResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<IResult> VerifyUploadAsync(
        S3ObjectStorage storage,
        ObjectStorageArea area,
        VerifyObjectUploadRequest request,
        CancellationToken cancellationToken)
    {
        var (outcome, verified) = await storage.VerifyUploadAsync(area, request, cancellationToken);
        return outcome switch
        {
            ObjectVerificationOutcome.Verified => Results.Ok(verified),
            ObjectVerificationOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Upload not found",
                detail: "The object was not found; the upload may have failed or its URL expired."),
            ObjectVerificationOutcome.Mismatch => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ContentLength)] = ["The stored object does not match the declared size or type and was removed."]
            }),
            _ => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ObjectKey)] = ["ObjectKey was not issued by this BM object storage service."]
            })
        };
    }
}
