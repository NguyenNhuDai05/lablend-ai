// Browser contract tests: every API response below is a labelled test fixture; no live Gemini call.
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createServer } from 'vite';
import { chromium } from 'playwright';

const server = await createServer({ configFile: resolve('vite.config.ts'), define: { 'import.meta.env.VITE_API_BASE_URL': JSON.stringify('') }, server: { host: '127.0.0.1', port: 0 } });
await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`;
const evidence = resolve('../../.tmp/recommendation-browser'); await mkdir(evidence, { recursive: true });
const ids = ['e0000000-0000-0000-0000-000000000001', 'e0000000-0000-0000-0000-000000000002'];
const categoryId = 'c0000000-0000-0000-0000-000000000001', sessionId = 'd0000000-0000-0000-0000-000000000001';
const purpose = 'Tôi muốn đo tín hiệu trong thí nghiệm điện tử cơ bản';
const types = ids.map((id, i) => ({ id, categoryId, categoryName: 'Test electronics', name: `Test kit ${i + 1}`, description: 'Catalog test fixture', specifications: 'Real contract field', tags: [], imageUrl: null, isActive: true }));
const result = { id: sessionId, purpose, constraints: [], categoryId: null, createdAt: new Date().toISOString(), modelUsed: 'mock-browser-only', availabilityCheckedAt: new Date().toISOString(), recommendations: types.map((t, i) => ({ equipmentTypeId: t.id, equipmentName: t.name, categoryName: t.categoryName, description: t.description, specifications: t.specifications, imageUrl: null, isActive: true, rank: i + 1, reason: '<img src=x onerror=alert(1)> — text only', usageNotes: 'Test guidance', available: 3, total: 5 })) };
let browser; const errors = []; let role = 'STUDENT', failure = null, release, loanPosts = [], postPending = false, stockAvailable = 3;
try {
  browser = await chromium.launch({ headless: true, ...(process.env.LABLEND_BROWSER_CHANNEL ? { channel: process.env.LABLEND_BROWSER_CHANNEL } : {}) });
  const context = await browser.newContext({ viewport: { width: 1365, height: 950 } });
  await context.route('**/api/**', async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/auth/refresh') return json({ accessToken: 'browser-test-token', user: { id: sessionId, email: 'fixture@example.test', displayName: 'Test Student', isActive: true, roles: [role] } });
    if (path === '/api/notifications/unread-count') return json({ count: 0 });
    if (path === '/api/categories') return json([{ id: categoryId, name: 'Test electronics', isActive: true }]);
    if (path === '/api/equipment-types') return json({ items: types, total: 2, page: 1, pageSize: 20 });
    if (path.startsWith('/api/equipment-types/')) return json(types.find(t => path.endsWith(t.id)));
    if (path === '/api/inventory/availability') return json(types.map(t => ({ equipmentTypeId: t.id, available: stockAvailable, total: 5 })));
    if (path === '/api/recommendations' && request.method() === 'POST') {
      postPending = true; await new Promise(r => { release = r; }); postPending = false;
      if (failure) return json({ code: failure, correlationId: 'browser-fixture', title: 'Provider failure fixture' }, failure === 'RATE_LIMITED' ? 429 : 503);
      return json(result, 201);
    }
    if (path === '/api/recommendations/history') return json({ items: [{ ...result, itemCount: 2 }], total: 1, page: 1, pageSize: 10 });
    if (path === `/api/recommendations/${sessionId}`) return json(result);
    if (path === '/api/loans' && request.method() === 'POST') { loanPosts.push({ body: request.postDataJSON(), key: request.headers()['idempotency-key'] }); return json({ code: 'SERVICE_UNAVAILABLE', title: 'Deliberate test failure after confirming payload' }, 503); }
    return json({ title: `Unhandled test fixture ${path}` }, 404);
  });
  const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message));
  await page.goto(origin + '/recommendations'); await page.getByRole('heading', { name: 'AI tư vấn thiết bị phòng thí nghiệm' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).isDisabled(), true);
  await page.getByLabel('Bạn muốn thực hiện dự án gì?').fill(purpose);
  await page.screenshot({ path: resolve(evidence, 'desktop-form.png'), fullPage: true });
  await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).click();
  await page.getByRole('status').filter({ hasText: 'Đang đọc danh mục' }).waitFor(); assert.equal(postPending, true); release();
  await page.waitForURL(`**/recommendations/${sessionId}`); await page.getByRole('heading', { name: 'Test kit 1', exact: true }).waitFor();
  assert.equal(await page.locator('.ai-result').count(), 2); assert.equal(await page.locator('.ai-result img').count(), 0);
  await page.getByRole('checkbox', { name: 'Chọn thiết bị Test kit 1', exact: true }).check();
  assert.equal(await page.getByRole('checkbox', { checked: true }).count(), 1);
  await page.getByRole('checkbox', { name: 'Chọn thiết bị Test kit 2', exact: true }).check();
  assert.equal(await page.getByRole('checkbox', { checked: true }).count(), 2);
  await page.screenshot({ path: resolve(evidence, 'desktop-results.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'Mobile has horizontal overflow');
  await page.screenshot({ path: resolve(evidence, 'mobile-results.png'), fullPage: true });
  await page.getByRole('button', { name: 'Tiếp tục tạo phiếu mượn' }).click(); await page.waitForURL('**/loans/new');
  assert.equal(await page.getByLabel('Mục đích mượn').inputValue(), purpose); assert.equal(await page.locator('.selection-row').count(), 2); assert.equal(loanPosts.length, 0);
  const tomorrow = new Date(Date.now() + 86400000); const local = new Date(tomorrow.getTime() - tomorrow.getTimezoneOffset() * 60000).toISOString().slice(0, 19);
  await page.getByLabel('Hạn trả dự kiến').fill(local); await page.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click();
  await page.getByRole('dialog').waitFor(); assert.equal(loanPosts.length, 0);
  await page.getByRole('button', { name: 'Gửi yêu cầu mượn', exact: true }).click();
  await page.getByRole('dialog').getByRole('alert').waitFor(); assert.equal(loanPosts.length, 1); assert.ok(loanPosts[0].key);
  assert.deepEqual(loanPosts[0].body.items, ids.map(equipmentTypeId => ({ equipmentTypeId })));
  // AI selections are revalidated, while the pre-existing Catalog request flow remains available at zero stock.
  stockAvailable = 0;
  await page.getByRole('button', { name: 'Quay lại chỉnh sửa', exact: true }).click();
  await page.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click();
  await page.getByRole('alert').filter({ hasText: 'Tồn kho của thiết bị được AI gợi ý' }).waitFor();
  assert.equal(await page.getByRole('dialog').count(), 0); assert.equal(loanPosts.length, 1);
  await page.goto(origin + '/loans/new?equipmentTypeId=' + ids[0]);
  await page.getByLabel('Mục đích mượn').fill(purpose); await page.getByLabel('Hạn trả dự kiến').fill(local);
  await page.getByText('Tồn kho hiện tại chưa đủ.', { exact: false }).waitFor();
  assert.equal(await page.getByText('Đã điền từ tư vấn AI.', { exact: false }).count(), 0);
  await page.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click(); await page.getByRole('dialog').waitFor();
  stockAvailable = 3;
  await page.goto(origin + '/recommendations/history'); await page.getByRole('link', { name: 'Xem gợi ý và tồn kho hiện tại' }).waitFor();
  for (const code of ['AI_UNAVAILABLE', 'RATE_LIMITED']) {
    failure = code; await page.goto(origin + '/recommendations'); await page.getByLabel('Bạn muốn thực hiện dự án gì?').fill(purpose);
    await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).click(); await page.getByRole('status').filter({ hasText: 'Đang đọc danh mục' }).waitFor(); release();
    await page.getByRole('alert').waitFor(); assert.equal(await page.getByRole('link', { name: 'Xem danh mục' }).count(), 1); assert.equal(await page.locator('.ai-result').count(), 0);
  }
  await page.screenshot({ path: resolve(evidence, 'mobile-unavailable.png'), fullPage: true });
  role = 'LAB_STAFF'; await page.goto(origin + '/recommendations'); await page.getByRole('heading', { name: 'Không có quyền truy cập' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).count(), 0);
  assert.deepEqual(errors, []);
  console.log('PASS: browser mock contracts — validation, loading, safe cards, single/multiple selection, mobile, loan prefill/review/idempotency, history, unavailable/rate errors, Student role guard. No live Gemini claim.');
} finally { await browser?.close(); await server.close(); }
