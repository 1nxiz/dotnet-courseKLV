// =====================================================================
// Проєкт №3 — FeedbackDb: запити перевірки (структура, гнучка схема,
// валідатор, унікальний індекс, агрегації). Нічого не змінює назавжди.
// Запуск: mongosh "mongodb://localhost:27017" --file db/p3/checks.js
// =====================================================================
const feedback = db.getSiblingDB('FeedbackDb');
function assert(condition, message) { if (!condition) throw new Error('CHECK FAILED: ' + message); print('OK: ' + message); }

// 1) Документи з РІЗНИМИ наборами полів в одній колекції (гнучка схема)
const shapes = feedback.reviews.aggregate([
  { $project: { keys: { $map: { input: { $objectToArray: '$$ROOT' }, in: '$$this.k' } } } },
  { $group: { _id: '$keys', count: { $sum: 1 } } }
]).toArray();
shapes.forEach(s => print('  shape: ' + s._id.join(', ') + '  x' + s.count));
assert(shapes.length >= 3, 'reviews contain documents with different sets of fields');
assert(feedback.reviews.countDocuments({ photos: { $exists: true } }) > 0 &&
       feedback.reviews.countDocuments({ photos: { $exists: false } }) > 0, 'photos exists only in some reviews');

// 2) Embedding: автор і коментарі всередині відгуку
const r1 = feedback.reviews.findOne({ _id: ObjectId('66a000000000000000000001') });
assert(r1.author.displayName === 'Олена' && r1.comments.length === 2, 'author and comments are embedded into review');

// 3) Referencing: відгук -> обговорення -> повідомлення ($lookup)
const withDiscussion = feedback.reviews.aggregate([
  { $match: { _id: r1._id } },
  { $lookup: { from: 'discussions', localField: 'discussionId', foreignField: '_id', as: 'discussion' } },
  { $unwind: '$discussion' },
  { $lookup: { from: 'messages', localField: 'discussion._id', foreignField: 'discussionId', as: 'messages' } },
  { $project: { bookTitle: 1, 'discussion.title': 1, messagesTotal: { $size: '$messages' }, embeddedLast: { $size: '$discussion.lastMessages' } } }
]).toArray()[0];
printjson(withDiscussion);
assert(withDiscussion.messagesTotal === 4 && withDiscussion.embeddedLast === 3, 'hybrid: 3 last messages embedded, all 4 referenced');

// 4) Валідатор відхиляє rating = 10
let validationCode = 0;
try {
  feedback.reviews.insertOne({ bookId: NumberLong(1), bookTitle: 'X', author: { userId: 'u-x', displayName: 'X' },
    rating: NumberInt(10), text: 'bad', comments: [], createdAt: new Date() });
} catch (e) { validationCode = e.code; }
assert(validationCode === 121, 'JSON Schema validator rejects rating = 10 (code 121)');

// 5) Унікальний індекс: другий відгук того самого користувача на ту саму книгу
let duplicateCode = 0;
try {
  feedback.reviews.insertOne({ bookId: NumberLong(4), bookTitle: 'Clean Architecture', author: { userId: 'u-olena', displayName: 'Олена' },
    rating: NumberInt(3), text: 'duplicate', comments: [], createdAt: new Date() });
} catch (e) { duplicateCode = e.code; }
assert(duplicateCode === 11000, 'unique index rejects second review of the same user for the same book (E11000)');

// 6) Індекси створено
const indexNames = feedback.reviews.getIndexes().map(i => i.name);
printjson(indexNames);
assert(indexNames.includes('ix_reviews_bookId_createdAt') && indexNames.includes('ix_reviews_text'), 'reviews indexes exist');

// 7) Денормалізоване bookTitle читається без звернення до Каталогу; рейтинги — computed pattern
printjson(feedback.bookRatings.find({}, { bookTitle: 1, average: 1, ratingsCount: 1, distribution: 1 }).sort({ average: -1 }).toArray());
assert(feedback.bookRatings.findOne({ _id: NumberLong(6) }).ratingsCount === 2, 'bookRatings recalculated by aggregation');

// 8) Повнотекстовий пошук
const found = feedback.reviews.find({ $text: { $search: 'консистентність узгодженість' } }, { bookTitle: 1 }).toArray();
assert(found.length >= 1, 'text index search works');

// 9) Ідемпотентність seed: документи з фіксованими _id існують рівно один раз
const seedIds = ['66a000000000000000000001', '66a000000000000000000002', '66a000000000000000000003',
                 '66a000000000000000000004', '66a000000000000000000005', '66a000000000000000000006'].map(x => ObjectId(x));
print(`  counts: reviews=${feedback.reviews.countDocuments()}, discussions=${feedback.discussions.countDocuments()}, messages=${feedback.messages.countDocuments()}, bookRatings=${feedback.bookRatings.countDocuments()}`);
assert(feedback.reviews.countDocuments({ _id: { $in: seedIds } }) === 6, 'seed reviews present exactly once');
print('FeedbackDb checks passed');
