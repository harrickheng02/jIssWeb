# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project shape

JIssWeb is a Vue 3 + .NET 8 monorepo evolving toward a forum product. The frontend is a single SPA (`frontend/`) that talks to several ASP.NET Core APIs (`backend/src/JIssWeb.*.Api`), optionally fronted by a YARP gateway (`JIssWeb.Gateway.Api`) and a BFF (`JIssWeb.Frontend.Bff`). Local MongoDB and Redis are provisioned via Docker Compose at the repo root.

The User API is the **only** JWT issuer; every other API validates tokens it minted. Forum features (posts, replies, reports, moderation, notifications) all live in `JIssWeb.Model.Api` — that is the service to look at first for any forum domain change.

## Commands

All commands assume the repo root unless noted.

| Purpose | Command |
|------|------|
| Frontend dev server | `cd frontend && npm run dev` |
| Frontend build (type-check + Vite) | `cd frontend && npm run build` |
| Frontend tests (Vitest) | `cd frontend && npm test` (watch: `npm run test:watch`) |
| Single Vitest spec | `cd frontend && npx vitest run path/to/file.test.ts` |
| Backend build (full solution) | `cd backend/src && dotnet build JIssWeb.sln` |
| Run a single API locally | `cd backend/src && dotnet run --project JIssWeb.User.Api` (substitute the service) |
| Backend tests | `cd backend && dotnet test tests/JIssWeb.Model.Api.Tests` |
| Single backend test | `dotnet test --filter "FullyQualifiedName~ForumPostsSearchTests"` |
| Local infra (Mongo + Redis) | `docker compose up -d mongo redis` |
| Full local stack | `docker compose up -d --build` |
| Install pm-sync deps | `npm run pm:ci` |
| Pull GitHub Issues → `pm-plan.yaml` | `npm run pm:pull` |
| Push `pm-plan.yaml` → GitHub Issues | `npm run pm:push` (runs a dry-run first) |
| Dry-run pm-sync | `npm run pm:dry` |

## Configuration model (read before changing ports/URLs)

- The repo root `.env` is the single source for ports, secrets, JWT keys, and proxy targets. `vite.config.ts` calls `loadEnv` against the **repo root** (not `frontend/`) and will throw at startup if any `VITE_PROXY_*` / `VITE_DEV_SERVER_PORT` is missing — so never silently delete a key from `.env.example`.
- Each `*.Api/` has a `appsettings.Local.example.json` that must be copied to `appsettings.Local.json` for connection strings, mail, and forum board IDs. Program.cs files explicitly add `appsettings.Local.json` as a config source (e.g. `JIssWeb.Model.Api/Program.cs:16`).
- `docker-compose.yml` consumes the same root `.env`. Container ports vs. host ports are separate (`*_CONTAINER_PORT` vs `*_PORT`); changing one without the other breaks the gateway/BFF wiring.

## Frontend architecture

- Routing is in `frontend/src/router/index.ts`. Three meta flags drive guards: `requiresAuth` (token check), `requiresModerate` (delegates to `useAuthStore().canModerate`), and `hideAppShell` (auth pages render without the chrome).
- All HTTP calls go through `frontend/src/api/clients.ts`. `createClient(prefix)` attaches the bearer token, and on 401 (except for `/auth/*` paths themselves) it runs a **single-flight** refresh keyed by base prefix, then retries. If you add a new API client, reuse `createClient` so the refresh contract stays consistent — don't open a fresh axios instance.
- State lives in Pinia stores under `frontend/src/stores/` (`auth`, `theme`, `legalUi`). The auth store owns token persistence and the `canModerate` derivation used by route guards.
- Vite proxies (defined in `frontend/vite.config.ts`) split traffic by prefix: `/api-user`, `/api-customer`, `/api-model`, `/api-accounting`, `/api-report` strip their prefix and hit the corresponding service directly; `/api/forum` goes straight to the model API; everything else under `/api` falls through to the gateway. Forum work usually goes via `/api/forum`.

## Forum UI rules (enforced)

