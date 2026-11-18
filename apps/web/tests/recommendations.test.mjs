import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';
async function source(path, imports = {}) {
  let text = await readFile(new URL(path, import.meta.url), 'utf8');
  for (const [name, url] of Object.entries(imports)) text = text.replaceAll(`'${name}'`, `'${url}'`);
  return `data:text/javascript;base64,${Buffer.from(ts.transpileModule(text, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText + `\n// ${crypto.randomUUID()}`).toString('base64')}`;
}
const id = '6cd85251-a692-4a42-a5e0-f04c9944c91b', second = 'e0000000-0000-0000-0000-000000000002';
test('AI form rejects short purpose and invalid constraint lengths before HTTP', async t => {
  const apiUrl = await source('../src/lib/api.ts');
  const { recommendationApi } = await import(await source('../src/features/recommendations/recommendationApi.ts', { '../../lib/api': apiUrl }));
  t.mock.method(globalThis, 'fetch', () => { throw Error('No transport expected'); });
  for (const [purpose, constraints] of [['short', []], ['x'.repeat(2001), []], ['Project description', ['x'.repeat(301)]], ['Project description', Array(9).fill('constraint')]]) assert.throws(() => recommendationApi.create(purpose, constraints));
});
test('AI create uses backend JWT transport; history and detail use owner-scoped routes', async t => {
  const apiUrl = await source('../src/lib/api.ts'); const { configureApiClient } = await import(apiUrl);
  configureApiClient({ getAccessToken: () => 'student-test-jwt', onUnauthorized() {} });
  const { recommendationApi } = await import(await source('../src/features/recommendations/recommendationApi.ts', { '../../lib/api': apiUrl }));
  const paths = []; t.mock.method(globalThis, 'fetch', async (url, init) => {
    paths.push(url); assert.equal(init.headers.get('Authorization'), 'Bearer student-test-jwt');
    if (init.method === 'POST') assert.deepEqual(JSON.parse(init.body), { purpose: 'Project description', constraints: ['Beginner'], categoryId: id });
    return new Response('{}', { status: 200 });
  });
  await recommendationApi.create(' Project description ', ['Beginner'], id); await recommendationApi.history(2); await recommendationApi.detail(id);
  assert.deepEqual(paths, ['/api/recommendations', '/api/recommendations/history?page=2&pageSize=10', `/api/recommendations/${id}`]);
});
test('AI failures keep useful state and correlation without fabricating recommendations', async () => {
  const apiUrl = await source('../src/lib/api.ts'); const { ApiError } = await import(apiUrl);
  const { recommendationError } = await import(await source('../src/features/recommendations/recommendationApi.ts', { '../../lib/api': apiUrl }));
  for (const code of ['AI_UNAVAILABLE', 'AI_INVALID_RESPONSE', 'RATE_LIMITED', 'CATALOG_UNAVAILABLE', 'INVENTORY_UNAVAILABLE']) assert.match(recommendationError(new ApiError('fallback', 503, code, 'test-correlation')), /test-correlation/);
  assert.match(recommendationError(new ApiError('disabled', 503, 'AI_UNAVAILABLE')), /Bạn vẫn có thể tìm và mượn/);
});
test('loan prefill accepts one or many validated IDs and preserves quantities and purpose', async () => {
  const { parseLoanPrefill } = await import(await source('../src/features/loans/prefill.ts'));
  for (const selections of [[{ equipmentTypeId: id, quantity: 1 }], [{ equipmentTypeId: id, quantity: 2 }, { equipmentTypeId: second, quantity: 3 }]]) {
    const result = parseLoanPrefill({ recommendationPrefill: { purpose: 'Project description', selections } }, null);
    assert.equal(result.invalid, false); assert.equal(result.fromRecommendation, true); assert.deepEqual(result.selections, selections); assert.equal(result.purpose, 'Project description');
  }
  assert.deepEqual(parseLoanPrefill(null, id).selections, [{ equipmentTypeId: id, quantity: 1 }]);
  assert.equal(parseLoanPrefill(null, id).fromRecommendation, false);
});
test('loan prefill rejects malformed, duplicate, zero IDs, fractional quantities and over 20 units', async () => {
  const { parseLoanPrefill } = await import(await source('../src/features/loans/prefill.ts'));
  const rows = [null, [], [{ equipmentTypeId: 'bad', quantity: 1 }], [{ equipmentTypeId: id, quantity: 1.2 }], [{ equipmentTypeId: id, quantity: 21 }], [{ equipmentTypeId: id, quantity: 1 }, { equipmentTypeId: id.toUpperCase(), quantity: 1 }], [{ equipmentTypeId: '00000000-0000-0000-0000-000000000000', quantity: 1 }], [{ equipmentTypeId: id, quantity: 20 }, { equipmentTypeId: second, quantity: 1 }]];
  for (const selections of rows) { const result = parseLoanPrefill({ recommendationPrefill: { purpose: 'Project description', selections } }, null); assert.equal(result.invalid, true); assert.deepEqual(result.selections, []); }
  assert.equal(parseLoanPrefill(null, 'javascript:alert(1)').invalid, true);
});
