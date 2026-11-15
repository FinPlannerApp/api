using System.Linq;
using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit;

public class TransactionTagsTests
{
    [Fact]
    public void Pack_LowercasesTrimsAndDeDuplicates()
        => TransactionTags.Pack(new[] { " Trip ", "TRIP", "#Work", "my  tag" }).Should().Be("|trip|work|my tag|");

    [Fact]
    public void Pack_ReturnsNullWhenNothingLeft()
    {
        TransactionTags.Pack(null).Should().BeNull();
        TransactionTags.Pack(new[] { "", "  ", "|" }).Should().BeNull();
    }

    [Fact]
    public void Pack_CapsCountAndLength()
    {
        var many = Enumerable.Range(1, 12).Select(i => "t" + i);
        TransactionTags.Unpack(TransactionTags.Pack(many)).Should().HaveCount(TransactionTags.MaxTags);
        TransactionTags.Unpack(TransactionTags.Pack(new[] { new string('a', 80) })).Single().Should().HaveLength(TransactionTags.MaxTagLength);
    }

    [Fact]
    public void Unpack_RoundTrips()
        => TransactionTags.Unpack("|a|b c|").Should().Equal("a", "b c");

    [Fact]
    public void Pattern_MatchesOnlyTheExactTagAndEscapesWildcards()
    {
        TransactionTags.Pattern("Trip").Should().Be("%|trip|%");
        TransactionTags.Pattern("50%_off").Should().Be("%|50\\%\\_off|%");
    }

    [Fact]
    public void CleanNotes_TrimsAndCaps()
    {
        TransactionTags.CleanNotes("   ").Should().BeNull();
        TransactionTags.CleanNotes(new string('x', 900))!.Length.Should().Be(TransactionTags.MaxNotesLength);
    }
}
