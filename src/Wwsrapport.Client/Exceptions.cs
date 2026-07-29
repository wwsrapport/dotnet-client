using System.Net;
using System.Text.Json.Nodes;

namespace Wwsrapport;

public class WwsrapportException : Exception
{
    public WwsrapportException(
        string message,
        HttpStatusCode? statusCode = null,
        JsonObject? problem = null,
        string? responseBody = null,
        string? requestId = null,
        Exception? innerException = null
    ) : base(message, innerException)
    {
        StatusCode = statusCode;
        Problem = problem;
        ResponseBody = responseBody;
        RequestId = requestId;
    }

    public HttpStatusCode? StatusCode { get; }
    public JsonObject? Problem { get; }
    public string? ResponseBody { get; }
    public string? RequestId { get; }
}

public sealed class WwsrapportAuthenticationException : WwsrapportException
{
    public WwsrapportAuthenticationException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportPaymentRequiredException : WwsrapportException
{
    public WwsrapportPaymentRequiredException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportForbiddenException : WwsrapportException
{
    public WwsrapportForbiddenException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportNotFoundException : WwsrapportException
{
    public WwsrapportNotFoundException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportConflictException : WwsrapportException
{
    public WwsrapportConflictException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportValidationException : WwsrapportException
{
    public WwsrapportValidationException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}

public sealed class WwsrapportRateLimitException : WwsrapportException
{
    public WwsrapportRateLimitException(string message, HttpStatusCode statusCode, JsonObject? problem, string? responseBody, string? requestId)
        : base(message, statusCode, problem, responseBody, requestId)
    {
    }
}
