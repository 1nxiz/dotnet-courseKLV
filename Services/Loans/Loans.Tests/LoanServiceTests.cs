using AutoMapper;
using Loans.Bll.Dtos;
using Loans.Bll.Mapping;
using Loans.Bll.Services;
using Loans.Dal.Abstractions;
using Loans.Domain.Constants;
using Loans.Domain.Exceptions;
using Loans.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Data;

namespace Loans.Tests;

/// <summary>Unit-тести BLL: DAL/UoW замокано, перевіряється бізнес-логіка і керування транзакцією.</summary>
public class LoanServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILoanRepository> _loans = new();
    private readonly Mock<IBookRepository> _books = new();
    private readonly Mock<IReaderRepository> _readers = new();
    private readonly LoanService _service;

    public LoanServiceTests()
    {
        _uow.SetupGet(u => u.Loans).Returns(_loans.Object);
        _uow.SetupGet(u => u.Books).Returns(_books.Object);
        _uow.SetupGet(u => u.Readers).Returns(_readers.Object);
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(LoanProfile).Assembly), NullLoggerFactory.Instance).CreateMapper();
        _service = new LoanService(_uow.Object, mapper, NullLogger<LoanService>.Instance);
    }

    [Fact]
    public async Task Confirm_of_missing_loan_throws_NotFound_and_rolls_back()
    {
        _loans.Setup(l => l.GetWithItemsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync((Loan?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmAsync(42, "test", CancellationToken.None));

        _uow.Verify(u => u.BeginTransactionAsync(IsolationLevel.ReadCommitted, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_reserves_every_item_and_commits()
    {
        _loans.Setup(l => l.GetWithItemsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(LoanWithItems(1,
            new LoanItem { BookId = 10, Quantity = 1 }, new LoanItem { BookId = 11, Quantity = 2 }));

        await _service.ConfirmAsync(1, "test", CancellationToken.None);

        _loans.Verify(l => l.ConfirmAsync(1, "test", It.IsAny<CancellationToken>()), Times.Once);
        _books.Verify(b => b.ReserveCopiesAsync(10, 1, It.IsAny<CancellationToken>()), Times.Once);
        _books.Verify(b => b.ReserveCopiesAsync(11, 2, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_with_not_enough_copies_rolls_back_status_change()
    {
        _loans.Setup(l => l.GetWithItemsAsync(1, It.IsAny<CancellationToken>()))
              .ReturnsAsync(LoanWithItems(1, new LoanItem { BookId = 8, Quantity = 1 }));
        _books.Setup(b => b.ReserveCopiesAsync(8, 1, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new BusinessConflictException("Not enough copies"));

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.ConfirmAsync(1, "test", CancellationToken.None));

        _loans.Verify(l => l.ConfirmAsync(1, "test", It.IsAny<CancellationToken>()), Times.Once);   // перший крок виконано...
        _uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);              // ...і відкочено
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_of_loan_without_items_is_a_business_conflict()
    {
        _loans.Setup(l => l.GetWithItemsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(LoanWithItems(5));

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.ConfirmAsync(5, "test", CancellationToken.None));
        _loans.Verify(l => l.ConfirmAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_takes_title_and_price_snapshot_from_local_books()
    {
        Loan? saved = null;
        _readers.Setup(r => r.ExistsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _books.Setup(b => b.GetByIdsAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<Book> { new() { Id = 4, Title = "Clean Architecture", Price = 60m, AvailableCopies = 2 } });
        _loans.Setup(l => l.AddAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>()))
              .Callback<Loan, CancellationToken>((loan, _) => { loan.Id = 77; saved = loan; })
              .ReturnsAsync(77);
        _loans.Setup(l => l.GetCardAsync(77, It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => saved);

        var dto = await _service.CreateAsync(new CreateLoanDto
        {
            ReaderId = 1,
            Items = { new CreateLoanItemDto { BookId = 4, Quantity = 2 } }
        }, "test", CancellationToken.None);

        Assert.NotNull(saved);
        var item = Assert.Single(saved!.Items);
        Assert.Equal("Clean Architecture", item.BookTitle);
        Assert.Equal(60m, item.UnitPrice);
        Assert.Equal(120m, dto.Total);
        _uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_with_courier_and_without_address_is_rejected_before_database_write()
    {
        _readers.Setup(r => r.ExistsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _books.Setup(b => b.GetByIdsAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<Book> { new() { Id = 1, Title = "Кобзар", Price = 25m } });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(new CreateLoanDto
        {
            ReaderId = 1,
            DeliveryType = DeliveryType.Courier,
            Items = { new CreateLoanItemDto { BookId = 1, Quantity = 1 } }
        }, "test", CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("deliveryAddress"));
        _loans.Verify(l => l.AddAsync(It.IsAny<Loan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_for_unknown_reader_and_books_returns_all_errors()
    {
        _readers.Setup(r => r.ExistsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _books.Setup(b => b.GetByIdsAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<Book>());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(new CreateLoanDto
        {
            ReaderId = 999,
            Items = { new CreateLoanItemDto { BookId = 555, Quantity = 1 } }
        }, "test", CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("readerId"));
        Assert.True(ex.Errors.ContainsKey("items"));
    }

    private static Loan LoanWithItems(long id, params LoanItem[] items) => new()
    {
        Id = id,
        Number = $"LN-{id}",
        Status = LoanStatus.Submitted,
        RowVersion = new byte[8],
        Items = items.ToList()
    };
}
