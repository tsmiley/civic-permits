using CivicPermits.Api.Security;
using CivicPermits.Application.Messaging;
using CivicPermits.Application.Permits;
using CivicPermits.Domain.Applications;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.Endpoints;

/// <summary>Permit types, applications, inspections, and the public lookup.</summary>
internal static class PermitEndpoints
{
    public static RouteGroupBuilder MapPermitEndpoints(this RouteGroupBuilder group)
    {
        MapPermitTypes(group.MapGroup("/permit-types").WithTags("Permit types"));
        MapApplications(group.MapGroup("/applications").WithTags("Applications"));
        MapPublic(group.MapGroup("/public").WithTags("Public"));

        return group;
    }

    private static void MapPermitTypes(RouteGroupBuilder types)
    {
        types.MapGet("/", async (bool? includeInactive, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListPermitTypesQuery(includeInactive ?? false), cancel)))
            .WithName("ListPermitTypes")
            .WithSummary("Lists the kinds of permit. Needs no sign-in.")
            .WithDescription("Staff may add includeInactive=true to see the types closed to new applications.")
            .Produces<IReadOnlyList<PermitTypeResponse>>();

        types.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetPermitTypeQuery(id), cancel)))
            .WithName("GetPermitType")
            .WithSummary("Reads one kind of permit. Needs no sign-in.")
            .Produces<PermitTypeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        types.MapPost("/", async (PermitTypeRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
            {
                PermitTypeResponse created = await dispatcher.SendAsync(new SavePermitTypeCommand(null, request), cancel);

                return TypedResults.Created($"/api/v1/permit-types/{created.Id}", created);
            })
            .RequireAuthorization(Policies.Administrator)
            .WithName("CreatePermitType")
            .WithSummary("Defines a new kind of permit. Administrators only.")
            .Produces<PermitTypeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        types.MapPut("/{id:guid}", async (Guid id, PermitTypeRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new SavePermitTypeCommand(id, request), cancel)))
            .RequireAuthorization(Policies.Administrator)
            .WithName("ChangePermitType")
            .WithSummary("Changes a kind of permit. Administrators only.")
            .WithDescription("Applications already submitted keep the fee they were assessed.")
            .Produces<PermitTypeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        types.MapPost("/{id:guid}/open", async (Guid id, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new SetPermitTypeActiveCommand(id, true), cancel)))
            .RequireAuthorization(Policies.Administrator)
            .WithName("OpenPermitType")
            .WithSummary("Opens a kind of permit to new applications. Administrators only.")
            .Produces<PermitTypeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        types.MapPost("/{id:guid}/close", async (Guid id, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new SetPermitTypeActiveCommand(id, false), cancel)))
            .RequireAuthorization(Policies.Administrator)
            .WithName("ClosePermitType")
            .WithSummary("Closes a kind of permit to new applications. Administrators only.")
            .Produces<PermitTypeResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapApplications(RouteGroupBuilder applications)
    {
        applications.RequireAuthorization(Policies.SignedIn);

        applications.MapPost("/", async (CreateApplicationRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
            {
                ApplicationResponse created = await dispatcher.SendAsync(new CreateApplicationCommand(request), cancel);

                return TypedResults.Created($"/api/v1/applications/{created.Id}", created);
            })
            .RequireAuthorization(Policies.Applicant)
            .WithName("CreateApplication")
            .WithSummary("Starts a draft application. Applicants only.")
            .Produces<ApplicationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        applications.MapGet("/", async (ApplicationStatus? status, int? page, int? pageSize, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new SearchApplicationsQuery(status, page ?? 1, pageSize ?? 20), cancel)))
            .WithName("SearchApplications")
            .WithSummary("Lists the applications you are allowed to see, newest first.")
            .WithDescription("Applicants see their own, staff see all, inspectors see those with an inspection booked for them.")
            .Produces<PagedResponse<ApplicationSummaryResponse>>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        applications.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetApplicationQuery(id), cancel)))
            .WithName("GetApplication")
            .WithSummary("Reads one application you are allowed to see.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        applications.MapPut("/{id:guid}", async (Guid id, ReviseApplicationRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ReviseApplicationCommand(id, request), cancel)))
            .RequireAuthorization(Policies.Applicant)
            .WithName("ReviseApplication")
            .WithSummary("Changes a draft, or an application sent back for corrections. Its applicant only.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        MapStep(applications, "submit", ApplicationAction.Submit, "SubmitApplication", "Sends the application to the office. Its applicant only.");
        MapStep(applications, "withdraw", ApplicationAction.Withdraw, "WithdrawApplication", "Withdraws the application. Its applicant only. A reason is optional.");
        MapStep(applications, "start-review", ApplicationAction.StartReview, "StartReview", "Takes a submitted application into review. Staff only.");
        MapStep(applications, "request-corrections", ApplicationAction.RequestCorrections, "RequestCorrections", "Sends the application back to the applicant. Staff only. A reason is required.");
        MapStep(applications, "approve", ApplicationAction.Approve, "ApproveApplication", "Approves the application. Staff only. A note is optional.");
        MapStep(applications, "deny", ApplicationAction.Deny, "DenyApplication", "Refuses the application. Staff only. A reason is required.");
        MapStep(applications, "issue", ApplicationAction.Issue, "IssuePermit", "Issues the permit once the fee is paid and any inspection has passed. Staff only.");

        applications.MapPost("/{id:guid}/payment", async (Guid id, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new RecordFeePaymentCommand(id), cancel)))
            .RequireAuthorization(Policies.Staff)
            .WithName("RecordFeePayment")
            .WithSummary("Records that the fee was paid at the counter. Staff only.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        applications.MapPost("/{id:guid}/inspections", async (Guid id, ScheduleInspectionRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ScheduleInspectionCommand(id, request), cancel)))
            .RequireAuthorization(Policies.Staff)
            .WithName("ScheduleInspection")
            .WithSummary("Books an inspection for an approved application. Staff only.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        applications.MapPost("/{id:guid}/inspections/{inspectionId:guid}/result",
                async (Guid id, Guid inspectionId, InspectionResultRequest request, IDispatcher dispatcher, CancellationToken cancel) =>
                    TypedResults.Ok(await dispatcher.SendAsync(new RecordInspectionResultCommand(id, inspectionId, request), cancel)))
            .RequireAuthorization(Policies.Inspection)
            .WithName("RecordInspectionResult")
            .WithSummary("Records what an inspection found. The assigned inspector, or an administrator.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        applications.MapPost("/{id:guid}/inspections/{inspectionId:guid}/cancel",
                async (Guid id, Guid inspectionId, ReasonRequest? body, IDispatcher dispatcher, CancellationToken cancel) =>
                    TypedResults.Ok(await dispatcher.SendAsync(new CancelInspectionCommand(id, inspectionId, body?.Reason), cancel)))
            .RequireAuthorization(Policies.Staff)
            .WithName("CancelInspection")
            .WithSummary("Calls off a booked inspection. Staff only.")
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapStep(RouteGroupBuilder applications, string path, ApplicationAction action, string name, string summary) =>
        applications.MapPost($"/{{id:guid}}/{path}", async (Guid id, ReasonRequest? body, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new TransitionApplicationCommand(id, action, body?.Reason), cancel)))
            .WithName(name)
            .WithSummary(summary)
            .Produces<ApplicationResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static void MapPublic(RouteGroupBuilder publicApi)
    {
        publicApi.MapGet("/permits/{number}", async (string number, IDispatcher dispatcher, CancellationToken cancel) =>
                TypedResults.Ok(await dispatcher.SendAsync(new LookupPermitQuery(number), cancel)))
            .RequireRateLimiting(RateLimits.Public)
            .WithName("LookUpPermit")
            .WithSummary("Looks up a permit by its number. Needs no sign-in.")
            .WithDescription("Returns only the kind of permit, its status and its dates: nothing about who applied or where. A number that is malformed and one that does not exist get the same answer.")
            .Produces<PublicPermitStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }
}
