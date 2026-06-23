using FluentValidation;

namespace NutriForge.Api.Setup;

/// <summary>
/// Endpoint filter that runs the FluentValidation validator for the request body type
/// <typeparamref name="T"/>. A failure throws <see cref="ValidationException"/>, which the
/// global handler renders as a Problem Details 400.
/// </summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is not null)
        {
            await validator.ValidateAndThrowAsync(arg, context.HttpContext.RequestAborted).ConfigureAwait(false);
        }

        return await next(context).ConfigureAwait(false);
    }
}

/// <summary>Sugar for attaching <see cref="ValidationFilter{T}"/> to a route.</summary>
public static class ValidationFilterExtensions
{
    public static TBuilder ValidatesBody<TBuilder, T>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter<TBuilder, ValidationFilter<T>>();
        return builder;
    }
}
