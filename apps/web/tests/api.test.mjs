import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

async function sourceUrl(path, imports = {}) {
  let source = await readFile(new URL(path, import.meta.url), 'utf8');
  for (const [specifier, url] of Object.entries(imports)) source = source.replaceAll(`'${specifier}'`, `'${url}'`);
  const { outputText } = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } });
  return `data:text/javascript;base64,${Buffer.from(outputText + `\n// ${crypto.randomUUID()}`).toString('base64')}`;
}
async function sourceModule(path) { return import(await sourceUrl(path)); }
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

test('auth POST sends the HttpOnly-cookie transport and CSRF marker without an old bearer token', async t => {
  const { apiRequest, configureApiClient } = await sourceModule('../src/lib/api.ts');
  configureApiClient({ getAccessToken: () => 'previous-token', onUnauthorized() {} });
  const requests = [];
  t.mock.method(globalThis, 'fetch', async (url, init) => { requests.push({ url, init }); return json({ ok: true }); });
  for (const path of ['login', 'register', 'refresh', 'logout']) await apiRequest(`/api/auth/${path}`, { method: 'POST' });
  for (const { init } of requests) {
    assert.equal(init.credentials, 'include');
    assert.equal(init.headers.get('X-LabLend-CSRF'), '1');
    assert.equal(init.headers.has('Authorization'), false);
  }
});

test('concurrent expired requests renew once and retry with the new JWT', async t => {
  const { apiRequest, configureApiClient } = await sourceModule('../src/lib/api.ts');
  let token = 'expired'; let refreshes = 0; let clears = 0;
  configureApiClient({ getAccessToken: () => token, onUnauthorized() { clears++; }, refreshAccessToken: async () => {
    refreshes++; await new Promise(resolve => setTimeout(resolve, 15)); token = 'renewed';
  } });
  t.mock.method(globalThis, 'fetch', async (_url, init) => init.headers.get('Authorization') === 'Bearer expired'
    ? json({ code: 'UNAUTHENTICATED' }, 401) : json({ authorized: true }));
  const results = await Promise.all([apiRequest('/api/loans/my'), apiRequest('/api/notifications')]);
  assert.deepEqual(results, [{ authorized: true }, { authorized: true }]);
  assert.equal(refreshes, 1); assert.equal(clears, 0);
});

test('failed cookie refresh clears the session without a retry loop', async t => {
  const { apiRequest, configureApiClient, ApiError } = await sourceModule('../src/lib/api.ts');
  let calls = 0; let refreshes = 0; let clears = 0;
  configureApiClient({ getAccessToken: () => 'expired', onUnauthorized() { clears++; }, refreshAccessToken: async () => {
    refreshes++; throw new ApiError('Expired session', 401, 'UNAUTHENTICATED');
  } });
  t.mock.method(globalThis, 'fetch', async () => { calls++; return json({}, 401); });
  await assert.rejects(apiRequest('/api/loans/my'), error => error.status === 401);
  assert.equal(calls, 1); assert.equal(refreshes, 1); assert.equal(clears, 1);
});

test('invalid login does not renew a session or clear a different existing session', async t => {
  const { apiRequest, configureApiClient } = await sourceModule('../src/lib/api.ts');
  let refreshes = 0; let clears = 0;
  configureApiClient({ getAccessToken: () => null, onUnauthorized() { clears++; }, refreshAccessToken: async () => { refreshes++; } });
  t.mock.method(globalThis, 'fetch', async () => json({ title: 'Invalid credentials', code: 'UNAUTHENTICATED' }, 401));
  await assert.rejects(apiRequest('/api/auth/login', { method: 'POST' }), error => error.code === 'UNAUTHENTICATED');
  assert.equal(refreshes, 0); assert.equal(clears, 0);
});

test('a late response from a previous login cannot retry or clear the current account', async t => {
  const { apiRequest, configureApiClient } = await sourceModule('../src/lib/api.ts');
  let version = 1; let clears = 0; let refreshes = 0;
  configureApiClient({ getAccessToken: () => 'token', getSessionVersion: () => version,
    onUnauthorized() { clears++; }, refreshAccessToken: async () => { refreshes++; } });
  t.mock.method(globalThis, 'fetch', async () => { version++; return json({}, 401); });
  await assert.rejects(apiRequest('/api/users/me'), error => error.code === 'SESSION_CHANGED');
  assert.equal(clears, 0); assert.equal(refreshes, 0);
});

