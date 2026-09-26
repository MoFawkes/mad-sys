# Generator maintenance Worker

This Worker invokes the single transactional Supabase maintenance RPC every day
at 02:17 UTC. Configure these Worker secrets/bindings before deployment:

- `SUPABASE_URL`: the project URL.
- `SUPABASE_AUTOMATION_SECRET`: the dedicated Supabase secret/service-role key;
  never reuse or expose a desktop/mobile application key.
- `ORGANIZATION_ID`: the organization passed to the service-only RPC.

From this directory, store secrets with `wrangler secret put` and deploy with
`wrangler deploy`. A non-2xx response or a run record whose `error` is non-null
fails the Cron invocation so it is visible in Worker logs/observability. Each
request has a 60-second timeout and one transient failure is retried after one
second; authorization failures and recorded timetable errors are never retried.
