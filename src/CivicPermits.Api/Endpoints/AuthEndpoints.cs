using System.ComponentModel.DataAnnotations;
using CivicPermits.Api.Security;
using CivicPermits.Application.Abstractions;
using CivicPermits.Domain.Applications;
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
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("Register")
            .WithSummary("Makes an applicant account.")
            .WithDescription("Returns 202 whether or not the address was already registered, so the response cannot be used to find out who has an account. There is no email confirmation in this demonstration.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("Login")
            .WithSummary("Signs in and returns a bearer token.")
            .WithDescription("Five wrong passwords lock the account for fifteen minutes. A wrong password, an unknown address and a locked account all get the same answer.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/refresh", RefreshAsync)
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("RefreshToken")
            .WithSummary("Exchanges a refresh token for a new access token.")
            .Produces<TokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapGet("/me", MeAsync)
            .RequireAuthorization(Policies.SignedIn)
            .WithName("GetCurrentUser")
            .WithSummary("Says who the access token belongs to.")
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return group;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest? request, UserManager<ApplicationUser> users)
    {
        Dictionary<string, string[]> errors = [];

        string email = request?.Email?.Trim() ?? string.Empty;
        string name = request?.DisplayName?.Trim() ?? string.Empty;

        if (email.Length is 0 or > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            errors["email"] = ["Enter a valid email address."];
        }

        if (name.Length is < 2 or > 80)
        {
            errors["displayName"] = ["Enter your name (2 to 80 characters)."];
        }

        if (string.IsNullOrEmpty(request?.Password))
        {
            errors["password"] = ["Enter a password."];
        }

        if (errors.Count > 0)
        {
            return ValidationFailed(errors);
        }

        ApplicationUser person = new() { Id = Guid.CreateVersion7(), UserName = email, Email = email, DisplayName = name };
        IdentityResult created = await users.CreateAsync(person, request!.Password!);

        if (!created.Succeeded)
        {
            // A taken address is answered exactly like a new one. Anything else is the person's to fix.
            string[] fixable = [.. created.Errors
                .Where(error => !error.Code.StartsWith("Duplicate", StringComparison.Ordinal))
                .Select(error => error.Description)];

            return fixable.Length == 0
                ? TypedResults.Accepted((string?)null)
                : ValidationFailed(new Dictionary<string, string[]> { ["password"] = fixable });
        }

        await users.AddToRoleAsync(person, nameof(ActorRole.Applicant));

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> LoginAsync(LoginRequest? request, SignInManager<ApplicationUser> signIn)
    {
        if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Rejected();
        }

        // Signing in through the bearer scheme makes the handler write the token response itself.
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        SignInResult result = await signIn.PasswordSignInAsync(request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);

        return result.Succeeded ? TypedResults.Empty : Rejected();
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
