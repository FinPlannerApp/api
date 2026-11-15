using System;
using System.Collections.Generic;
using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit;

public class ReminderEmailBuilderTests
{
    private static readonly DateTime Today = new(2026, 11, 15);

    [Fact]
    public void Build_ReturnsNullWithNoItemsOrNoAddress()
    {
        ReminderEmailBuilder.Build("a@b.c", new List<ReminderItem>(), Today).Should().BeNull();
        ReminderEmailBuilder.Build(" ", new List<ReminderItem> { new("Rent", Today, 100m, "recurring") }, Today).Should().BeNull();
    }

    [Fact]
    public void Build_SingleItemUsesItsNameAndDay()
    {
        var m = ReminderEmailBuilder.Build("a@b.c", new List<ReminderItem> { new("Netflix", Today.AddDays(1), 649m, "subscription") }, Today)!;
        m.Subject.Should().Be("Reminder: Netflix due tomorrow");
        m.Body.Should().Contain("Netflix").And.Contain("649");
    }

    [Fact]
    public void Build_SeveralItemsAreCountedAndHtmlEncoded()
    {
        var m = ReminderEmailBuilder.Build("a@b.c", new List<ReminderItem> {
            new("<script>x</script>", Today, null, "card bill"), new("Rent", Today.AddDays(2), 9000m, "recurring") }, Today)!;
        m.Subject.Should().Be("Reminder: 2 payments coming up");
        m.Body.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Theory]
    [InlineData(0, "due today")]
    [InlineData(1, "due tomorrow")]
    public void When_DescribesNearDays(int add, string expected) => ReminderEmailBuilder.When(Today.AddDays(add), Today).Should().Be(expected);
}
