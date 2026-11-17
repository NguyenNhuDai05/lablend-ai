import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { chromium } from 'playwright';

const env = process.env;
// This destructive-to-fixture scenario refuses to target a shared/default Compose project.
assert.match(env.LABLEND_E2E_PROJECT ?? '', /^lablend-t20-[0-9a-f]{12}$/);
for (const key of ['LABLEND_E2E_ORIGIN', 'LABLEND_E2E_GATEWAY', 'LABLEND_E2E_ADMIN_PASSWORD', 'LABLEND_E2E_EVIDENCE']) assert.ok(env[key], `Missing ${key}`);
const origin = env.LABLEND_E2E_ORIGIN;
const gateway = env.LABLEND_E2E_GATEWAY.replace(/\/$/, '');
const evidence = env.LABLEND_E2E_EVIDENCE;
const password = 'Aa1!' + randomBytes(24).toString('hex');
const run = randomUUID();
const results = [];
let browser;
const redact = text => [password, env.LABLEND_E2E_ADMIN_PASSWORD].reduce((v, secret) => v.replaceAll(secret, '[redacted]'), String(text));
const record = (name, expected, actual) => { results.push({ name, expected, actual, status: 'PASS' }); console.log(`PASS ${name}`); };
async function request(path, { token, method = 'GET', body, expected = 200, headers = {} } = {}) {
  const response = await fetch(gateway + path, { method, headers: { 'Content-Type': 'application/json', Origin: origin, 'X-LabLend-CSRF': '1', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers }, ...(body ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(20000) });
  assert.equal(response.status, expected, `${method} ${path} expected ${expected}, got ${response.status}`);
  return response.status === 204 ? null : response.json();
}
async function eventually(check, timeout = 90000) {
  const end = Date.now() + timeout;
  while (!await check()) { if (Date.now() >= end) throw new Error('Timed out waiting for real state'); await new Promise(r => setTimeout(r, 1000)); }
}
function docker(args, input) {
  return execFileSync('docker', ['compose', '-p', env.LABLEND_E2E_PROJECT, ...args], { cwd: resolve('../..'), input, encoding: 'utf8', timeout: 60000, stdio: ['pipe', 'pipe', 'pipe'] }).trim();
}
function sql(service, query) { return docker(['exec', '-T', 'postgres', 'psql', '-U', `${service}_user`, '-d', `${service}_db`, '-At', '-v', 'ON_ERROR_STOP=1'], query); }
async function login(page, email, secret) {
  await page.goto(origin + '/login');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Mật khẩu', { exact: true }).fill(secret);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).waitFor();
}
async function screen(page, name) { await page.screenshot({ path: resolve(evidence, name + '.png'), fullPage: true }); }
async function confirmReturn(page, condition) {
  await page.getByRole('combobox', { name: /Tình trạng trả/ }).first().selectOption(condition);
  const review = page.getByRole('button', { name: 'Xem lại thiết bị nhận trả', exact: true });
  await review.click();
  await page.getByRole('button', { name: 'Xác nhận nhận trả', exact: true }).click();
}
try {
  await mkdir(evidence, { recursive: true });
  const admin = await request('/api/auth/login', { method: 'POST', body: { email: 'admin@t20.test', password: env.LABLEND_E2E_ADMIN_PASSWORD } });
  async function register(role) {
    const email = `${role.toLowerCase()}-${run}@t20.test`;
    const user = await request('/api/auth/register', { method: 'POST', expected: 201, body: { displayName: `${role} T20`, email, password, confirmPassword: password } });
    if (role === 'LAB_STAFF') await request(`/api/users/${user.id}/role`, { method: 'PUT', token: admin.accessToken, body: { role } });
    const session = await request('/api/auth/login', { method: 'POST', body: { email, password } });
    return { ...session, email };
  }
  const staff = await register('LAB_STAFF'); const student = await register('STUDENT');
  const category = await request('/api/categories', { method: 'POST', token: staff.accessToken, expected: 201, body: { name: `T20 ${run}`, description: 'Isolated clean-install scenario' } });
  const type = await request('/api/equipment-types', { method: 'POST', token: staff.accessToken, expected: 201, body: { categoryId: category.id, name: `T20 Signal ${run}`, specifications: 'Three physical units', tags: ['t20'] } });
  const assets = [];
  for (let i = 0; i < 3; i++) assets.push(await request('/api/inventory/assets', { method: 'POST', token: staff.accessToken, expected: 201, body: { equipmentTypeId: type.id, serialNumber: `T20-${run}-${i}`, location: 'T20 private lab' } }));
  assert.equal(sql('catalog', `SELECT count(*) FROM "EquipmentTypes" WHERE "Id"='${type.id}';`), '1');
  assert.equal(sql('inventory', `SELECT count(*) FROM "AssetUnits" WHERE "EquipmentTypeId"='${type.id}' AND "Status"='AVAILABLE';`), '3');
  record('Flow 3 registration', 'real Catalog type and three AVAILABLE Inventory units persisted', '1 type / 3 units');
  await request('/api/inventory/assets', { token: student.accessToken, expected: 403 });
  await request('/api/users', { token: student.accessToken, expected: 403 });
  record('RBAC', 'Student denied staff inventory and admin users', '403 / 403');
  browser = await chromium.launch({ headless: true, ...(env.LABLEND_BROWSER_CHANNEL ? { channel: env.LABLEND_BROWSER_CHANNEL } : {}) });
  const studentContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const staffContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const studentPage = await studentContext.newPage(); const staffPage = await staffContext.newPage();
  const errors = []; for (const page of [studentPage, staffPage]) page.on('pageerror', e => errors.push(e.message));
  await studentPage.goto(origin + '/catalog');
  await studentPage.getByRole('heading', { name: 'Danh mục thiết bị', exact: true }).waitFor();
  await login(studentPage, student.email, password);
  await studentPage.goto(`${origin}/loans/new?equipmentTypeId=${type.id}`);
  await studentPage.getByLabel('Mục đích mượn', { exact: true }).fill(`Clean install lab measurement ${run}`);
  const due = new Date(Date.now() + 120000);
  await studentPage.getByLabel('Hạn trả dự kiến').fill(new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 19));
  await studentPage.getByRole('spinbutton').first().fill('3');
  const review = studentPage.getByRole('button', { name: 'Xem lại yêu cầu', exact: true });
  await review.click();
  let creates = 0; studentPage.on('request', r => { if (r.method() === 'POST' && r.url().endsWith('/api/loans')) creates++; });
  await studentPage.getByRole('button', { name: 'Gửi yêu cầu mượn', exact: true }).evaluate(button => { button.click(); button.click(); });
  await studentPage.waitForURL(/\/loans\/[0-9a-f-]{36}$/);
  const loanId = new URL(studentPage.url()).pathname.split('/').at(-1); assert.match(loanId, /^[0-9a-f-]{36}$/);
  assert.equal(creates, 1);
  assert.equal(sql('borrowing', `SELECT count(*) FROM "LoanRequests" WHERE "Id"='${loanId}' AND "Status"='PENDING';`), '1');
  await screen(studentPage, 't20-student-created');
  record('Flow 1 create', 'double click creates one persisted PENDING loan', 'one POST / one loan');
  await login(staffPage, staff.email, password); await staffPage.goto(`${origin}/staff/loans/${loanId}`);
  await staffPage.getByRole('button', { name: 'Duyệt phiếu', exact: true }).click();
  await staffPage.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click();
  await staffPage.getByRole('button', { name: 'Bàn giao thiết bị', exact: true }).waitFor();
  assert.equal(sql('inventory', `SELECT count(*) FROM "AssetUnits" WHERE "EquipmentTypeId"='${type.id}' AND "Status"='RESERVED';`), '3');
  await request(`/api/loans/${loanId}/approve`, { method: 'POST', token: staff.accessToken, expected: 409 });
  const noStock = await request('/api/loans', { method: 'POST', token: student.accessToken, expected: 201, body: { purpose: 'Insufficient stock T20 failure scenario', dueAt: new Date(Date.now() + 86400000).toISOString(), items: [{ equipmentTypeId: type.id }] } });
  await request(`/api/loans/${noStock.id}/approve`, { method: 'POST', token: staff.accessToken, expected: 409 });
  assert.equal((await request(`/api/loans/${noStock.id}`, { token: student.accessToken })).status, 'APPROVAL_FAILED');
  record('Reservation failures', 'duplicate approval / insufficient stock rejected; no partial reserve', '409 / APPROVAL_FAILED');
  await staffPage.getByRole('button', { name: 'Bàn giao thiết bị', exact: true }).click();
  await staffPage.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click();
  await staffPage.getByRole('heading', { name: 'Nhận trả từng thiết bị', exact: true }).waitFor();
  assert.equal(sql('inventory', `SELECT count(*) FROM "AssetUnits" WHERE "EquipmentTypeId"='${type.id}' AND "Status"='ON_LOAN';`), '3');
  record('Flow 2 handover', 'REST -> real Inventory gRPC -> three ON_LOAN units', '3 ON_LOAN');
  async function notices() { return (await request('/api/notifications?pageSize=100', { token: student.accessToken })).items.filter(n => n.aggregateId === loanId); }
  await eventually(async () => { const received = await notices(); return ['LoanApproved', 'LoanHandedOver', 'LoanDueSoon'].every(type => received.some(n => n.eventType === type)); });
  // Wait for a real due date; never change shared DB state or synthesize events.
  await eventually(async () => (await notices()).some(n => n.eventType === 'LoanOverdue'), 170000);
  assert.ok(Number(sql('notification', 'SELECT count(*) FROM inbox_messages;')) > 0);
  await studentPage.goto(origin + '/notifications'); await studentPage.locator('.notification-list article').first().waitFor();
  await studentPage.getByRole('button', { name: 'Đánh dấu đã đọc' }).first().click();
  await studentPage.getByText('Đã đọc', { exact: true }).first().waitFor(); await screen(studentPage, 't20-notifications');
  record('Jobs and broker', 'real due-soon/overdue -> Outbox -> Redis -> Inbox -> Student UI', 'all four lifecycle/reminder events received');
  await confirmReturn(staffPage, 'DAMAGED');
  await eventually(async () => (await request(`/api/loans/${loanId}`, { token: student.accessToken })).items.filter(i => i.itemStatus === 'BORROWED').length === 2);
  await eventually(() => staffPage.getByRole('combobox', { name: /Tình trạng trả/ }).count().then(n => n === 2));
  await confirmReturn(staffPage, 'LOST');
  await eventually(() => staffPage.getByRole('combobox', { name: /Tình trạng trả/ }).count().then(n => n === 1));
  await confirmReturn(staffPage, 'NORMAL');
  await eventually(async () => (await request(`/api/loans/${loanId}`, { token: student.accessToken })).status === 'RETURNED');
  const returned = await request(`/api/loans/${loanId}`, { token: student.accessToken });
  assert.deepEqual(returned.items.map(i => i.returnCondition).sort(), ['DAMAGED', 'LOST', 'NORMAL']);
  const damaged = returned.items.find(i => i.returnCondition === 'DAMAGED').assetUnitId;
  const normal = returned.items.find(i => i.returnCondition === 'NORMAL').assetUnitId;
  const lost = returned.items.find(i => i.returnCondition === 'LOST').assetUnitId;
  for (const [id, status] of [[damaged, 'MAINTENANCE'], [normal, 'AVAILABLE'], [lost, 'LOST']]) assert.equal((await request(`/api/inventory/assets/${id}`, { token: staff.accessToken })).status, status);
  await request(`/api/loans/${loanId}/returns`, { method: 'POST', token: staff.accessToken, body: { items: [{ loanItemId: returned.items.find(i => i.assetUnitId === normal).id, condition: 'BROKEN' }] }, expected: 400 });
  await request(`/api/loans/${loanId}/returns`, { method: 'POST', token: staff.accessToken, body: { items: [{ loanItemId: returned.items.find(i => i.assetUnitId === normal).id, condition: 'NORMAL' }] } });
  record('Flow 2 returns', 'partial then full; DAMAGED/LOST/NORMAL; invalid condition; repeat safe', 'MAINTENANCE / LOST / AVAILABLE; RETURNED');
  const movementsBefore = await request(`/api/inventory/assets/${damaged}/movements`, { token: staff.accessToken });
  await staffPage.goto(origin + '/staff/inventory');
  await staffPage.getByPlaceholder('Serial hoặc vị trí').fill(assets.find(a => a.id === damaged).serialNumber);
  await staffPage.getByRole('button', { name: 'Tìm', exact: true }).click();
  await staffPage.getByRole('button', { name: 'Hoàn tất bảo trì', exact: true }).click();
  await staffPage.getByLabel('Kết quả bảo trì').fill('T20 inspected and repaired');
  await staffPage.getByRole('button', { name: 'Hoàn tất', exact: true }).click();
  await eventually(async () => (await request(`/api/inventory/assets/${damaged}`, { token: staff.accessToken })).status === 'AVAILABLE');
  await staffPage.getByRole('button', { name: 'Bảo trì', exact: true }).click();
  await staffPage.getByLabel('Lý do', { exact: true }).fill('T20 scheduled inspection');
  await staffPage.getByRole('button', { name: 'Bắt đầu bảo trì', exact: true }).click();
  await eventually(async () => (await request(`/api/inventory/assets/${damaged}`, { token: staff.accessToken })).status === 'MAINTENANCE');
  await staffPage.getByRole('button', { name: 'Hoàn tất bảo trì', exact: true }).click();
  await staffPage.getByLabel('Kết quả bảo trì').fill('T20 scheduled inspection complete');
  await staffPage.getByRole('button', { name: 'Hoàn tất', exact: true }).click();
  await eventually(async () => (await request(`/api/inventory/assets/${damaged}`, { token: staff.accessToken })).status === 'AVAILABLE');
  await staffPage.getByRole('button', { name: 'Lịch sử', exact: true }).click();
  await staffPage.locator('dialog .timeline li').first().waitFor(); await screen(staffPage, 't20-maintenance-history');
  await staffPage.getByRole('button', { name: 'Đóng', exact: true }).click();
  const movementsAfter = await request(`/api/inventory/assets/${damaged}/movements`, { token: staff.accessToken });
  assert.equal(movementsAfter.length, movementsBefore.length + 3);
  for (const movement of movementsBefore) assert.deepEqual(movementsAfter.find(m => m.id === movement.id), movement);
  assert.equal(sql('inventory', `SELECT "Status" FROM "AssetUnits" WHERE "Id"='${damaged}';`), 'AVAILABLE');
  record('Flow 3 maintenance', 'repair and maintenance persist; original StockMovements preserved', 'AVAILABLE; 3 appended movements');
  // Recovery only touches this verified random T20 project.
  docker(['stop', 'redis']);
  let outageLoan;
  try { outageLoan = await request('/api/loans', { method: 'POST', expected: 201, token: student.accessToken, body: { purpose: 'T20 Redis outage preserves business commit', dueAt: new Date(Date.now() + 86400000).toISOString(), items: [{ equipmentTypeId: type.id }] } }); }
  finally { docker(['start', 'redis']); }
  await eventually(async () => (await request('/api/notifications?pageSize=100', { token: student.accessToken })).items.some(n => n.aggregateId === outageLoan.id));
  docker(['restart', 'borrowing-worker']);
  await eventually(() => {
    const state = JSON.parse(docker(['ps', '--format', 'json', 'borrowing-worker']));
    return state.State === 'running' && state.Health === 'healthy';
  });
  record('Recovery', 'Redis reconnect delivers persisted outbox; worker restarts healthy', 'notification delivered / healthy');
  await studentPage.goto(`${origin}/loans/${loanId}`); await studentPage.locator('.timeline li').first().waitFor(); await screen(studentPage, 't20-timeline');
  await studentPage.setViewportSize({ width: 390, height: 844 });
  for (const path of ['/catalog', '/loans', '/notifications']) {
    await studentPage.goto(origin + path); await studentPage.locator('h1').waitFor();
    if (path === '/loans') await studentPage.getByRole('link', { name: type.name, exact: true }).first().waitFor();
    assert.ok(await studentPage.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await screen(studentPage, 't20-mobile-' + path.slice(1));
  }
  assert.deepEqual(errors, []);
  record('Browser', 'desktop/mobile production web, timeline and notifications; no JS errors', 'verified');
} catch (error) {
  results.push({ name: 'scenario', expected: 'all stages pass', actual: redact(error.stack ?? error), status: 'FAIL' });
  console.error(redact(error.stack ?? error)); process.exitCode = 1;
} finally {
  if (browser) await browser.close();
  await mkdir(evidence, { recursive: true });
  await writeFile(resolve(evidence, 'docker-e2e.json'), JSON.stringify({ project: env.LABLEND_E2E_PROJECT, results }, null, 2));
}
