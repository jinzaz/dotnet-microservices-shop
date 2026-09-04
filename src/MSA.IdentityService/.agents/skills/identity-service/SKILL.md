---
name: identity-service
description: Change the OpenIddict and ASP.NET Core Identity authentication service, token endpoints, clients, scopes, users, roles, certificates, or identity persistence.
---

# Identity service

This service owns users, credentials, clients, consents, scopes, signing material, and its PostgreSQL database. Other services reference stable subject identifiers; they do not read identity tables.

Use Authorization Code with PKCE for interactive clients and Client Credentials for trusted service identities when required. Validate redirect URIs exactly. Issue only necessary claims, audiences, and scopes. Production signing/encryption certificates must come from secure external configuration; development certificates are development-only.

Keep access tokens signed but unencrypted so independently deployed APIs can validate them from discovery signing keys. Keep authorization codes and refresh tokens protected by the Identity service. Seed roles, scopes, and configured clients idempotently; never embed a bootstrap administrator password or confidential client secret in source or committed settings.

Separate authentication from authorization: the identity service issues identity and coarse scopes, while each business service evaluates its own resource policies. Never log passwords, tokens, authorization codes, client secrets, or security answers.
