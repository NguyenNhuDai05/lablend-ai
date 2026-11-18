import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build, preview } from 'vite';
import { chromium } from 'playwright';

const env = process.env;
const required = ['LABLEND_E2E_GATEWAY', 'LABLEND_E2E_PORT', 'LABLEND_E2E_STUDENT', 'LABLEND_E2E_STAFF', 'LABLEND_E2E_ADMIN', 'LABLEND_E2E_ADMIN_PASSWORD', 'LABLEND_E2E_PASSWORD', 'LABLEND_E2E_TYPE', 'LABLEND_E2E_STUDENT_ID'];
for (const name of required) if (!env[name]) throw new Error(`Run through Member3BrowserTests; missing private fixture setting ${name}.`);
const origin = `http://localhost:${env.LABLEND_E2E_PORT}`;
const evidence = resolve('../../.tmp/m3-browser'); await mkdir(evidence, { recursive: true });
// Serve a fresh production bundle: cold dev-server transforms/dependency optimization
// must not race the first browser navigation on CI. Keep API calls on the real Gateway.
const config = { configFile: resolve('vite.config.ts'),
  define: { 'import.meta.env.VITE_API_BASE_URL': JSON.stringify('') },
  build: { outDir: resolve(evidence, 'web'), emptyOutDir: true } };
await build(config);
const server = await preview({ ...config,
  preview: { host: 'localhost', port: Number(env.LABLEND_E2E_PORT), strictPort: true, proxy: { '/api': { target: env.LABLEND_E2E_GATEWAY, changeOrigin: false } } } });
let browser;
const errors = []; const secretValues = [env.LABLEND_E2E_PASSWORD, env.LABLEND_E2E_ADMIN_PASSWORD, env.LABLEND_E2E_PASSWORD + 'Changed1!'];
const redact = value => secretValues.reduce((text, secret) => text.replaceAll(secret, '[redacted]'), String(value));
async function login(page, email, password, destination = '/login') {
  await page.goto(origin + destination);
  await page.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor();
  await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Mật khẩu', { exact: true }).fill(password);
  const response = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  const session = await response; assert.equal(session.status(), 200);
  const body = await session.json(); await page.getByRole('button', { name: 'Đăng xuất', exact: true }).waitFor(); return body;
}
async function status(page, text) { await page.locator('.status-pill').filter({ hasText: text }).first().waitFor(); }
async function eventually(test, milliseconds = 45000) { const end = Date.now() + milliseconds; while (!await test()) { if (Date.now() > end) throw new Error('Real browser condition timed out.'); await new Promise(r => setTimeout(r, 250)); } }
async function screenshot(page, name) { await page.screenshot({ path: resolve(evidence, name + '.png'), fullPage: true }); }

