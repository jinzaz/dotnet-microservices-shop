using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSA.CertificateService.Application;
using Npgsql;

namespace MSA.CertificateService.Endpoints;

/// <summary>证书规则与业务冲突的中文说明直接放进 ProblemDetails.detail，BM 前端原样展示。</summary>
public sealed class CertificateApiExceptionHandler(ILogger<CertificateApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            CertificateRuleException => (StatusCodes.Status400BadRequest, "Invalid certificate request", exception.Message),
            CertificateConflictException => (StatusCodes.Status409Conflict, "Certificate state conflict", exception.Message),
            CertificateNotFoundException => (StatusCodes.Status404NotFound, "Not found", "证书或 ACME 托管不存在。"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Uniqueness conflict", "与已有的证书或 ACME 托管冲突，请刷新后重试。"),
            _ => (0, string.Empty, string.Empty)
        };

        if (status == 0)
            return false;

        logger.LogInformation(exception, "Certificate API request rejected with status {StatusCode}", status);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
