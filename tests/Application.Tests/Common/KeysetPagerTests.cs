using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Common.Models;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Domain.Entities;

namespace Application.Tests.Common;

/// <summary>The generic keyset pager over real EF queries (in-memory provider): every key type, both directions, ties.</summary>
public class KeysetPagerTests
{
    private static ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new HttpContextAccessor());

    private static async Task<List<Merchant>> SeedMerchantsAsync(ApplicationDbContext ctx, int count)
    {
        // Names repeat in pairs so the id tie-break is exercised.
        for (var i = 0; i < count; i++)
            ctx.Merchants.Add(new Merchant { UserId = "u", Name = $"Shop {i / 2:D2}" });
        await ctx.SaveChangesAsync();
        return await ctx.Merchants.OrderBy(m => m.Name).ThenBy(m => m.Id).ToListAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StringKey_WalkEqualsTheFullOrder_InEitherDirection(bool descending)
    {
        await using var ctx = NewContext();
        await SeedMerchantsAsync(ctx, 23);
        var expected = descending
            ? await ctx.Merchants.OrderByDescending(m => m.Name).ThenByDescending(m => m.Id).Select(m => m.Id).ToListAsync()
            : await ctx.Merchants.OrderBy(m => m.Name).ThenBy(m => m.Id).Select(m => m.Id).ToListAsync();

        var walked = new List<int>();
        string? cursor = null;
        for (var guard = 0; guard < 20; guard++)
        {
            var page = await ctx.Merchants.ToKeysetPageAsync(m => m.Name, m => m.Id, descending, descending, cursor, 5);
            walked.AddRange(page.Items.Select(m => m.Id));
            if (!page.HasMore) { page.NextCursor.Should().BeNull(); break; }
            page.NextCursor.Should().NotBeNull();
            cursor = page.NextCursor;
        }

        walked.Should().Equal(expected);
    }

    [Fact]
    public async Task IdKey_AndPageNumberSkip_Work()
    {
        await using var ctx = NewContext();
        await SeedMerchantsAsync(ctx, 12);
        var all = await ctx.Merchants.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();

        var first = await ctx.Merchants.ToKeysetPageAsync(m => m.Id, m => m.Id, false, false, null, 5);
        first.Items.Select(m => m.Id).Should().Equal(all.Take(5));
        first.UsedCursor.Should().BeFalse();

        // A page-number jump (no cursor) is a skip; a cursor replaces the skip completely.
        var third = await ctx.Merchants.ToKeysetPageAsync(m => m.Id, m => m.Id, false, false, null, 5, skip: 10);
        third.Items.Select(m => m.Id).Should().Equal(all.Skip(10));
        third.HasMore.Should().BeFalse();

        var next = await ctx.Merchants.ToKeysetPageAsync(m => m.Id, m => m.Id, false, false, first.NextCursor, 5, skip: 999);
        next.UsedCursor.Should().BeTrue();
        next.Items.Select(m => m.Id).Should().Equal(all.Skip(5).Take(5));
    }

    [Fact]
    public async Task ACursorOfTheWrongTypeIsIgnored_NotAnError()
    {
        await using var ctx = NewContext();
        await SeedMerchantsAsync(ctx, 6);
        var dateCursor = KeysetPager.EncodeCursor(DateTime.UtcNow, 3);

        var page = await ctx.Merchants.ToKeysetPageAsync(m => m.Name, m => m.Id, false, false, dateCursor, 4);

        page.UsedCursor.Should().BeFalse();
        page.Items.Should().HaveCount(4);
    }

    [Fact]
    public void CursorsRoundTripForEveryKeyType()
    {
        var at = new DateTime(2026, 11, 16, 8, 5, 3, DateTimeKind.Utc).AddTicks(123);
        KeysetPager.TryDecodeCursor<DateTime>(KeysetPager.EncodeCursor(at, 7), out var d, out var di).Should().BeTrue();
        (d, di).Should().Be((at, 7));

        KeysetPager.TryDecodeCursor<string>(KeysetPager.EncodeCursor("Café: a/b+c=d", 9), out var s, out var si).Should().BeTrue();
        (s, si).Should().Be(("Café: a/b+c=d", 9));

        KeysetPager.TryDecodeCursor<double>(KeysetPager.EncodeCursor(950.25d, 2), out var f, out _).Should().BeTrue();
        f.Should().Be(950.25d);

        KeysetPager.TryDecodeCursor<decimal>(KeysetPager.EncodeCursor(12.5m, 2), out var m, out _).Should().BeTrue();
        m.Should().Be(12.5m);

        KeysetPager.TryDecodeCursor<int>(KeysetPager.EncodeCursor(42, 2), out var i, out _).Should().BeTrue();
        i.Should().Be(42);

        KeysetPager.EncodeCursor("x", 1).Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%%%")]
    public void BadCursorsAreRejected(string? cursor)
    {
        KeysetPager.TryDecodeCursor<string>(cursor, out _, out _).Should().BeFalse();
        KeysetPager.TryDecodeCursor<DateTime>(cursor, out _, out _).Should().BeFalse();
    }
}
