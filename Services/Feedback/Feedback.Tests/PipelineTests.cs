using Feedback.Application;
using Feedback.Application.Commands.CreateReview;
using Feedback.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Feedback.Tests;

/// <summary>ValidationBehavior відхиляє команду до виклику обробника — репозиторій не викликається.</summary>
public class PipelineTests
{
    [Fact]
    public async Task Command_with_rating_10_is_rejected_before_handler()
    {
        var repository = new Mock<IReviewRepository>();
        var services = new ServiceCollection()
            .AddLogging().AddMemoryCache(o => o.SizeLimit = 100).AddDistributedMemoryCache().AddMetrics()
            .AddSingleton(repository.Object);
        services.AddApplication(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => sender.Send(
            new CreateReviewCommand(2, "Тигролови", "u-x", "X", 10, "?")));

        Assert.Contains(ex.Errors, e => e.PropertyName == "Rating");
        repository.Verify(r => r.AddAsync(It.IsAny<Feedback.Domain.Entities.Review>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_review_is_rejected_by_async_rule()
    {
        var repository = new Mock<IReviewRepository>();
        repository.Setup(r => r.ExistsAsync(4, "u-olena", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var services = new ServiceCollection().AddLogging().AddMemoryCache(o => o.SizeLimit = 100).AddDistributedMemoryCache().AddMetrics().AddSingleton(repository.Object);
        services.AddApplication(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => provider.GetRequiredService<ISender>().Send(
            new CreateReviewCommand(4, "Clean Architecture", "u-olena", "Олена", 3, "Ще раз")));
        Assert.Contains(ex.Errors, e => e.PropertyName == "AuthorId");
    }

    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        var services = new ServiceCollection().AddLogging().AddMemoryCache(o => o.SizeLimit = 100).AddDistributedMemoryCache().AddMetrics();
        services.AddApplication(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<AutoMapper.IMapper>().ConfigurationProvider.AssertConfigurationIsValid();
    }
}
