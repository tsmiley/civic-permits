namespace CivicPermits.Application.Abstractions;

/// <summary>How a request failed, in terms the host maps to a response.</summary>
public enum RequestFailure
{
    /// <summary>Nobody is signed in.</summary>
    Unauthorized = 1,

    /// <summary>The person may not do this.</summary>
    Forbidden = 2,

    /// <summary>The thing does not exist, or the person may not know that it does.</summary>
    NotFound = 3,

    /// <summary>The request clashes with the current state, such as a name already taken or a concurrent change.</summary>
    Conflict = 4,
}

/// <summary>
/// A request that cannot be carried out for a reason that is not a broken domain rule: not signed in, not
/// allowed, not found, or in conflict. Messages are written to be shown to the person.
/// </summary>
public sealed class RequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RequestException(RequestFailure failure, string code, string message)
        : base(message)
    {
        Failure = failure;
        Code = code;
    }

    /// <summary>Creates the exception with the cause of the failure.</summary>
    public RequestException(RequestFailure failure, string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
        Code = code;
    }

    /// <summary>Creates the exception with no details.</summary>
    public RequestException()
        : this(RequestFailure.Conflict, "request.failed", "The request could not be carried out.")
    {
    }

    /// <summary>Creates the exception with a message only.</summary>
    public RequestException(string message)
        : this(RequestFailure.Conflict, "request.failed", message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    public RequestException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = RequestFailure.Conflict;
        Code = "request.failed";
    }

    /// <summary>The kind of failure.</summary>
    public RequestFailure Failure { get; }

    /// <summary>A stable identifier for the reason.</summary>
    public string Code { get; }

    /// <summary>The thing does not exist, or the person may not know that it does.</summary>
    public static RequestException NotFound(string code, string message) => new(RequestFailure.NotFound, code, message);

    /// <summary>The person may not do this.</summary>
    public static RequestException Forbidden(string message) => new(RequestFailure.Forbidden, "request.forbidden", message);

    /// <summary>The request clashes with the current state.</summary>
    public static RequestException Conflict(string code, string message) => new(RequestFailure.Conflict, code, message);
}
