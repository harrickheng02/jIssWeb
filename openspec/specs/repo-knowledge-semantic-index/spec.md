# repo-knowledge-semantic-index Specification

## Purpose

在 `openspec/specs` 与 `openspec/changes` 下的 Markdown 语料上构建可选向量索引，提供自然语言 `search` 子命令；与默认 `graph:verify` 解耦。

## Environment variables

| Variable | Required | Default | Role |
|----------|----------|---------|------|
| `REPO_KNOWLEDGE_EMBEDDING_API_KEY` | yes for `build` / `search` | — | Bearer token for embedding HTTP API |
| `REPO_KNOWLEDGE_EMBEDDING_URL` | no | `https://api.openai.com/v1/embeddings` | Embeddings endpoint (OpenAI-compatible POST body) |
| `REPO_KNOWLEDGE_EMBEDDING_MODEL` | no | `text-embedding-3-small` | Model id sent in request body |

首版实现为 **HTTP 远程嵌入**；本地模型嵌入留作后续扩展。

## Requirements

### Requirement: Optional semantic index scope

The semantic index tooling SHALL operate only on repository paths under version control configured by implementation defaults: at minimum `openspec/specs/**/*.md`, and MAY include `openspec/changes/**/*.md` chunks. It SHALL produce a persistent index artifact under `scripts/repo-knowledge-router/data/` (exact filenames documented in implementation) that SHALL be gitignored or documented as generated-only.

#### Scenario: No network required for index layout

- **WHEN** semantic index build is not invoked
- **THEN** the repository SHALL remain valid for default `graph:build` and `graph:verify` per `repo-knowledge-router`

### Requirement: Embedding and build invocation

The semantic index build SHALL be invocable only via an explicit subcommand (e.g. `semantic-index build` or npm `graph:semantic-index`). It MAY call remote embedding APIs or load local embedding models when configured via environment variables documented in implementation. Default `graph:verify` and default `graph:refresh` SHALL NOT invoke semantic index build.

#### Scenario: Verify does not require embeddings

- **WHEN** `graph:verify` runs without semantic index artifacts present
- **THEN** it SHALL NOT fail solely due to missing semantic index files

### Requirement: Semantic search command

The tooling SHALL provide a `search` (or equivalently named) subcommand accepting a non-empty natural-language query string and SHALL print a deterministic ordered list of repo-relative source paths with scores or ranks and short excerpts or chunk ids as documented in implementation.

#### Scenario: Empty query rejected

- **WHEN** semantic search is invoked with an empty query
- **THEN** it SHALL exit non-zero and SHALL print a usage message

### Requirement: Index metadata

The index artifact SHALL record embedding model identifier and schema version sufficient for rebuild decisions after dependency upgrades.

#### Scenario: Version mismatch detectable

- **WHEN** search runs against an index built with a different documented schema version than the current tool
- **THEN** the tool SHALL exit non-zero or print an explicit rebuild instruction
