import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import ts from 'typescript';

async function sourceModule(path) {
  const source = await readFile(new URL(path, import.meta.url), 'utf8');
  const { outputText } = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } });
  return import(`data:text/javascript;base64,${Buffer.from(outputText + `\n// ${crypto.randomUUID()}`).toString('base64')}`);
}
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
