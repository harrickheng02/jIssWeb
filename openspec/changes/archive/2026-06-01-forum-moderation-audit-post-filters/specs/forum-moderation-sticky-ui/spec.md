## MODIFIED Requirements

### Requirement: Post detail exposes moderation audit history panel

The frontend SHALL provide a panel on the post detail view that loads and displays moderation audit items for the post. The panel SHALL allow moderators and admins to filter audit items by moderation action type and by an occurred-at time range, and SHALL paginate results when totalCount exceeds pageSize. Changing filters SHALL reset pagination to page 1 and re-fetch from the backend with matching query parameters.

#### Scenario: Authorized user opens audit panel

- **WHEN** a moderator/admin opens the audit history panel for a post
- **THEN** the client SHALL call `GET /api/mod/audit?targetType=post&targetId={postId}` with pagination parameters
- **AND** the UI SHALL render each returned item including a user-facing action label, operator display name, and occurred-at timestamp

#### Scenario: User applies action filter

- **WHEN** a moderator selects a specific action type filter in the audit panel
- **THEN** the client SHALL include the corresponding `action` query parameter on the audit request
- **AND** the UI SHALL display only items returned by the filtered response

#### Scenario: User applies time range filter

- **WHEN** a moderator selects a start and end datetime in the audit panel
- **THEN** the client SHALL send `fromUtc` and `toUtc` query parameters in ISO-8601 form
- **AND** the UI SHALL reflect the filtered result set

#### Scenario: Pagination loads next page

- **WHEN** totalCount exceeds pageSize and the user navigates to page 2
- **THEN** the client SHALL request the audit endpoint with `page=2`
- **AND** the UI SHALL replace the list with the second page of items

## ADDED Requirements

### Requirement: Audit panel communicates empty and error states

The audit panel SHALL present a clear empty state when the filtered query returns zero items, and SHALL present a user-visible error when the audit request fails, without clearing unrelated governance controls on the post detail view.

#### Scenario: Filtered query returns no rows

- **WHEN** the audit API returns success with zero items for the current filters
- **THEN** the UI SHALL show an empty-state message indicating no matching records

#### Scenario: Audit request fails

- **WHEN** the audit API returns a non-success envelope or network error
- **THEN** the UI SHALL show an error message and SHALL allow retry
