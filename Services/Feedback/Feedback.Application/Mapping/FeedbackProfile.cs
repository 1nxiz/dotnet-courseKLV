using AutoMapper;
using Feedback.Application.Commands.CreateReview;
using Feedback.Application.Common.Models;
using Feedback.Domain.Entities;
using Feedback.Domain.ValueObjects;

namespace Feedback.Application.Mapping;

/// <summary>Мапінг сутностей, value objects і BSON-типів (ObjectId -> string) у DTO, включно з вкладеними масивами.</summary>
public class FeedbackProfile : Profile
{
    public FeedbackProfile()
    {
        CreateMap<Author, AuthorDto>();
        CreateMap<ReadingProgress, ReadingProgressDto>();
        CreateMap<Comment, CommentDto>();

        CreateMap<Review, ReviewDto>()
            .ForCtorParam(nameof(ReviewDto.Id), o => o.MapFrom(s => s.Id.ToString()))
            .ForCtorParam(nameof(ReviewDto.AuthorName), o => o.MapFrom(s => s.Author.DisplayName))
            .ForCtorParam(nameof(ReviewDto.Rating), o => o.MapFrom(s => s.Rating.Value))
            .ForCtorParam(nameof(ReviewDto.CommentsCount), o => o.MapFrom(s => s.Comments.Count));

        CreateMap<Review, ReviewDetailsDto>()
            .ForCtorParam(nameof(ReviewDetailsDto.Id), o => o.MapFrom(s => s.Id.ToString()))
            .ForCtorParam(nameof(ReviewDetailsDto.Rating), o => o.MapFrom(s => s.Rating.Value))
            .ForCtorParam(nameof(ReviewDetailsDto.DiscussionId), o => o.MapFrom(s => s.DiscussionId.HasValue ? s.DiscussionId.Value.ToString() : null))
            .ForCtorParam(nameof(ReviewDetailsDto.Comments), o => o.MapFrom(s => s.Comments));

        CreateMap<MessagePreview, MessagePreviewDto>()
            .ForCtorParam(nameof(MessagePreviewDto.MessageId), o => o.MapFrom(s => s.MessageId.ToString()));
        CreateMap<Message, MessageDto>()
            .ForCtorParam(nameof(MessageDto.Id), o => o.MapFrom(s => s.Id.ToString()))
            .ForCtorParam(nameof(MessageDto.DiscussionId), o => o.MapFrom(s => s.DiscussionId.ToString()))
            .ForCtorParam(nameof(MessageDto.ReplyToId), o => o.MapFrom(s => s.ReplyToId.HasValue ? s.ReplyToId.Value.ToString() : null));
        CreateMap<Discussion, DiscussionDto>()
            .ForCtorParam(nameof(DiscussionDto.Id), o => o.MapFrom(s => s.Id.ToString()))
            .ForCtorParam(nameof(DiscussionDto.Tags), o => o.MapFrom(s => s.Tags))
            .ForCtorParam(nameof(DiscussionDto.LastMessages), o => o.MapFrom(s => s.LastMessages));

        // запит API -> команда
        CreateMap<CreateReviewRequest, CreateReviewCommand>();
    }
}
