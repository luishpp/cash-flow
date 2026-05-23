using System.Globalization;
using CashFlow.Balance.API.Domain.Entities;
using CashFlow.Balance.API.Domain.Exceptions;
using FluentAssertions;
using Reqnroll;

namespace CashFlow.Bdd.Tests.Steps;

[Binding]
public sealed class SaldoConsolidadoSteps
{
    private readonly Dictionary<DateOnly, DailyBalance> _balances = new();
    private Exception? _capturedException;

    private static DateOnly ParseDate(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private DailyBalance Get(DateOnly date)
    {
        if (!_balances.TryGetValue(date, out var balance))
        {
            balance = DailyBalance.New(date);
            _balances[date] = balance;
        }
        return balance;
    }

    [Given(@"que o saldo do dia ""([^""]*)"" está zerado")]
    public void DadoSaldoZerado(string iso)
    {
        var date = ParseDate(iso);
        _balances[date] = DailyBalance.New(date);
    }

    [Given(@"que apliquei um crédito de (.*) no saldo do dia ""([^""]*)""")]
    public void DadoAplicouCredito(string valor, string iso)
    {
        Get(ParseDate(iso)).ApplyCredit(ParseAmount(valor));
    }

    [When(@"eu aplico um (crédito|débito) de (.*) no saldo do dia ""([^""]*)""")]
    public void QuandoAplicaPorTipo(string tipo, string valor, string iso)
    {
        var balance = Get(ParseDate(iso));
        var amount = ParseAmount(valor);
        if (tipo == "crédito") balance.ApplyCredit(amount);
        else balance.ApplyDebit(amount);
    }

    [When(@"eu tento aplicar um crédito de (.*) no saldo do dia ""([^""]*)""")]
    public void QuandoTentaAplicarCredito(string valor, string iso)
    {
        try { Get(ParseDate(iso)).ApplyCredit(ParseAmount(valor)); }
        catch (Exception ex) { _capturedException = ex; }
    }

    [Then(@"o total de créditos do dia ""([^""]*)"" deve ser (.*)")]
    public void EntaoTotalCreditos(string iso, string valor)
    {
        Get(ParseDate(iso)).TotalCredits.Should().Be(ParseAmount(valor));
    }

    [Then(@"o total de débitos do dia ""([^""]*)"" deve ser (.*)")]
    public void EntaoTotalDebitos(string iso, string valor)
    {
        Get(ParseDate(iso)).TotalDebits.Should().Be(ParseAmount(valor));
    }

    [Then(@"o saldo do dia ""([^""]*)"" deve ser (.*)")]
    public void EntaoSaldo(string iso, string valor)
    {
        Get(ParseDate(iso)).Balance.Should().Be(ParseAmount(valor));
    }

    [Then(@"uma DomainException deve ser lançada com mensagem contendo ""([^""]*)""")]
    public void EntaoExceptionLancada(string snippet)
    {
        _capturedException.Should().NotBeNull("uma DomainException deveria ter sido capturada");
        _capturedException.Should().BeOfType<DomainException>();
        _capturedException!.Message.Should().Contain(snippet);
    }
}
