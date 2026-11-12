using MediatR;

namespace Feedback.Application.Common.Messaging;

/// <summary>Команда (запис/зміна/видалення) без результату.</summary>
public interface ICommand : IRequest { }

/// <summary>Команда з результатом (наприклад, Id створеного документа).</summary>
public interface ICommand<TResponse> : IRequest<TResponse> { }

/// <summary>Запит (лише читання).</summary>
public interface IQuery<TResponse> : IRequest<TResponse> { }
