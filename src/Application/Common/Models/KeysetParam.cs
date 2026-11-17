using System.Linq.Expressions;

namespace Application.Common.Models;

/// <summary>
/// Wraps a cursor value so EF Core sends it as a query PARAMETER. A bare <c>Expression.Constant</c> is written into the SQL as a
/// literal, which gives every cursor its own SQL text and its own compiled query: no plan reuse and a growing query cache.
/// A property read on a captured object is what EF treats as a parameter (the same as a variable captured by a lambda).
/// </summary>
internal sealed class KeysetParam<T>
{
    public T Value { get; }
    public KeysetParam(T value) => Value = value;
}

internal static class KeysetParam
{
    public static Expression Of<T>(T value)
        => Expression.Property(Expression.Constant(new KeysetParam<T>(value)), nameof(KeysetParam<T>.Value));
}
