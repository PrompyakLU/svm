// _health_check.tpl.js
//
// Bundled template. Copied to <target>/qa-automation-testing/scripts/_health_check.js.
// Called by the orchestrator before Scale Suite to verify environment readiness.
//
// Usage: node _health_check.js <base_url> [timeout_ms]
// Exit 0 = healthy, Exit 1 = unhealthy (stderr has details).

// Native global fetch() requires Node.js 18+. Fail fast with a clear message on
// older runtimes instead of crashing with "fetch is not defined".
const [major] = process.versions.node.split('.').map(Number);
if (major < 18) {
  console.error(JSON.stringify({ ok: false, error: `Node.js 18+ required (got ${process.version})` }));
  process.exit(2);
}

const BASE_URL = process.argv[2];
const TIMEOUT = Number(process.argv[3] || 10000);

if (!BASE_URL) {
  console.error('Usage: node _health_check.js <base_url> [timeout_ms]');
  process.exit(2);
}

async function check() {
  const start = Date.now();
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), TIMEOUT);

    const res = await fetch(BASE_URL, {
      method: 'GET',
      signal: controller.signal,
      redirect: 'follow',
    });
    clearTimeout(timer);

    const elapsed = Date.now() - start;

    if (res.status >= 200 && res.status < 400) {
      console.log(JSON.stringify({ ok: true, status: res.status, elapsed_ms: elapsed }));
      process.exit(0);
    } else {
      console.error(JSON.stringify({ ok: false, status: res.status, elapsed_ms: elapsed }));
      process.exit(1);
    }
  } catch (err) {
    const elapsed = Date.now() - start;
    console.error(JSON.stringify({ ok: false, error: err.message, elapsed_ms: elapsed }));
    process.exit(1);
  }
}

check();
