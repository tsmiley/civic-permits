using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace CivicPermits.Api.Problems;

/// <summary>
/// Turns the two kinds of expected failure into RFC 9457 problem documents with a stable <c>code</c>. Anything else
/// is left for the default handler, which answers 500 with no detail. The messages these exceptions carry are
/// written for the person to read; nothing here exposes a stack, a type name or a connection string.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    // Rule violations that are about the state of things, not about a bad value: the request was understood, and
    // is refused because of where the application is now.
    private static readonly HashSet<string> StateConflicts = new(StringComparer.Ordinal)
    {
        "workflow.transition_not_allowed",
        "application.not_editable",
        "application.number_changed",
        "application.permit_type_retired",
        "payment.not_approved",
        "payment.already_recorded",
        "issue.fee_unpaid",
        "issue.inspection_not_passed",
        "inspection.not_approved",
        "inspection.not_required",
        "inspection.already_scheduled",
        "inspection.closed",
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string title, string detail, string code)? mapped = exception switch
        {
            DomainRuleException rule => MapRule(rule),
            RequestException request => MapRequest(request),
            BadHttpRequestException malformed => (malformed.StatusCode, "Bad request", "The request could not be read. Check that it is well-formed JSON of the expected shape.", "request.malformed"),
            _ => null,
        };

        if (mapped is not var (status, title, detail, code))
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = null,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }

    private static (int, string, string, string) MapRule(DomainRuleException rule) => rule.Code switch
    {
        "workflow.role_not_allowed" => (StatusCodes.Status403Forbidden, "Not allowed", rule.Message, rule.Code),
        "workflow.not_your_application" or "inspection.unknown" => (StatusCodes.Status404NotFound, "Not found", "That was not found.", rule.Code),
        _ when StateConflicts.Contains(rule.Code) => (StatusCodes.Status409Conflict, "Not possible now", rule.Message, rule.Code),
        _ => (StatusCodes.Status422UnprocessableEntity, "Check what you entered", rule.Message, rule.Code),
    };

    private static (int, string, string, string) MapRequest(RequestException request) => request.Failure switch
    {
        RequestFailure.Unauthorized => (StatusCodes.Status401Unauthorized, "Sign in required", request.Message, request.Code),
        RequestFailure.Forbidden => (StatusCodes.Status403Forbidden, "Not allowed", request.Message, request.Code),
        RequestFailure.NotFound => (StatusCodes.Status404NotFound, "Not found", request.Message, request.Code),
        _ => (StatusCodes.Status409Conflict, "Conflict", request.Message, request.Code),
    };
}
