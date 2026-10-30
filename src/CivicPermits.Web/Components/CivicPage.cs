using System.Security.Claims;
using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Web.Infrastructure;
using Microsoft.AspNetCore.Components;

namespace CivicPermits.Web.Components;

/// <summary>One thing the person must fix, and which field it belongs to (when it belongs to one).</summary>
/// <param name="Field">The id of the field, so the error summary can link to it; null when it belongs to no field.</param>
/// <param name="Message">What is wrong and how to put it right.</param>
public sealed record FormProblem(string? Field, string Message);

/// <summary>
/// What every page of the site shares: the dispatcher, the posted values, and a single way of turning a refused
/// request into messages the person can act on, with the field each one belongs to.
/// </summary>
public abstract class CivicPage : ComponentBase
{
    /// <summary>The dispatcher the page sends its requests to.</summary>
    [Inject]
    protected IDispatcher Dispatcher { get; set; } = default!;

    /// <summary>Navigation, for redirects after a successful post.</summary>
    [Inject]
    protected NavigationManager Navigation { get; set; } = default!;

    /// <summary>The current request.</summary>
    [CascadingParameter]
    protected HttpContext? Http { get; set; }

    /// <summary>What went wrong on this request, if anything.</summary>
    protected List<FormProblem> Problems { get; } = [];

    /// <summary>Whether the page should show an error summary.</summary>
    protected bool HasProblems => Problems.Count > 0;

    /// <summary>The cancellation token for the request.</summary>
    protected CancellationToken Cancel => Http?.RequestAborted ?? CancellationToken.None;

    /// <summary>The signed-in person's identifier, or null.</summary>
    protected Guid? UserId =>
        Guid.TryParse(Http?.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null;

    /// <summary>Whether the signed-in person acts in the role.</summary>
    protected bool InRole(string role) => Http?.User.IsInRole(role) == true;

    /// <summary>Whether this request is a form post, as opposed to a first visit.</summary>
    protected bool IsPosted => Http is { Request: { Method: "POST", HasFormContentType: true } };

    /// <summary>The message for a field, or null.</summary>
    protected string? ErrorFor(string field) => Problems.Find(problem => problem.Field == field)?.Message;

    /// <summary>A value the person posted, or an empty string. Used to show their input again after an error.</summary>
    protected string Posted(string name) =>
        Http is { Request.HasFormContentType: true } http && http.Request.Form.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : string.Empty;

    /// <summary>Which field an error code belongs to. Pages override this for fields the base does not know.</summary>
    protected virtual string? FieldFor(string code) => code switch
    {
        "address.line1" => "line1",
        "address.line2" => "line2",
        "address.city" => "city",
        "address.region" => "region",
        "address.postal_code" => "postal-code",
        "application.description" => "description",
        "application.valuation_required" or "money.negative" or "money.fraction_of_a_cent" => "valuation",
        _ => null,
    };

    /// <summary>
    /// Checks the posted project details and value together, adding a problem for every one that is wrong, so the
    /// person sees everything to fix in one go. Returns the value in cents when it could be read.
    /// </summary>
    protected long CheckApplicationInput(bool valuationRequired)
    {
        foreach (RuleViolation violation in PermitApplication.CheckDetails(
                     Posted("description"), Posted("line1"), Posted("line2"), Posted("city"), Posted("region"), Posted("postalCode")))
        {
            Problems.Add(new FormProblem(FieldFor(violation.Code), violation.Message));
        }

        string text = Posted("valuation");

        if (string.IsNullOrWhiteSpace(text))
        {
            if (valuationRequired)
            {
                Problems.Add(new FormProblem("valuation", "Enter the value of the project in dollars: part of the fee depends on it."));
            }

            return 0;
        }

        if (!Display.TryParseDollars(text, out long cents))
        {
            Problems.Add(new FormProblem("valuation", "Enter an amount in dollars, like 24000 or 24,000.50."));
        }

        return cents;
    }

    /// <summary>
    /// Runs a request. A refusal that is the person's to fix becomes a problem on the page and the result is the
    /// default value; anything unexpected is left to the error page.
    /// </summary>
    protected async Task<T?> TryAsync<T>(Func<ValueTask<T>> request, string fieldPrefix = "")
    {
        try
        {
            return await request();
        }
        catch (DomainRuleException rule)
        {
            Problems.Add(new FormProblem(Prefixed(fieldPrefix, FieldFor(rule.Code)), rule.Message));
        }
        catch (RequestException failure) when (failure.Failure is RequestFailure.Conflict or RequestFailure.Forbidden)
        {
            Problems.Add(new FormProblem(Prefixed(fieldPrefix, FieldFor(failure.Code)), failure.Message));
        }

        return default;
    }

    /// <summary>Loads something for a page that cannot work without it; a missing or hidden item is the not-found page.</summary>
    protected async Task<T?> LoadOrNotFoundAsync<T>(Func<ValueTask<T>> request)
    {
        try
        {
            return await request();
        }
        catch (RequestException failure) when (failure.Failure == RequestFailure.NotFound)
        {
            // For the refusal log: a thing someone may not know about is logged as refused, not as missing.
            Http?.Items[CivicPermits.Infrastructure.Hosting.RefusalLogMiddleware.CodeKey] = failure.Code;
            Http?.Items[CivicPermits.Infrastructure.Hosting.RefusalLogMiddleware.RouteKey] =
                (Http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            Navigation.NotFound();

            return default;
        }
    }

    private static string? Prefixed(string prefix, string? field) =>
        field is null ? null : prefix.Length == 0 ? field : prefix + "-" + field;
}
