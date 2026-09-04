---
name: payment-core
description: Add or change provider-neutral payment abstractions and Essensoft Paylinks Alipay/WeChat Pay adapters in MSA.PaymentCore.
---

# Payment provider core

Keep this library free of ASP.NET endpoints, EF entities, RabbitMQ, and order workflow policy. Expose provider-neutral gateways and notification verification results; contain all Essensoft Paylinks SDK types inside adapters.

Use stable merchant trade numbers for provider idempotency. Verify callback signatures with the SDK and return merchant trade number, provider transaction id, status, amount, and currency. Do not treat payment creation or redirect as proof of payment.

Read Alipay and WeChat Pay keys, merchant identifiers, endpoints, and API v3 secrets from secure external configuration. Never log them or callback signatures.
