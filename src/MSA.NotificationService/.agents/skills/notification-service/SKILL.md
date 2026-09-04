---
name: notification-service
description: Change notification templates/preferences, order event consumers, delivery workers, retries, or notification query APIs.
---

# Notification bounded context

Notification reacts to terminal business events and owns delivery records, templates, preferences, deduplication keys, and retry state. Consumers use the local Inbox and never call upstream service databases.

Delivery is at-least-once. Workers claim bounded batches with a lock id, lease expiry, and concurrency version so another instance can recover abandoned work. Use exponential retry and retain terminal failures for operations. Provider adapters should implement `INotificationSender`; the logging sender is only a local placeholder.

Restrict user feeds to the token subject unless the caller has the explicit administrative scope. Never place passwords, tokens, or payment credentials in notification bodies or logs.
