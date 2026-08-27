import assert from "node:assert/strict";
import test from "node:test";
import { runGeneratorMaintenance } from "../src/index.js";

const env = {
  SUPABASE_URL: "https://example.supabase.co/",
  SUPABASE_AUTOMATION_SECRET: "automation-secret",
  ORGANIZATION_ID: "00000000-0000-0000-0000-000000000001",
};

test("posts the organization to the service-only maintenance RPC", async () => {
  let request;
  const run = await runGeneratorMaintenance(env, async (url, options) => {
    request = { url, options };
    return new Response(JSON.stringify({ error: null, timetables_written: 1 }), { status: 200 });
  });

  assert.equal(request.url, "https://example.supabase.co/rest/v1/rpc/run_generator_maintenance");
  assert.equal(request.options.headers.apikey, env.SUPABASE_AUTOMATION_SECRET);
  assert.equal(request.options.headers.authorization, `Bearer ${env.SUPABASE_AUTOMATION_SECRET}`);
  assert.ok(request.options.signal instanceof AbortSignal);
  assert.deepEqual(JSON.parse(request.options.body), { p_org_id: env.ORGANIZATION_ID });
  assert.equal(run.timetables_written, 1);
});

test("fails the scheduled run when the RPC records a timetable error", async () => {
  await assert.rejects(
    runGeneratorMaintenance(env, async () =>
      new Response(JSON.stringify({ error: "Timetable abc: missing duration" }), { status: 200 })),
    /completed with errors/);
});

test("does not hide an HTTP failure", async () => {
  await assert.rejects(
    runGeneratorMaintenance(env, async () => new Response("denied", { status: 403 })),
    /403.*denied/);
});

test("retries one transient failure and then succeeds", async () => {
  let attempts = 0;
  let waits = 0;
  const run = await runGeneratorMaintenance(env, async () => {
    attempts++;
    return attempts === 1
      ? new Response("temporary", { status: 503 })
      : new Response(JSON.stringify({ error: null, timetables_written: 0 }), { status: 200 });
  }, async milliseconds => {
    assert.equal(milliseconds, 1_000);
    waits++;
  });

  assert.equal(attempts, 2);
  assert.equal(waits, 1);
  assert.equal(run.error, null);
});
