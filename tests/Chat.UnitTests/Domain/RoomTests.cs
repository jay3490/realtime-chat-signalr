using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;

namespace Chat.UnitTests.Domain;

public class RoomTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Direct_key_is_the_same_whichever_user_opens_the_conversation()
    {
        Assert.Equal(Room.DirectKey("alice", "bob"), Room.DirectKey("bob", "alice"));
    }

    [Theory]
    [InlineData("General", "general")]
    [InlineData("  dev-ops ", "dev-ops")]
    public void Channel_names_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, Room.NormalizeChannelName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    public void Invalid_channel_names_are_rejected(string input)
    {
        Assert.Throws<DomainRuleException>(() => Room.NormalizeChannelName(input));
    }

    [Fact]
    public void Creating_a_channel_makes_the_creator_a_member_and_raises_an_event()
    {
        var room = Room.CreateChannel("general", "alice", Now);

        Assert.True(room.HasMember("alice"));
        var created = Assert.IsType<RoomCreated>(Assert.Single(room.DomainEvents));
        Assert.Equal(["alice"], created.Members);
    }

    [Fact]
    public void A_direct_conversation_with_yourself_is_rejected()
    {
        Assert.Throws<DomainRuleException>(() => Room.CreateDirect("alice", "alice", Now));
    }

    [Fact]
    public void Direct_conversations_cannot_be_joined()
    {
        var room = Room.CreateDirect("alice", "bob", Now);

        Assert.Throws<DomainRuleException>(() => room.Join("carol", Now));
    }

    [Fact]
    public void Joining_twice_raises_only_one_event()
    {
        var room = Room.CreateChannel("general", "alice", Now);
        room.ClearDomainEvents();

        room.Join("bob", Now);
        room.Join("bob", Now);

        Assert.IsType<MemberJoined>(Assert.Single(room.DomainEvents));
    }

    [Fact]
    public void Non_members_cannot_post()
    {
        var room = Room.CreateChannel("general", "alice", Now);

        Assert.Throws<DomainRuleException>(() => room.Post("mallory", "hi", Now));
    }

    [Fact]
    public void Posting_trims_the_body_and_raises_message_posted()
    {
        var room = Room.CreateChannel("general", "alice", Now);

        var message = room.Post("alice", "  hello  ", Now);

        Assert.Equal("hello", message.Body);
        Assert.IsType<MessagePosted>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void Read_marker_only_moves_forward()
    {
        var room = Room.CreateChannel("general", "alice", Now);
        room.ClearDomainEvents();

        room.MarkRead("alice", 10, Now);
        room.MarkRead("alice", 7, Now.AddSeconds(1));

        var member = Assert.Single(room.Members);
        Assert.Equal(10, member.LastReadMessageId);
        Assert.Equal(Now, member.LastReadAt);
        Assert.Single(room.DomainEvents);
    }
}
