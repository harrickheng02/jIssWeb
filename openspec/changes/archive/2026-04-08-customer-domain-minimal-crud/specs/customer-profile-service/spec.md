## ADDED Requirements

### Requirement: Protected customer-domain business endpoints

The customer profile service SHALL expose at least one business route group under the `api` prefix (for example `api/customers`) for customer record CRUD, in addition to health and any skeleton placeholders; these endpoints SHALL require JWT bearer authentication and SHALL enforce per-owner data access using `sub` as defined in `customer-record-crud`.

#### Scenario: Unauthenticated CRUD request rejected

- **WHEN** a client calls a customer CRUD endpoint without a Bearer token
- **THEN** the response SHALL be HTTP 401

### Requirement: No token issuance in customer service

The customer profile service SHALL NOT add token issuance endpoints; authentication continues to originate from the user service.

#### Scenario: No login endpoint on customer service

- **WHEN** the customer service is deployed
- **THEN** it SHALL NOT expose `/api/auth/login` or equivalent issuance routes as part of this change