`.cursor/rules/forum-ui.mdc` is authoritative for any `frontend/**/*.vue` or `frontend/**/*.css` change. The non-negotiables:

- **No hardcoded colors or spacing.** Use the CSS variables defined in `frontend/src/styles/forum-tokens.css` (`--color-primary`, `--bg-main`, `--text-primary`, `--border-color`, `--space-*`, `--radius-*`, `--font-*`). Spacing is on an 8px grid (4/8/12/16/24/32/40); arbitrary values like 13px are forbidden.
- Primary actions use `el-button type="primary"` (or `BaseButton type="primary"`). Don't hand-roll button styles.
- Avoid `::v-deep` / `:deep()` to override Element Plus internals — prefer EP slots/classes or the forum-tokens EP bridge variables.
- Post titles and summaries are clamped to **2 lines max**.
- Dark mode is driven by `html.dark[data-theme='dark']` overrides in `forum-tokens.css`.

## Backend architecture

- Solution layout: `JIssWeb.Common` (cross-cutting hosting / middleware / options), `JIssWeb.Domain` and `JIssWeb.Application` (DDD layers), `JIssWeb.Infrastructure` (Mongo, etc.), and one `*.Api` per bounded context (User, Customer, Model, Accounting, Report) plus `Gateway.Api` (YARP) and `Frontend.Bff`.
- Each API's `Program.cs` follows the same pattern: load `appsettings.Local.json`, call `builder.UseJIssWebHttpPort(<default>)`, bind option sections, register Mongo via `AddMongoInfrastructure`, register the shared API plumbing via `AddJIssWebCoreApi`, then `app.UseExceptionHandling()` + `app.UseCors()`. When adding a new service, mirror this pattern rather than reinventing it.
- Forum domain controllers (`backend/src/JIssWeb.Model.Api/Controllers/`) are split into public-facing (`ForumPostsController`, `ForumReportsController`, `ForumMeController`, `ForumNotificationsController`, `ForumAnnouncementsController`, `ForumTagsController`, `ForumConfigController`) and moderator-only (`Mod*Controller`). Moderator authorization flows through `ForumModerationAccessService`; access decisions ultimately rely on the JWT's `forumBoardIds` claim being aligned with `ForumBoardsOptions`.
- Backend tests live under `backend/tests/JIssWeb.Model.Api.Tests/` and use integration fixtures (`*IntegrationFixture.cs`) plus `JwtTestTokens.cs` for forging signed tokens. New forum endpoints should add a fixture/spec there.

## OpenSpec + pm-plan workflow

This repo uses a **spec-driven** workflow. Before non-trivial implementation:

1. Discuss with `/opsx-explore` (read-only). 
2. Open a change with `/opsx-propose` (writes `openspec/changes/<change>/proposal.md`, `tasks.md`, etc.).
3. Implement with `/opsx-apply`. A single GitHub Issue can fan out to multiple changes/PRs.
4. Self-review with `change-review`, then PR using `.github/pull_request_template.md`.
5. Archive with `/opsx-archive` once merged. Specs land in `openspec/specs/<capability>/spec.md`.

Planning lives in `scripts/github-sync/pm-plan.yaml`, mirrored to GitHub Issues. Important distinctions:

- `git pull` only updates committed files. To refresh open Issues into `pm-plan.yaml`, run **`npm run pm:pull`** — never confuse the two.
- `npm run pm:push` should run **after** the related code merges to main, so remote Issue state and main stay aligned.
- Closed/rejected entries keep their `title`, `body`, and `issue_number` with `state` flipped — don't delete them.
- `priority` uses the five Chinese tiers from `priority_definitions`. Never write `P0`/`P1`/etc. The `remote_priority` and `issue_content_classifications` contract sections are required — don't remove them. See `.cursor/rules/pm-plan.mdc` for the full contract.

## Docs

- `docs/ui/design-spec.md` — canonical design spec (the forum-ui rule is the short summary).
- `openspec/specs/<capability>/spec.md` — authoritative behavior contracts. Read the relevant spec before changing a forum endpoint.
