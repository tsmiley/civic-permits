using CivicPermits.Application.Abstractions;
using CivicPermits.Application.Messaging;
using CivicPermits.Domain.Applications;
using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Application.Permits;

/// <summary>An applicant starts a draft.</summary>
/// <param name="Request">What they send.</param>
public sealed record CreateApplicationCommand(CreateApplicationRequest Request) : IRequest<ApplicationResponse>;

/// <summary>An applicant changes a draft, or an application sent back for corrections.</summary>
/// <param name="Id">The application.</param>
/// <param name="Request">What they send.</param>
public sealed record ReviseApplicationCommand(Guid Id, ReviseApplicationRequest Request) : IRequest<ApplicationResponse>;

/// <summary>Takes one workflow step on an application.</summary>
/// <param name="Id">The application.</param>
/// <param name="Action">The step.</param>
/// <param name="Reason">The reason or note, where the step takes one.</param>
public sealed record TransitionApplicationCommand(Guid Id, ApplicationAction Action, string? Reason) : IRequest<ApplicationResponse>;

/// <summary>Staff record that the fee was paid at the counter.</summary>
/// <param name="Id">The application.</param>
public sealed record RecordFeePaymentCommand(Guid Id) : IRequest<ApplicationResponse>;

/// <summary>Reads one application the person is allowed to see.</summary>
/// <param name="Id">The application.</param>
public sealed record GetApplicationQuery(Guid Id) : IRequest<ApplicationResponse>;

/// <summary>Lists the applications the person is allowed to see, newest first.</summary>
/// <param name="Status">Only this status, or null for any.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">The most results on a page, 1 to 100.</param>
public sealed record SearchApplicationsQuery(ApplicationStatus? Status, int Page, int PageSize)
    : IRequest<PagedResponse<ApplicationSummaryResponse>>;

/// <summary>Anyone looks up a permit by its public number.</summary>
/// <param name="Number">The number as written, such as <c>CP-2026-000042</c>.</param>
public sealed record LookupPermitQuery(string? Number) : IRequest<PublicPermitStatusResponse>;

