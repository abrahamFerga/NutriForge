using Microsoft.EntityFrameworkCore;
using NutriForge.Application.Abstractions;
using NutriForge.Infrastructure.Persistence;

namespace NutriForge.Api.Middleware;

/// <summary>
/// Honors the <c>Idempotency-Key</c> header on mutations: a repeated key from the same user
/// within a 24h window replays the original response instead of re-executing the write
/// (ARCH idempotency). Keyless or read requests pass straight through.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public async Task InvokeAsync(HttpContext context, NutriForgeDbContext db, ICurrentUser user, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsMutation(context.Request.Method)
            || !context.Request.Headers.TryGetValue(HeaderName, out var keyValues)
            || user.UserId is not { } userId)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var key = keyValues.ToString();
        var now = clock.UtcNow;

        var existing = await db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.UserId == userId, context.RequestAborted)
            .ConfigureAwait(false);

        if (existing is not null && existing.ExpiresAt > now)
        {
            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Idempotency-Replayed"] = "true";
            await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        // Buffer the response so we can persist it after a successful write.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context).ConfigureAwait(false);

            buffer.Position = 0;
            var body = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);

            if (context.Response.StatusCode is >= 200 and < 300)
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Key = key,
                    UserId = userId,
                    StatusCode = context.Response.StatusCode,
                    ResponseBody = body,
                    CreatedAt = now,
                    ExpiresAt = now + Window,
                });
                try
                {
                    await db.SaveChangesAsync(context.RequestAborted).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    // A concurrent request stored the same key first — harmless.
                }
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsMutation(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
