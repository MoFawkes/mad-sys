const retryableStatus = status => status === 408 || status === 429 || status >= 500;

const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

export async function runGeneratorMaintenance(env, fetchImplementation = fetch, wait = delay) {
  for (const name of ["SUPABASE_URL", "SUPABASE_AUTOMATION_SECRET", "ORGANIZATION_ID"])
    if (!env[name]) throw new Error(`Missing Worker binding: ${name}`);

  let response;
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      response = await fetchImplementation(
        `${env.SUPABASE_URL.replace(/\/$/, "")}/rest/v1/rpc/run_generator_maintenance`,
        {
          method: "POST",
          headers: {
            apikey: env.SUPABASE_AUTOMATION_SECRET,
            authorization: `Bearer ${env.SUPABASE_AUTOMATION_SECRET}`,
            "content-type": "application/json",
          },
          body: JSON.stringify({ p_org_id: env.ORGANIZATION_ID }),
          signal: AbortSignal.timeout(60_000),
        });
      if (!retryableStatus(response.status) || attempt === 1) break;
    } catch (error) {
      if (attempt === 1) throw error;
    }
    await wait(1_000);
  }

  if (!response) throw new Error("Generator maintenance RPC produced no response");
  if (!response.ok)
    throw new Error(`Generator maintenance RPC failed (${response.status}): ${await response.text()}`);
  const run = await response.json();
  if (run.error) throw new Error(`Generator maintenance completed with errors: ${run.error}`);
  return run;
}

export default {
  async scheduled(_controller, env) {
    await runGeneratorMaintenance(env);
  },
};
