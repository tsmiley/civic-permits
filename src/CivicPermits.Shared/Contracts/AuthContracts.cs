namespace CivicPermits.Shared.Contracts;

/// <summary>What a person sends to sign in.</summary>
/// <param name="Email">The email address they registered with.</param>
/// <param name="Password">Their password.</param>
public sealed record LoginRequest(string? Email, string? Password);

/// <summary>What a person sends to make an applicant account.</summary>
/// <param name="Email">The email address to register.</param>
/// <param name="Password">A password of at least twelve characters.</param>
/// <param name="DisplayName">The name shown on correspondence.</param>
public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);

/// <summary>What a signed-in person sends to get a new access token.</summary>
/// <param name="RefreshToken">The refresh token from sign-in.</param>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>The tokens a sign-in returns.</summary>
/// <param name="TokenType">Always Bearer.</param>
/// <param name="AccessToken">Sent in the Authorization header on later calls.</param>
/// <param name="ExpiresIn">How many seconds the access token lasts.</param>
/// <param name="RefreshToken">Exchanged for a new access token when it expires.</param>
public sealed record TokenResponse(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

/// <summary>Who the access token belongs to.</summary>
/// <param name="Id">The person's identifier.</param>
/// <param name="DisplayName">The name shown on correspondence.</param>
/// <param name="Role">The role they act in.</param>
public sealed record CurrentUserResponse(Guid Id, string DisplayName, string Role);

/// <summary>Closes the signed-in applicant's account. The password is asked again so a borrowed session cannot do it.</summary>
/// <param name="Password">The account's password.</param>
public sealed record CloseAccountRequest(string? Password);
