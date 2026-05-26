# Diagramas C4 — CashFlow

Notação [C4 Model](https://c4model.com/) (Simon Brown) — quatro níveis de zoom progressivo. Cada nível tem **três arquivos**: a página `.md` com contexto, decisões e fluxos textuais; o `.mmd` com a fonte Mermaid (única fonte de verdade — editar aqui); e o `.png` pré-renderizado (visualizável em qualquer markdown viewer, embedado nas páginas `.md`).

| Nível | Página | Fonte Mermaid | PNG | Pergunta que responde |
| --- | --- | --- | --- | --- |
| 1. Contexto | [`c4-contexto.md`](c4-contexto.md) | [`c4-contexto.mmd`](c4-contexto.mmd) | [`c4-contexto.png`](c4-contexto.png) | Quem usa o sistema e com quais sistemas externos ele conversa? |
| 2. Containers | [`c4-containers.md`](c4-containers.md) | [`c4-containers.mmd`](c4-containers.mmd) | [`c4-containers.png`](c4-containers.png) | Quais aplicações/serviços/databases compõem o sistema e como se comunicam? |
| 3. Componentes (Transactions API) | [`c4-componentes-transactions.md`](c4-componentes-transactions.md) | [`c4-componentes-transactions.mmd`](c4-componentes-transactions.mmd) | [`c4-componentes-transactions.png`](c4-componentes-transactions.png) | Quais componentes internos formam a Transactions API? |
| 3. Componentes (Balance API) | [`c4-componentes-balance.md`](c4-componentes-balance.md) | [`c4-componentes-balance.mmd`](c4-componentes-balance.mmd) | [`c4-componentes-balance.png`](c4-componentes-balance.png) | Quais componentes internos formam a Balance API + Consumer? |

**Nível 4 (código)** não é incluído — a abertura natural para o nível 4 é o próprio código-fonte em `src/`. C4 explicitamente desencoraja diagramas de classes/sequence neste nível porque envelhecem rapidamente.

> **Linguagem nos diagramas:** narrativa em pt-br (audiência brasileira); identificadores de código, schemas e queues em en-us para casar com `src/`. Glossário em [`../../README.md`](../../README.md#glossário-de-termos).

## Visualização

Para apenas **ver**: os `.png` estão embedados nas páginas `.md` correspondentes — abra qualquer uma e a imagem aparece. Nenhuma ferramenta extra necessária.

Para **editar** o diagrama:

1. Modifique o `.mmd` (única fonte de verdade).
2. Re-gere o `.png`: `mmdc -i c4-XXX.mmd -o c4-XXX.png` (Mermaid CLI: `npm i -g @mermaid-js/mermaid-cli`).
3. Alternativas pra preview rápido sem CLI: cole o `.mmd` em [Mermaid Live Editor](https://mermaid.live/), ou abra-o no VS Code com a extensão **Markdown Preview Mermaid Support**.

## Convenções nos diagramas

- **Pessoas** (atores) — em destaque (estilo arredondado).
- **Containers** (.NET API, banco, broker) — caixas com tecnologia entre colchetes.
- **Setas** rotuladas com **protocolo + payload** (ex: `HTTPS/JSON`, `AMQP/TransactionRegistered`).
- **Cor por papel** quando aplicável (write side vs. read side vs. infraestrutura).

## Regra de edição

`.mmd` é a única fonte de verdade. O `.png` é artefato gerado — versionado no repo para que o `.md` renderize em qualquer viewer, mas nunca edite o PNG diretamente. Sempre que mudar o `.mmd`, re-gere o `.png` (passo 2 acima).
