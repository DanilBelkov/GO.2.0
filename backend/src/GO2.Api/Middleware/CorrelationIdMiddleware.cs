namespace GO2.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string CorrelationIdKey = "CorrelationId";

    public async Task Invoke(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[CorrelationIdKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }
}

