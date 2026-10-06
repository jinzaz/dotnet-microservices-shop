---
name: msa-bff
description: Implement or change the repository's browser-facing MSA.BFF.Web and MSA.BFF.BM authentication, session, YARP proxy, or BM permission behavior.
---

# BFF conventions

- Reuse BFF authentication, server-side session, token refresh, CSRF, and YARP code through `MSA.BFF.Common`; keep business-manager PBAC code in `MSA.BFF.BM`.
- Use OpenIddict Authorization Code with PKCE. Tokens stay in the encrypted server-side session and never reach browser JavaScript.
- BFF-owned endpoints start with `/bff`. Only gateway pass-through endpoints start with `/api`; forward their path unchanged to the API gateway.
- OIDC callbacks are `/bff/callback/login` and `/bff/callback/logout`; Identity `RedirectUris` / `PostLogoutRedirectUris` must use exactly these paths.
- Strip inbound `Cookie` and `Authorization` headers before proxying, then attach the current session's access token.
- Require CSRF validation for every unsafe browser request except OpenID Connect callbacks.
- BM proxy access is deny-by-default: a `/api` route must match a persisted route-permission entry and an explicit allow policy. Any matching deny policy wins.
- Permission codes use `{module}.{action}`. Actions are `query`, `create`, `edit`, and `delete`; route mappings decide the action for non-CRUD HTTP endpoints.
- Treat the Identity `sub` claim as the only link to `bm_users`; BM users, roles, assignments, policies, and permissions live entirely in the BM database and never depend on Identity roles.
- Do not reinterpret or redesign Identity scopes in PBAC. After BM authorization succeeds, forward the session access token unchanged to the API gateway.
- Allow only configured bootstrap user IDs to initialize `/bff/pbac/**`; ordinary proxied requests always pass through the persisted PBAC policy check.
- Keep BFF-owned tables in the BFF's PostgreSQL schema. Do not move PBAC data into Identity or a business service.
- For RustFS/S3-compatible media, let the BM BFF issue short-lived, purpose-scoped pre-signed URLs and let browsers upload directly. Store permanent public URLs for catalog media; store stable keys for private objects and sign reads on demand. Keep the adapter local to the BM BFF until a second server-side consumer justifies extracting a dedicated infrastructure package.
- Load OIDC client and body-encryption keys from the certificate store (`AddMsaCertificates` + `UseMsaCertificates`); keep the environment-variable pfx only as the fallback when the store has no active certificate. Body-encryption requests carry the server key id in `X-Msa-Kid`; on `X-Msa-Kid-Unknown` the browser refetches `/bff/security/public-key` and retries once. DataProtection keys persist in the certificate store, never in the container.