internal sealed class ApplicationHandlers(
    IPermitApplicationStore applications,
    IPermitTypeStore types,
    IPermitNumberAllocator numbers,
    ICurrentUser user,
    IUnitOfWork work,
    TimeProvider time) :
    IRequestHandler<CreateApplicationCommand, ApplicationResponse>,
    IRequestHandler<ReviseApplicationCommand, ApplicationResponse>,
    IRequestHandler<TransitionApplicationCommand, ApplicationResponse>,
    IRequestHandler<RecordFeePaymentCommand, ApplicationResponse>,
    IRequestHandler<GetApplicationQuery, ApplicationResponse>,
    IRequestHandler<SearchApplicationsQuery, PagedResponse<ApplicationSummaryResponse>>,
    IRequestHandler<LookupPermitQuery, PublicPermitStatusResponse>
{
    private const int MaximumPageSize = 100;

    public async ValueTask<ApplicationResponse> HandleAsync(CreateApplicationCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Applicant);
        CreateApplicationRequest values = request.Request;

        PermitType type = await types.FindAsync(values.PermitTypeId, cancellationToken)
            ?? throw RequestException.NotFound("permit_type.not_found", "That kind of permit was not found.");

        PermitApplication application = PermitApplication.CreateDraft(
            Guid.CreateVersion7(),
            type,
            actor.Id,
            values.ProjectDescription,
            values.SiteAddress.ToAddress(),
            new Money(values.DeclaredValuationCents),
            time.GetUtcNow());

        applications.Add(application);
        await work.SaveChangesAsync(cancellationToken);

        return application.ToResponse(type, actor);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(ReviseApplicationCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Applicant);
        PermitApplication application = await applications.LoadVisibleAsync(request.Id, actor, cancellationToken);

        application.Revise(
            actor,
            request.Request.ProjectDescription,
            request.Request.SiteAddress.ToAddress(),
            new Money(request.Request.DeclaredValuationCents));
        await work.SaveChangesAsync(cancellationToken);

        return application.ToResponse(await TypeOf(application, cancellationToken), actor);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(TransitionApplicationCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require();
        PermitApplication application = await applications.LoadVisibleAsync(request.Id, actor, cancellationToken);
        PermitType type = await TypeOf(application, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();

        switch (request.Action)
        {
            case ApplicationAction.Submit:
                // The number is claimed only on the first submission, and saved together with the submission.
                PermitNumber number = application.Number
                    ?? await numbers.NextAsync(now.UtcDateTime.Year, cancellationToken);
                application.Submit(actor, type, number, now);
                break;
            case ApplicationAction.StartReview:
                application.StartReview(actor, now);
                break;
            case ApplicationAction.RequestCorrections:
                application.RequestCorrections(actor, request.Reason ?? string.Empty, now);
                break;
            case ApplicationAction.Approve:
                application.Approve(actor, request.Reason, now);
                break;
            case ApplicationAction.Deny:
                application.Deny(actor, request.Reason ?? string.Empty, now);
                break;
            case ApplicationAction.Issue:
                application.Issue(actor, type, now);
                break;
            case ApplicationAction.Withdraw:
                application.Withdraw(actor, request.Reason, now);
                break;
            default:
                throw Access.Invalid("workflow.unknown_action", "That is not a step in the process.");
        }

        await work.SaveChangesAsync(cancellationToken);

        return application.ToResponse(type, actor);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(RecordFeePaymentCommand request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require(ActorRole.Clerk, ActorRole.Administrator);
        PermitApplication application = await applications.LoadVisibleAsync(request.Id, actor, cancellationToken);

        application.RecordFeePayment(actor, time.GetUtcNow());
        await work.SaveChangesAsync(cancellationToken);

        return application.ToResponse(await TypeOf(application, cancellationToken), actor);
    }

    public async ValueTask<ApplicationResponse> HandleAsync(GetApplicationQuery request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require();
        PermitApplication application = await applications.LoadVisibleAsync(request.Id, actor, cancellationToken);

        return application.ToResponse(await TypeOf(application, cancellationToken), actor);
    }

    public async ValueTask<PagedResponse<ApplicationSummaryResponse>> HandleAsync(SearchApplicationsQuery request, CancellationToken cancellationToken)
    {
        Actor actor = user.Require();

        if (request.Page < 1)
        {
            throw Access.Invalid("paging.page", "The page must be 1 or more.");
        }

        if (request.PageSize is < 1 or > MaximumPageSize)
        {
            throw Access.Invalid("paging.page_size", $"The page size must be 1 to {MaximumPageSize}.");
        }

        // Who is asking decides how much of the list they can see; the request cannot widen it.
        ApplicationSearch search = new(
            request.Status,
            actor.Role == ActorRole.Applicant ? actor.Id : null,
            actor.Role == ActorRole.Inspector ? actor.Id : null,
            request.Page,
            request.PageSize);

        return await applications.SearchAsync(search, cancellationToken);
    }

    public async ValueTask<PublicPermitStatusResponse> HandleAsync(LookupPermitQuery request, CancellationToken cancellationToken)
    {
        // Every failure looks the same, so the lookup cannot be used to learn which numbers exist.
        RequestException notFound = RequestException.NotFound("permit.not_found", "No permit was found with that number.");

        if (!PermitNumber.TryParse(request.Number, out PermitNumber number))
        {
            throw notFound;
        }

        PermitApplication application = await applications.FindByNumberAsync(number, cancellationToken) ?? throw notFound;
        PermitType type = await TypeOf(application, cancellationToken);

        return new PublicPermitStatusResponse(
            number.ToString(),
            type.Name,
            application.Status.ToString(),
            application.IssuedOn,
            application.ExpiresOn);
    }

    private async Task<PermitType> TypeOf(PermitApplication application, CancellationToken cancellationToken) =>
        await types.FindAsync(application.PermitTypeId, cancellationToken)
            ?? throw new InvalidOperationException("An application refers to a permit type that does not exist.");
}
