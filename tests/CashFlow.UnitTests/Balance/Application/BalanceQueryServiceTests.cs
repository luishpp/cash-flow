using CashFlow.Balance.API.Application.Services;
using CashFlow.Balance.Core.Domain.Entities;
using CashFlow.Balance.Core.Infrastructure.Persistence;
using CashFlow.Balance.Core.Infrastructure.Repositories;
using FluentAssertions;
using Moq;

namespace CashFlow.UnitTests.Balance.Application;

public class BalanceQueryServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IBalanceRepository> _repo = new();
    private readonly BalanceQueryService _sut;

    public BalanceQueryServiceTests()
    {
        _sut = new BalanceQueryService(_uow.Object, _repo.Object);
    }

    private static DailyBalance Build(DateOnly date, decimal credits, decimal debits)
    {
        var b = DailyBalance.New(date);
        if (credits > 0) b.ApplyCredit(credits);
        if (debits > 0) b.ApplyDebit(debits);
        return b;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ListByPeriodAsync — shape envelope: totais no topo + lista diária em Days.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListByPeriodAsync_ReturnsAggregatesAtTopAndDailyRowsInDays()
    {
        var from = new DateOnly(2026, 5, 22);
        var to = new DateOnly(2026, 5, 25);
        _repo.Setup(r => r.ListByPeriodAsync(from, to, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[]
             {
                 Build(new DateOnly(2026, 5, 22), 1200.00m, 136.50m),
                 Build(new DateOnly(2026, 5, 25), 1050.00m, 0m),
             });

        var response = await _sut.ListByPeriodAsync(from, to);

        response.From.Should().Be(from);
        response.To.Should().Be(to);
        response.TotalCredits.Should().Be(2250.00m);
        response.TotalDebits.Should().Be(136.50m);
        response.Balance.Should().Be(2113.50m);

        response.Days.Should().HaveCount(2);
        response.Days[0].Date.Should().Be(new DateOnly(2026, 5, 22));
        response.Days[0].Balance.Should().Be(1063.50m);
        response.Days[1].Date.Should().Be(new DateOnly(2026, 5, 25));
        response.Days[1].Balance.Should().Be(1050.00m);
    }

    [Fact]
    public async Task ListByPeriodAsync_EmptyPeriodReturnsZerosAndEmptyDays()
    {
        var from = new DateOnly(2026, 5, 22);
        var to = new DateOnly(2026, 5, 25);
        _repo.Setup(r => r.ListByPeriodAsync(from, to, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<DailyBalance>());

        var response = await _sut.ListByPeriodAsync(from, to);

        response.From.Should().Be(from);
        response.To.Should().Be(to);
        response.TotalCredits.Should().Be(0m);
        response.TotalDebits.Should().Be(0m);
        response.Balance.Should().Be(0m);
        response.Days.Should().BeEmpty();
    }

    [Fact]
    public async Task ListByPeriodAsync_NegativeBalanceWhenDebitsExceedCredits()
    {
        var from = new DateOnly(2026, 5, 22);
        var to = new DateOnly(2026, 5, 23);
        _repo.Setup(r => r.ListByPeriodAsync(from, to, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[]
             {
                 Build(new DateOnly(2026, 5, 22), 100m, 0m),
                 Build(new DateOnly(2026, 5, 23), 0m, 350m),
             });

        var response = await _sut.ListByPeriodAsync(from, to);

        response.Balance.Should().Be(-250m);
        response.Days.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListByPeriodAsync_SingleDayInRange()
    {
        var single = new DateOnly(2026, 5, 22);
        _repo.Setup(r => r.ListByPeriodAsync(single, single, It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { Build(single, 500m, 200m) });

        var response = await _sut.ListByPeriodAsync(single, single);

        response.From.Should().Be(single);
        response.To.Should().Be(single);
        response.TotalCredits.Should().Be(500m);
        response.TotalDebits.Should().Be(200m);
        response.Balance.Should().Be(300m);
        response.Days.Should().ContainSingle().Which.Balance.Should().Be(300m);
    }

    [Fact]
    public async Task ListByPeriodAsync_ToBeforeFromThrowsArgumentException()
    {
        var from = new DateOnly(2026, 5, 25);
        var to = new DateOnly(2026, 5, 22);

        var act = () => _sut.ListByPeriodAsync(from, to);

        await act.Should().ThrowAsync<ArgumentException>()
                 .WithMessage("*'to'*maior ou igual*'from'*");
        _repo.Verify(r => r.ListByPeriodAsync(It.IsAny<DateOnly>(),
                                              It.IsAny<DateOnly>(),
                                              It.IsAny<CancellationToken>()),
                     Times.Never);
    }

    [Fact]
    public async Task ListByPeriodAsync_BeginsUnitOfWorkBeforeQuerying()
    {
        var d = new DateOnly(2026, 5, 22);
        _repo.Setup(r => r.ListByPeriodAsync(d, d, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<DailyBalance>());

        await _sut.ListByPeriodAsync(d, d);

        _uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
