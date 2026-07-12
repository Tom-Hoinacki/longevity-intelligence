# API health endpoints

The API exposes separate liveness and readiness checks for deployment platforms.

`GET /health/live` is process liveness. It performs no dependency checks and returns HTTP 200 with the stable response `{ "status": "live" }` when the process can serve a request.

`GET /health/ready` is dependency readiness. Registered probes run sequentially, in deterministic component-name order, exactly once per request. It returns HTTP 200 when every enabled probe is healthy and HTTP 503 when any probe is unhealthy. The response contains only an overall `ready` flag and safe component names, statuses, and elapsed durations.

Postgres readiness is registered only when `Postgres:Enabled` is true. It uses the existing data source, runs a bounded `select 1`, honors cancellation, and reports only `healthy` or `unavailable`. When disabled, no data source is resolved and Postgres does not affect readiness.

Exceptions, stack traces, SQL, connection strings, credentials, paths, source content, and model content are deliberately omitted. Client cancellation propagates; dependency checks have a short local timeout and do not retry. These endpoints are intended for load balancers, orchestrators, and deployment probes. They do not replace deeper operational monitoring.
