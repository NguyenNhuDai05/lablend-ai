import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { build, preview } from 'vite';
import { chromium } from 'playwright';

const env = process.env;
for (const name of ['GATEWAY', 'PORT', 'STUDENT', 'STAFF', 'ADMIN', 'ADMIN_PASSWORD', 'PASSWORD', 'TYPE'])
  if (!env['LABLEND_E2E_' + name]) throw new Error('Run through Member3BrowserTests; missing fixture ' + name);
const origin = `http://localhost:${env.LABLEND_E2E_PORT}`;
const evidence = resolve('../../.tmp/incident-browser'); await mkdir(evidence, { recursive: true });
const config = { configFile: resolve('vite.config.ts'), define: { 'import.meta.env.VITE_API_BASE_URL': JSON.stringify('') }, build: { outDir: resolve(evidence, 'web'), emptyOutDir: true } };
await build(config);
const server = await preview({ ...config, preview: { host: 'localhost', port: Number(env.LABLEND_E2E_PORT), strictPort: true,
  proxy: { '/api': { target: env.LABLEND_E2E_GATEWAY, changeOrigin: false } } } });
let browser;
const redact = value => [env.LABLEND_E2E_PASSWORD, env.LABLEND_E2E_ADMIN_PASSWORD].reduce((s, secret) => s.replaceAll(secret, '[redacted]'), String(value));
async function login(page, email, password) {
  await page.goto(origin + '/login');
  await page.getByLabel('Email', { exact: true }).fill(email); await page.getByLabel('Mật khẩu', { exact: true }).fill(password);
  const session = page.waitForResponse(r => r.url().endsWith('/api/auth/login') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  const response = await session; assert.equal(response.status(), 200); const body = await response.json();
  await page.getByRole('button', { name: 'Đăng xuất', exact: true }).waitFor(); return body;
}
async function confirm(page) { await page.getByRole('button', { name: 'Xác nhận thao tác sự cố', exact: true }).click(); }
async function read(path, token) {
  const response = await fetch(env.LABLEND_E2E_GATEWAY + path, { headers: { Authorization: `Bearer ${token}` } });
  assert.equal(response.status, 200, path); return response.json();
}
async function status(page, label) { await page.locator('.status-pill').filter({ hasText: label }).first().waitFor(); }
async function shot(page, name) { await page.screenshot({ path: resolve(evidence, name + '.png'), fullPage: true }); }
try {
  browser = await chromium.launch({ headless: true, ...(env.LABLEND_BROWSER_CHANNEL ? { channel: env.LABLEND_BROWSER_CHANNEL } : {}) });
  const student = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const staff = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const admin = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errors = [];
  for (const page of [student, staff, admin]) {
    page.on('pageerror', error => { errors.push(error.message); console.error(redact(error.stack ?? error)); });
    page.on('requestfailed', request => console.error(redact(`requestfailed ${new URL(request.url()).pathname}: ${request.failure()?.errorText}`)));
  }
  const studentSession = await login(student, env.LABLEND_E2E_STUDENT, env.LABLEND_E2E_PASSWORD);
  const staffSession = await login(staff, env.LABLEND_E2E_STAFF, env.LABLEND_E2E_PASSWORD);
  await student.goto(`${origin}/loans/new?equipmentTypeId=${env.LABLEND_E2E_TYPE}`);
  await student.getByLabel('Mục đích mượn', { exact: true }).fill('Thực hành Arduino và kiểm tra luồng sự cố thiết bị');
  const due = new Date(Date.now() + 2 * 86400000);
  await student.getByLabel('Hạn trả dự kiến').fill(new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 19));
  await student.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click();
  await student.getByRole('button', { name: 'Gửi yêu cầu mượn', exact: true }).click();
  await student.waitForURL(/\/loans\/[0-9a-f-]{36}$/); const loanId = new URL(student.url()).pathname.split('/').at(-1);
  await staff.goto(`${origin}/staff/loans/${loanId}`);
  await staff.getByRole('button', { name: 'Duyệt phiếu', exact: true }).click(); await staff.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click();
  await status(staff, 'Đã duyệt'); await staff.getByRole('button', { name: 'Bàn giao thiết bị', exact: true }).click();
  await staff.getByRole('button', { name: 'Xác nhận thao tác', exact: true }).click(); await status(staff, 'Đang mượn');
  const before = await read(`api/loans/${loanId}`, studentSession.accessToken);
  const identities = await read(`api/loans/${loanId}/asset-identities`, studentSession.accessToken);
  const old = identities[0];
  await student.goto(`${origin}/loans/${loanId}`); await student.getByRole('link', { name: 'Báo sự cố', exact: true }).click();
  await student.getByRole('heading', { name: 'Báo sự cố thiết bị', exact: true }).waitFor();
  await student.getByText(old.serialNumber, { exact: true }).waitFor();
  await student.getByLabel('Mô tả sự cố', { exact: true }).fill('Thiết bị Arduino không lên nguồn sau khi kết nối theo hướng dẫn.');
  await student.getByLabel('Ảnh hỗ trợ (không bắt buộc)', { exact: true }).setInputFiles({ name: 'power-failure.png', mimeType: 'image/png',
    buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64') });
  await student.getByLabel('Tôi hiểu rằng phải mang thiết bị đến nhân viên để kiểm tra trực tiếp', { exact: true }).check();
  await student.getByRole('button', { name: 'Xem lại báo cáo', exact: true }).click();
  await student.getByRole('button', { name: 'Gửi báo sự cố', exact: true }).click(); await student.waitForURL(/\/incidents\/[0-9a-f-]{36}$/);
  const incidentId = new URL(student.url()).pathname.split('/').at(-1);
  await student.getByRole('img', { name: 'Ảnh thiết bị do người mượn cung cấp để hỗ trợ kiểm tra' }).waitFor(); await shot(student, 'student-report');
  await staff.goto(origin + '/notifications'); await staff.getByRole('heading', { name: 'Có báo cáo sự cố thiết bị', exact: true }).waitFor({ timeout: 45000 });
  await staff.getByRole('link', { name: 'Xem sự cố thiết bị', exact: true }).first().click();
  await staff.getByRole('button', { name: 'Bắt đầu kiểm tra', exact: true }).click(); await confirm(staff);
  await staff.getByLabel('Kết luận kiểm tra', { exact: true }).selectOption('DEFECTIVE');
  await staff.getByLabel('Kết quả kiểm tra', { exact: true }).fill('Nhân viên đã kiểm tra trực tiếp và xác nhận lỗi nguồn của thiết bị.');
  await staff.getByLabel('Serial đối chiếu thực tế', { exact: true }).fill(old.serialNumber);
  await staff.getByLabel('Tôi đã trực tiếp nhận, đối chiếu đúng thiết bị và serial', { exact: true }).check();
  await staff.getByRole('button', { name: 'Xem lại kết luận', exact: true }).click(); await confirm(staff);
  await status(staff, 'Đã xác nhận, chờ xử lý');
  await staff.getByLabel('Xác nhận đang bàn giao thiết bị thay thế cho đúng người mượn', { exact: true }).check();
  await staff.getByRole('button', { name: 'Cấp đổi thiết bị cùng loại', exact: true }).click(); await confirm(staff);
  await status(staff, 'Đã cấp đổi'); await shot(staff, 'staff-replaced');
  const after = await read(`api/loans/${loanId}`, studentSession.accessToken);
  assert.equal(after.status, 'BORROWED'); assert.equal(after.dueAt, before.dueAt); assert.equal(after.items.length, 2);
  const replacement = after.items.find(i => i.id !== old.loanItemId);
  assert.equal(after.items.find(i => i.id === old.loanItemId).itemStatus, 'DAMAGED');
  assert.equal(replacement.replacesLoanItemId, old.loanItemId); assert.equal(replacement.incidentId, incidentId);
  assert.equal((await read(`api/inventory/assets/${old.assetUnitId}`, staffSession.accessToken)).status, 'MAINTENANCE');
  assert.equal((await read(`api/inventory/assets/${replacement.assetUnitId}`, staffSession.accessToken)).status, 'ON_LOAN');
  await student.goto(`${origin}/incidents/${incidentId}`); await status(student, 'Đã cấp đổi');
  await student.setViewportSize({ width: 390, height: 844 });
  assert.equal(await student.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await shot(student, 'mobile-incident');
  await student.goto(origin + '/notifications'); await student.getByRole('heading', { name: 'Đã bàn giao thiết bị thay thế', exact: true }).waitFor({ timeout: 45000 });
  await login(admin, env.LABLEND_E2E_ADMIN, env.LABLEND_E2E_ADMIN_PASSWORD); await admin.goto(origin + '/admin/incidents');
  await admin.getByRole('heading', { name: 'Thống kê sự cố thiết bị', exact: true }).waitFor();
  await admin.getByLabel('Loại thiết bị', { exact: true }).selectOption(env.LABLEND_E2E_TYPE);
  await admin.getByRole('link', { name: (await read(`api/incidents/${incidentId}`, studentSession.accessToken)).code, exact: true }).waitFor();
  await admin.getByRole('heading', { name: 'Thiết bị có lỗi đã được xác nhận', exact: true }).waitFor(); await shot(admin, 'admin-statistics');
  await staff.goto(origin + '/staff/inventory'); await staff.getByPlaceholder('Serial hoặc vị trí').fill(old.serialNumber);
  await staff.getByRole('button', { name: 'Tìm', exact: true }).click(); await staff.getByRole('button', { name: 'Hoàn tất bảo trì', exact: true }).click();
  await staff.getByLabel('Kết quả bảo trì').fill('Đã sửa lỗi nguồn, kiểm tra đạt và đưa vào sử dụng.');
  await staff.getByRole('button', { name: 'Hoàn tất', exact: true }).click();
  await staff.goto(`${origin}/staff/loans/${loanId}`); await staff.getByRole('combobox', { name: /Tình trạng trả/ }).selectOption('NORMAL');
  await staff.getByRole('button', { name: 'Xem lại thiết bị nhận trả', exact: true }).click(); await staff.getByRole('button', { name: 'Xác nhận nhận trả', exact: true }).click(); await status(staff, 'Đã trả');
  await student.goto(origin + '/staff/incidents'); await student.getByRole('heading', { name: 'Không có quyền truy cập', exact: true }).waitFor();
  assert.deepEqual(errors, []);
  console.log('PASS incident browser: real loan approval/handover, Student report/private image, Redis staff notification, physical serial inspection, same-loan exchange, real stock/maintenance, Student result/notification, Admin database statistics, mobile, maintenance completion and normal replacement return. No business mocks.');
} catch (error) {
  console.error(redact(error.stack ?? error)); process.exitCode = 1;
  if (browser) for (const [index, context] of browser.contexts().entries()) for (const page of context.pages()) {
    await shot(page, `failure-${index}`).catch(() => {});
    console.error(redact((await page.locator('body').innerText().catch(() => '')).slice(0, 4500)));
  }
} finally { await browser?.close(); await server.close(); }
