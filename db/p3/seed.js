// =====================================================================
// Проєкт №3 — FeedbackDb: тестові дані (ІДЕМПОТЕНТНО).
// Кожен документ має фіксований _id і вставляється через
// updateOne({_id}, {$setOnInsert: doc}, {upsert: true}) — find-or-create:
// повторний запуск не створює дублікатів і не перезаписує змінені дані.
// Числа явно типізовані (NumberInt / NumberLong) — так само їх пише C# драйвер (Лаб. 04).
// =====================================================================
const feedback = db.getSiblingDB('FeedbackDb');

function upsertAll(collection, docs) {
  let inserted = 0;
  docs.forEach(doc => {
    const res = feedback[collection].updateOne({ _id: doc._id }, { $setOnInsert: doc }, { upsert: true });
    inserted += res.upsertedCount;
  });
  print(`${collection}: ${inserted} inserted, ${docs.length - inserted} already existed`);
}

const users = {
  olena:  { userId: 'u-olena',  displayName: 'Олена' },
  andrii: { userId: 'u-andrii', displayName: 'Андрій' },
  iryna:  { userId: 'u-iryna',  displayName: 'Ірина' },
  taras:  { userId: 'u-taras',  displayName: 'Тарас' }
};

const ids = {
  r1: ObjectId('66a000000000000000000001'), r2: ObjectId('66a000000000000000000002'),
  r3: ObjectId('66a000000000000000000003'), r4: ObjectId('66a000000000000000000004'),
  r5: ObjectId('66a000000000000000000005'), r6: ObjectId('66a000000000000000000006'),
  d1: ObjectId('66b000000000000000000001'), d2: ObjectId('66b000000000000000000002'),
  m1: ObjectId('66c000000000000000000001'), m2: ObjectId('66c000000000000000000002'),
  m3: ObjectId('66c000000000000000000003'), m4: ObjectId('66c000000000000000000004'),
  m5: ObjectId('66c000000000000000000005')
};

// ---------------- discussions (гібрид: 3 останні повідомлення вбудовано) ----------------
upsertAll('discussions', [
  {
    _id: ids.d1, bookId: NumberLong(4), bookTitle: 'Clean Architecture',
    title: 'Чи потрібен окремий шар Application у невеликому сервісі?',
    startedBy: users.andrii, tags: ['architecture', 'clean-architecture', 'reading-club'],
    messagesCount: NumberInt(4),
    lastMessages: [
      { messageId: ids.m2, author: users.olena, text: 'Для CRUD-сервісу — ні, але для домену з правилами — так.', createdAt: new Date('2026-10-02T10:05:00Z') },
      { messageId: ids.m3, author: users.iryna, text: 'Погоджуюсь, головне — правило залежностей.', createdAt: new Date('2026-10-02T11:00:00Z') },
      { messageId: ids.m4, author: users.andrii, text: 'Дякую, спробую на практиці в Лаб. 04 :)', createdAt: new Date('2026-10-03T09:00:00Z') }
    ],
    createdAt: new Date('2026-10-02T10:00:00Z'), lastActivityAt: new Date('2026-10-03T09:00:00Z')
  },
  {
    _id: ids.d2, bookId: NumberLong(1), bookTitle: 'Кобзар',
    title: 'Улюблений вірш із «Кобзаря»',
    startedBy: users.taras, tags: ['poetry', 'classic'],
    messagesCount: NumberInt(1),
    lastMessages: [
      { messageId: ids.m5, author: users.taras, text: '«Заповіт» — без варіантів.', createdAt: new Date('2026-09-20T18:00:00Z') }
    ],
    createdAt: new Date('2026-09-20T18:00:00Z'), lastActivityAt: new Date('2026-09-20T18:00:00Z')
  }
]);

// ---------------- messages (reference: необмежена кількість повідомлень) ----------------
upsertAll('messages', [
  { _id: ids.m1, discussionId: ids.d1, author: users.andrii, text: 'Читаю розділ про межі. Чи завжди потрібен Application?', replyToId: null, createdAt: new Date('2026-10-02T10:00:00Z') },
  { _id: ids.m2, discussionId: ids.d1, author: users.olena, text: 'Для CRUD-сервісу — ні, але для домену з правилами — так.', replyToId: ids.m1, reactions: { '👍': NumberInt(3) }, createdAt: new Date('2026-10-02T10:05:00Z') },
  { _id: ids.m3, discussionId: ids.d1, author: users.iryna, text: 'Погоджуюсь, головне — правило залежностей.', replyToId: ids.m2, createdAt: new Date('2026-10-02T11:00:00Z') },
  { _id: ids.m4, discussionId: ids.d1, author: users.andrii, text: 'Дякую, спробую на практиці в Лаб. 04 :)', createdAt: new Date('2026-10-03T09:00:00Z'), editedAt: new Date('2026-10-03T09:02:00Z') },
  { _id: ids.m5, discussionId: ids.d2, author: users.taras, text: '«Заповіт» — без варіантів.', createdAt: new Date('2026-09-20T18:00:00Z') }
]);

