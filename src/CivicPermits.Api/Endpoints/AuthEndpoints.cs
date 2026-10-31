using CivicPermits.Api.Security;
using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
using CivicPermits.Infrastructure.Hosting;
using CivicPermits.Infrastructure.Identity;
using CivicPermits.Infrastructure.Persistence;
using CivicPermits.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CivicPermits.Api.Endpoints;

/// <summary>Registering, signing in, refreshing a token, and asking who the token belongs to.</summary>
internal static class AuthEndpoints
{
    private const string BadCredentials = "The email address or password is not right.";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder auth = group.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("Register")
            .WithSummary("Makes an applicant account.")
            .WithDescription("Returns 202 whether or not the address was already registered, so the response cannot be used to find out who has an account. There is no email confirmation in this demonstration.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("Login")
            .WithSummary("Signs in and returns a bearer token.")
            .WithDescription("Five wrong passwords lock the account for fifteen minutes. A wrong password, an unknown address and a locked account all get the same answer.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("RefreshToken")
            .WithSummary("Exchanges a refresh token for a new access token.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/sign-out", SignOutAsync)
            .RequireAuthorization(Policies.SignedIn)
            .WithName("SignOut")
            .WithSummary("Ends every refresh token of the signed-in account.")
            .WithDescription("Changes the account's security stamp, so no refresh token made before this call can be exchanged again. An access token already issued keeps working until its 15 minutes are up.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/close-account", CloseAccountAsync)
            .RequireAuthorization(Policies.Applicant)
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("CloseAccount")
            .WithSummary("Closes the signed-in applicant's own account.")
            .WithDescription("Needs the password again. Removes the account's name and email address, ends its sessions and refresh tokens, and means it can never sign in again; its applications are kept under the retention periods. An access token already issued lasts until its 15 minutes are up.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapGet("/me", MeAsync)
            .RequireAuthorization(Policies.SignedIn)
            .WithName("GetCurrentUser")
            .WithSummary("Says who the access token belongs to.")
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest? request, RegistrationService registration)
    {
        RegistrationOutcome outcome = await registration.RegisterApplicantAsync(request?.Email, request?.Password, request?.DisplayName);

        return outcome.Accepted
            ? TypedResults.Accepted((string?)null)
            : ValidationFailed(outcome.Errors.ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        AbsentAccountTiming timing,
        SignInActivity activity,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Rejected();
        }

        ApplicationUser? person = await users.FindByNameAsync(request.Email.Trim());

        if (person is null)
        {
            // An address nobody registered costs as much as one that exists, so the speed of the answer says nothing.
            timing.SpendTheTimeOfACheck(request.Password);

            return Rejected();
        }

        // Signing in through the bearer scheme makes the handler write the token response itself.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        SignInResult result = await signIn.PasswordSignInAsync(person, request.Password, isPersistent: false, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            // For the refusal log: which account was attacked, by identifier, never by address.
            http.Items[RefusalLogMiddleware.AccountKey] = person.Id.ToString();
            http.Items[RefusalLogMiddleware.CodeKey] = "auth.sign_in_failed";

            return Rejected();
        }

        // Where the retention period for an unused account is counted from.
        await activity.RecordAsync(person);

        return TypedResults.Empty;
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest? request,
        SignInManager<ApplicationUser> signIn,
        IOptionsMonitor<BearerTokenOptions> bearer,
        TimeProvider time)
    {
        if (string.IsNullOrEmpty(request?.RefreshToken))
        {
            return Rejected();
        }

        var ticket = bearer.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);

        if (ticket?.Properties.ExpiresUtc is not { } expires
            || time.GetUtcNow() >= expires
            || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } person)
        {
            return Rejected();
        }

        return TypedResults.SignIn(await signIn.CreateUserPrincipalAsync(person), authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> CloseAccountAsync(CloseAccountRequest? request, ICurrentUser user, AccountClosing closing)
    {
        Actor actor = user.Actor ?? throw new RequestException(RequestFailure.Unauthorized, "request.unauthorized", "Sign in to do this.");
        ClosingOutcome outcome = await closing.CloseApplicantAsync(actor.Id, request?.Password);

        return outcome.Closed
            ? TypedResults.NoContent()
            : ValidationFailed(new Dictionary<string, string[]> { ["password"] = [outcome.Problem ?? "The password is not right."] });
    }

    private static async Task<IResult> SignOutAsync(ICurrentUser user, UserManager<ApplicationUser> users)
    {
        Actor actor = user.Actor ?? throw new RequestException(RequestFailure.Unauthorized, "request.unauthorized", "Sign in to do this.");
        ApplicationUser? person = await users.FindByIdAsync(actor.Id.ToString());

        if (person is not null)
        {
            IdentityResult updated = await users.UpdateSecurityStampAsync(person);

            if (!updated.Succeeded)
            {
                throw new InvalidOperationException("The security stamp could not be changed, so the refresh tokens were not ended: " + string.Join("; ", updated.Errors.Select(error => error.Code)));
            }
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(ICurrentUser user, UserManager<ApplicationUser> users)
    {
        Actor actor = user.Actor ?? throw new RequestException(RequestFailure.Unauthorized, "request.unauthorized", "Sign in to do this.");
        ApplicationUser? person = await users.FindByIdAsync(actor.Id.ToString());

        return TypedResults.Ok(new CurrentUserResponse(actor.Id, person?.DisplayName ?? string.Empty, actor.Role.ToString()));
    }

    private static IResult ValidationFailed(Dictionary<string, string[]> errors) =>
        Results.Problem(new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Check what you entered",
        });

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Rejected() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: BadCredentials);
}
