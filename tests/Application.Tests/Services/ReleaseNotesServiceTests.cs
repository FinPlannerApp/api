using System;
using System.Linq;
using System.Threading.Tasks;
using Application.Services;
using Application.Tests.Fixtures;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Xunit;

namespace Application.Tests.Services;

public class ReleaseNotesServiceTests
{
    private static ReleaseNote Release(string stream, string version, string date, string? slug = null, params (string type, string text)[] items)
        => new()
        {
            Stream = stream,
            Version = version,
            ReleasedOn = DateTime.Parse(date),
            BlogSlug = slug,
            Items = items.Select((it, i) => new ReleaseNoteItem { SortOrder = i + 1, Type = it.type, Text = it.text }).ToList()
        };

    private static async Task<(ApplicationDbContext Db, ReleaseNotesService Service)> SeedAsync(params ReleaseNote[] releases)
    {
        var db = TestDbContextFactory.Create();
        db.ReleaseNotes.AddRange(releases);
        await db.SaveChangesAsync();
        return (db, new ReleaseNotesService(db));
    }

    // ── version helpers ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("6.2.0", "minor")]
    [InlineData("6.2.3", "patch")]
    [InlineData("7.0.0", "major")]
    [InlineData("0.1.0", "minor")]
    public void Level_comes_from_the_version_number(string version, string expected)
        => ReleaseVersion.Level(version).Should().Be(expected);

    [Fact]
    public void Versions_compare_numerically_not_alphabetically()
    {
        ReleaseVersion.Compare("6.10.0", "6.9.0").Should().BePositive();
        ReleaseVersion.Compare("6.1.2", "6.1.10").Should().BeNegative();
        ReleaseVersion.Compare("6.1.0", "6.1.0").Should().Be(0);
    }

    // ── recent releases (What's new window) ───────────────────────────────────────

    [Fact]
    public async Task Without_afterId_it_returns_the_latest_releases_newest_first_with_ordered_items()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.1.0", "2026-11-01", null, ("new", "B"), ("fixed", "C")),
            Release("app", "6.2.0", "2026-11-14", null, ("new", "A")));

        var recent = await service.GetRecentAsync(null, 5);

        recent.Select(r => r.Version).Should().Equal("6.2.0", "6.1.0");
        recent[0].Level.Should().Be("minor");
        recent[1].Items.Select(i => i.Text).Should().Equal("B", "C");
    }

    [Fact]
    public async Task With_afterId_it_returns_only_what_the_person_has_not_seen()
    {
        var (db, service) = await SeedAsync(
            Release("app", "6.1.0", "2026-11-01", null, ("new", "old")),
            Release("app", "6.1.1", "2026-11-05", null, ("fixed", "patch")),
            Release("app", "6.2.0", "2026-11-14", null, ("new", "newest")));
        var seenId = db.ReleaseNotes.Single(r => r.Version == "6.1.0").Id;

        var unseen = await service.GetRecentAsync(seenId, 5);

        unseen.Select(r => r.Version).Should().Equal("6.2.0", "6.1.1");
    }

    [Fact]
    public async Task Count_is_capped_and_never_below_one()
    {
        var releases = Enumerable.Range(0, 15)
            .Select(i => Release("app", $"6.{i}.0", "2026-11-14", null, ("new", $"n{i}")))
            .ToArray();
        var (_, service) = await SeedAsync(releases);

        (await service.GetRecentAsync(null, 100)).Should().HaveCount(10);
        (await service.GetRecentAsync(null, 0)).Should().HaveCount(1);
    }

    // ── patches rolled into the next minor/major article ─────────────────────────

    [Fact]
    public async Task A_minor_article_lists_the_patches_since_the_previous_minor()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.1.0", "2026-11-01", "post-6-1", ("new", "feature")),
            Release("app", "6.1.1", "2026-11-05", null, ("fixed", "Fixed the login glitch")),
            Release("app", "6.1.2", "2026-11-09", null, ("improved", "Faster dashboard"), ("fixed", "Fixed a typo")),
            Release("app", "6.2.0", "2026-11-14", "post-6-2", ("new", "bigger feature")));

        var md = await service.BuildAccumulatedPatchesMarkdownAsync("post-6-2");

        md.Should().Contain("## Also fixed and improved since 6.1.0");
        md.Should().Contain("- Fixed the login glitch *(6.1.1)*")
          .And.Contain("- Faster dashboard *(6.1.2)*")
          .And.Contain("- Fixed a typo *(6.1.2)*");
        md.IndexOf("6.1.1", StringComparison.Ordinal).Should().BeLessThan(md.IndexOf("(6.1.2)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Patches_before_the_previous_minor_are_not_repeated()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.0.1", "2026-10-01", null, ("fixed", "old patch")),
            Release("app", "6.1.0", "2026-11-01", "post-6-1", ("new", "feature")),
            Release("app", "6.1.1", "2026-11-05", null, ("fixed", "new patch")),
            Release("app", "6.2.0", "2026-11-14", "post-6-2", ("new", "x")));

        var md = await service.BuildAccumulatedPatchesMarkdownAsync("post-6-2");

        md.Should().Contain("new patch").And.NotContain("old patch");
    }

    [Fact]
    public async Task A_major_article_also_collects_the_last_patches()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.4.0", "2026-11-01", "post-6-4", ("new", "feature")),
            Release("app", "6.4.1", "2026-11-05", null, ("fixed", "a fix")),
            Release("app", "7.0.0", "2026-12-01", "post-7", ("new", "big")));

        (await service.BuildAccumulatedPatchesMarkdownAsync("post-7")).Should().Contain("a fix *(6.4.1)*");
    }

    [Fact]
    public async Task Patches_of_the_other_stream_are_not_mixed_in()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.1.0", "2026-11-01", null, ("new", "a")),
            Release("app", "6.2.0", "2026-11-14", "post-6-2", ("new", "b")),
            Release("server", "5.0.1", "2026-11-05", null, ("fixed", "server fix")));

        (await service.BuildAccumulatedPatchesMarkdownAsync("post-6-2")).Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_added_for_unknown_slugs_patch_releases_or_when_there_are_no_patches()
    {
        var (_, service) = await SeedAsync(
            Release("app", "6.1.0", "2026-11-01", "post-6-1", ("new", "a")),
            Release("app", "6.1.1", "2026-11-05", "post-patch", ("fixed", "p")),
            Release("app", "6.2.0", "2026-11-14", "post-6-2", ("new", "b")));

        (await service.BuildAccumulatedPatchesMarkdownAsync("no-such-post")).Should().BeEmpty();
        (await service.BuildAccumulatedPatchesMarkdownAsync("post-patch")).Should().BeEmpty("a patch has no article of its own");
        (await service.BuildAccumulatedPatchesMarkdownAsync("post-6-1")).Should().BeEmpty("the first release has nothing before it");
    }
}
