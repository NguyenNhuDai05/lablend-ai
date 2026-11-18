// Opt-in live acceptance. Exactly one create request; no mocks and no provider key in this process/browser.
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { createHmac } from 'node:crypto';
import { chromium } from 'playwright';

if (!process.env.LABLEND_LIVE_MANIFEST) throw new Error('Explicit ignored isolated manifest is required.');
const config = JSON.parse(await readFile(process.env.LABLEND_LIVE_MANIFEST, 'utf8'));
assert.match(config.project, /^lablend-gemini-live-[0-9a-f]{10}$/);
assert.equal(config.maximumGenerationAttempts, 1);
const evidence = config.evidenceDirectory; await mkdir(evidence, { recursive: true });
const secrets = new Set([config.studentPassword, config.otherPassword, config.signingKey]);
const redact = value => [...secrets].filter(Boolean).reduce((text, secret) => text.replaceAll(secret, '[redacted]'), String(value));
const summary = { environment: config.project, webUrl: config.webUrl, gatewayUrl: config.gatewayUrl, mocked: false, configuredModel: config.configuredModel, generationRequests: 0, steps: {}, recommendationId: null, loanId: null };
const mark = (name, status, evidence) => { summary.steps[name] = { status, evidence }; };
const csrf = { Origin: config.webUrl, 'X-LabLend-CSRF': '1', 'Content-Type': 'application/json' };
let browser, page;
async function api(path, options = {}) { return fetch(config.gatewayUrl + path, options); }
function token(role) {
  const b64 = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const unsigned = b64({ alg: 'HS256', typ: 'JWT' }) + '.' + b64({ iss: 'lablend-ai', aud: 'lablend-web', sub: crypto.randomUUID(), role, exp: Math.floor(Date.now() / 1000) + 300 });
  return unsigned + '.' + createHmac('sha256', config.signingKey).update(unsigned).digest('base64url');
}
try {
  for (const [email, password, displayName] of [[config.studentEmail, config.studentPassword, 'Gemini Acceptance Student'], [config.otherEmail, config.otherPassword, 'Other Acceptance Student']]) {
    const response = await api('/api/auth/register', { method: 'POST', headers: csrf, body: JSON.stringify({ email, password, confirmPassword: password, displayName }) });
    assert.equal(response.status, 201, 'Register isolated test account failed (no response body logged).');
  }
  mark('testDataIsolation', 'PASS', 'Separate Docker project, databases, credentials and new test accounts; no production writes.');
  for (const role of [null, 'LAB_STAFF', 'ADMIN']) {
    const response = await api('/api/recommendations', { method: 'POST', headers: { 'Content-Type': 'application/json', ...(role ? { Authorization: 'Bearer ' + token(role) } : {}) }, body: JSON.stringify({ purpose: 'Authorization verification only, not a provider call' }) });
    assert.equal(response.status, role ? 403 : 401);
  }
  mark('authorization', 'PASS', 'Anonymous 401; JWT LAB_STAFF and ADMIN 403 before provider invocation.');
  const catalog = []; let pageNo = 1;
  for (;;) {
    const response = await api(`/api/equipment-types?page=${pageNo}&pageSize=100`); assert.equal(response.status, 200);
    const data = await response.json(); catalog.push(...data.items); if (catalog.length >= data.total) break; pageNo++;
  }
  assert.ok(catalog.length > 0); const known = new Map(catalog.map(item => [item.id, item]));
  mark('catalog', 'PASS', `${catalog.length} actual active records from isolated Catalog REST/PostgreSQL, not mock responses.`);
  browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1365, height: 950 } }); page = await context.newPage(); page.setDefaultTimeout(25000);
  const pageErrors = []; page.on('pageerror', error => pageErrors.push(redact(error.message)));
  await context.route('**/api/recommendations', async route => {
    if (route.request().method() === 'POST') {
      summary.generationRequests++;
      if (summary.generationRequests > 1) { await route.abort(); throw new Error('Second live recommendation request blocked by acceptance budget.'); }
    }
    await route.continue(); // No response mocking: every allowed request reaches the real backend.
  });
  await page.goto(config.webUrl + '/login');
  await page.getByLabel('Email', { exact: true }).fill(config.studentEmail);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(config.studentPassword);
  const loginResponse = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  const login = await loginResponse; assert.equal(login.status(), 200); const session = await login.json(); secrets.add(session.accessToken);
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).waitFor(); mark('studentLogin', 'PASS', 'Real Identity login in the browser; JWT retained only in memory.');
  await page.goto(config.webUrl + '/recommendations'); await page.getByRole('heading', { name: 'AI tư vấn thiết bị phòng thí nghiệm', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).isDisabled(), true);
  const purpose = 'Tôi muốn khảo sát mạch lọc RC dùng điện áp thấp: tạo tín hiệu sin 1 kHz, quan sát và so sánh tín hiệu vào/ra, đo điện áp một chiều và cấp nguồn DC an toàn cho mạch.';
  await page.getByLabel('Bạn muốn thực hiện dự án gì?').fill(purpose);
  await page.getByLabel('Yêu cầu bổ sung').fill('Ưu tiên thao tác phù hợp cho người mới bắt đầu; chỉ thí nghiệm điện áp thấp.');
  await page.screenshot({ path: join(evidence, '01-student-form.png'), fullPage: true });
  mark('formValidation', 'PASS', 'Empty purpose disables submit; valid non-personal experiment description entered.');
  // Persistent exclusive marker prevents accidentally repeating generation by rerunning this fixture.
  await writeFile(join(resolve(evidence, '..'), 'generation-attempt.json'), JSON.stringify({ attemptedAt: new Date().toISOString(), maximum: 1 }), { flag: 'wx' });
  const recommendationResponse = page.waitForResponse(r => r.url().endsWith('/api/recommendations') && r.request().method() === 'POST', { timeout: 125000 });
  await page.getByRole('button', { name: 'Nhận tư vấn AI', exact: true }).click();
  const loading = await page.getByRole('button', { name: 'Gemini đang phân tích…', exact: true }).isVisible().catch(() => false);
  mark('loading', loading ? 'PASS' : 'NOT RUN', loading ? 'Observed analyzing state during the live request.' : 'Response finished too quickly to observe analyzing state.');
  const response = await recommendationResponse; summary.recommendationHttpStatus = response.status();
  const result = await response.json(); summary.correlationId = result.correlationId ?? response.headers()['x-correlation-id']?.split(',')[0].trim();
  if (response.status() !== 201) {
    mark('recommendationApi', 'FAIL', `HTTP ${response.status()}, code ${result.code ?? 'unknown'}; no automatic retry.`);
    await page.getByRole('alert').first().waitFor(); await page.screenshot({ path: join(evidence, '02-provider-error.png'), fullPage: true });
    throw new Error(`Live backend request failed: HTTP ${response.status()}, ${result.code ?? 'unknown'}. No second generation request sent.`);
  }
  summary.recommendationId = result.id; summary.recommendationCount = result.recommendations.length;
  assert.equal(result.modelUsed, config.configuredModel); assert.equal(result.purpose, purpose);
  const seen = new Set();
  for (const item of result.recommendations) {
    const c = known.get(item.equipmentTypeId); assert.ok(c && c.isActive); assert.ok(!seen.has(item.equipmentTypeId)); seen.add(item.equipmentTypeId);
    assert.equal(item.equipmentName, c.name); assert.equal(item.specifications, c.specifications); assert.ok(item.reason && item.usageNotes);
    const actual = await (await api('/api/inventory/availability?equipmentTypeIds=' + item.equipmentTypeId)).json();
    assert.equal(item.available, actual[0].available); assert.equal(item.total, actual[0].total);
  }
  mark('recommendationApi', 'PASS', `HTTP 201; ${result.recommendations.length} validated results, model ${result.modelUsed}.`);
  mark('businessResult', 'PASS', result.recommendations.length ? 'Actual Gemini choices belong to the real Catalog; metadata matches Catalog and stock matches Inventory REST.' : 'Valid empty array; no suitable equipment is a legitimate business result.');
  await page.waitForURL(`**/recommendations/${result.id}`); await page.getByRole('heading', { name: 'Thiết bị cho ý tưởng của bạn', exact: true }).waitFor();
  if (result.recommendations.length) await page.locator('.ai-result').first().waitFor();
  await page.screenshot({ path: join(evidence, '02-live-results.png'), fullPage: true });
  mark('frontendResults', 'PASS', 'Live backend result rendered by production Docker frontend, no mocked cards.');
  const history = await api('/api/recommendations/history', { headers: { Authorization: 'Bearer ' + session.accessToken } }); assert.equal(history.status, 200);
  assert.ok((await history.json()).items.some(item => item.id === result.id));
  const otherLogin = await api('/api/auth/login', { method: 'POST', headers: csrf, body: JSON.stringify({ email: config.otherEmail, password: config.otherPassword }) }); assert.equal(otherLogin.status, 200);
  const other = await otherLogin.json(); secrets.add(other.accessToken);
  assert.equal((await api('/api/recommendations/' + result.id, { headers: { Authorization: 'Bearer ' + other.accessToken } })).status, 404);
  assert.equal((await (await api('/api/recommendations/history', { headers: { Authorization: 'Bearer ' + other.accessToken } })).json()).total, 0);
  mark('historyOwnership', 'PASS', 'Owner history contains the session; separate real Student gets 404 detail and empty own history.');
  const selectable = result.recommendations.filter(item => item.isActive && item.available > 0).slice(0, 3);
  if (!selectable.length) { mark('loanWorkflow', 'BLOCKED', 'No live recommendations with available stock; no fake selection or loan created.'); }
  else {
    for (const item of selectable) await page.getByRole('checkbox', { name: 'Chọn thiết bị ' + item.equipmentName, exact: true }).check();
    mark('multiSelect', selectable.length > 1 ? 'PASS' : 'BLOCKED', `${selectable.length} actual Gemini equipment types selected.`);
    await page.getByRole('button', { name: 'Tiếp tục tạo phiếu mượn' }).click(); await page.waitForURL('**/loans/new');
    assert.equal(await page.getByLabel('Mục đích mượn').inputValue(), purpose); assert.equal(await page.locator('.selection-row').count(), selectable.length);
    const quantities = selectable.map((item, i) => i === 0 && item.available >= 2 ? 2 : 1);
    await page.getByRole('spinbutton').first().fill(String(quantities[0]));
    const due = new Date(Date.now() + 86400000); await page.getByLabel('Hạn trả dự kiến').fill(new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 19));
    let creates = 0; page.on('request', r => { if (r.method() === 'POST' && r.url().endsWith('/api/loans')) creates++; });
    await page.screenshot({ path: join(evidence, '03-prefilled-loan.png'), fullPage: true }); assert.equal(creates, 0);
    await page.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click(); await page.getByRole('dialog').waitFor(); assert.equal(creates, 0);
    await page.screenshot({ path: join(evidence, '04-review.png'), fullPage: true });
    const createdResponse = page.waitForResponse(r => r.url().endsWith('/api/loans') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Gửi yêu cầu mượn', exact: true }).click(); const created = await createdResponse; assert.equal(created.status(), 201);
    const loan = await created.json(); summary.loanId = loan.id; summary.selectedTypeIds = selectable.map(item => item.equipmentTypeId); summary.loanUnits = loan.items.length;
    assert.equal(loan.purpose, purpose); assert.equal(loan.items.length, quantities.reduce((a, b) => a + b, 0)); assert.equal(creates, 1);
    await page.waitForURL(`**/loans/${loan.id}`); await page.locator('.status-pill').filter({ hasText: 'Chờ duyệt' }).waitFor();
    await page.screenshot({ path: join(evidence, '05-created-loan.png'), fullPage: true });
    await page.goto(config.webUrl + '/loans'); await page.getByRole('link', { name: 'Phiếu ' + loan.id.slice(0, 8), exact: true }).waitFor();
    mark('loanWorkflow', 'PASS', 'Purpose/types prefilled, quantity and due date edited, explicit review before exactly one real Borrowing POST, HTTP 201, detail and Student list verified.');
  }
  await page.goto(config.webUrl + '/recommendations/history'); await page.getByRole('link', { name: 'Xem gợi ý và tồn kho hiện tại' }).first().waitFor();
  await page.screenshot({ path: join(evidence, '06-history.png'), fullPage: true }); mark('historyUi', 'PASS', 'Saved live session visible in Student history UI.');
  assert.equal(summary.generationRequests, 1); assert.deepEqual(pageErrors, []);
} catch (error) {
  summary.error = redact(error.message); process.exitCode = 1;
} finally {
  await writeFile(join(evidence, 'live-ui-results.json'), JSON.stringify(summary, null, 2));
  await browser?.close();
  console.log(JSON.stringify({ environment: summary.environment, generationRequests: summary.generationRequests, recommendationHttpStatus: summary.recommendationHttpStatus, recommendationCount: summary.recommendationCount, recommendationId: summary.recommendationId, loanId: summary.loanId, error: summary.error ?? null, evidenceDirectory: evidence }));
}
