// =====================================================================
// Проєкт №3 — FeedbackDb (контекст «Відгуки та обговорення», MongoDB)
// Створює 4 колекції з JSON Schema Validation та індекси.
// Ідемпотентно: наявна колекція отримує оновлений валідатор (collMod),
// повторне створення наявного індексу MongoDB ігнорує.
// Запуск: mongosh "mongodb://localhost:27017" --file db/p3/collections.js
//
// Колекції та прийоми моделювання:
//   reviews     — EMBEDDING: author (value object) і comments (обмежений масив)
//                 вбудовані; REFERENCE: discussionId -> discussions; bookId -> Каталог (без FK);
//                 денормалізоване bookTitle; гнучка схема (photos, readingProgress, quote — лише в частини).
//   discussions — ГІБРИД (subset pattern): заголовок обговорення + 3 останні повідомлення
//                 вбудовані (lastMessages), а ВСІ повідомлення — в окремій колекції messages.
//   messages    — REFERENCE: необмежено велика підколекція обговорення (тисячі повідомлень).
//   bookRatings — ГІБРИД (computed pattern): агрегований рейтинг книги + topReviewIds
//                 (посилання на reviews); перераховується aggregation pipeline з $merge.
// =====================================================================
const feedback = db.getSiblingDB('FeedbackDb');

function ensureCollection(name, schema) {
  const options = { validator: { $jsonSchema: schema }, validationLevel: 'strict', validationAction: 'error' };
  if (feedback.getCollectionNames().includes(name)) {
    feedback.runCommand(Object.assign({ collMod: name }, options));
    print(`collection ${name}: validator updated`);
  } else {
    feedback.createCollection(name, options);
    print(`collection ${name}: created`);
  }
}

const authorSchema = {
  bsonType: 'object',
  required: ['userId', 'displayName'],
  properties: {
    userId: { bsonType: 'string', minLength: 1 },
    displayName: { bsonType: 'string', minLength: 1, maxLength: 100 }
  }
};

// ---------------- reviews ----------------
ensureCollection('reviews', {
  bsonType: 'object',
  required: ['bookId', 'bookTitle', 'author', 'rating', 'text', 'comments', 'createdAt'],
  properties: {
    bookId: { bsonType: ['int', 'long'], minimum: 1, description: 'Id книги в BooksDb — логічне посилання без FK' },
    bookTitle: { bsonType: 'string', minLength: 1, description: 'денормалізовано з Каталогу, оновлюється подіями' },
    author: authorSchema,
    rating: { bsonType: 'int', minimum: 1, maximum: 5 },
    text: { bsonType: 'string', minLength: 1, maxLength: 2000 },
    comments: {
      bsonType: 'array',
      maxItems: 100,                       // обмежений масив — тому вбудовуємо
      items: {
        bsonType: 'object',
        required: ['author', 'text', 'createdAt'],
        properties: {
          author: authorSchema,
          text: { bsonType: 'string', minLength: 1, maxLength: 1000 },
          createdAt: { bsonType: 'date' }
        }
      }
    },
    discussionId: { bsonType: ['objectId', 'null'], description: 'reference на discussions' },
    photos: { bsonType: 'array', items: { bsonType: 'string' } },
    readingProgress: {
      bsonType: 'object',
      properties: { pagesRead: { bsonType: ['int', 'long'] }, finished: { bsonType: 'bool' } }
    },
    createdAt: { bsonType: 'date' },
    updatedAt: { bsonType: ['date', 'null'] },
    version: { bsonType: ['int', 'long'], minimum: 0 }
  }
});

// ---------------- discussions ----------------
ensureCollection('discussions', {
  bsonType: 'object',
  required: ['bookId', 'bookTitle', 'title', 'startedBy', 'tags', 'messagesCount', 'lastMessages', 'createdAt', 'lastActivityAt'],
  properties: {
    bookId: { bsonType: ['int', 'long'], minimum: 1 },
    bookTitle: { bsonType: 'string' },
    title: { bsonType: 'string', minLength: 3, maxLength: 200 },
    startedBy: authorSchema,
    tags: { bsonType: 'array', items: { bsonType: 'string' } },
    messagesCount: { bsonType: ['int', 'long'], minimum: 0 },
    lastMessages: {
      bsonType: 'array',
      maxItems: 3,                          // subset pattern: лише 3 останні повідомлення
      items: {
        bsonType: 'object',
        required: ['messageId', 'author', 'text', 'createdAt'],
        properties: {
          messageId: { bsonType: 'objectId' },
          author: authorSchema,
          text: { bsonType: 'string' },
          createdAt: { bsonType: 'date' }
        }
      }
    },
    createdAt: { bsonType: 'date' },
    lastActivityAt: { bsonType: 'date' }
  }
});

