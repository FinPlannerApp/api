using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Models;

/// <summary>One page found by keyset paging, and whether another follows (found without counting).</summary>
public sealed record KeysetPage<T>(List<T> Items, bool HasMore, string? NextCursor, bool UsedCursor);

/// <summary>
/// Keyset ("seek") paging for any list with one sort key plus the id as tie-break, in either direction and for dates,
/// text, numbers and ids. The next page is "everything after the last row seen", found straight from an index on
/// (owner, key, id) with no OFFSET, so the cost of a page does not grow with how far you have scrolled.
/// Use it for a list's default order; a custom sort chosen by the client keeps using page numbers.
/// </summary>
public static class KeysetPager
{
    /// <summary>The cursor that fetches the page after a row with this key and id.</summary>
    public static string EncodeCursor(object? key, int id)
    {
        var (kind, raw) = key switch
        {
            DateTime d => ("d", d.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)),
            DateTimeOffset o => ("d", o.UtcTicks.ToString(CultureInfo.InvariantCulture)),
            int i => ("i", i.ToString(CultureInfo.InvariantCulture)),
            long l => ("i", l.ToString(CultureInfo.InvariantCulture)),
            decimal m => ("n", m.ToString(CultureInfo.InvariantCulture)),
            double f => ("f", f.ToString("R", CultureInfo.InvariantCulture)),
            bool b => ("i", b ? "1" : "0"),
            string s => ("s", Convert.ToBase64String(Encoding.UTF8.GetBytes(s))),
            _ => ("s", Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty))),
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{kind}:{raw}:{id}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads a cursor back as the key (as the requested type) and id; false for a missing, malformed or wrong-typed cursor.</summary>
    public static bool TryDecodeCursor<TKey>(string? cursor, out TKey key, out int id)
    {
        key = default!; id = 0;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 600) return false;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var first = text.IndexOf(':');
            var last = text.LastIndexOf(':');
            if (first < 1 || last <= first) return false;
            var kind = text[..first];
            var raw = text[(first + 1)..last];
            if (!int.TryParse(text[(last + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out id)) return false;

            var type = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
            object? value;
            if (type == typeof(DateTime) && kind == "d" && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks) value = new DateTime(ticks, DateTimeKind.Utc);
            else if (type == typeof(string) && kind == "s") value = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            else if (type == typeof(int) && kind == "i" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) value = i;
            else if (type == typeof(long) && kind == "i" && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) value = l;
            else if (type == typeof(double) && kind == "f" && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) value = f;
            else if (type == typeof(decimal) && kind == "n" && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)) value = m;
            else return false;
            key = (TKey)value!;
            return true;
        }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Rows strictly after (key, id) in the list's order. Written as "key at or after k AND (key strictly after k OR id after i)" so
    /// the database uses the index range on the key and compares ids only among rows with the exact same key.
    /// </summary>
    public static IQueryable<T> SeekAfter<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, Expression<Func<T, int>> id,
        TKey afterKey, int afterId, bool keyDescending, bool idDescending)
    {
        var p = key.Parameters[0];
        var idBody = new Swap(id.Parameters[0], p).Visit(id.Body)!;
        var k = KeysetParam.Of(afterKey);
        var i = KeysetParam.Of(afterId);

        Expression Cmp(Expression left, Expression right, bool greater, bool orEqual)
        {
            if (typeof(TKey) == typeof(string))
            {
                var compare = typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!;
                var c = Expression.Call(compare, left, right);
                var zero = Expression.Constant(0);
                return greater ? (orEqual ? Expression.GreaterThanOrEqual(c, zero) : Expression.GreaterThan(c, zero))
                               : (orEqual ? Expression.LessThanOrEqual(c, zero) : Expression.LessThan(c, zero));
            }
            return greater ? (orEqual ? Expression.GreaterThanOrEqual(left, right) : Expression.GreaterThan(left, right))
                           : (orEqual ? Expression.LessThanOrEqual(left, right) : Expression.LessThan(left, right));
        }

        // "After" means greater when ascending and smaller when descending.
        var keyAfterOrEqual = Cmp(key.Body, k, !keyDescending, true);
        var keyAfter = Cmp(key.Body, k, !keyDescending, false);
        var idAfter = idDescending ? Expression.LessThan(idBody, i) : Expression.GreaterThan(idBody, i);
        var body = Expression.AndAlso(keyAfterOrEqual, Expression.OrElse(keyAfter, idAfter));
        return query.Where(Expression.Lambda<Func<T, bool>>(body, p));
    }

    /// <summary>
    /// One page: after the cursor (when it is valid), in order, plus one extra row to learn whether another page follows.
    /// An invalid cursor is ignored (first page). Pass <paramref name="include"/> to load navigation properties the key needs.
    /// </summary>
    public static async Task<KeysetPage<T>> ToKeysetPageAsync<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key,
        Expression<Func<T, int>> id, bool keyDescending, bool idDescending, string? cursor, int pageSize,
        Func<IQueryable<T>, IQueryable<T>>? include = null, int skip = 0, CancellationToken ct = default)
    {
        var usedCursor = TryDecodeCursor<TKey>(cursor, out var afterKey, out var afterId);
        if (usedCursor)
            query = query.SeekAfter(key, id, afterKey, afterId, keyDescending, idDescending);

        var ordered = keyDescending ? query.OrderByDescending(key) : query.OrderBy(key);
        ordered = idDescending ? ordered.ThenByDescending(id) : ordered.ThenBy(id);
        IQueryable<T> final = ordered;
        if (include != null) final = include(final);

        // skip is only for a page-number jump without a cursor (page 1 is 0); a cursor replaces it entirely.
        if (!usedCursor && skip > 0) final = final.Skip(skip);
        var rows = await final.Take(pageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToList() : rows;
        string? next = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            next = EncodeCursor(key.Compile()(last), id.Compile()(last));
        }
        return new KeysetPage<T>(items, hasMore, next, usedCursor);
    }

    private sealed class Swap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
