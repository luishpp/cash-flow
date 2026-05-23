# Diagramas C4 — CashFlow

Notação [C4 Model](https://c4model.com/) (Simon Brown) — quatro níveis de zoom progressivo. Os diagramas usam **Mermaid**, que o GitHub renderiza nativamente no preview do arquivo `.md`.

| Nível | Arquivo | Pergunta que responde |
|---|---|---|
| 1. Contexto | [`c4-contexto.md`](c4-contexto.md) | Quem usa o sistema e com quais sistemas externos ele conversa? |
| 2. Containers | [`c4-containers.md`](c4-containers.md) | Quais aplicações/serviços/databases compõem o sistema e como se comunicam? |
| 3. Componentes (Transactions API) | [`c4-componentes-transactions.md`](c4-componentes-transactions.md) | Quais componentes internos formam a Transactions API? |
| 3. Componentes (Balance API) | [`c4-componentes-balance.md`](c4-componentes-balance.md) | Quais componentes internos formam a Balance API + Consumer? |

**Nível 4 (código)** não é incluído — a abertura natural para o nível 4 é o próprio código-fonte em `src/`. C4 explicitamente desencoraja diagramas de classes/sequence neste nível porque envelhecem rapidamente.

> **Linguagem nos diagramas:** narrativa em pt-br (audiência brasileira); identificadores de código, schemas e queues em en-us para casar com `src/`. Glossário em [`../../README.md`](../../README.md#glossário-de-termos).

## Convenções nos diagramas

- **Pessoas** (atores) — em destaque (estilo arredondado).
- **Containers** (.NET API, banco, broker) — caixas com tecnologia entre colchetes.
- **Setas** rotuladas com **protocolo + payload** (ex: `HTTPS/JSON`, `AMQP/TransactionRegistered`).
- **Cor por papel** quando aplicável (write side vs. read side vs. infraestrutura).

## Para editar

Mermaid é texto. Edite o conteúdo dentro do bloco ` ```mermaid ` e o GitHub re-renderiza automaticamente. Para preview local, use a extensão **Markdown Preview Mermaid Support** no VS Code.
