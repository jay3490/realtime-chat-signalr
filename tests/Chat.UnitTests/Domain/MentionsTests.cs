using Chat.Domain.Messages;
using Chat.Domain.Rooms;

namespace Chat.UnitTests.Domain;

public class MentionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("hi @bob", "bob")]
    [InlineData("@Alice and @bob, then @alice again", "alice bob")]
    [InlineData("thanks @bob.", "bob")]
    [InlineData("mail me at carol@example.org", "")]
    [InlineData("@x is too short", "")]
    [InlineData("no mentions here", "")]
    public void Mentions_are_found_lower_cased_and_without_duplicates(string body, string expected)
    {
        Assert.Equal(expected, string.Join(' ', Mentions.Find(body)));
    }

    [Fact]
    public void Posting_resolves_mentions_against_members_and_skips_the_sender()
    {
        var room = Room.CreateChannel("general", "alice", Now);
        room.Join("bob", Now);
        room.ClearDomainEvents();

        var message = room.Post("alice", "@bob @alice @stranger have a look", Now);

        var posted = Assert.IsType<MessagePosted>(Assert.Single(message.DomainEvents));
        Assert.Equal(["bob"], posted.Mentions);
    }
}
