using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Common.Models;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Application.Tests.Common;

/// <summary>
/// The same keyset walk as KeysetPagerTests, but against a real PostgreSQL. The in-memory provider compares strings
/// the .NET way; Postgres uses the database collation, so a text-key cursor could repeat or skip rows there without
/// any in-memory test noticing. Needs Docker; set FINPLANNER_PG_TESTS=1 to run (CI with Docker should set it).
/// </summary>
public class KeysetPagerPostgresTests : IAsyncLifetime
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("FINPLANNER_PG_TESTS") == "1";
    private PostgreSqlContainer? _pg;

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _pg.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_pg != null) await _pg.DisposeAsync();
    }

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_pg!.GetConnectionString()).Options, new HttpContextAccessor());

    [Fact]
    public async Task MerchantNames_WithMixedCaseAccentsAndSpaces_WalkWithoutRepeatsOrGaps()
    {
        if (!Enabled) return; // Docker-only test: a no-op unless FINPLANNER_PG_TESTS=1

        await using var ctx = NewContext();
        await ctx.Database.EnsureCreatedAsync();

        // Names that sort differently under .NET ordinal rules and a database collation: case, accents, spaces, digits.
        var names = new[] { "amazon", "Amazon", "Amazon Pay", "ÄPFEL", "apple", "Apple Store", "shop 10", "shop 9", "Shop 9", "zomato", "Zomato", "_misc", "7-eleven" };
        foreach (var n in names) ctx.Merchants.Add(new Merchant { UserId = "u", Name = n });
        foreach (var n in names) ctx.Merchants.Add(new Merchant { UserId = "u", Name = n }); // duplicates exercise the id tie-break
        await ctx.SaveChangesAsync();

        foreach (var descending in new[] { false, true })
        {
            var seen = new List<int>();
            string? cursor = null;
            for (var guard = 0; guard < 40; guard++)
            {
                var page = await ctx.Merchants.Where(m => m.UserId == "u")
                    .ToKeysetPageAsync(m => m.Name, m => m.Id, descending, descending, cursor, 4);
                seen.AddRange(page.Items.Select(m => m.Id));
                if (!page.HasMore) break;
                cursor = page.NextCursor;
            }

            seen.Should().OnlyHaveUniqueItems("a cursor must never repeat a row");
            seen.Should().HaveCount(names.Length * 2, "a cursor must never skip a row");
        }
    }
}
