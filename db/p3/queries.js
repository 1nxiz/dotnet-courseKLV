// =====================================================================
// Проєкт №3 — приклади запитів та aggregation pipeline (довідково).
// Запуск: mongosh "mongodb://localhost:27017" --file db/p3/queries.js
// =====================================================================
const feedback = db.getSiblingDB('FeedbackDb');

print('--- 1. Останні відгуки про книгу (індекс ix_reviews_bookId_createdAt), сторінка 1 по 2');
printjson(feedback.reviews.find({ bookId: NumberLong(4) }, { bookTitle: 1, 'author.displayName': 1, rating: 1, createdAt: 1 })
  .sort({ createdAt: -1 }).skip(0).limit(2).toArray());

print('--- 2. Курсорна пагінація повідомлень обговорення: наступні 2 після останнього отриманого');
const lastSeen = feedback.messages.find({ discussionId: ObjectId('66b000000000000000000001') }).sort({ createdAt: 1 }).limit(2).toArray().pop();
printjson(feedback.messages.find({ discussionId: lastSeen.discussionId, createdAt: { $gt: lastSeen.createdAt } })
  .sort({ createdAt: 1 }).limit(2).toArray());

print('--- 3. Топ книг за середнім рейтингом (computed pattern bookRatings)');
printjson(feedback.bookRatings.find({}, { bookTitle: 1, average: 1, ratingsCount: 1 }).sort({ average: -1, ratingsCount: -1 }).limit(5).toArray());

print('--- 4. Агрегація: кількість коментарів і середній рейтинг по книгах ($unwind вкладеного масиву)');
printjson(feedback.reviews.aggregate([
  { $project: { bookId: 1, bookTitle: 1, rating: 1, commentsCount: { $size: '$comments' } } },
  { $group: { _id: '$bookId', bookTitle: { $first: '$bookTitle' }, reviews: { $sum: 1 }, avgRating: { $avg: '$rating' }, comments: { $sum: '$commentsCount' } } },
  { $sort: { avgRating: -1 } }
]).toArray());

print('--- 5. Найактивніші коментатори (розгортання вбудованого масиву comments)');
printjson(feedback.reviews.aggregate([
  { $unwind: '$comments' },
  { $group: { _id: '$comments.author.userId', name: { $first: '$comments.author.displayName' }, comments: { $sum: 1 } } },
  { $sort: { comments: -1 } }
]).toArray());

print('--- 6. Обговорення за тегом з останніми повідомленнями (без $lookup — subset pattern)');
printjson(feedback.discussions.find({ tags: 'architecture' }, { title: 1, messagesCount: 1, lastMessages: 1 }).toArray());

print('--- 7. Повнотекстовий пошук по відгуках');
printjson(feedback.reviews.find({ $text: { $search: 'архітектура шарів' } }, { score: { $meta: 'textScore' }, bookTitle: 1, text: 1 })
  .sort({ score: { $meta: 'textScore' } }).toArray());

print('--- 8. Оновлення денормалізованої назви книги в усіх відгуках (так робить consumer у Лаб. 08)');
print('    feedback.reviews.updateMany({ bookId: NumberLong(4) }, { $set: { bookTitle: "Clean Architecture (2nd ed.)" } })');
