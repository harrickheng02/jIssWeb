## ADDED Requirements

### Requirement: Customer records UI entry

The frontend SHALL provide a routed view or section that performs authenticated calls to the customer service CRUD API (via the configured proxy prefix), including at least list and create (or equivalent minimal flow) to validate the login-to-customer pipeline.

#### Scenario: Authenticated customer list request

- **WHEN** a logged-in user opens the customer records view
- **THEN** the application SHALL request the customer list endpoint with `Authorization: Bearer` when a token is present

#### Scenario: Unauthenticated user cannot load protected customer data

- **WHEN** no token is present and the user attempts the same protected action
- **THEN** the UI SHALL avoid sending the request or SHALL handle 401 without exposing other users' data
