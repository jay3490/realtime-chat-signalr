using Chat.Domain.Common;
using Chat.Domain.Messages;
using Chat.Domain.Rooms;

namespace Chat.UnitTests.Domain;

public class MessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Editing_replaces_the_text_and_bumps_the_revision()
    {
        var message = Post("first draft");

        Assert.True(message.Edit("alice", "  second draft ", Now.AddMinutes(5)));

        Assert.Equal(("second draft", 2, Now.AddMinutes(5)), (message.Body, message.Revision, message.EditedAt));
        Assert.IsType<MessageEdited>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void Editing_to_the_same_text_changes_nothing()
    {
        var message = Post("same");

        Assert.False(message.Edit("alice", " same ", Now));

        Assert.Equal(1, message.Revision);
        Assert.Empty(message.DomainEvents);
    }

    [Fact]
    public void Only_the_author_can_edit_or_delete()
    {
        var message = Post("mine");

        Assert.Throws<DomainRuleException>(() => message.Edit("bob", "yours now", Now));
        Assert.Throws<DomainRuleException>(() => message.Delete("bob", Now));
    }

    [Fact]
    public void Edits_are_refused_after_the_edit_window()
    {
        var message = Post("old");

        message.Edit("alice", "just in time", Now + Message.EditWindow);

        Assert.Throws<DomainRuleException>(() => message.Edit("alice", "too late", Now + Message.EditWindow + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Edits_must_still_be_valid_messages()
    {
        var message = Post("text");

        Assert.Throws<DomainRuleException>(() => message.Edit("alice", "   ", Now));
        Assert.Throws<DomainRuleException>(() => message.Edit("alice", new string('x', Message.MaxBodyLength + 1), Now));
    }

    [Fact]
    public void Deleting_leaves_a_tombstone_and_happens_once()
    {
        var message = Post("regrettable");

        Assert.True(message.Delete("alice", Now.AddDays(3)));
        Assert.False(message.Delete("alice", Now.AddDays(4)));

        Assert.True(message.IsDeleted);
        Assert.Equal((string.Empty, Now.AddDays(3)), (message.Body, message.DeletedAt));
        Assert.IsType<MessageDeleted>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void A_deleted_message_cannot_be_edited()
    {
        var message = Post("gone");
        message.Delete("alice", Now);

        Assert.Throws<DomainRuleException>(() => message.Edit("alice", "back", Now));
    }

    private static Message Post(string body)
    {
        var room = Room.CreateChannel("general", "alice", Now);
        room.Join("bob", Now);
        var message = room.Post("alice", body, Now);
        message.ClearDomainEvents();
        return message;
    }
}
