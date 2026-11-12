using Feedback.Domain.Entities;
using Feedback.Domain.Exceptions;
using Feedback.Domain.ValueObjects;

namespace Feedback.Tests;

/// <summary>Контрольна точка Лаб. 04: створення Review з Rating = 10 кидає DomainException без звернення до БД.</summary>
public class DomainTests
{
    private static readonly Author Olena = new("u-olena", "Олена");

    [Fact]
    public void Review_with_rating_10_throws_DomainException()
        => Assert.Throws<DomainException>(() => new Review(42, "Ноутбук X15", Olena, 10, "Чудовий екран"));

    [Fact]
    public void Value_objects_are_compared_by_value()
    {
        Assert.Equal(new Author("u-1", "Ann"), new Author("u-1", "Ann"));
        Assert.NotEqual(new Author("u-1", "Ann"), new Author("u-2", "Ann"));
        Assert.True(new Rating(5) == new Rating(5));
    }

    [Fact]
    public void Adding_comment_touches_review()
    {
        var review = new Review(1, "Кобзар", Olena, 5, "Перечитую");
        review.AddComment(new Author("u-2", "Андрій"), "Згоден");
        Assert.Single(review.Comments);
        Assert.NotNull(review.UpdatedAt);
    }

    [Fact]
    public void Discussion_keeps_only_three_last_messages_embedded()
    {
        var discussion = new Discussion(4, "Clean Architecture", "Шари", Olena);
        for (var i = 0; i < 5; i++)
            discussion.RegisterMessage(new Message(discussion.Id, Olena, $"msg {i}"));
        Assert.Equal(5, discussion.MessagesCount);
        Assert.Equal(3, discussion.LastMessages.Count);
        Assert.Equal("msg 4", discussion.LastMessages.Last().Text);
    }
}
