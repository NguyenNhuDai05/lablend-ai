import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

async function moduleUrl(path, imports = {}) {
  let source = await readFile(new URL(path, import.meta.url), 'utf8');
  for (const [name, url] of Object.entries(imports)) source = source.replaceAll(`'${name}'`, `'${url}'`);
  return `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText + `\n// ${crypto.randomUUID()}`).toString('base64')}`;
}
async function setup() {
  const apiUrl = await moduleUrl('../src/lib/api.ts'); const api = await import(apiUrl);
  api.configureApiClient({ getAccessToken: () => 'incident-student-jwt', onUnauthorized() {} });
  const incidents = await import(await moduleUrl('../src/features/incidents/api.ts', { '../../lib/api': apiUrl }));
  return { ...api, ...incidents };
}
test('report and exchange preserve durable keys and send no client ownership or inventory-state fields', async () => {
  const { incidentApi } = await setup(); const requests = []; const original = globalThis.fetch;
  globalThis.fetch = async (url, init) => { requests.push({ url, init }); return new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }); };
  try {
    const key = crypto.randomUUID(); const body = { loanId: crypto.randomUUID(), loanItemId: crypto.randomUUID(), problemCategory: 'POWER_FAILURE', description: 'Không lên nguồn khi đang thí nghiệm', replacementRequested: true };
    await incidentApi.report(body, key); await incidentApi.report(body, key);
    await incidentApi.command('incident-id', 'replace', { confirmHandover: true }, key);
    for (const { init } of requests) { assert.equal(init.headers.get('Idempotency-Key'), key); assert.equal(init.headers.get('Authorization'), 'Bearer incident-student-jwt'); assert.equal(init.credentials, 'include'); }
    assert.deepEqual(JSON.parse(requests[0].init.body), body); assert.deepEqual(JSON.parse(requests[2].init.body), { confirmHandover: true });
    assert.equal(requests[2].url, '/api/incidents/incident-id/replace');
  } finally { globalThis.fetch = original; }
});
test('private evidence uses the shared JWT refresh path and returns a real blob without putting a token in the URL', async () => {
  const { incidentApi, configureApiClient } = await setup(); let token = 'expired', refreshed = 0; const requests = []; const original = globalThis.fetch;
  configureApiClient({ getAccessToken: () => token, onUnauthorized() { throw new Error('unexpected logout'); }, refreshAccessToken: async () => { token = 'renewed'; refreshed++; } });
  globalThis.fetch = async (url, init) => { requests.push({ url, token: init.headers.get('Authorization') }); return requests.length === 1 ? new Response('', { status: 401 }) : new Response(new Uint8Array([137, 80, 78, 71]), { headers: { 'Content-Type': 'image/png' } }); };
  try {
    const blob = await incidentApi.image('a', 'b'); assert.equal(blob.type, 'image/png'); assert.equal(blob.size, 4); assert.equal(refreshed, 1);
    assert.deepEqual(requests.map(r => r.token), ['Bearer expired', 'Bearer renewed']); assert.ok(requests.every(r => r.url === '/api/incidents/a/evidence/b'));
  } finally { globalThis.fetch = original; }
});
test('optional image limits reject oversized, unsupported and too many files before upload', async () => {
  const { validateImages } = await setup(); const image = { type: 'image/png', size: 1024 };
  assert.doesNotThrow(() => validateImages([])); assert.doesNotThrow(() => validateImages([image, image, image]));
  assert.throws(() => validateImages([image, image, image, image])); assert.throws(() => validateImages([{ ...image, size: 5 * 1024 * 1024 + 1 }]));
  assert.throws(() => validateImages([{ type: 'image/svg+xml', size: 1024 }]));
});
