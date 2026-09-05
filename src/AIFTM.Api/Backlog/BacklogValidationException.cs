namespace AIFTM.Api.Backlog;

/// <summary>Raised when a request would produce an invalid backlog. The message is written for the
/// person using the app, not for a log file, so endpoints can return it verbatim.</summary>
public sealed class BacklogValidationException(string message) : Exception(message);
