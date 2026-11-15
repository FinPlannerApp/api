using System;
using System.Linq;
using Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common;

public class KeysetTests
{
    private sealed record Row(int Id, DateTime At);

    [Fact]
    public void CursorRoundTrips()
    {
        var at = new DateTime(2026, 11, 16, 9, 30, 15, 123, DateTimeKind.Utc).AddTicks(4567);
        var cursor = Keyset.Encode(at, 4242);

        Keyset.TryDecode(cursor, out var key, out var id).Should().BeTrue();
        key.Should().Be(at);
        id.Should().Be(4242);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cursor!")]
    [InlineData("bm90LWEtbnVtYmVy")] // base64 of "not-a-number"
    public void ABadCursorIsRejectedNotThrown(string? cursor)
    {
        Keyset.TryDecode(cursor, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ACursorIsUrlSafe()
    {
        var cursor = Keyset.Encode(DateTime.UtcNow, int.MaxValue);
        cursor.Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public void AfterContinuesExactlyWhereThePageEnded_EvenWhenRowsShareATimestamp()
    {
        // 30 rows, 5 per timestamp: ids tie-break within the same instant, newest first.
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(1, 30).Select(i => new Row(i, start.AddHours(i / 5))).ToList();
        var ordered = rows.OrderByDescending(r => r.At).ThenByDescending(r => r.Id).ToList();

        var walked = new System.Collections.Generic.List<int>();
        string? cursor = null;
        for (var guard = 0; guard < 20; guard++)
        {
            var query = rows.AsQueryable();
            if (Keyset.TryDecode(cursor, out var after, out var afterId)) query = query.After(r => r.At, r => r.Id, after, afterId);
            var page = query.OrderByDescending(r => r.At).ThenByDescending(r => r.Id).Take(7).ToList();
            if (page.Count == 0) break;
            walked.AddRange(page.Select(r => r.Id));
            cursor = Keyset.Encode(page[^1].At, page[^1].Id);
        }

        walked.Should().Equal(ordered.Select(r => r.Id));
    }
}