// ---------------- reviews (embedding + reference + гнучка схема) ----------------
upsertAll('reviews', [
  {
    _id: ids.r1, bookId: NumberLong(4), bookTitle: 'Clean Architecture',
    author: users.olena, rating: NumberInt(5),
    text: 'Чудово пояснено правило залежностей і межі шарів.',
    photos: ['https://cdn.library.example/reviews/r1-1.jpg', 'https://cdn.library.example/reviews/r1-2.jpg'],   // лише в цього відгуку
    comments: [
      { author: users.andrii, text: 'Згоден, особливо розділ про компоненти.', createdAt: new Date('2026-10-01T12:00:00Z') },
      { author: users.iryna, text: 'А приклади коду там є?', createdAt: new Date('2026-10-01T13:00:00Z') }
    ],
    discussionId: ids.d1,
    createdAt: new Date('2026-10-01T10:00:00Z'), updatedAt: null, version: NumberInt(0)
  },
  {
    _id: ids.r2, bookId: NumberLong(4), bookTitle: 'Clean Architecture',
    author: users.andrii, rating: NumberInt(4),
    text: 'Корисно, але подекуди забагато теорії.',
    readingProgress: { pagesRead: NumberInt(432), finished: true },                                            // інший набір полів
    comments: [],
    discussionId: ids.d1,
    createdAt: new Date('2026-10-02T09:00:00Z'), updatedAt: null, version: NumberInt(0)
  },
  {
    _id: ids.r3, bookId: NumberLong(6), bookTitle: 'Designing Data-Intensive Applications',
    author: users.iryna, rating: NumberInt(5),
    text: 'Найкраща книга про реплікацію, партиціювання та узгодженість.',
    quote: 'Data outlives code.',                                                                              // ще одне необов'язкове поле
    comments: [
      { author: users.olena, text: 'Розділ про eventual consistency — must read перед Лаб. 08.', createdAt: new Date('2026-10-06T08:00:00Z') }
    ],
    discussionId: null,
    createdAt: new Date('2026-10-05T19:00:00Z'), updatedAt: new Date('2026-10-06T07:00:00Z'), version: NumberInt(1)
  },
  {
    _id: ids.r4, bookId: NumberLong(1), bookTitle: 'Кобзар',
    author: users.taras, rating: NumberInt(5),
    text: 'Перечитую щороку.',
    comments: [],
    discussionId: ids.d2,
    createdAt: new Date('2026-09-20T17:00:00Z'), updatedAt: null, version: NumberInt(0)
  },
  {
    _id: ids.r5, bookId: NumberLong(2), bookTitle: 'Тигролови',
    author: users.olena, rating: NumberInt(4),
    text: 'Динамічний сюжет, читається на одному диханні.',
    readingProgress: { pagesRead: NumberInt(200), finished: false },
    comments: [],
    createdAt: new Date('2026-09-25T20:00:00Z'), updatedAt: null, version: NumberInt(0)
  },
  {
    _id: ids.r6, bookId: NumberLong(6), bookTitle: 'Designing Data-Intensive Applications',
    author: users.taras, rating: NumberInt(3),
    text: 'Складно для новачка, але варте зусиль.',
    comments: [],
    createdAt: new Date('2026-10-07T21:00:00Z'), updatedAt: null, version: NumberInt(0)
  }
]);

// ---------------- bookRatings (computed pattern, aggregation pipeline + $merge) ----------------
// Перерахунок ідемпотентний: $merge замінює документ рейтингу книги новим значенням.
feedback.reviews.aggregate([
  { $sort: { rating: -1, createdAt: -1 } },
  {
    $group: {
      _id: '$bookId',
      bookTitle: { $first: '$bookTitle' },
      ratingsCount: { $sum: 1 },
      average: { $avg: '$rating' },
      ratings: { $push: '$rating' },
      reviewIds: { $push: '$_id' }
    }
  },
  {
    $project: {
      bookTitle: 1,
      ratingsCount: 1,
      average: { $round: ['$average', 2] },
      topReviewIds: { $slice: ['$reviewIds', 3] },
      distribution: {
        $arrayToObject: {
          $map: {
            input: [1, 2, 3, 4, 5],
            as: 'star',
            in: {
              k: { $toString: '$$star' },
              v: { $size: { $filter: { input: '$ratings', as: 'r', cond: { $eq: ['$$r', '$$star'] } } } }
            }
          }
        }
      },
      updatedAt: '$$NOW'
    }
  },
  { $merge: { into: 'bookRatings', on: '_id', whenMatched: 'replace', whenNotMatched: 'insert' } }
]);
print(`bookRatings: ${feedback.bookRatings.countDocuments()} documents recalculated`);
print('FeedbackDb seed completed');