// ---------------- messages ----------------
ensureCollection('messages', {
  bsonType: 'object',
  required: ['discussionId', 'author', 'text', 'createdAt'],
  properties: {
    discussionId: { bsonType: 'objectId', description: 'reference на discussions' },
    author: authorSchema,
    text: { bsonType: 'string', minLength: 1, maxLength: 4000 },
    replyToId: { bsonType: ['objectId', 'null'], description: 'reference на інше повідомлення (гілка відповіді)' },
    reactions: { bsonType: 'object' },      // довільний набір реакцій: { "👍": 3, "❤️": 1 }
    createdAt: { bsonType: 'date' },
    editedAt: { bsonType: ['date', 'null'] }
  }
});

// ---------------- bookRatings ----------------
ensureCollection('bookRatings', {
  bsonType: 'object',
  required: ['_id', 'bookTitle', 'ratingsCount', 'average', 'distribution', 'topReviewIds', 'updatedAt'],
  properties: {
    _id: { bsonType: ['int', 'long'], description: '_id = bookId' },
    bookTitle: { bsonType: 'string' },
    ratingsCount: { bsonType: ['int', 'long'], minimum: 0 },
    average: { bsonType: ['double', 'int', 'decimal'], minimum: 0, maximum: 5 },
    distribution: { bsonType: 'object' },   // { "1": n, ..., "5": n }
    topReviewIds: { bsonType: 'array', maxItems: 3, items: { bsonType: 'objectId' } },
    updatedAt: { bsonType: 'date' }
  }
});

// =====================================================================
// ІНДЕКСИ (імена фіксовані — їх використовує й IndexCreationService у Лаб. 04)
// =====================================================================
// «Останні відгуки про книгу»: find({bookId}).sort({createdAt:-1})
feedback.reviews.createIndex({ bookId: 1, createdAt: -1 }, { name: 'ix_reviews_bookId_createdAt' });
// Один відгук від користувача на книгу (цілісність даних)
feedback.reviews.createIndex({ bookId: 1, 'author.userId': 1 }, { name: 'ux_reviews_bookId_authorUserId', unique: true });
// Повнотекстовий пошук по тексту відгуку
feedback.reviews.createIndex({ text: 'text' }, { name: 'ix_reviews_text', default_language: 'none' });
// «Мої відгуки» в кабінеті користувача
feedback.reviews.createIndex({ 'author.userId': 1, createdAt: -1 }, { name: 'ix_reviews_authorUserId_createdAt' });

// «Активні обговорення книги»: find({bookId}).sort({lastActivityAt:-1})
feedback.discussions.createIndex({ bookId: 1, lastActivityAt: -1 }, { name: 'ix_discussions_bookId_lastActivityAt' });
// Пошук обговорень за тегом (multikey-індекс по масиву)
feedback.discussions.createIndex({ tags: 1 }, { name: 'ix_discussions_tags' });

// Сторінка повідомлень обговорення в хронологічному порядку (курсорна пагінація)
feedback.messages.createIndex({ discussionId: 1, createdAt: 1 }, { name: 'ix_messages_discussionId_createdAt' });
// Відповіді на конкретне повідомлення (частковий індекс — лише відповіді)
feedback.messages.createIndex({ replyToId: 1 }, { name: 'ix_messages_replyToId', partialFilterExpression: { replyToId: { $type: 'objectId' } } });

// «Топ книг за рейтингом»
feedback.bookRatings.createIndex({ average: -1, ratingsCount: -1 }, { name: 'ix_bookRatings_average' });

print('FeedbackDb collections and indexes are ready');
