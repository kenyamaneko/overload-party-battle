namespace OverloadParty.Battle.Data;

/// <summary>
/// Thrown when a requested resource (e.g. a Firestore document) does not exist.
/// Repositories throw this to signal fail-fast to callers.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception inner) : base(message, inner) { }
}
