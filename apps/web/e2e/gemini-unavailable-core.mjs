// Verifies ordinary Borrowing remains usable after live Gemini failure. Never sends a recommendation POST.
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { chromium } from 'playwright';
const config = JSON.parse(await readFile(process.env.LABLEND_LIVE_MANIFEST, 'utf8'));
assert.match(config.project, /^lablend-gemini-live-[0-9a-f]{10}$/);
const secrets = [config.studentPassword, config.otherPassword, config.signingKey];
const redact = value => secrets.reduce((text, secret) => text.replaceAll(secret, '[redacted]'), String(value));
let browser; const evidence = { mocked: false, status: 'FAIL', recommendationPosts: 0, loanId: null };
try {
  const catalog = await (await fetch(config.gatewayUrl + '/api/equipment-types?pageSize=100')).json();
  const stock = await (await fetch(config.gatewayUrl + '/api/inventory/availability?' + catalog.items.map(i => 'equipmentTypeIds=' + i.id).join('&'))).json();
  const choices = catalog.items.filter(i => i.isActive && stock.some(s => s.equipmentTypeId === i.id && s.available > 0)).slice(0, 2); assert.equal(choices.length, 2);
  browser = await chromium.launch({ headless: true }); const context = await browser.newContext({ viewport: { width: 1365, height: 950 } });
  await context.route('**/api/recommendations', async route => {
    if (route.request().method() === 'POST') { evidence.recommendationPosts++; await route.abort(); throw new Error('No further Gemini calls allowed.'); }
    await route.continue();
  });
  const page = await context.newPage(); await page.goto(config.webUrl + '/login');
  await page.getByLabel('Email', { exact: true }).fill(config.studentEmail); await page.getByLabel('Mật khẩu', { exact: true }).fill(config.studentPassword);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await page.getByRole('button', { name: 'Đăng xuất', exact: true }).waitFor();
  await page.goto(config.webUrl + '/recommendations/history'); await page.getByText('Bạn chưa có lịch sử tư vấn.', { exact: false }).waitFor();
  await page.screenshot({ path: join(config.evidenceDirectory, '03-failed-call-no-history.png'), fullPage: true });
  await page.goto(config.webUrl + '/loans/new?equipmentTypeId=' + choices[0].id);
  await page.getByLabel('Mục đích mượn').fill('Nghiệm thu luồng Catalog và Borrowing khi Gemini tạm thời không khả dụng.');
  await page.locator('.choice-list li').filter({ hasText: choices[1].name }).getByRole('button', { name: 'Thêm', exact: true }).click();
  const due = new Date(Date.now() + 86400000); await page.getByLabel('Hạn trả dự kiến').fill(new Date(due.getTime() - due.getTimezoneOffset() * 60000).toISOString().slice(0, 19));
  assert.equal(await page.locator('.selection-row').count(), 2);
  let creates = 0; page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/loans')) creates++; });
  await page.getByRole('button', { name: 'Xem lại yêu cầu', exact: true }).click(); await page.getByRole('dialog').waitFor(); assert.equal(creates, 0);
  await page.screenshot({ path: join(config.evidenceDirectory, '04-core-borrowing-review.png'), fullPage: true });
  const response = page.waitForResponse(r => r.url().endsWith('/api/loans') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Gửi yêu cầu mượn', exact: true }).click(); const created = await response; assert.equal(created.status(), 201);
  const loan = await created.json(); evidence.loanId = loan.id; assert.equal(loan.items.length, 2); assert.equal(creates, 1);
  await page.waitForURL(`**/loans/${loan.id}`); await page.locator('.status-pill').filter({ hasText: 'Chờ duyệt' }).waitFor();
  await page.screenshot({ path: join(config.evidenceDirectory, '05-core-loan-created.png'), fullPage: true });
  await page.goto(config.webUrl + '/loans'); await page.getByRole('link', { name: 'Phiếu ' + loan.id.slice(0, 8), exact: true }).waitFor();
  assert.equal(evidence.recommendationPosts, 0); evidence.status = 'PASS'; evidence.borrowingHttpStatus = 201; evidence.loanUnits = 2;
  evidence.explanation = 'Ordinary Catalog -> real Borrowing -> Student detail/list succeeded in isolated test DB after Gemini HTTP 503. This is not a successful live AI-to-loan flow.';
} catch (error) { evidence.error = redact(error.message); process.exitCode = 1; }
finally { await writeFile(join(config.evidenceDirectory, 'core-after-ai-failure.json'), JSON.stringify(evidence, null, 2)); await browser?.close(); console.log(JSON.stringify(evidence)); }
