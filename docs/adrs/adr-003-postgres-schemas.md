# ADR-003: PostgreSQL com um database e dois schemas separados

**Status:** Aceita *(revisada — versão anterior previa dois databases)*

## Contexto

O **RNF-01** exige que a indisponibilidade do Balance não afete o Transactions. A separação precisa se estender até a camada de dados. O desafio também exige execução local (Docker) em qualquer plataforma (Windows, macOS Intel e Apple Silicon). A pergunta não é *se* separar, mas *com que granularidade* — instância, database ou schema.

## Decisão

Usar **PostgreSQL com um único database (`cashflow`) e dois schemas separados** (`transactions` e `balance`). Cada serviço possui usuário próprio com `GRANT` restrito ao seu schema. Em produção (Azure Database for PostgreSQL), a evolução natural é separar em instâncias dedicadas.

## Por que PostgreSQL (e não SQL Server)

| Critério | PostgreSQL | SQL Server |
|---|---|---|
| Docker ARM64 (Apple Silicon) | Imagem nativa, funciona perfeitamente | Apenas AMD64, requer Rosetta 2, instável |
| Tamanho da imagem | ~80MB | ~1.5GB |
| Startup time | ~2 segundos | ~15-30 segundos |
| Licença | Open source, gratuito | Licença Microsoft (Developer Edition gratuita para dev) |
| Suporte ao Dapper | Nativo via Npgsql | Nativo via Microsoft.Data.SqlClient |
| Azure (produção) | Azure Database for PostgreSQL (gerenciado) | Azure SQL Database (gerenciado) |

O fator decisivo é pragmático: se o avaliador tiver um MacBook com Apple Silicon, o SQL Server em Docker pode simplesmente não subir. PostgreSQL elimina esse risco.

## Por que um database com dois schemas (e não dois databases)

| Abordagem | Vantagem | Desvantagem |
|---|---|---|
| **Um database, dois schemas** *(escolhida)* | Isolamento lógico real (GRANTs por usuário); migrations independentes por schema; uma única connection string base; demonstração de bounded contexts | Compartilha CPU/I/O/lock manager do mesmo cluster |
| Dois databases na mesma instância | Isolamento adicional de catálogo | Mesmo I/O/CPU compartilhado — isolamento é simbólico no Docker; duas migrations strategies; duas connection strings |
| Dois containers separados | Isolamento real de recursos | Dobra a complexidade do Compose, mais memória; over-engineering para escopo local |

### Por que schemas são suficientes para o RNF-01 nesse contexto

O argumento "dois databases isolam I/O" não se sustenta quando ambos rodam no MESMO container Docker (mesma CPU, mesma RAM, mesmo SSD). Schemas separados oferecem o que importa de fato no escopo do desafio: **isolamento lógico de domínio + controle de acesso por usuário**. Em produção, o isolamento físico é endereçado movendo cada schema para uma instância dedicada gerenciada — uma decisão de infraestrutura, não de código.

## Configuração concreta

```sql
CREATE DATABASE cashflow;
\c cashflow
CREATE SCHEMA transactions;
CREATE SCHEMA balance;

CREATE USER app_transactions WITH PASSWORD '***';
CREATE USER app_balance      WITH PASSWORD '***';

GRANT USAGE, CREATE ON SCHEMA transactions TO app_transactions;
GRANT USAGE, CREATE ON SCHEMA balance      TO app_balance;
REVOKE ALL ON SCHEMA balance      FROM app_transactions;
REVOKE ALL ON SCHEMA transactions FROM app_balance;
```

A connection string de cada serviço aponta para o mesmo host/database, mas com usuário diferente — o PostgreSQL aplica o isolamento via permissões. Script completo: [`../../infra/postgres/init.sql`](../../infra/postgres/init.sql).

## Trade-offs

| Ganha | Perde |
|---|---|
| Isolamento lógico real via GRANTs por usuário (não cosmético) | Compartilham cluster — em produção a separação por instância é necessária |
| Migrations independentes por schema | Backup/restore precisa ser planejado por schema |
| Roda em qualquer plataforma sem emulação | Precisa do pacote Npgsql |
| Menos cerimônia no docker-compose | — |
| Atende RNF-01 na camada lógica de dados | — |

## Alternativa descartada

**Dois databases na mesma instância** — funcionava conceitualmente, mas o isolamento prometido era cosmético (mesmo cluster compartilhado). Schemas com GRANTs oferecem isolamento equivalente com metade da cerimônia operacional. Revisão honesta: a versão anterior desta ADR caiu no anti-pattern de "decisão por intuição" — esta versão substitui o argumento simbólico por isolamento concreto.

## ADRs relacionadas

- [ADR-001](adr-001-cqrs.md) — CQRS pede separação até o storage
- [ADR-010](adr-010-dapper.md) — uso de Dapper sobre Postgres via Npgsql
- [ADR-008](adr-008-docker-compose.md) — orquestra container Postgres com `init.sql`
