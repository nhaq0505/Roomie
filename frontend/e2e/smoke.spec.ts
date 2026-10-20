import { test, expect } from '@playwright/test';
const owner = {id:'fixture-owner',name:'Smoke Owner',email:'smoke@example.invalid',role:'Owner',status:'Approved'};
const emptyInvoices={items:[],total:0,draftCount:0,totals:{billed:0,paid:0,outstanding:0}};
const dashboard = {totalRooms:0,occupiedRooms:0,totalBilled:0,totalPaid:0,totalOutstanding:0,pendingReadings:0,monthlyBilled:0,monthlyPaid:0,monthlyOutstanding:0,collectionRate:0,period:'2026-10'};
test.describe('Smoke Suite', () => {
  test.describe.configure({ mode: 'serial' });

test('login enters the owner area and sends credentials only once', async ({page}) => {
  let authenticated=false, logins=0;
  await page.route('**/api/**', async route => {
    const path=new URL(route.request().url()).pathname;
    if(path==='/api/auth/login') {authenticated=true; logins++; return route.fulfill({json:owner});}
    if(path==='/api/auth/me') return route.fulfill({status:authenticated?200:401,json:authenticated?owner:{message:'Please sign in.'}});
    return route.fulfill({json:path==='/api/dashboard'?dashboard:path==='/api/invoices'?emptyInvoices:[]});
  });
  await page.goto('/login');
  await expect(page.locator('input[autocomplete=one-time-code]')).toHaveCount(0);
  await page.getByLabel('Email',{exact:true}).fill(owner.email);
  await page.getByLabel('Mật khẩu',{exact:true}).fill('SmokeOwner123!');
  await page.locator('button[type=submit]').click();
  await expect(page).toHaveURL(/dashboard/); expect(logins).toBe(1);
});
test('temporary session lookup failure offers retry instead of login redirect',async({page})=>{
  let failed=true;
  await page.route('**/api/**',route=>route.fulfill({status:failed?503:200,json:failed?{message:'Temporary failure'}:(route.request().url().endsWith('/me')?owner:new URL(route.request().url()).pathname==='/api/invoices'?emptyInvoices:dashboard)}));
  await page.goto('/dashboard'); await expect(page.getByRole('button',{name:'Thử lại'})).toBeVisible();
  await expect(page).toHaveURL(/dashboard/); failed=false;
  await page.getByRole('button',{name:'Thử lại'}).click();
  await expect(page.getByRole('button',{name:'Thử lại'})).toHaveCount(0);
});
test('expired session redirects to login',async({page})=>{
  await page.route('**/api/**',route=>route.fulfill({status:401,json:{message:'Please sign in.'}}));
  await page.goto('/dashboard'); await expect(page).toHaveURL(/login/);
});
test('admin enters owners directly even with stale MFA flags',async({page})=>{
  await page.route('**/api/**',route=>route.fulfill({json:new URL(route.request().url()).pathname==='/api/auth/me'
    ? {...owner,role:'Admin',mfaRequired:true,mfaEnabled:true}
    : {items:[],total:0,pendingCount:0,page:1,pageSize:10}}));
  await page.goto('/admin/owners'); await expect(page).toHaveURL(/admin\/owners/);
  await expect(page.getByRole('heading',{level:2})).toBeVisible();
});
test('removed MFA setup URL displays not found',async({page})=>{
  await page.route('**/api/auth/me',route=>route.fulfill({status:401,json:{message:'Please sign in.'}}));
  await page.goto('/mfa-setup');
  await expect(page.getByRole('heading',{name:'Xác thực hai bước'})).toHaveCount(0);
  await expect(page.getByText('Không tìm thấy trang',{exact:true})).toBeVisible();
});
test('invoice pagination uses server totals and requests the next page',async({page})=>{
  const pages:number[]=[];
  await page.route('**/api/**',async route=>{
    const url=new URL(route.request().url());
    if(url.pathname==='/api/auth/me') return route.fulfill({json:owner});
    if(url.pathname==='/api/invoices') {
      const number=Number(url.searchParams.get('page'));pages.push(number);
      return route.fulfill({json:{items:[{id:`invoice-${number}`,kind:'Monthly',roomName:`Page ${number}`,buildingName:'Fixture',tenantName:'Tenant',period:'2026-10',lifecycleState:'Issued',total:100,paid:0,outstanding:100,status:'Unpaid',depositSettlements:[]}],total:20,draftCount:0,totals:{billed:2000,paid:0,outstanding:2000}}});
    }
    return route.fulfill({json:[]});
  });
  await page.goto('/invoices'); await expect(page.getByText('Page 1',{exact:true})).toBeVisible();
  await page.locator('.ant-pagination-next').click();
  await expect(page.getByText('Page 2',{exact:true})).toBeVisible(); expect(pages).toContain(2);
});
test('checkout loads the selected older invoice directly',async({page})=>{
  const wanted='00000000-0000-0000-0000-000000000001';
  let selected=false;
  await page.route('**/api/tenant/**',route=>{
    const url=new URL(route.request().url());selected=url.searchParams.get('invoiceId')===wanted;
    return route.fulfill({json:{room:{name:'Older invoice room',buildingName:'Fixture'},occupancy:{},invoices:selected?[{id:wanted,kind:'Monthly',period:'2026-10',status:'Paid',outstanding:0,total:100,paid:100}]:[],total:1,totals:{outstanding:0}}});
  });
  await page.goto(`/t/fixture-private-token/invoices/${wanted}/checkout`);
  await expect(page.getByText('Đã thanh toán',{exact:true})).toBeVisible();expect(selected).toBe(true);
});
test('a delayed failed session lookup cannot overwrite a successful login',async({page})=>{
  let release:()=>void=()=>{};
  const pending=new Promise<void>(resolve=>{release=resolve;});
  await page.route('**/api/**',async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path==='/api/auth/me'){await pending;return route.fulfill({status:503,json:{message:'Temporary failure'}});}
    if(path==='/api/auth/login')return route.fulfill({json:owner});
    return route.fulfill({json:path==='/api/invoices'?emptyInvoices:dashboard});
  });
  await page.goto('/login');
  await page.getByLabel('Email',{exact:true}).fill(owner.email);
  await page.getByLabel('Mật khẩu',{exact:true}).fill('SmokeOwner123!');
  await page.locator('button[type=submit]').click();await expect(page).toHaveURL(/dashboard/);
  await expect(page.getByRole('heading',{level:2})).toBeVisible();
  const response=page.waitForResponse('**/api/auth/me');release();await response;
  await expect(page.getByRole('heading',{level:2})).toBeVisible();
  await expect(page.getByRole('button',{name:'Thử lại'})).toHaveCount(0);
  await expect(page).toHaveURL(/dashboard/);
});

