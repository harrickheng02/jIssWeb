## MODIFIED Requirements

### Requirement: Frontend-oriented backend entry

The system SHALL provide a dedicated ASP.NET Core BFF service for frontend-facing API composition, separating frontend response shaping from the underlying domain services. The BFF SHALL be accessible via the Gateway's existing YARP route `/api/bff/**` → `frontend-bff` cluster.

#### Scenario: Frontend calls unified BFF endpoint

- **WHEN** the SPA needs data that belongs to a frontend use case (authentication lifecycle or page-level data aggregation)
- **THEN** the request SHALL be routed through the Gateway to the BFF service and handled without requiring the browser to compose multiple domain-service calls directly

#### Scenario: Gateway routes BFF traffic correctly

- **WHEN** a request path matches `/api/bff/{**catch-all}`
- **THEN** the Gateway YARP `bff-route` SHALL forward it to the `frontend-bff` cluster; no additional Gateway configuration is needed for new BFF endpoints

---

### Requirement: BFF does not replace gateway routing

The BFF SHALL serve frontend-specific orchestration and response shaping only; it SHALL NOT become the generic infrastructure router for all inter-service traffic.

#### Scenario: Generic service routing stays in gateway

- **WHEN** a route only requires direct service forwarding without frontend composition
- **THEN** that responsibility SHALL remain with the gateway layer rather than being moved into the BFF

#### Scenario: Forum mutation requests bypass BFF

- **WHEN** the frontend performs mutation operations (create post, like, reply, report, moderation actions)
- **THEN** those requests SHALL continue to be sent directly to the downstream service via Gateway; they SHALL NOT be proxied through the BFF

---

### Requirement: BFF aggregates or reshapes responses

The BFF SHALL be able to call one or more downstream services and return a frontend-oriented contract that reduces direct coupling between the SPA and backend topology. Downstream calls SHALL be made concurrently where possible using `Task.WhenAll`.

#### Scenario: BFF returns frontend-shaped payload

- **WHEN** a frontend page needs a tailored response composed from downstream APIs
- **THEN** the BFF SHALL be allowed to aggregate or reshape the payload before returning it to the SPA

#### Scenario: Partial downstream failure results in degraded but valid response

- **WHEN** one or more downstream services fail during a BFF aggregation request
- **THEN** the BFF SHALL return a degraded response with available data, setting failed fields to null/empty, and SHALL include a `warnings` array in the response indicating which data sources were unavailable; the HTTP status SHALL remain 200 for partial failures

## ADDED Requirements

### Requirement: BFF 对所有变更端点施加来源校验

BFF SHALL 要求所有非只读端点（Cookie Session 鉴权端点）的请求携带 `X-BFF-Source: web` 自定义 Header 作为 CSRF 防护第一道防线，并将此校验与 `SameSite=Strict` Cookie 属性组合使用。

#### Scenario: 缺少来源 Header 时拒绝变更请求

- **WHEN** 任意 BFF 变更端点收到不含 `X-BFF-Source: web` Header 的请求
- **THEN** BFF SHALL 返回 HTTP 400，记录警告日志，不执行任何下游调用
