namespace GO2.Api.Middleware;

// Проставляет correlation-id в каждый запрос/ответ для трассировки цепочки вызовов.
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string CorrelationIdKey = "CorrelationId";

    public async Task Invoke(HttpContext context)
    {
        // Если клиент не передал id, генерируем новый.
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[CorrelationIdKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }
}

