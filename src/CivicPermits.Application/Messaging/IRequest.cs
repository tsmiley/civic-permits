namespace CivicPermits.Application.Messaging;

/// <summary>
/// A use case's input: a question to answer or a command to carry out.
/// </summary>
/// <typeparam name="TResponse">What the use case returns.</typeparam>
public interface IRequest<out TResponse>;
