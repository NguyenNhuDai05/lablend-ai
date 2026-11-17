import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

async function source(path, imports = {}) {
  let text = await readFile(new URL(path, import.meta.url), 'utf8');
  for (const [name, url] of Object.entries(imports)) text = text.replaceAll(`'${name}'`, `'${url}'`);
  return `data:text/javascript;base64,${Buffer.from(ts.transpileModule(text, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText + `\n// ${crypto.randomUUID()}`).toString('base64')}`;
}
async function modules() {
  const apiUrl = await source('../src/lib/api.ts');
  const workflowsUrl = await source('../src/lib/workflows.ts', { './api': apiUrl });
  const loanUrl = await source('../src/features/loans/api.ts', { '../../lib/api': apiUrl, '../../lib/workflows': workflowsUrl });
  return { ...await import(apiUrl), ...await import(workflowsUrl), ...await import(loanUrl), apiUrl };
}
const now = Date.parse('2026-11-16T00:00:00Z');
const type = '6cd85251-a692-4a42-a5e0-f04c9944c91b';
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

test('loan boundaries accept 1–20 physical units and normalize a timezone offset to UTC', async () => {
  const { validateLoan } = await modules();
  assert.equal(validateLoan('Laboratory project', '2026-11-17T07:00:00+07:00', [1, 19], now), '2026-11-17T00:00:00.000Z');
  assert.equal(validateLoan('a'.repeat(2000), '2026-12-16T00:00:00Z', [20], now), '2026-12-16T00:00:00.000Z');
});
test('loan validation rejects blank/oversize purposes, invalid dates and unit counts', async () => {
  const { validateLoan } = await modules();
  for (const purpose of ['short', ' '.repeat(20), 'a'.repeat(2001)]) assert.throws(() => validateLoan(purpose, '2026-11-17', [1], now));
  for (const date of ['', 'invalid', '2026-11-16T00:00:00Z', '2026-12-16T00:00:01Z']) assert.throws(() => validateLoan('Laboratory project', date, [1], now));
  for (const quantities of [[], [0], [-1], [1.5], [21], [10, 11], [NaN]]) assert.throws(() => validateLoan('Laboratory project', '2026-11-17', quantities, now));
});
test('create sends trimmed purpose, UTC and expanded items without client ownership fields', async t => {
  const { configureApiClient, loanApi } = await modules();
  configureApiClient({ getAccessToken: () => 'student', onUnauthorized() {} });
  t.mock.method(globalThis, 'fetch', async (url, init) => {
    assert.equal(url, '/api/loans'); assert.equal(init.method, 'POST');
    assert.deepEqual(JSON.parse(init.body), { purpose: 'Laboratory project', dueAt: '2026-11-17T00:00:00.000Z', items: [{ equipmentTypeId: type }, { equipmentTypeId: type }] });
    return json({ id: 'loan' }, 201);
  });
  await loanApi.create('  Laboratory project  ', '2026-11-17T07:00:00+07:00', [{ equipmentTypeId: type, quantity: 2 }], now);
});
test('invalid create fails before transport and a network failure is never automatically retried', async t => {
  const { configureApiClient, loanApi } = await modules(); let requests = 0;
  configureApiClient({ getAccessToken: () => 'student', onUnauthorized() {} });
  t.mock.method(globalThis, 'fetch', async () => { requests++; throw new TypeError('offline'); });
  assert.throws(() => loanApi.create('short', '2026-11-17', [{ equipmentTypeId: type, quantity: 1 }], now));
  assert.throws(() => loanApi.create('Laboratory project', '2026-11-17', [{ equipmentTypeId: 'invalid', quantity: 1 }], now));
  assert.equal(requests, 0);
  await assert.rejects(loanApi.create('Laboratory project', '2026-11-17', [{ equipmentTypeId: type, quantity: 1 }], now), e => e.code === 'NETWORK_ERROR');
  assert.equal(requests, 1);
});
test('double click joins one command; failed flight releases for an explicit retry', async () => {
  const { singleFlight } = await modules(); const run = singleFlight(); let calls = 0; let release;
  const gate = new Promise(resolve => { release = resolve; });
  const work = async () => { calls++; await gate; throw new Error('conflict'); };
  const first = run(work); const second = run(work); assert.equal(first, second);
  const checks = [assert.rejects(first, /conflict/), assert.rejects(second, /conflict/)]; release(); await Promise.all(checks);
  assert.equal(calls, 1); assert.equal(await run(async () => ++calls), 2);
});
test('owner cancellation and partial return preserve exact API paths and per-item condition', async t => {
  const { configureApiClient, loanApi } = await modules(); const calls = [];
  configureApiClient({ getAccessToken: () => 'staff', onUnauthorized() {} });
  t.mock.method(globalThis, 'fetch', async (url, init) => { calls.push({ url, method: init.method, body: init.body && JSON.parse(init.body) }); return json({}); });
  await loanApi.cancel('loan'); await loanApi.returns('loan', [{ loanItemId: 'item-1', condition: 'DAMAGED' }]);
  assert.deepEqual(calls, [{ url: '/api/loans/loan/cancel', method: 'POST', body: undefined }, { url: '/api/loans/loan/returns', method: 'POST', body: { items: [{ loanItemId: 'item-1', condition: 'DAMAGED' }] } }]);
});
test('cancelled reads retain AbortError so stale resource loads are ignored', async t => {
  const { apiRequest } = await modules(); const controller = new AbortController();
  t.mock.method(globalThis, 'fetch', async (_url, init) => { assert.equal(init.signal.aborted, true); throw new DOMException('aborted', 'AbortError'); });
  controller.abort(); await assert.rejects(apiRequest('/api/loans/my', { signal: controller.signal }), e => e.name === 'AbortError');
});
test('return destinations accept protected internal URLs and reject external or malformed paths', async () => {
  const { safeDestination } = await modules();
  assert.equal(safeDestination('/loans/new?equipmentTypeId=abc'), '/loans/new?equipmentTypeId=abc');
  for (const path of ['//evil.test', 'https://evil.test', '/\\evil.test', '/login', '/loanster', undefined]) assert.equal(safeDestination(path), '/');
});
test('Profile and Admin send only supported fields and preserve last-admin conflicts', async t => {
  const { configureApiClient, apiUrl } = await modules();
  const { accountApi } = await import(await source('../src/features/account/api.ts', { '../../lib/api': apiUrl }));
  configureApiClient({ getAccessToken: () => 'admin', onUnauthorized() { assert.fail('409 must preserve session'); } });
  const calls = [];
  t.mock.method(globalThis, 'fetch', async (url, init) => { calls.push({ url, method: init.method, body: JSON.parse(init.body) }); return url.endsWith('/role') ? json({ title: 'last active administrator' }, 409) : json({}); });
  await accountApi.updateProfile('  Updated Student  '); await accountApi.active('user', false);
  await assert.rejects(accountApi.role('user', 'STUDENT'), e => e.status === 409);
  assert.deepEqual(calls, [{ url: '/api/users/me', method: 'PUT', body: { displayName: 'Updated Student' } }, { url: '/api/users/user/active', method: 'PATCH', body: { isActive: false } }, { url: '/api/users/user/role', method: 'PUT', body: { role: 'STUDENT' } }]);
});
test('action errors expose safe HTTP hints and a correlation ID for recovery', async () => {
  const { errorText, ApiError } = await modules();
  for (const status of [401, 403, 404, 409, 503]) { const text = errorText(new ApiError('Server detail', status, 'ERROR', 'trace-123')); assert.match(text, /Server detail/); assert.match(text, /trace-123/); assert.ok(text.length > 40); }
});

