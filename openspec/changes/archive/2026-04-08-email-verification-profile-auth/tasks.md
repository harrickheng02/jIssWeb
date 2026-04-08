## 1. User service — data model and verification core

- [x] 1.1 Add email verification fields to user persistence (e.g. `EmailVerifiedAt` / status) and indexes; define migration or backfill strategy for existing users
- [x] 1.2 Implement signed verification token generation (HMAC/JWT) with `exp`, purpose, and user binding; single-use store with TTL in Redis or Mongo
- [x] 1.3 Change registration flow: create pending user, send email, do not issue normal access/refresh until verified (per design)
- [x] 1.4 Implement `verify-email` endpoint (server-side validation, redirect or JSON to SPA success route)
- [x] 1.5 Implement rate-limited `resend-verification` and apply rate limits to `register`/`resend` with bounded Redis keys
- [x] 1.6 Enforce verified-email check on login and refresh issuance paths; return distinct error for unverified login
- [x] 1.7 Add email provider abstraction and configuration (SMTP or cloud); dev fallback (log or file) documented in appsettings

## 2. Customer service — Profile

- [x] 2.1 Add `Profile` model and collection separate from `CustomerRecord`; unique index on `ownerUserId`
- [x] 2.2 Expose `GET/PATCH` (or `PUT`) profile routes under `api/profile` with JWT and `sub` ownership checks
- [x] 2.3 Add minimal fields: nickname, birth date, gender (optional/nullable as per design)

## 3. Frontend

- [x] 3.1 Add routes: pending verification (resend only), verification success, adjust login/register flow
- [x] 3.2 Implement remember-me: persist refresh token storage choice; on app init silent `refresh` when token present
- [x] 3.3 Guard protected views for unverified state when API exposes it; wire new user API paths via Vite proxy if needed
- [x] 3.4 Add profile API client and minimal UI to view/edit profile after login (optional if scoped to shell only)

## 4. Docker, docs, and verification

- [x] 4.1 Update `docker-compose` / env samples for mail and verification secrets where applicable
- [x] 4.2 Manual test checklist: register → email → verify → login → profile → customer CRUD
