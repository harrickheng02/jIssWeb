## ADDED Requirements

### Requirement: Frontend-oriented backend entry

The system SHALL provide a dedicated ASP.NET Core BFF service for frontend-facing API composition, separating frontend response shaping from the underlying domain services.

#### Scenario: Frontend calls unified BFF endpoint
- **WHEN** the SPA needs data that belongs to a frontend use case
- **THEN** the request SHALL be allowed to target a BFF endpoint rather than directly composing multiple domain-service calls in the browser

### Requirement: BFF does not replace gateway routing

The BFF SHALL serve frontend-specific orchestration and response shaping only; it SHALL NOT become the generic infrastructure router for all inter-service traffic.

#### Scenario: Generic service routing stays in gateway
- **WHEN** a route only requires direct service forwarding without frontend composition
- **THEN** that responsibility SHALL remain with the gateway layer rather than being moved into the BFF

### Requirement: BFF aggregates or reshapes responses

The BFF SHALL be able to call one or more downstream services and return a frontend-oriented contract that reduces direct coupling between the SPA and backend topology.

#### Scenario: BFF returns frontend-shaped payload
- **WHEN** a frontend page needs a tailored response composed from downstream APIs
- **THEN** the BFF SHALL be allowed to aggregate or reshape the payload before returning it to the SPA
