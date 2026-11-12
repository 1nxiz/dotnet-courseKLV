# Політика дублювання даних і узгодженості між базами

Дублювання між сервісами допустиме й очікуване: воно роз'єднує сервіси та прибирає синхронний
міжсервісний виклик при кожному читанні. Для **кожного** поля, що дублюється, визначено власника
(джерело істини) і політику узгодженості. Двох незалежно редагованих копій без власника (dual write) немає.

| Поле | Де зберігається | Джерело (власник даних) | Політика узгодженості |
|---|---|---|---|
| `LoanItems.BookTitle`, `LoanItems.UnitPrice` | LoansDb (`dbo.LoanItems`) | `Book` у BooksDb (Каталог) | **Знімок** на момент оформлення видачі — **не оновлюється** (як `OrderItem.ProductName/UnitPrice` в eShop) |
| `Books.Title`, `Books.Isbn`, `Books.Price` (локальна копія довідника) | LoansDb (`dbo.Books`) | `Book` у BooksDb (Каталог) | **Копія для читання**, оновлюється подіями `BookPriceChanged` / `BookRenamed` з Каталогу (Лаб. 08); `SourceUpdatedAt` захищає від застосування застарілої події |
| `Books.Id` (LoansDb), `LoanItems.BookId` | LoansDb | `Book.Id` у BooksDb | Логічне посилання за ідентифікатором, **без FOREIGN KEY** між базами; цілісність перевіряє прикладний код |
| `reviews.bookTitle` | FeedbackDb (MongoDB, `reviews`) | `Book` у BooksDb (Каталог) | **Копія для читання** (денормалізація), оновлюється подією `BookRenamed` з Каталогу (`updateMany` за `bookId`) |
| `discussions.bookTitle`, `bookRatings.bookTitle` | FeedbackDb (`discussions`, `bookRatings`) | `Book` у BooksDb (Каталог) | **Копія для читання**, оновлюється тією самою подією `BookRenamed` |
| `reviews.bookId`, `discussions.bookId`, `bookRatings._id` | FeedbackDb | `Book.Id` у BooksDb | Логічне посилання без FK (у MongoDB зовнішніх ключів немає взагалі) |
| `reviews.author.displayName`, `comments[].author.displayName` | FeedbackDb | Профіль користувача (Identity, Лаб. 09) | **Знімок** імені на момент написання відгуку/коментаря — не оновлюється (історичний підпис) |
| `discussions.lastMessages[]` (3 останні повідомлення) | FeedbackDb (`discussions`) | `messages` у тій самій FeedbackDb | Дублювання **всередині** контексту (subset pattern): оновлюється в тій самій операції, що додає повідомлення |
| `bookRatings.average`, `ratingsCount`, `distribution` | FeedbackDb (`bookRatings`) | `reviews` у тій самій FeedbackDb | Обчислюване значення (computed pattern): перераховується агрегацією з `$merge` після зміни відгуків |

### Що НЕ дублюється

* `Books.AvailableCopies` у LoansDb — **власні** дані контексту «Видачі» (скільки примірників зараз на полиці;
  змінюється при резервуванні/поверненні). Каталог зберігає інше поле — `TotalCopies` (розмір фонду).
  Це два різні факти з різними власниками, а не дві копії одного поля.
* Між **Loans** і **Feedback** дубльованих полів немає — ці контексти не взаємодіють напряму.

### Мінімум один рядок на кожну пару сервісів, що взаємодіють

| Пара | Рядки політики |
|---|---|
| Books → Loans | `LoanItems.BookTitle/UnitPrice` (знімок), `Books.Title/Isbn/Price` (копія за подіями) |
| Books → Feedback | `reviews.bookTitle`, `discussions.bookTitle`, `bookRatings.bookTitle` (копії за подіями) |
| Loans ↔ Feedback | не взаємодіють — дубльованих полів немає |
