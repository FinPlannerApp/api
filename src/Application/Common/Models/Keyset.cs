using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Application.Common.Models;

/// <summary>
/// Keyset ("seek") paging for lists ordered newest first by a date and then by id: instead of OFFSET, the next page is
/// "everything older than the last row you saw". With an index on (owner, date DESC, id DESC) the database jumps
/// straight to that spot, so page 5000 costs the same as page 2. The cursor is an opaque token the client hands back.
/// </summary>
public static class Keyset
{
    /// <summary>Turns the last row of a page into the cursor that fetches the page after it.</summary>
    public static string Encode(DateTime key, int id)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key.ToUniversalTime().Ticks}.{id}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Reads a cursor back; false when it is missing or was not produced by <see cref="Encode"/>.</summary>
    public static bool TryDecode(string? cursor, out DateTime key, out int id)
    {
        key = default; id = 0;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 64) return false;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('.');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
            key = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Rows strictly after the cursor in "key DESC, id DESC" order. Written as key &lt;= k AND (key &lt; k OR id &lt; i) so the
    /// database can use the index on the key for the range and only compares ids among rows that share the exact key.
    /// </summary>
    public static IQueryable<T> After<T>(this IQueryable<T> query, Expression<Func<T, DateTime>> keySelector,
        Expression<Func<T, int>> idSelector, DateTime key, int id)
    {
        var p = keySelector.Parameters[0];
        var idBody = new ReplaceParameter(idSelector.Parameters[0], p).Visit(idSelector.Body)!;
        var keyBody = keySelector.Body;
        var k = KeysetParam.Of(key);
        var i = KeysetParam.Of(id);
        var body = Expression.AndAlso(
            Expression.LessThanOrEqual(keyBody, k),
            Expression.OrElse(Expression.LessThan(keyBody, k), Expression.LessThan(idBody, i)));
        return query.Where(Expression.Lambda<Func<T, bool>>(body, p));
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
