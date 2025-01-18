using Chat.Application.Common;
using Chat.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Chat.Api.Errors;

/// <summary>Turns expected failures into RFC 7807 problem responses; anything else stays a 500.</summary>
internal sealed class ChatExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var status = StatusFor(exception);
        if (status is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = status.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = exception.Message,
            },
        });
    }

    public static int? StatusFor(Exception exception) => exception switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        ForbiddenException => StatusCodes.Status403Forbidden,
        ConflictException => StatusCodes.Status409Conflict,
        RequestValidationException or DomainRuleException => StatusCodes.Status400BadRequest,
        _ => null,
    };
}