try {
  // Warm the public read pipeline before the UI's bounded request timeout on small CI/dev machines.
  for (const path of ['api/equipment-types?page=1&pageSize=9&sortBy=name&direction=asc', `api/inventory/availability?equipmentTypeIds=${env.LABLEND_E2E_TYPE}`]) {
    const response = await fetch(env.LABLEND_E2E_GATEWAY + path);
    assert.equal(response.status, 200, `Real public Gateway read failed: ${path}`);
    await response.arrayBuffer();
  }
  browser = await chromium.launch({ headless: true, ...(env.LABLEND_BROWSER_CHANNEL ? { channel: env.LABLEND_BROWSER_CHANNEL } : {}) });
  const studentContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const staffContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const adminContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const student = await studentContext.newPage(); const staff = await staffContext.newPage(); const admin = await adminContext.newPage();
  for (const [role, page] of [['student', student], ['staff', staff], ['admin', admin]]) {
    page.on('pageerror', error => {
      errors.push(error.message);
      console.error(redact(`[${role}] pageerror: ${error.stack ?? error.message}`));
    });
    page.on('requestfailed', request => {
      console.error(redact(`[${role}] requestfailed: ${request.method()} ${new URL(request.url()).pathname}: ${request.failure()?.errorText}`));
    });
    page.on('response', response => {
      if (response.status() >= 400 && ['document', 'script', 'stylesheet'].includes(response.request().resourceType()))
        console.error(redact(`[${role}] resource HTTP ${response.status()}: ${new URL(response.url()).pathname}`));
    });
  }

  await student.goto(`${origin}/catalog`);
  await student.getByRole('heading', { name: 'Danh mục thiết bị', exact: true }).waitFor();
  await student.locator('.equipment-card').first().waitFor(); await screenshot(student, 'catalog');
  await student.goto(`${origin}/equipment/${env.LABLEND_E2E_TYPE}`);
  await student.getByRole('link', { name: 'Đăng nhập để mượn' }).waitFor();
  await student.getByRole('link', { name: 'Đăng nhập để mượn' }).click();
  const studentSession = await login(student, env.LABLEND_E2E_STUDENT, env.LABLEND_E2E_PASSWORD, new URL(student.url()).pathname + new URL(student.url()).search);
  await student.waitForURL(`**/loans/new?equipmentTypeId=${env.LABLEND_E2E_TYPE}`);
  const emptySubmission = await student.locator('form').first().evaluate(form => form.checkValidity()); assert.equal(emptySubmission, false);
  const purpose = 'Browser E2E measuring laboratory signals';
  await student.getByLabel('Mục đích mượn', { exact: true }).fill(purpose);
  const due = new Date(Date.now() + 90000);
  const localDue = new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 19);
  await student.getByLabel('Hạn trả dự kiến').fill(localDue);
  const quantity = student.getByRole('spinbutton').first(); await quantity.fill('2');
  await eventually(() => student.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).isEnabled());
  assert.equal(await student.locator('form').first().evaluate(form => form.checkValidity()), true);
  let creates = 0; student.on('request', r => { if (r.method() === 'POST' && r.url().endsWith('/api/loans')) creates++; });
  await student.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click();
  await student.getByRole('button', { name: 'Gửi yêu cầu mượn' }).evaluate(button => { button.click(); button.click(); });
  await student.waitForURL(/\/loans\/[0-9a-f-]{36}$/); const loanId = new URL(student.url()).pathname.split('/').at(-1);
  await status(student, 'Chờ duyệt'); assert.equal(creates, 1); await screenshot(student, 'student-loan');
  const staffSession = await login(staff, env.LABLEND_E2E_STAFF, env.LABLEND_E2E_PASSWORD);
  await staff.goto(`${origin}/staff/loans/${loanId}`); await status(staff, 'Chờ duyệt');
  await staff.getByRole('button', { name: 'Duyệt phiếu', exact: true }).click();
  await staff.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).evaluate(button => { button.click(); button.click(); });
  await status(staff, 'Đã duyệt');
  await staff.getByRole('button', { name: 'Bàn giao thiết bị', exact: true }).click(); await staff.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click();
  await status(staff, 'Đang mượn'); await screenshot(staff, 'staff-handover');
  // Read the authoritative Inventory through the real Gateway, using the JWT issued by the browser's actual login.
  const assets = env.LABLEND_E2E_ASSETS.split(',');
  async function asset(id) { const response = await fetch(`${env.LABLEND_E2E_GATEWAY}api/inventory/assets/${id}`, { headers: { Authorization: `Bearer ${staffSession.accessToken}` } }); assert.equal(response.status, 200); return response.json(); }
  assert.equal((await asset(assets[0])).status, 'ON_LOAN');

  await student.goto(`${origin}/staff/loans`); await student.getByRole('heading', { name: 'Không có quyền truy cập' }).waitFor();
  await student.goto(`${origin}/admin/users`); await student.getByRole('heading', { name: 'Không có quyền truy cập' }).waitFor();
  await student.bringToFront();
  await student.goto(`${origin}/notifications`);
  await student.getByRole('heading', { name: 'Phiếu mượn đã được duyệt', exact: true }).waitFor();
  await student.getByRole('heading', { name: 'Sắp đến hạn trả thiết bị', exact: true }).waitFor();
  await student.getByRole('button', { name: 'Đánh dấu đã đọc' }).first().click();
  await student.getByText('Đã đọc', { exact: true }).first().waitFor();

  // Cancel a second genuine Student request and confirm the cancel UI is absent after finalization.
  await student.goto(`${origin}/loans/new?equipmentTypeId=${env.LABLEND_E2E_TYPE}`);
  await student.getByLabel('Mục đích mượn', { exact: true }).fill('Cancel browser E2E extra request');
  const later = new Date(Date.now() + 86400000); await student.getByLabel('Hạn trả dự kiến').fill(new Date(later.getTime() - later.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
  await student.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click();
  await student.getByRole('button', { name: 'Gửi yêu cầu mượn' }).click(); await student.waitForURL(/\/loans\/[0-9a-f-]{36}$/);
  await student.getByRole('button', { name: 'Hủy phiếu mượn', exact: true }).click(); await student.getByRole('button', { name: 'Xác nhận hủy phiếu', exact: true }).click(); await status(student, 'Đã hủy');
  assert.equal(await student.getByRole('button', { name: 'Hủy phiếu mượn', exact: true }).count(), 0);

  await student.bringToFront(); await student.goto(`${origin}/notifications`);
  await eventually(() => student.getByRole('heading', { name: 'Thiết bị đã quá hạn trả', exact: true }).count().then(n => n > 0), 110000);
  await screenshot(student, 'notifications');
  await staff.getByRole('combobox', { name: /Tình trạng trả/ }).first().selectOption('DAMAGED');
  await staff.getByRole('button', { name: 'Xem lại thiết bị nhận trả', exact: true }).click();
  await staff.getByRole('button', { name: 'Xác nhận nhận trả', exact: true }).click();
  await eventually(async () => (await Promise.all(assets.map(asset))).some(a => a.status === 'MAINTENANCE'));
  await eventually(() => staff.getByRole('combobox', { name: /Tình trạng trả/ }).count().then(n => n === 1));
  await status(staff, 'Đang mượn');
  await staff.getByRole('combobox', { name: /Tình trạng trả/ }).first().selectOption('NORMAL');
  await staff.getByRole('button', { name: 'Xem lại thiết bị nhận trả', exact: true }).click();
  await staff.getByRole('button', { name: 'Xác nhận nhận trả', exact: true }).click(); await status(staff, 'Đã trả');
  await student.getByRole('heading', { name: 'Đã ghi nhận trả thiết bị', exact: true }).first().waitFor({ timeout: 25000 });

  // Finish the physical lifecycle through the actual Staff maintenance UI and inspect append-only history.
  const damagedAsset = (await Promise.all(assets.map(asset))).find(a => a.status === 'MAINTENANCE');
  assert.ok(damagedAsset);
  const beforeMovements = await fetch(`${env.LABLEND_E2E_GATEWAY}api/inventory/assets/${damagedAsset.id}/movements`, { headers: { Authorization: `Bearer ${staffSession.accessToken}` } }).then(r => r.json());
  await staff.goto(`${origin}/staff/inventory`);
  await staff.getByPlaceholder('Serial hoặc vị trí').fill(damagedAsset.serialNumber);
  await staff.getByRole('button', { name: 'Tìm', exact: true }).click();
  // Other browser scenarios leave valid maintenance records in this collection's database.
  // Filter results update asynchronously; always act on the exact physical serial, never a page-wide button.
  const damagedRow = staff.getByRole('row').filter({ has: staff.getByRole('cell', { name: damagedAsset.serialNumber, exact: true }) });
  await damagedRow.getByRole('button', { name: 'Hoàn tất bảo trì', exact: true }).click();
  await staff.getByLabel('Kết quả bảo trì').fill('Browser verified repair and inspection');
  await staff.getByRole('button', { name: 'Hoàn tất', exact: true }).click();
  await eventually(async () => (await asset(damagedAsset.id)).status === 'AVAILABLE');
  await damagedRow.getByRole('button', { name: 'Bảo trì', exact: true }).click();
  await staff.getByLabel('Lý do', { exact: true }).fill('Browser scheduled inspection');
  await staff.getByRole('button', { name: 'Bắt đầu bảo trì', exact: true }).click();
  await eventually(async () => (await asset(damagedAsset.id)).status === 'MAINTENANCE');
  await damagedRow.getByRole('button', { name: 'Hoàn tất bảo trì', exact: true }).click();
  await staff.getByLabel('Kết quả bảo trì').fill('Browser scheduled inspection complete');
  await staff.getByRole('button', { name: 'Hoàn tất', exact: true }).click();
  await eventually(async () => (await asset(damagedAsset.id)).status === 'AVAILABLE');
  const afterMovements = await fetch(`${env.LABLEND_E2E_GATEWAY}api/inventory/assets/${damagedAsset.id}/movements`, { headers: { Authorization: `Bearer ${staffSession.accessToken}` } }).then(r => r.json());
  assert.equal(afterMovements.length, beforeMovements.length + 3);
  for (const movement of beforeMovements) assert.deepEqual(afterMovements.find(m => m.id === movement.id), movement);
  await damagedRow.getByRole('button', { name: 'Lịch sử', exact: true }).click();
  await staff.getByRole('heading', { name: `Lịch sử ${damagedAsset.serialNumber}`, exact: true }).waitFor();
  await staff.locator('dialog .timeline li').first().waitFor();
  await screenshot(staff, 'staff-maintenance-history');
  await staff.getByRole('button', { name: 'Đóng', exact: true }).click();

  const adminSession = await login(admin, env.LABLEND_E2E_ADMIN, env.LABLEND_E2E_ADMIN_PASSWORD);
  await admin.goto(`${origin}/admin/users`);
  await admin.getByLabel('Tìm tài khoản', { exact: true }).fill(env.LABLEND_E2E_STUDENT);
  await admin.getByRole('button', { name: 'Tìm kiếm', exact: true }).click();
  await admin.getByRole('cell', { name: env.LABLEND_E2E_STUDENT, exact: true }).waitFor();
  await admin.goto(`${origin}/admin/users/${adminSession.user.id}`);
  await admin.getByLabel('Quyền tài khoản').selectOption('STUDENT'); await admin.getByRole('button', { name: 'Lưu quyền', exact: true }).click();
  await admin.getByRole('alert').filter({ hasText: 'last active administrator' }).waitFor();
  await admin.goto(`${origin}/admin/users/${env.LABLEND_E2E_STUDENT_ID}`);
  await admin.getByLabel('Quyền tài khoản').selectOption('LAB_STAFF'); await admin.getByRole('button', { name: 'Lưu quyền', exact: true }).click(); await admin.getByText('Đã cập nhật tài khoản.', { exact: true }).waitFor();
  await admin.getByRole('button', { name: 'Vô hiệu hóa tài khoản', exact: true }).click(); await admin.getByRole('button', { name: 'Xác nhận thay đổi trạng thái' }).click();
  await admin.getByRole('button', { name: 'Kích hoạt tài khoản', exact: true }).waitFor();
  await admin.getByRole('button', { name: 'Kích hoạt tài khoản', exact: true }).click(); await admin.getByRole('button', { name: 'Xác nhận thay đổi trạng thái' }).click();
  await admin.getByRole('button', { name: 'Vô hiệu hóa tài khoản', exact: true }).waitFor();
  await admin.getByLabel('Quyền tài khoản').selectOption('STUDENT'); await admin.getByRole('button', { name: 'Lưu quyền', exact: true }).click(); await admin.getByText('Đã cập nhật tài khoản.', { exact: true }).waitFor();
  await screenshot(admin, 'admin-user');
  // Reauthenticate after role changes revoked the Student refresh family.
  await studentContext.clearCookies(); await login(student, env.LABLEND_E2E_STUDENT, env.LABLEND_E2E_PASSWORD);
  await student.goto(`${origin}/profile`); await student.getByLabel('Họ và tên', { exact: true }).fill('Student Browser Updated'); await student.getByRole('button', { name: 'Lưu hồ sơ' }).click(); await student.getByText('Đã cập nhật hồ sơ.', { exact: true }).waitFor();
  await student.getByLabel('Mật khẩu hiện tại').fill(env.LABLEND_E2E_PASSWORD); await student.getByLabel('Mật khẩu mới', { exact: true }).fill(env.LABLEND_E2E_PASSWORD + 'Changed1!'); await student.getByLabel('Xác nhận mật khẩu mới').fill(env.LABLEND_E2E_PASSWORD + 'Changed1!'); await student.getByRole('button', { name: 'Đổi mật khẩu và đăng nhập lại' }).click();
  await student.getByRole('heading', { name: 'Đăng nhập', exact: true }).waitFor(); await login(student, env.LABLEND_E2E_STUDENT, env.LABLEND_E2E_PASSWORD + 'Changed1!');
  await student.goto(`${origin}/loans`); await student.setViewportSize({ width: 390, height: 844 }); await student.getByRole('heading', { name: 'Phiếu mượn của tôi', exact: true }).waitFor(); await student.locator('.loan-list article').first().waitFor();
  const listedType = await fetch(`${env.LABLEND_E2E_GATEWAY}api/equipment-types/${env.LABLEND_E2E_TYPE}`).then(r => r.json());
  await student.getByRole('link', { name: listedType.name, exact: true }).first().waitFor(); await screenshot(student, 'mobile-loans');
  assert.equal(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await student.goto(`${origin}/catalog`); await student.locator('.equipment-card').first().waitFor();
  assert.equal(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await screenshot(student, 'mobile-catalog');
  await student.goto(`${origin}/notifications`); await student.locator('.notification-list article').first().waitFor();
  assert.equal(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await screenshot(student, 'mobile-notifications');
  assert.equal(await student.evaluate(() => [...Object.keys(localStorage), ...Object.keys(sessionStorage)].some(key => /token/i.test(key))), false);
  assert.deepEqual(errors, []);
  console.log('PASS M3 browser: real Catalog/Student create-cancel-history, Staff approve/handover/partial damaged return, Redis Notifications/read/overdue, Admin role/active/last-admin guard, Profile/password/relogin, mobile and memory-token regression. No business response mocks.');
} catch (error) {
  console.error(redact(error.stack ?? error));
  if (browser) for (const [index, context] of browser.contexts().entries()) for (const page of context.pages()) {
    await screenshot(page, `failure-${index}`).catch(() => {});
    console.error(redact((await page.locator('body').innerText().catch(() => '')).slice(0, 3500)));
  }
  process.exitCode = 1;
}
finally { if (browser) await browser.close(); await server.close(); }