test('an explicit resubmission preserves its draft key and payload after a lost network reply', async t => {
  const { configureApiClient, loanApi } = await modules();
  configureApiClient({ getAccessToken: () => 'student', onUnauthorized() {} });
  const calls = []; const key = crypto.randomUUID();
  t.mock.method(globalThis, 'fetch', async (_url, init) => {
    calls.push({ key: init.headers.get('Idempotency-Key'), body: init.body });
    if (calls.length === 1) throw new TypeError('lost reply');
    return json({ id: 'one-persisted-loan' }, 201);
  });
  const submit = () => loanApi.create('Laboratory project', '2026-11-17T00:00:00Z', [{ equipmentTypeId: type, quantity: 2 }], now, key);
  await assert.rejects(submit(), e => e.code === 'NETWORK_ERROR');
  assert.equal(calls.length, 1);
  assert.equal((await submit()).id, 'one-persisted-loan');
  assert.equal(calls[0].key, key); assert.deepEqual(calls[0], calls[1]);
});

test('Admin global filters and pagination are encoded together and never expose a page-only filter', async t => {
  const { configureApiClient, apiUrl } = await modules();
  const { accountApi } = await import(await source('../src/features/account/api.ts', { '../../lib/api': apiUrl }));
  configureApiClient({ getAccessToken: () => 'admin', onUnauthorized() {} });
  t.mock.method(globalThis, 'fetch', async url => {
    const parsed = new URL(url, 'http://localhost');
    assert.equal(parsed.pathname, '/api/users');
    assert.deepEqual(Object.fromEntries(parsed.searchParams), { page: '2', pageSize: '20', keyword: 'name+lab@example.test', role: 'STUDENT', isActive: 'false' });
    return json({ items: [], total: 23, page: 2, pageSize: 20 });
  });
  assert.equal((await accountApi.list(2, undefined, { keyword: 'name+lab@example.test', role: 'STUDENT', isActive: 'false' })).total, 23);
});

test('loan-related notifications use the server aggregate filter without accepting a user ID', async t => {
  const { configureApiClient, apiUrl } = await modules();
  const { notificationApi } = await import(await source('../src/features/notifications/api.ts', { '../../lib/api': apiUrl }));
  configureApiClient({ getAccessToken: () => 'student', onUnauthorized() {} });
  t.mock.method(globalThis, 'fetch', async url => {
    const params = new URL(url, 'http://localhost').searchParams;
    assert.deepEqual(Object.fromEntries(params), { page: '1', pageSize: '20', aggregateId: type });
    return json({ items: [], total: 0, page: 1, pageSize: 20 });
  });
  assert.equal((await notificationApi.list(1, 20, undefined, type)).total, 0);
});

test('persisted notification events have Vietnamese copy without inventing full-return state or replacing unknown event content', async () => {
  const { notificationCopy } = await import(await source('../src/features/notifications/copy.ts'));
  for (const eventType of ['LoanSubmitted', 'LoanApproved', 'LoanRejected', 'LoanHandedOver', 'LoanReturned', 'LoanDueSoon', 'LoanOverdue']) {
    const copy = notificationCopy({ eventType, title: 'Original event', message: 'Original message' });
    assert.ok(copy.title.length > 5); assert.ok(copy.message.length > 10);
    assert.notEqual(copy.title, 'Original event');
  }
  assert.match(notificationCopy({ eventType: 'LoanReturned', title: '', message: '' }).message, /một lần nhận trả/);
  assert.deepEqual(notificationCopy({ eventType: 'FutureEvent', title: 'Provider title', message: 'Provider detail' }), { title: 'Provider title', message: 'Provider detail' });
});
