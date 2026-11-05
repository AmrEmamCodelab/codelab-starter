// Phase 3 Playwright exit-criteria driver, run through the Playwright MCP tool `browser_run_code_unsafe`
// through the loopback relay ./secret-relay.mjs. It is evidence tooling, not product code: see ../playwright.md.
//
// The MCP code sandbox has no `require`, `process` or file system, so the relay serves both this file and the two
// local dev passwords (http://127.0.0.1:5399); they never appear in a file or a transcript. One MCP call per
// language x viewport keeps each call under the MCP time limit:
//
//   async (page) => eval(await (await page.request.get('http://127.0.0.1:5399/driver')).text())(page,
//     { lang: 'ar', view: 'mobile', tag: 'aam', workbook: 'C:/path/to/sample-users.xlsx' })
//
// RUN.tag makes the role code unique per run; RUN.workbook is a fresh tools/devseed/user-import workbook;
// RUN.evidenceDir (optional) is where the screenshots go.
async (page, RUN) => {

  const WEB = 'http://localhost:4280';
  const RELAY = 'http://127.0.0.1:5399/';
  const EVIDENCE_DIR = RUN.evidenceDir ?? 'E:/codelab-starter/docs/evidence/P3/';
  const VIEWPORTS = { desktop: { width: 1440, height: 900 }, mobile: { width: 390, height: 844 } };
  const LIVE_SYNC_TIMEOUT_MS = 90000;
  const POLL_MS = 1000;
  const SETTLE_MS = 1200;
  const STEP_TIMEOUT_MS = 20000;
  const VIEWER = 'dev.viewer';
  const SSO_USER_BUTTON = /Dev Admin/;
  const LABELS = {
    en: {
      switchTo: 'English', userName: 'User name', passLabel: 'Password', signIn: 'Sign in', admin: 'Administration',
      dashboard: 'Dashboard', roles: 'Roles and permissions', newRole: 'New role', code: 'Code',
      nameEn: 'English name', nameAr: 'Arabic name', createRole: 'Create and choose permissions',
      adminArea: 'Open the access administration area', viewUsers: 'View user accounts',
      saveRole: 'Save changes', users: 'Users', grants: 'Permissions',
      saveGrants: 'Save grants', importUsers: 'Import users', preview: 'Preview', commit: /^Create \d+ accounts?$/,
      sso: 'Single sign-on', ssoContinue: 'Continue with single sign-on', signOut: 'Sign out', newUser: 'New user',
      name: 'Name', dirLogin: 'Directory login name (optional)', cancel: 'Cancel',
    },
    ar: {
      switchTo: null, userName: 'اسم المستخدم', passLabel: 'كلمة المرور', signIn: 'تسجيل الدخول', admin: 'الإدارة',
      dashboard: 'لوحة المعلومات', roles: 'الأدوار والصلاحيات', newRole: 'دور جديد', code: 'الرمز',
      nameEn: 'الاسم بالإنجليزية', nameAr: 'الاسم بالعربية', createRole: 'إنشاء واختيار الصلاحيات',
      adminArea: 'فتح منطقة إدارة الوصول', viewUsers: 'عرض حسابات المستخدمين',
      saveRole: 'حفظ التغييرات', users: 'المستخدمون', grants: 'الصلاحيات',
      saveGrants: 'حفظ الصلاحيات', importUsers: 'استيراد المستخدمين', preview: 'معاينة', commit: /^إنشاء \d+ حساب$/,
      sso: 'الدخول الموحد', ssoContinue: 'المتابعة بالدخول الموحد', signOut: 'تسجيل الخروج', newUser: 'مستخدم جديد',
      name: 'الاسم', dirLogin: 'اسم الدخول في الدليل (اختياري)', cancel: 'إلغاء',
    },
  };

  const L = LABELS[RUN.lang];
  const suffix = `${RUN.lang}-${RUN.view}`;
  const results = { run: suffix, rows: {}, shots: [], horizontalScroll: [], notes: [] };
  const secrets = await (await page.request.get(RELAY)).json();
  const browser = page.context().browser();

  const settle = (p) => p.waitForTimeout(SETTLE_MS);
  // Each nav link's text starts with its icon glyph, so match by "contains", not equality.
  const navLinks = async (p) => (await p.getByRole('banner').getByRole('navigation').getByRole('link').allInnerTexts())
    .map((t) => t.replace(/\s+/g, ' ').trim());
  const hasLink = (links, label) => links.some((t) => t.includes(label));
  // In-app navigation through the router (popstate), not a reload, so the in-memory session is kept.
  const routerGo = (p, path) => p.evaluate((to) => { history.pushState({}, '', to); dispatchEvent(new PopStateEvent('popstate')); }, path);
  // Long screens (the tree editors, the import preview and its result) are captured full page.
  async function shot(p, name, fullPage = false) {
    await settle(p);
    const file = `${name}-${suffix}.png`;
    await p.screenshot({ path: EVIDENCE_DIR + file, fullPage });
    const overflow = await p.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    if (overflow) results.horizontalScroll.push(file);
    results.shots.push(file);
  }
  async function newPage() {
    const ctx = await browser.newContext({ viewport: VIEWPORTS[RUN.view] });
    const p = await ctx.newPage();
    p.setDefaultTimeout(STEP_TIMEOUT_MS);
    await p.goto(`${WEB}/login`);
    if (L.switchTo) await p.getByRole('button', { name: L.switchTo, exact: true }).click();
    return { ctx, p };
  }
  async function basicLogin(p, user, password) {
    await p.getByRole('textbox', { name: L.userName, exact: true }).fill(user);
    await p.getByRole('textbox', { name: L.passLabel, exact: true }).fill(password);
    await p.getByRole('tabpanel').getByRole('button', { name: L.signIn, exact: true }).click();
    await p.waitForURL((u) => !u.pathname.startsWith('/login'));
    await settle(p);
  }
  async function openAdmin(p, card) {
    await p.getByRole('banner').getByRole('link', { name: L.admin, exact: true }).click();
    await p.getByRole('main').getByRole('link', { name: card, exact: true }).click();
    await settle(p);
  }
  async function until(predicate) {
    const start = Date.now();
    while (Date.now() - start < LIVE_SYNC_TIMEOUT_MS) {
      if (await predicate()) return Date.now() - start;
      await new Promise((r) => setTimeout(r, POLL_MS));
    }
    return -1;
  }

  const admin = await newPage();
  const viewer = await newPage();
  const viewer403 = [];
  viewer.p.on('response', (r) => { if (r.status() === 403) viewer403.push(`${r.request().method()} ${new URL(r.url()).pathname}`); });
  try {
    try {
      // Scenario 2: role, grant, live menu, revoke, 403.
      await basicLogin(admin.p, 'breakglass', secrets.breakglass);
      await basicLogin(viewer.p, VIEWER, secrets.viewer);
      const menuBefore = await navLinks(viewer.p);
      await shot(viewer.p, '02c-viewer-menu-before-grant');

      const roleCode = `p3-viewer-${RUN.tag}`;
      const roleName = `P3 viewer ${RUN.tag}`;
      const roleNameAr = `مراجع ${RUN.tag}`;
      const roleLabel = RUN.lang === 'ar' ? roleNameAr : roleName;
      await openAdmin(admin.p, L.roles);
      await admin.p.getByRole('button', { name: L.newRole, exact: true }).click();
      const dialog = admin.p.getByRole('dialog');
      await dialog.getByRole('textbox', { name: L.code, exact: true }).fill(roleCode);
      await dialog.getByRole('textbox', { name: L.nameEn, exact: true }).fill(roleName);
      await dialog.getByRole('textbox', { name: L.nameAr, exact: true }).fill(roleNameAr);
      await shot(admin.p, '02a-role-create-dialog');
      await dialog.getByRole('button', { name: L.createRole, exact: true }).click();
      await admin.p.waitForURL(/\/admin\/access\/roles\/m1_/);
      // The Administration entry needs access.admin.view (shell-nav.ts); users.view opens the users screen.
      await admin.p.getByRole('checkbox', { name: L.adminArea, exact: true }).check();
      await admin.p.getByRole('checkbox', { name: L.viewUsers, exact: true }).check();
      await admin.p.getByRole('button', { name: L.saveRole, exact: true }).click();
      await shot(admin.p, '02b-role-editor-tree', true);

      await openAdmin(admin.p, L.users);
      await shot(admin.p, '02d-users-list');
      await admin.p.getByRole('row', { name: new RegExp(`^${VIEWER.replace('.', '\\.')} `) })
        .getByRole('link', { name: L.grants, exact: true }).click();
      await admin.p.waitForURL(/\/grants$/);
      await settle(admin.p);
      await admin.p.getByRole('checkbox', { name: roleLabel, exact: true }).check();
      await admin.p.getByRole('button', { name: L.saveGrants, exact: true }).click();
      await shot(admin.p, '02e-user-grants-role-assigned', true);

      const grantMs = await until(async () => hasLink(await navLinks(viewer.p), L.admin));
      const menuAfterGrant = await navLinks(viewer.p);
      await shot(viewer.p, '02f-viewer-menu-after-grant-no-relogin');
      await openAdmin(viewer.p, L.users);
      const viewerUsersUrl = new URL(viewer.p.url()).pathname;
      await shot(viewer.p, '02g-viewer-opens-users-after-grant');

      await admin.p.getByRole('checkbox', { name: roleLabel, exact: true }).uncheck();
      await admin.p.getByRole('button', { name: L.saveGrants, exact: true }).click();
      await settle(admin.p);
      // The viewer stays on /admin/users; the background sync must drop the menu entry without a relogin.
      const revokeMs = await until(async () => !hasLink(await navLinks(viewer.p), L.admin));
      const menuAfterRevoke = await navLinks(viewer.p);
      await shot(viewer.p, '02i-viewer-menu-after-revoke');
      // The open users screen asks the API again (search): the call must answer 403.
      const usersCall = viewer.p.waitForResponse((r) => new URL(r.url()).pathname === '/api/admin/users');
      await viewer.p.getByRole('main').getByRole('searchbox').fill('dev');
      const usersCallStatus = (await usersCall).status();
      await settle(viewer.p);
      // Opening the admin route again through the router (a different URL: the router ignores a same-URL
      // navigation) must land on /unauthorized.
      await routerGo(viewer.p, '/admin');
      await until(async () => new URL(viewer.p.url()).pathname === '/unauthorized');
      const viewerRevokedUrl = new URL(viewer.p.url()).pathname;
      await shot(viewer.p, '02h-viewer-after-revoke-403');
      results.rows.roleGrantRevoke = {
        pass: !hasLink(menuBefore, L.admin) && grantMs >= 0 && hasLink(menuAfterGrant, L.admin)
          && viewerUsersUrl === '/admin/users' && revokeMs >= 0 && !hasLink(menuAfterRevoke, L.admin)
          && usersCallStatus === 403 && viewerRevokedUrl === '/unauthorized',
        menuBefore, menuAfterGrant, grantMs, viewerUsersUrl, revokeMs, menuAfterRevoke, usersCallStatus, viewerRevokedUrl,
        api403: [...new Set(viewer403)],
      };

      // Scenario 3: Excel import, 3 rows, 1 rejected, 2 accounts with activation links.
      await openAdmin(admin.p, L.importUsers);
      await admin.p.locator('input[type=file]').setInputFiles(RUN.workbook);
      await admin.p.getByRole('button', { name: L.preview, exact: true }).click();
      const previewTable = admin.p.getByRole('main').getByRole('table');
      await previewTable.waitFor();
      const previewRows = await previewTable.getByRole('rowgroup').nth(1).getByRole('row').count();
      const rejected = await previewTable.getByRole('row').filter({ has: admin.p.getByRole('listitem') }).allInnerTexts();
      await shot(admin.p, '03a-import-preview', true);
      await admin.p.getByRole('button', { name: L.commit }).click();
      await settle(admin.p);
      const links = await admin.p.getByRole('main').getByText('/activate', { exact: false }).count();
      const inputsWithLinks = await admin.p.getByRole('main').locator('input, code, a')
        .evaluateAll((els) => els.map((e) => e.value || e.textContent || e.href || '').filter((v) => v.includes('/activate')).length);
      await shot(admin.p, '03b-import-commit-links', true);
      results.rows.import = {
        pass: previewRows === 3 && rejected.length === 1 && Math.max(links, inputsWithLinks) === 2,
        previewRows, rejectedRow: rejected.map((t) => t.replace(/\s+/g, ' ').trim()), activationLinks: Math.max(links, inputsWithLinks),
      };

      // Scenario 4 setup screen: the SSO account dialog (dev.admin exists already; the dialog is cancelled).
      await openAdmin(admin.p, L.users);
      await admin.p.getByRole('button', { name: L.newUser, exact: true }).click();
      const userDialog = admin.p.getByRole('dialog');
      await userDialog.getByRole('textbox', { name: L.userName, exact: true }).fill('dev.admin');
      await userDialog.getByRole('textbox', { name: L.name, exact: true }).fill('Dev Admin');
      await userDialog.getByRole('radio', { name: L.sso, exact: true }).check();
      await userDialog.getByRole('textbox', { name: L.dirLogin, exact: true }).fill('dev.admin');
      await shot(admin.p, '04a-create-sso-user');
      await userDialog.getByRole('button', { name: L.cancel, exact: true }).click();
    } catch (error) {
      results.notes.push(`admin/viewer part stopped: ${String(error).slice(0, 400)}`);
    }
  } finally {
    await viewer.ctx.close();
    await admin.ctx.close();
  }

  // Scenario 4: SSO code flow through the dev IdP, then logout through end_session.
  const sso = await newPage();
  const trace = [];
  // Every top-level navigation request (redirect hops included) plus the SPA's logout call, query strings dropped.
  sso.p.on('request', (r) => {
    const u = new URL(r.url());
    const isLogout = u.pathname === '/api/auth/sso/logout';
    if ((r.isNavigationRequest() && r.frame() === sso.p.mainFrame()) || isLogout) {
      trace.push(`${r.method()} ${u.host}${u.pathname}`);
    }
  });
  try {
    await sso.p.getByRole('tab', { name: L.sso, exact: true }).click();
    await shot(sso.p, '04b-sso-tab');
    await sso.p.getByRole('tabpanel').getByRole('button', { name: L.ssoContinue, exact: true }).click();
    await sso.p.waitForURL(/localhost:5190\/authorize/);
    await shot(sso.p, '04c-devidp-picker');
    await sso.p.getByRole('button', { name: SSO_USER_BUTTON }).click();
    await sso.p.waitForURL((u) => u.port === '4280' && u.pathname === '/');
    await sso.p.getByRole('main').getByRole('heading', { level: 1 }).waitFor();
    const signedInDir = await sso.p.evaluate(() => document.documentElement.dir);
    await shot(sso.p, '04d-sso-signed-in');
    await sso.p.getByRole('banner').getByRole('button').last().click();
    await sso.p.getByRole('menuitem', { name: L.signOut, exact: true }).click();
    await sso.p.waitForURL((u) => u.port === '4280' && u.pathname === '/login');
    await shot(sso.p, '04e-sso-logout-back-to-login');
    const path = trace.join(' > ');
    results.rows.sso = {
      pass: path.includes('localhost:5190/authorize') && path.includes('/api/auth/sso/signin-oidc')
        && path.includes('/auth/sso/callback') && path.includes('localhost:5190/end-session') && path.endsWith('GET localhost:4280/login'),
      signedInDir, trace: path,
    };
  } catch (error) {
    results.notes.push(`sso part stopped: ${String(error).slice(0, 400)}`);
    results.rows.ssoTrace = trace.join(' > ');
  } finally {
    await sso.ctx.close();
  }
  return results;
}
