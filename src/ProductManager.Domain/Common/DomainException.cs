namespace ProductManager.Domain.Common;

/// <summary>
/// Base for every failure the domain raises deliberately. Having a domain-owned root lets the API
/// tell "the caller asked for something the model forbids" apart from "the process or the database
/// misbehaved" — the two need different HTTP status codes, and framework exception types such as
/// <see cref="InvalidOperationException"/> are used by infrastructure (ADO.NET, EF Core) for the
/// second kind, so they cannot stand in for the first.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// A state-rule violation: the request is well-formed, but the aggregate's current state forbids it
/// (adopting into a non-live roadmap, deciding an already-decided opportunity, mutating an immutable
/// run artifact). The caller can only fix it by changing the state or the request — retrying the same
/// call verbatim will fail identically. Maps to HTTP 409 Conflict.
/// </summary>
/// <remarks>
/// Use <see cref="ArgumentException"/> (→ 400) for malformed input instead: the distinction is
/// "your values are wrong" versus "your values are fine, the world is not in a state that allows this".
/// </remarks>
public sealed class DomainConflictException : DomainException
{
    public DomainConflictException(string message) : base(message)
    {
    }

    public DomainConflictException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
