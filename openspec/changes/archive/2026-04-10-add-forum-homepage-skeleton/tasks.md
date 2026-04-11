## 1. Route split

- [x] 1.1 Move the current unified auth page into a dedicated routed view for `/auth`
- [x] 1.2 Update router configuration so `/` renders the forum homepage and protected routes redirect unauthenticated users to `/auth`

## 2. Forum homepage shell

- [x] 2.1 Create the homepage header, left sidebar, center feed, right panel (hot/tags/announcement; no duplicate user identity card), and footer skeleton with static placeholder data
- [x] 2.2 Render post summary cards with title, excerpt, author/time, tags, and counters for likes, comments, and views
- [x] 2.3 Add homepage login-state branches for post entry and header user area (avatar menu) using the existing auth store

## 3. Responsive polish and link fixes

- [x] 3.1 Add desktop, tablet, and mobile layout behavior for the forum homepage shell
- [x] 3.2 Update in-app navigation labels and links so authentication-related actions point to `/auth` instead of treating the root route as the login page

## 4. Verification

- [x] 4.1 Build the frontend and fix any issues introduced by the homepage split
- [x] 4.2 Mark completed OpenSpec tasks to reflect the implemented MVP

## 5. Main spec sync

- [x] 5.1 Merge `specs/forum-homepage-shell` delta into `openspec/specs/forum-homepage-shell/spec.md`
- [x] 5.2 Merge `specs/frontend-app-shell` delta additions into `openspec/specs/frontend-app-shell/spec.md`
