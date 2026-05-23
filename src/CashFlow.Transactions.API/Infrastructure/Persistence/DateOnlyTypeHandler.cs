using System.Data;
using Dapper;

namespace CashFlow.Transactions.API.Infrastructure.Persistence;

/// <summary>
/// Dapper 2.x não tem handler nativo para <see cref="DateOnly"/> em todos os providers.
/// Npgsql aceita DateOnly diretamente, mas Dapper precisa do mapeamento explícito
/// para evitar <c>"DateOnly cannot be used as a parameter value"</c>.
/// </summary>
public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new InvalidCastException(
            $"Não foi possível converter {value.GetType().Name} para DateOnly.")
    };
}
