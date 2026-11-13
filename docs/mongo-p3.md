# Проєкт №3 `FeedbackDb` (MongoDB) — колекції, документи, запити

Скрипти: [`db/p3/collections.js`](../db/p3/collections.js) (колекції + JSON Schema Validation + індекси),
[`db/p3/seed.js`](../db/p3/seed.js) (ідемпотентні дані), [`db/p3/queries.js`](../db/p3/queries.js) (запити й агрегації),
[`db/p3/checks.js`](../db/p3/checks.js) (перевірки). Приклади документів: [`docs/examples/`](examples/).

## Колекції (4) і прийоми моделювання

| Колекція | Прийом | Чому так |
|---|---|---|
| `reviews` | **Embedding**: `author` (value object), `comments[]` (≤ 100). **Referencing**: `discussionId` → `discussions`, `bookId` → Каталог (без FK). **Денормалізація**: `bookTitle` | Коментарі нечисленні й завжди показуються разом із відгуком — читаються одним запитом. Обговорення може бути великим — тому лише посилання |
| `discussions` | **Гібрид (subset pattern)**: заголовок + 3 останні повідомлення вбудовано (`lastMessages[]`), а всі повідомлення — у `messages` | Список обговорень показує «останні репліки» без `$lookup`, а повна стрічка читається посторінково |
| `messages` | **Referencing**: `discussionId` → `discussions`, `replyToId` → `messages` | Необмежено велика підколекція (тисячі повідомлень) — вбудовування перевищило б 16 МБ і роздувало б документ |
| `bookRatings` | **Гібрид (computed pattern)**: агрегований рейтинг + `topReviewIds[]` (посилання на `reviews`), `_id = bookId` | Рейтинг читається дуже часто, а рахується рідко — зберігаємо готовий результат агрегації (`$merge`) |

### Гнучка схема

В одній колекції `reviews` лежать документи з різними наборами полів: у частини є `photos[]`,
у частини — `readingProgress { pagesRead, finished }`, в одного — `quote`; `discussionId` є не скрізь.
`db.reviews.find()` повертає документи різної форми, а JSON Schema перевіряє лише обов'язкове ядро
(`bookId, bookTitle, author, rating 1..5, text, comments, createdAt`).

## Приклад документа `reviews` (усі прийоми разом)

```jsonc
{
  "_id": { "$oid": "66a000000000000000000001" },
  "bookId": 4,                                     // посилання на Каталог (без FK)
  "bookTitle": "Clean Architecture",               // денормалізовано для читання
  "author": { "userId": "u-olena", "displayName": "Олена" },   // вбудований value object
  "rating": 5,
  "text": "Чудово пояснено правило залежностей і межі шарів.",
  "photos": ["https://cdn.library.example/reviews/r1-1.jpg"],  // поле є лише в частини документів
  "comments": [                                    // вбудований обмежений масив
    { "author": { "userId": "u-andrii", "displayName": "Андрій" }, "text": "Згоден", "createdAt": { "$date": "2026-10-01T12:00:00Z" } }
  ],
  "discussionId": { "$oid": "66b000000000000000000001" },      // reference на discussions
  "createdAt": { "$date": "2026-10-01T10:00:00Z" },
  "updatedAt": null,
  "version": 0                                     // оптимістична конкурентність (Лаб. 04)
}
```

Інші приклади: [`reviews.json`](examples/reviews.json), [`discussions.json`](examples/discussions.json),
[`messages.json`](examples/messages.json), [`bookRatings.json`](examples/bookRatings.json).

## Індекси

| Колекція | Індекс | Запит |
|---|---|---|
| reviews | `ix_reviews_bookId_createdAt` `{bookId:1, createdAt:-1}` | останні відгуки про книгу |
| reviews | `ux_reviews_bookId_authorUserId` `{bookId:1, "author.userId":1}` **unique** | один відгук від користувача на книгу |
| reviews | `ix_reviews_text` `{text:"text"}` | повнотекстовий пошук |
| reviews | `ix_reviews_authorUserId_createdAt` | «мої відгуки» |
| discussions | `ix_discussions_bookId_lastActivityAt` | активні обговорення книги |
| discussions | `ix_discussions_tags` (multikey) | пошук за тегом |
| messages | `ix_messages_discussionId_createdAt` | стрічка повідомлень, курсорна пагінація |
| messages | `ix_messages_replyToId` (partial) | відповіді на повідомлення |
| bookRatings | `ix_bookRatings_average` | топ книг за рейтингом |

## Запити й агрегації (див. `db/p3/queries.js`)

* пагінація `skip/limit` і **курсорна** пагінація (`createdAt > lastSeen`);
* `$lookup` відгук → обговорення → повідомлення;
* `$unwind` вбудованого масиву `comments` — найактивніші коментатори;
* перерахунок `bookRatings` pipeline-ом `$group → $project ($arrayToObject, $filter) → $merge`;
* повнотекстовий пошук `$text` із сортуванням за `textScore`;
* `updateMany({bookId}, {$set: {bookTitle}})` — оновлення денормалізованої назви (consumer Лаб. 08).