test('password reset rejects eight characters and accepts nine lowercase characters',async({page})=>{
  let resets=0;
  await page.route('**/api/**',async route=>{
    const path=new URL(route.request().url()).pathname;
    if(path==='/api/auth/reset-password') {
      resets++; expect(route.request().postDataJSON().newPassword).toBe('abcdefghi');
      return route.fulfill({json:{success:true}});
    }
    return route.fulfill({status:401,json:{message:'Please sign in.'}});
  });
  await page.goto('/reset-password?method=admin');
  await page.getByLabel('Email',{exact:true}).fill('fixture@example.invalid');
  await page.getByLabel('Mã đặt lại mật khẩu',{exact:true}).fill('a'.repeat(64));
  await page.getByLabel('Mật khẩu mới',{exact:true}).fill('abcdefgh');
  await page.getByLabel('Xác nhận mật khẩu mới',{exact:true}).fill('abcdefgh');
  await page.getByRole('button',{name:'Lưu mật khẩu',exact:true}).click();
  await expect(page.getByText('Mật khẩu mới cần từ 9 đến 256 ký tự.',{exact:true})).toBeVisible();
  expect(resets).toBe(0);
  await page.getByLabel('Mật khẩu mới',{exact:true}).fill('abcdefghi');
  await page.getByLabel('Xác nhận mật khẩu mới',{exact:true}).fill('abcdefghi');
  await page.getByRole('button',{name:'Lưu mật khẩu',exact:true}).click();
  await expect(page).toHaveURL(/login/); expect(resets).toBe(1);
});

test('mobile payment header keeps its title above navigation',async({page})=>{
  await page.route('**/api/**',route=>route.fulfill({json:new URL(route.request().url()).pathname==='/api/auth/me'
    ? owner : {clientId:'',webhookStatus:'Unconfigured'}}));
  for (const width of [320,375,768]) {
    await page.setViewportSize({width,height:812});
    await page.goto('/settings/payment');
    const title=page.locator('.breadcrumb-label strong');
    await expect(title).toHaveText('Thanh toán trực tuyến');
    const headerBox=await page.locator('.topbar').boundingBox();
    const titleBox=await title.boundingBox();
    const navBox=await page.locator('.compact-navigation').boundingBox();
    expect(titleBox!.y).toBeGreaterThanOrEqual(headerBox!.y);
    expect(titleBox!.y+titleBox!.height).toBeLessThanOrEqual(headerBox!.y+headerBox!.height);
    expect(titleBox!.y+titleBox!.height).toBeLessThanOrEqual(navBox!.y);
    expect(await title.evaluate(el=>getComputedStyle(el).whiteSpace)).toBe('nowrap');
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await expect(page.getByRole('combobox',{name:'Ngôn ngữ'})).toBeVisible();
    await expect(page.locator('.profile-button')).toBeVisible();
  }
});
});