for (const outcome of ['success', 'failure']) test(`an account switch during refresh ${outcome} cannot retry as or clear the new account`, async t => {
  const { apiRequest, configureApiClient, ApiError } = await sourceModule('../src/lib/api.ts');
  let version = 1; let clears = 0; let calls = 0;
  let beginRefresh, finishRefresh;
  const started = new Promise(resolve => { beginRefresh = resolve; });
  const gate = new Promise(resolve => { finishRefresh = resolve; });
  configureApiClient({ getAccessToken: () => version === 1 ? 'old-account' : 'new-account',
    getSessionVersion: () => version, onUnauthorized() { clears++; }, refreshAccessToken: async () => {
      beginRefresh(); await gate;
      if (outcome === 'failure') throw new ApiError('Old refresh expired', 401, 'UNAUTHENTICATED');
    } });
  t.mock.method(globalThis, 'fetch', async () => { calls++; return json({}, 401); });
  const request = assert.rejects(apiRequest('/api/inventory/assets'), error => error.code === 'SESSION_CHANGED');
  await started; version++; finishRefresh(); await request;
  assert.equal(clears, 0); assert.equal(calls, 1);
});

test('403 preserves the current session and ProblemDetails', async t => {
  const { apiRequest, configureApiClient } = await sourceModule('../src/lib/api.ts');
  let clears = 0;
  configureApiClient({ getAccessToken: () => 'student', onUnauthorized() { clears++; } });
  t.mock.method(globalThis, 'fetch', async () => json({ code: 'FORBIDDEN', correlationId: 'merge-check', title: 'Forbidden' }, 403));
  await assert.rejects(apiRequest('/api/users'), error => error.status === 403 && error.correlationId === 'merge-check');
  assert.equal(clears, 0);
});

test('Identity roles[] maps to the routed UI and unknown/inactive roles fail closed', async () => {
  const { toCurrentUser } = await sourceModule('../src/auth/auth-user.ts');
  const user = { id: crypto.randomUUID(), email: 'student@example.test', displayName: 'Student', isActive: true, roles: ['STUDENT'] };
  assert.equal(toCurrentUser(user).role, 'STUDENT');
  assert.equal(toCurrentUser({ ...user, roles: ['LAB_STAFF'] }).role, 'LAB_STAFF');
  assert.equal(toCurrentUser({ ...user, roles: ['STUDENT', 'ADMIN'] }).role, 'ADMIN');
  assert.throws(() => toCurrentUser({ ...user, roles: ['UNKNOWN'] }));
  assert.throws(() => toCurrentUser({ ...user, isActive: false }));
});

test('Staff StockMovements uses the shared memory JWT and refreshes an expired session', async t => {
  const url = await sourceUrl('../src/lib/api.ts');
  const { configureApiClient } = await import(url);
  const { api } = await import(await sourceUrl('../src/features/staff/api.ts', {
    '../../lib/api': url, react: import.meta.resolve('react'),
  }));
  let token = 'expired'; let refreshes = 0;
  configureApiClient({ getAccessToken: () => token, onUnauthorized() { assert.fail('session cleared'); },
    refreshAccessToken: async () => { token = 'real-staff'; refreshes++; } });
  t.mock.method(globalThis, 'fetch', async (path, init) => {
    assert.equal(path, '/api/inventory/assets/asset-id/movements');
    assert.equal(init.credentials, 'include');
    return init.headers.get('Authorization') === 'Bearer expired'
      ? json({}, 401) : json([{ toStatus: 'MAINTENANCE' }]);
  });
  assert.deepEqual(await api('/api/inventory/assets/asset-id/movements'), [{ toStatus: 'MAINTENANCE' }]);
  assert.equal(refreshes, 1);
});

test('Staff mutations preserve validation, forbidden and conflict errors without clearing the session', async t => {
  const url = await sourceUrl('../src/lib/api.ts');
  const { configureApiClient } = await import(url);
  const { api } = await import(await sourceUrl('../src/features/staff/api.ts', {
    '../../lib/api': url, react: import.meta.resolve('react'),
  }));
  configureApiClient({ getAccessToken: () => 'real-staff', onUnauthorized() { assert.fail('session cleared'); } });
  let status = 400;
  t.mock.method(globalThis, 'fetch', async (_path, init) => {
    assert.equal(init.headers.get('Authorization'), 'Bearer real-staff');
    assert.deepEqual(JSON.parse(init.body), { name: 'Duplicate' });
    return json({ code: 'STAFF_ERROR', correlationId: 'staff-check', errors: { name: ['Category rejected'] } }, status);
  });
  for (status of [400, 403, 409]) await assert.rejects(api('/api/categories', 'POST', { name: 'Duplicate' }),
    error => error.status === status && error.correlationId === 'staff-check' && error.message === 'Category rejected');
});
