---
name: api-gateway
description: Configure or change the YARP API gateway, including routes, load balancing, canary traffic, rate limiting, resilience, logging, and token authentication.
---

# API gateway

The gateway authenticates access tokens but does not decide business authorization. Forward identity claims and correlation/trace context; each downstream service enforces its own policies.

Use YARP configuration for route matching, transforms, destination health, and load balancing. Canary rules must be deterministic and observable, with an explicit fallback. Apply rate limits by meaningful partitions such as client or subject rather than one process-wide key when identity is available.

Set timeouts and circuit breakers around proxy forwarding, with values configurable per route or cluster. Never retry unsafe requests unless idempotency is guaranteed. Emit structured logs and OpenTelemetry spans without tokens, credentials, or personal data.

Keep secrets and environment-specific destination addresses outside committed `appsettings.json`.
