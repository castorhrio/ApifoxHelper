'use strict';

// ---------- 基础工具：所有动态内容都用 textContent 写入，不拼接 HTML ----------

const TOKEN = document.querySelector('meta[name="helper-session"]').content;
const S = {
  view: 'tasks', profiles: [], tasks: [], taskId: null, tab: 'source', profileId: null, session: null, task: null, job: null,
  // 页面重绘之间需要保留的界面状态（按任务区分）：差异页的勾选、源码页的筛选与当前位置。
  selection: {}, sourceView: {},
};

function h(tag, props, ...children) {
  const el = document.createElement(tag);
  if (props) {
    for (const [k, v] of Object.entries(props)) {
      if (v === undefined || v === null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
      else if (k === 'style') el.style.cssText = v; // 通过 CSSOM 设置，符合 CSP（不使用内联 style 属性）
      else if (k === 'value') el.value = v;
      else if (k === 'checked') el.checked = !!v;
      else if (k === 'disabled') el.disabled = !!v;
      else el.setAttribute(k, v === true ? '' : String(v));
    }
  }
  return add(el, ...children);
}

/** 追加子节点：数组展开，null/undefined/false 跳过（DOM 的 append 会把它们写成 "null" 或 "[object …]" 文本）。 */
function add(el, ...children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return el;
}

function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); return el; }

/** 清空后填入新内容。 */
function fill(el, ...children) { return add(clear(el), ...children); }

async function api(method, path, body, raw) {
  const init = { method, headers: { 'X-Helper-Session': TOKEN } };
  if (body !== undefined) {
    if (raw) { init.body = body; init.headers['Content-Type'] = 'application/json; charset=utf-8'; }
    else { init.body = JSON.stringify(body); init.headers['Content-Type'] = 'application/json; charset=utf-8'; }
  }
  const res = await fetch(path, init);
  const text = await res.text();
  let data = null;
  if (text) { try { data = JSON.parse(text); } catch { data = text; } }
  if (!res.ok) throw new Error((data && data.error) || ('请求失败：HTTP ' + res.status));
  return data;
}

async function download(path, fallbackName) {
  const res = await fetch(path, { headers: { 'X-Helper-Session': TOKEN } });
  if (!res.ok) {
    let message = 'HTTP ' + res.status;
    try { message = (await res.json()).error || message; } catch { /* ignore */ }
    throw new Error(message);
  }
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') || '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const a = h('a', { href: URL.createObjectURL(blob), download: match ? decodeURIComponent(match[1]) : fallbackName });
  document.body.append(a);
  a.click();
  // 桌面端会弹出“另存为”对话框，用户选择位置期间不能提前释放下载内容。
  setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 120000);
}

function toast(message, kind) {
  const el = h('div', { class: 'toast ' + (kind || ''), text: message });
  document.getElementById('toasts').append(el);
  setTimeout(() => el.remove(), kind === 'bad' ? 9000 : 4500);
}

async function guard(fn) {
  try { return await fn(); } catch (e) { toast(e.message || String(e), 'bad'); return undefined; }
}

/**
 * 模态框。动作可带 run：异步执行，返回真值才关闭（失败时保留已填写的内容）；Esc 等同取消。
 */
function modal(title, content, actions) {
  return new Promise(resolve => {
    let busy = false;
    const close = value => { mask.remove(); document.removeEventListener('keydown', onKey); resolve(value); };
    const onKey = e => { if (e.key === 'Escape' && !busy) close(undefined); };
    const buttons = (actions || [{ label: '确定', value: true, primary: true }]).map(a => {
      const button = h('button', { class: a.primary ? 'primary' : (a.danger ? 'danger' : ''), onclick: async () => {
        if (!a.run) { close(a.value); return; }
        if (busy) return;
        busy = true;
        button.disabled = true;
        try {
          const result = await a.run();
          if (result) close(result);
        } finally {
          busy = false;
          button.disabled = false;
        }
      } }, a.label);
      return button;
    });
    const mask = h('div', { class: 'modal-mask' },
      h('div', { class: 'modal' }, h('h2', { text: title }), content,
        h('div', { class: 'row', style: 'justify-content:flex-end;margin-top:14px' }, h('button', { onclick: () => { if (!busy) close(undefined); } }, '取消'), buttons)));
    document.body.append(mask);
    document.addEventListener('keydown', onKey);
  });
}

/** 下拉菜单：把同一类的低频操作收在一个按钮里。 */
function menu(label, items, props) {
  const list = h('div', { class: 'menu-list hidden' });
  const toggle = h('button', Object.assign({}, props, {
    onclick: e => {
      e.stopPropagation();
      const open = list.classList.contains('hidden');
      closeMenus();
      if (open) list.classList.remove('hidden');
    },
  }), label + ' ▾');
  for (const item of items.filter(Boolean)) {
    add(list, h('button', { class: 'menu-item', disabled: item.disabled, onclick: e => { e.stopPropagation(); closeMenus(); item.onclick(); } },
      h('div', { text: item.label }), item.hint ? h('div', { class: 'muted small', text: item.hint }) : null));
  }
  return h('div', { class: 'menu' }, toggle, list);
}

function closeMenus() { document.querySelectorAll('.menu-list').forEach(m => m.classList.add('hidden')); }
document.addEventListener('click', closeMenus);

function pickFile(accept) {
  return new Promise(resolve => {
    const done = file => { input.remove(); resolve(file); };
    const input = h('input', { type: 'file', accept, class: 'hidden', onchange: () => done(input.files[0] || null), oncancel: () => done(null) });
    document.body.append(input);
    input.click();
  });
}

function badge(text, kind) { return h('span', { class: 'badge ' + (kind || ''), text }); }

function fmtTime(value) {
  if (!value) return '';
  const d = new Date(value);
  return isNaN(d) ? String(value) : d.toLocaleString('zh-CN', { hour12: false });
}

function pretty(value) { return JSON.stringify(value, null, 2); }

const STATUS = {
  // 源码阶段
  notStarted: ['未开始', ''], scanned: ['已扫描', 'primary'], reviewing: ['草稿审阅中', 'warn'], planReady: ['注释待应用', 'warn'],
  applied: ['已回写', 'ok'], partiallyApplied: ['部分回写', 'warn'],
  // 生成文档阶段
  notChecked: ['未核对', ''], pending: ['待处理', 'warn'], verified: ['已核对通过', 'ok'], partial: ['部分完成', 'warn'], unavailable: ['不可用', 'bad'],
  // 远端阶段
  snapshotReady: ['已读取', 'primary'], previewable: ['可预览', 'primary'], awaitingConfirm: ['待确认', 'warn'], executing: ['执行中', 'warn'],
  failed: ['失败', 'bad'], expired: ['已过期', 'bad'], interrupted: ['已中断', 'bad'], canceled: ['已取消', ''], offline: ['离线检查', ''],
  // 注释位置
  valid: ['有效', 'ok'], missing: ['缺失', 'bad'], empty: ['空标签', 'bad'], stale: ['失效', 'warn'], inheritDoc: ['继承（待验证）', 'warn'],
  inheritDocUnresolved: ['继承未解析', 'bad'], invalidXml: ['非法 XML', 'bad'], unsupported: ['不支持', 'bad'],
  // 候选
  accepted: ['已接受', 'ok'], deferred: ['暂缓', ''], rejected: ['已拒绝', ''],
  code: ['代码可证实', 'ok'], confirmed: ['人工已确认', 'ok'], needsConfirmation: ['需业务确认', 'warn'], notCovered: ['分析未覆盖', 'bad'],
  // 契约 / 接口
  blocked: ['阻断', 'bad'], new: ['新增', 'primary'], structure: ['结构变化', 'warn'], docs: ['说明变化', 'primary'], unchanged: ['无变化', ''],
  sourceOnly: ['仅源码', 'bad'], changed: ['有变化', 'warn'], unknown: ['未知', 'bad'], mapped: ['已映射', 'ok'], candidate: ['候选绑定', 'warn'], ambiguous: ['同名多个', 'bad'],
  // 动作
  written: ['已写入待核对', 'warn'], writeFailed: ['写入失败', 'bad'], verifyFailed: ['核对失败', 'bad'], conflict: ['冲突', 'bad'], notRun: ['未执行', ''],
  // 风险
  normal: ['普通', ''], incompatible: ['潜在不兼容', 'warn'], manual: ['需人工判断', 'warn'], blocking: ['阻断', 'bad'],
  // 传播
  written2: ['已写入', 'ok'], notWritten: ['未写入', 'bad'], present: ['已出现', 'ok'], different: ['内容不同', 'warn'], noXml: ['无 XML', 'bad'],
  disabled: ['未启用', 'bad'], notRendered: ['生成器不呈现', ''], missingSymbol: ['符号不存在', 'bad'],
};

function status(value) {
  const entry = STATUS[value] || [value || '—', ''];
  return badge(entry[0], entry[1]);
}

// ---------- 后台作业 ----------

async function runJob(startPromise, onDone) {
  const job = await guard(() => startPromise);
  if (!job) return;
  S.job = job;
  renderJobBar(job);
  const poll = async () => {
    let current;
    try { current = await api('GET', '/api/jobs/' + job.id); } catch (e) { toast(e.message, 'bad'); return; }
    renderJobBar(current);
    if (current.state === 'running') { setTimeout(poll, 800); return; }
    S.job = null;
    setTimeout(() => renderJobBar(null), 1500);
    if (current.state === 'succeeded') toast(current.kind + '完成', 'ok');
    else if (current.state === 'canceled') toast(current.kind + '已取消');
    else toast(current.kind + '失败：' + (current.error || '未知错误'), 'bad');
    if (onDone) await onDone(current);
  };
  setTimeout(poll, 500);
}

/** 作业结束后：成功且仍停留在同一任务时跳到结果页签，否则只刷新当前页面。 */
function afterJob(taskId, tab) {
  return job => {
    if (tab && job.state === 'succeeded' && S.view === 'task' && S.taskId === taskId) navigate('task', { tab });
    else render();
  };
}

function renderJobBar(job) {
  const bar = clear(document.getElementById('jobbar'));
  if (!job) { bar.classList.add('hidden'); return; }
  bar.classList.remove('hidden');
  const pct = Math.round((job.progress || 0) * 100);
  add(bar, 
    h('b', { text: job.kind }),
    h('div', { class: 'progress' }, h('div', { style: 'width:' + pct + '%' })),
    h('span', { class: 'muted small', text: (job.progressText || '') + ' ' + pct + '%' }),
    job.state === 'running'
      ? h('button', { onclick: () => guard(() => api('POST', '/api/tasks/' + encodeURIComponent(job.scope) + '/cancel')) }, '取消')
      : status(job.state === 'succeeded' ? 'verified' : job.state === 'failed' ? 'failed' : 'canceled'));
}

// ---------- 导航 ----------

let renderSeq = 0;

function navigate(view, extra) {
  const moved = view !== S.view || (extra && ((extra.taskId && extra.taskId !== S.taskId) || (extra.tab && extra.tab !== S.tab)));
  S.view = view;
  Object.assign(S, extra || {});
  document.querySelectorAll('.nav-item').forEach(b => b.classList.toggle('active',
    b.dataset.view === view || (view === 'task' && b.dataset.view === 'tasks') || (view === 'profile' && b.dataset.view === 'profiles')));
  // 地址栏保留当前位置，刷新页面后回到同一任务与页签。
  const hash = view === 'task' ? '#task=' + encodeURIComponent(S.taskId) + '&tab=' + S.tab
    : view === 'profile' ? '#profile=' + encodeURIComponent(S.profileId || 'new') : '#' + view;
  if (location.hash !== hash) history.replaceState(null, '', hash);
  render(moved);
}

function parseHash() {
  const params = new URLSearchParams(location.hash.slice(1));
  if (params.get('task')) return ['task', { taskId: params.get('task'), tab: TAB_KEYS.includes(params.get('tab')) ? params.get('tab') : 'source' }];
  if (params.get('profile')) return ['profile', { profileId: params.get('profile') === 'new' ? null : params.get('profile') }];
  const view = location.hash.slice(1);
  return [['tasks', 'profiles', 'reports'].includes(view) ? view : 'tasks', {}];
}

/**
 * 先在离屏容器中渲染，完成后再替换页面内容：加载期间旧内容保留（不闪白、不丢滚动位置），
 * 并且较早开始、较晚完成的渲染不会覆盖较新的结果。
 */
async function render(toTop) {
  const seq = ++renderSeq;
  const view = h('div');
  closeMenus();
  try {
    if (S.view === 'profiles') await renderProfiles(view);
    else if (S.view === 'profile') await renderProfileEditor(view);
    else if (S.view === 'task') await renderTask(view);
    else if (S.view === 'reports') await renderReports(view);
    else await renderTasks(view);
  } catch (e) {
    add(view, h('div', { class: 'notice bad', text: e.message }));
  }
  if (seq !== renderSeq) return;
  fill(document.getElementById('main'), view);
  if (toTop) window.scrollTo(0, 0);
}

document.querySelectorAll('.nav-item').forEach(b => b.addEventListener('click', () => navigate(b.dataset.view)));
// 地址栏里手动修改或粘贴的地址（浏览器模式）也要切换页面；navigate 用 replaceState，不会再次触发 hashchange。
window.addEventListener('hashchange', () => { const [view, extra] = parseHash(); navigate(view, extra); });

function lines(text) { return String(text || '').split(/\r?\n/).map(s => s.trim()).filter(Boolean); }

function field(label, input, hint) {
  return [h('label', { text: label }), input, hint ? h('div', { class: 'hint', text: hint }) : null];
}

function jsonOrNull(text, name) {
  const t = String(text || '').trim();
  if (!t) return null;
  try { return JSON.parse(t); } catch { throw new Error(name + ' 不是合法 JSON'); }
}

/** 可点击的统计标签：点击后按该项筛选列表。 */
function chip(label, value, kind, onclick, active) {
  // 色调类名加前缀：可点击的标签是 button，不能与 button.primary 等按钮样式重名。
  return h(onclick ? 'button' : 'span', { class: 'chip' + (kind ? ' tone-' + kind : '') + (active ? ' active' : ''), onclick },
    h('span', { text: label }), h('b', { text: String(value ?? 0) }));
}

// ---------- 项目设置 ----------

function onboarding(hasProfile) {
  const step = (n, title, text, done, action) => h('div', { class: 'onboard-step' + (done ? ' done' : '') },
    h('div', { class: 'onboard-num', text: done ? '✓' : String(n) }),
    h('div', { class: 'grow' }, h('b', { text: title }), h('div', { class: 'muted small', text })),
    action || null);
  return h('div', { class: 'panel onboard' },
    h('h2', { text: '三步开始' }),
    step(1, '新建项目配置', '选择代码仓库与 API 项目，填写 Apifox 项目；保存后会自动核对 CLI、登录状态与项目读取。', hasProfile,
      hasProfile ? null : h('button', { class: 'primary', onclick: () => navigate('profile', { profileId: null }) }, '新建配置')),
    step(2, '创建检查任务', '选择模块或路径范围；创建后可以直接开始扫描源码注释（只读，不执行 build/restore）。', false,
      hasProfile ? h('button', { class: 'primary', onclick: () => newTaskDialog() }, '新建任务') : null),
    step(3, '按“下一步”提示完成', '审阅注释草稿 → 回写注释 → 核对生成文档 → 检查 Apifox 差异 → 确认同步 → 下载报告。任务页顶部会提示当前该做什么。', false));
}

async function renderProfiles(main) {
  const list = await api('GET', '/api/profiles');
  S.profiles = list;
  add(main, h('div', { class: 'row spread' }, h('h1', { text: '项目设置' }), h('button', { class: 'primary', onclick: () => navigate('profile', { profileId: null }) }, '新建配置')));
  if (list.length === 0) {
    add(main, onboarding(false));
    return;
  }
  const table = h('table', null, h('thead', null, h('tr', null, ['名称', '代码工作区 / API 项目', 'Apifox 项目 / 分支', 'Swagger', '更新时间', ''].map(t => h('th', { text: t })))));
  const body = h('tbody');
  for (const item of list) {
    const p = item.profile;
    const open = () => navigate('profile', { profileId: p.id });
    add(body, h('tr', { class: 'clickable', onclick: open },
      h('td', null, h('b', { text: p.name })),
      h('td', null, h('div', { class: 'mono', text: p.workspaceRoot }), h('div', { class: 'mono muted', text: p.apiProject })),
      h('td', { text: (p.apifox.projectName || p.apifox.projectId || '未设置') + ' / ' + (p.apifox.branchName || 'main') }),
      h('td', { text: p.swagger.type === 'none' ? '未配置' : p.swagger.type + '：' + (p.swagger.location || '') }),
      h('td', { class: 'small', text: fmtTime(p.updatedAt) }),
      h('td', null, h('button', null, '编辑'))));
  }
  add(table, body);
  add(main, h('div', { class: 'panel' }, table));
}

const GENERATION_MODES = [['none', '仅规则提取与手工填写'], ['openai', 'OpenAI 兼容模型服务'], ['file', '导出材料交给编码助手']];
const ENDPOINT_STATUSES = [['developing', '开发中'], ['designing', '设计中'], ['testing', '测试中'], ['released', '已发布']];

async function renderProfileEditor(main) {
  let item = S.profileId ? await api('GET', '/api/profiles/' + encodeURIComponent(S.profileId)) : null;
  const p = item ? structuredClone(item.profile) : {
    name: '', workspaceRoot: '', apiProject: '', allowedSourcePaths: [], analysis: { configuration: 'Debug', maxTraceDepth: 3, maxMethodsPerEndpoint: 40, suppressAsyncSuffixInActionNames: true, xmlDocPaths: {} },
    generation: { mode: 'none', timeoutSeconds: 120, maxSlotsPerRequest: 30 }, swagger: { type: 'none' }, modules: [], apifox: { readConcurrency: 4 },
    newEndpoint: { status: 'developing', headerParameters: [], tags: [] }, responseWrappers: [], serialization: { namingPolicy: 'camelCase' }, quality: { genericDataProperty: 'data', requireParameterDescription: true, requirePropertyDescription: true },
  };
  const f = {};
  const input = (key, value, type, extra) => (f[key] = h('input', Object.assign({ type: type || 'text', value: value ?? '' }, extra)));
  const area = (key, value, cls) => (f[key] = h('textarea', { class: cls || '', text: value ?? '' }));
  const select = (key, value, options) => (f[key] = h('select', null, options.map(([v, t]) => h('option', { value: v, text: t, selected: v === value }))));
  const check = (key, value) => (f[key] = h('input', { type: 'checkbox', checked: value }));
  // 折叠的高级设置在标题上显示当前取值，不展开也知道是否需要调整。
  const section = (title, summary, open, ...content) => h('details', { class: 'panel section', open },
    h('summary', null, h('b', { text: title }), summary ? h('span', { class: 'muted small', text: summary }) : null), h('div', { class: 'form-grid' }, content));

  const projectPick = h('select', { class: 'hidden picker', onchange: () => { if (projectPick.value) f.apiProject.value = projectPick.value; } });
  const projectSelect = h('select', { class: 'hidden picker' });
  const branchSelect = h('select', { class: 'hidden picker' });
  const capabilityBox = h('div');
  const savedNotice = h('div');
  const canPick = !!(S.session && S.session.canPickFolder);
  const wrappers = p.responseWrappers || [];
  const modeLabel = (GENERATION_MODES.find(([v]) => v === p.generation.mode) || [, p.generation.mode])[1];

  add(main, 
    h('div', { class: 'row' }, h('button', { class: 'link', onclick: () => navigate('profiles') }, '← 返回项目设置')),
    h('h1', { text: item ? '编辑配置：' + p.name : '新建配置' }),
    savedNotice,
    h('div', { class: 'panel' },
      h('h2', { text: '代码工作区' }),
      h('div', { class: 'form-grid' },
        field('配置名称', input('name', p.name, 'text', { placeholder: '例如：PNOS 后端' })),
        field('仓库根目录', h('div', { class: 'row' },
          h('div', { class: 'grow' }, input('workspaceRoot', p.workspaceRoot, 'text', { placeholder: 'C:\\repo\\my-service', onchange: () => { if (!f.apiProject.value.trim()) findProjects(true); } })),
          canPick ? h('button', { onclick: () => browseRoot() }, '浏览…') : null,
          h('button', { onclick: () => findProjects(false) }, '查找 API 项目')), '绝对路径；只读取源码，不执行 build/restore'),
        field('API 项目', h('div', null, input('apiProject', p.apiProject, 'text', { placeholder: 'src/Api/Api.csproj' }), projectPick), '相对仓库根目录的 .csproj；“查找 API 项目”会列出仓库中的项目，Web 项目排在前面'),
        field('允许回写范围', area('allowedSourcePaths', (p.allowedSourcePaths || []).join('\n')), '每行一个相对路径前缀，例如 PNOS.API/Controllers；为空时只能导出补丁，不会改源码'),
        field('版本备注', input('sourceVersionNote', p.sourceVersionNote, 'text', { placeholder: '可选' }), '提交号、构建号或版本描述；为空时报告显示“未绑定代码版本”'))),
    h('div', { class: 'panel' },
      h('h2', { text: 'Swagger 与模块' }),
      h('div', { class: 'form-grid' },
        field('来源类型', select('swaggerType', p.swagger.type, [['none', '暂不配置（源码阶段可为空）'], ['file', '本地 JSON 文件'], ['url', 'JSON 地址（本机读取）']])),
        field('位置', input('swaggerLocation', p.swagger.location), '文件可写相对仓库路径；地址需是 swagger.json，不是 Swagger UI 页面'),
        field('模块范围', area('modules', (p.modules || []).map(m => m.name + '=' + m.pathPrefixes.join(',')).join('\n')), '每行：模块名=路径前缀1,路径前缀2，例如 财务=/api/finance/；新建任务时可按模块选择范围'))),
    h('div', { class: 'panel' },
      h('h2', { text: 'Apifox 目标' }),
      h('div', { class: 'form-grid' },
        // 下拉框放在输入框所在的格子里：隐藏的元素不占网格单元，单独成行会让后面的标签错位。
        field('项目 ID', h('div', null, h('div', { class: 'row' }, h('div', { class: 'grow' }, input('projectId', p.apifox.projectId)), h('button', { onclick: () => loadProjects() }, '读取项目列表')), projectSelect), 'Apifox 项目 ID，与业务系统中的“项目”无关；读取列表需要先保存配置'),
        field('分支名', h('div', null, h('div', { class: 'row' }, h('div', { class: 'grow' }, input('branch', p.apifox.branchName, 'text', { placeholder: '留空表示主分支' })), h('button', { onclick: () => loadBranches() }, '读取分支')), branchSelect)))),
    section('序列化与响应封装', (p.serialization.namingPolicy === 'none' ? '保持属性名' : 'camelCase') + ' · ' + (wrappers.length ? '通用封装 ' + wrappers.map(w => w.typeName).join('、') : '未配置通用封装'), !item,
      field('JSON 命名', select('naming', p.serialization.namingPolicy, [['camelCase', 'camelCase（ASP.NET Core 默认）'], ['none', '保持 C# 属性名']])),
      field('枚举输出为字符串', check('enumString', p.serialization.enumAsString)),
      field('非空引用类型', check('nnrt', p.serialization.nonNullableReferenceTypes), '与 Swashbuckle SupportNonNullableReferenceTypes 一致'),
      field('通用响应封装', area('wrappers', wrappers.map(w => w.typeName + '=' + w.dataMember).join('\n')), '每行：类型名=数据属性名，例如 PNOSActionResult=Data；用于追踪 data 的实际类型（响应契约）')),
    section('代码分析', p.analysis.configuration + ' · 调用深度 ' + p.analysis.maxTraceDepth + ' · 方法预算 ' + p.analysis.maxMethodsPerEndpoint, false,
      field('分析配置名', input('configuration', p.analysis.configuration)),
      field('编译常量', input('symbols', (p.analysis.preprocessorSymbols || []).join(';')), '留空按目标框架与配置推导（DEBUG;TRACE;NET8_0…）'),
      field('调用深度 / 方法预算', h('div', { class: 'row' }, input('depth', p.analysis.maxTraceDepth, 'number'), input('budget', p.analysis.maxMethodsPerEndpoint, 'number'))),
      field('去掉 Async 后缀', check('suppressAsync', p.analysis.suppressAsyncSuffixInActionNames), '与 MvcOptions.SuppressAsyncSuffixInActionNames 一致（默认开启）'),
      field('XML 文档路径', area('xmlDocPaths', Object.entries(p.analysis.xmlDocPaths || {}).map(([k, v]) => k + '=' + v).join('\n')), '可选：项目名=路径，每行一个；默认按 bin/配置/目标框架 推断')),
    section('注释草稿生成', modeLabel + (p.generation.mode === 'openai' && p.generation.model ? '：' + p.generation.model : '') + (item && item.hasModelKey ? ' · 密钥已保存' : ''), false,
      field('生成方式', select('genMode', p.generation.mode, GENERATION_MODES)),
      field('服务地址 / 模型', h('div', { class: 'row' }, input('baseUrl', p.generation.baseUrl), input('model', p.generation.model)), '例如 https://api.openai.com/v1；只发送用户确认的材料'),
      field('超时秒数 / 每批数量', h('div', { class: 'row' }, input('timeout', p.generation.timeoutSeconds, 'number'), input('batch', p.generation.maxSlotsPerRequest, 'number'))),
      field('服务密钥', h('div', { class: 'row' }, f.key = h('input', { type: 'password', placeholder: item && item.hasModelKey ? '已保存（输入新值可替换）' : '未保存' }), h('button', { disabled: !item, title: item ? '' : '先保存配置', onclick: () => saveKey() }, '保存密钥')), '用 Windows DPAPI 加密保存在本机，不会显示、导出或写入报告')),
    section('新接口模板', '状态 ' + ((ENDPOINT_STATUSES.find(([v]) => v === p.newEndpoint.status) || [, p.newEndpoint.status])[1]) + (p.newEndpoint.folderId ? ' · 目录 ' + p.newEndpoint.folderId : ' · 未指定目录'), false,
      h('div', { class: 'hint wide', text: '只在 Apifox 中新建接口时使用；只复制列出的字段。' }),
      field('目录 ID / 状态', h('div', { class: 'row' }, input('folderId', p.newEndpoint.folderId), select('epStatus', p.newEndpoint.status, ENDPOINT_STATUSES))),
      field('服务 ID / 标签', h('div', { class: 'row' }, input('serverId', p.newEndpoint.serverId), input('tags', (p.newEndpoint.tags || []).join(',')))),
      field('鉴权 auth', area('auth', p.newEndpoint.auth ? pretty(p.newEndpoint.auth) : '', 'code')),
      field('安全方案 securityScheme', area('security', p.newEndpoint.securityScheme ? pretty(p.newEndpoint.securityScheme) : '', 'code')),
      field('公共 Header 参数', area('headers', (p.newEndpoint.headerParameters || []).length ? pretty(p.newEndpoint.headerParameters) : '', 'code')),
      field('从现有接口提取', h('div', { class: 'row' }, f.templateEndpoint = h('input', { type: 'text', placeholder: '接口 ID' }), h('button', { onclick: () => extractTemplate() }, '提取上述字段')), '只读取目录、状态、服务、鉴权、安全方案、Header 参数与标签')),
    section('质量规则', '参数说明' + (p.quality.requireParameterDescription ? '必填' : '可选') + ' · 字段说明' + (p.quality.requirePropertyDescription ? '必填' : '可选'), false,
      field('通用 data 属性名', input('dataProp', p.quality.genericDataProperty)),
      field('参数说明必填', check('qParam', p.quality.requireParameterDescription)),
      field('字段说明必填', check('qProp', p.quality.requirePropertyDescription))),
    section('Apifox CLI', '读取并发 ' + (p.apifox.readConcurrency || 4) + ' · ' + (p.apifox.nodePath || p.apifox.cliScriptPath ? '指定路径' : '自动查找'), false,
      field('读取并发', input('concurrency', p.apifox.readConcurrency, 'number'), '只读详情的并发数（1–8）；写入始终串行'),
      field('node / cli.js 路径', h('div', { class: 'row' }, input('nodePath', p.apifox.nodePath), input('cliPath', p.apifox.cliScriptPath)), '可选；为空时自动查找 PATH 与全局 npm 目录')),
    h('div', { class: 'sticky-actions' },
      h('button', { class: 'primary', onclick: () => save() }, item ? '保存' : '保存并检查连接'),
      item ? h('button', { onclick: () => guard(() => download('/api/profiles/' + p.id + '/export', 'profile.json')) }, '导出配置（不含凭据）') : null,
      h('span', { class: 'muted small', text: '保存后会核对 CLI、登录状态与项目读取（只读）。' })),
    capabilityBox);

  if (S.profileJustCreated && item && S.profileJustCreated === item.profile.id) {
    S.profileJustCreated = null;
    add(savedNotice, h('div', { class: 'notice ok row spread' }, h('span', { text: '配置已保存。下一步：为它创建检查任务。' }),
      h('button', { class: 'primary', onclick: () => { navigate('tasks'); newTaskDialog(item.profile.id); } }, '新建任务')));
  }

  function collect() {
    const wrappersValue = lines(f.wrappers.value).map(l => { const [t, d] = l.split('='); return { typeName: t.trim(), dataMember: (d || 'Data').trim() }; });
    const modules = lines(f.modules.value).map(l => { const [n, prefixes] = l.split('='); return { name: n.trim(), pathPrefixes: String(prefixes || '').split(',').map(s => s.trim()).filter(Boolean) }; });
    const xmlDocPaths = {};
    lines(f.xmlDocPaths.value).forEach(l => { const i = l.indexOf('='); if (i > 0) xmlDocPaths[l.slice(0, i).trim()] = l.slice(i + 1).trim(); });
    const symbols = f.symbols.value.split(/[;,]/).map(s => s.trim()).filter(Boolean);
    return {
      id: p.id, name: f.name.value, sourceVersionNote: f.sourceVersionNote.value || null,
      workspaceRoot: f.workspaceRoot.value, apiProject: f.apiProject.value, allowedSourcePaths: lines(f.allowedSourcePaths.value),
      analysis: { configuration: f.configuration.value || 'Debug', preprocessorSymbols: symbols.length ? symbols : null, maxTraceDepth: +f.depth.value || 3, maxMethodsPerEndpoint: +f.budget.value || 40, xmlDocPaths, suppressAsyncSuffixInActionNames: f.suppressAsync.checked },
      generation: { mode: f.genMode.value, baseUrl: f.baseUrl.value || null, model: f.model.value || null, timeoutSeconds: +f.timeout.value || 120, maxSlotsPerRequest: +f.batch.value || 30 },
      swagger: { type: f.swaggerType.value, location: f.swaggerLocation.value || null },
      modules,
      apifox: { projectId: f.projectId.value || null, projectName: p.apifox.projectName || null, branchName: f.branch.value || null, branchId: p.apifox.branchId || null, nodePath: f.nodePath.value || null, cliScriptPath: f.cliPath.value || null, readConcurrency: +f.concurrency.value || 4 },
      newEndpoint: {
        folderId: f.folderId.value ? +f.folderId.value : null, status: f.epStatus.value, serverId: f.serverId.value || null,
        auth: jsonOrNull(f.auth.value, '鉴权 auth'), securityScheme: jsonOrNull(f.security.value, '安全方案'),
        headerParameters: jsonOrNull(f.headers.value, '公共 Header 参数') || [], tags: f.tags.value.split(',').map(s => s.trim()).filter(Boolean),
      },
      responseWrappers: wrappersValue,
      serialization: { namingPolicy: f.naming.value, enumAsString: f.enumString.checked, nonNullableReferenceTypes: f.nnrt.checked },
      quality: { genericDataProperty: f.dataProp.value || 'data', requireParameterDescription: f.qParam.checked, requirePropertyDescription: f.qProp.checked },
    };
  }

  async function save() {
    const body = await guard(async () => collect());
    if (!body) return;
    const saved = await guard(() => item ? api('PUT', '/api/profiles/' + p.id, body) : api('POST', '/api/profiles', body));
    if (!saved) return;
    toast('配置已保存', 'ok');
    if (!item) S.profileJustCreated = saved.id;
    // 重新打开编辑页：标题、折叠摘要与连接检查都按保存后的配置显示。
    navigate('profile', { profileId: saved.id });
  }

  async function saveKey() {
    if (!item) return;
    const result = await guard(() => api('PUT', '/api/profiles/' + p.id + '/model-key', { key: f.key.value }));
    if (result) { toast(result.hasModelKey ? '密钥已加密保存' : '密钥已清除', 'ok'); f.key.value = ''; item.hasModelKey = result.hasModelKey; }
  }

  async function browseRoot() {
    const result = await guard(() => api('POST', '/api/local/pick-folder', { initial: f.workspaceRoot.value.trim() || null }));
    if (!result || !result.path) return;
    f.workspaceRoot.value = result.path;
    if (!f.name.value.trim()) f.name.value = result.path.split(/[\\/]/).filter(Boolean).pop() || '';
    await findProjects(true);
  }

  async function findProjects(auto) {
    const root = f.workspaceRoot.value.trim();
    if (!root) { if (!auto) toast('请先填写仓库根目录', 'bad'); return; }
    const result = await (auto ? api('GET', '/api/local/projects?root=' + encodeURIComponent(root)).catch(() => null) : guard(() => api('GET', '/api/local/projects?root=' + encodeURIComponent(root))));
    if (!result) return;
    if (!result.projects.length) {
      projectPick.classList.add('hidden');
      if (!auto) toast('该目录下没有找到 .csproj', 'bad');
      return;
    }
    const current = f.apiProject.value.trim();
    fill(projectPick, 
      h('option', { value: '', text: '从仓库中选择（共 ' + result.projects.length + ' 个' + (result.truncated ? '，只列出前一部分' : '') + '）…' }),
      result.projects.map(x => h('option', { value: x.path, text: x.path + (x.web ? '　· Web 项目' : ''), selected: x.path === current })));
    projectPick.classList.remove('hidden');
    const web = result.projects.filter(x => x.web);
    if (!current && web.length === 1) {
      f.apiProject.value = web[0].path;
      projectPick.value = web[0].path;
      toast('已选择 Web 项目 ' + web[0].path + '，如不对可在下拉框中更换', 'ok');
    }
  }

  async function loadProjects() {
    if (!item) { toast('请先保存配置', 'bad'); return; }
    const projects = await guard(() => api('GET', '/api/apifox/projects?profileId=' + p.id));
    if (!projects) return;
    fill(projectSelect, h('option', { value: '', text: '选择项目…' }), projects.map(x => h('option', { value: x.id, text: x.name + '（' + x.id + '）' + (x.teamName ? ' · ' + x.teamName : '') })));
    projectSelect.classList.remove('hidden');
    projectSelect.onchange = () => {
      const chosen = projects.find(x => x.id === projectSelect.value);
      if (chosen) { f.projectId.value = chosen.id; p.apifox.projectName = chosen.name; }
    };
  }

  async function loadBranches() {
    if (!item) { toast('请先保存配置', 'bad'); return; }
    const projectId = f.projectId.value;
    if (!projectId) { toast('请先填写项目 ID', 'bad'); return; }
    const branches = await guard(() => api('GET', '/api/apifox/branches?profileId=' + p.id + '&projectId=' + encodeURIComponent(projectId)));
    if (!branches) return;
    fill(branchSelect, h('option', { value: '', text: '选择分支…' }), branches.filter(b => !b.isArchived).map(b => h('option', { value: b.isMain ? '' : b.name, text: b.name + (b.isMain ? '（主分支）' : '') + ' · ID ' + b.id })));
    branchSelect.classList.remove('hidden');
    branchSelect.onchange = () => {
      f.branch.value = branchSelect.value;
      const chosen = branches.find(b => (b.isMain ? '' : b.name) === branchSelect.value);
      p.apifox.branchId = chosen ? chosen.id : null;
    };
  }

  async function extractTemplate() {
    if (!item) { toast('请先保存配置', 'bad'); return; }
    const id = f.templateEndpoint.value.trim();
    if (!/^\d+$/.test(id)) { toast('请输入接口 ID', 'bad'); return; }
    const t = await guard(() => api('GET', '/api/apifox/template-from-endpoint?profileId=' + p.id + '&endpointId=' + id));
    if (!t) return;
    f.folderId.value = t.folderId ?? '';
    f.epStatus.value = t.status || 'developing';
    f.serverId.value = t.serverId || '';
    f.tags.value = (t.tags || []).join(',');
    f.auth.value = t.auth ? pretty(t.auth) : '';
    f.security.value = t.securityScheme ? pretty(t.securityScheme) : '';
    f.headers.value = (t.headerParameters || []).length ? pretty(t.headerParameters) : '';
    toast('已填入模板字段，请检查后保存', 'ok');
  }

  async function showCapabilities(id) {
    fill(capabilityBox, h('div', { class: 'notice', text: '正在检查 CLI、登录状态与项目读取…' }));
    const c = await guard(() => api('GET', '/api/capabilities?profileId=' + id));
    clear(capabilityBox);
    if (!c) return;
    const probeFields = {};
    const probeResult = h('div');
    add(capabilityBox, h('div', { class: 'panel' },
      h('div', { class: 'row spread' }, h('h3', { text: '连接检查结果' }), h('button', { onclick: () => showCapabilities(id) }, '重新检查')),
      h('div', { class: 'kv' },
        h('div', { text: 'Apifox CLI' }), h('div', null, c.cli.found ? [badge('可用', 'ok'), ' ', c.cli.version, h('div', { class: 'mono small', text: c.cli.node + ' ' + c.cli.script })] : [badge('不可用', 'bad'), ' ', c.cli.error]),
        h('div', { text: '登录状态' }), h('div', null, c.login ? [badge(c.login.loggedIn ? '已登录' : '未登录', c.login.loggedIn ? 'ok' : 'bad'), ' ', c.login.message] : '—'),
        h('div', { text: '项目读取' }), h('div', null, c.project ? [badge(c.project.readable ? '可读取' : '失败', c.project.readable ? 'ok' : 'bad'), ' ', c.project.readable ? '接口总数 ' + c.project.total : c.project.message] : '未设置项目或未登录'),
        h('div', { text: '写入兼容' }), h('div', null, badge(c.writeCompat.verified ? '已通过探测' : '仅只读', c.writeCompat.verified ? 'ok' : 'warn'), ' ', c.writeCompat.message)),
      h('details', { style: 'margin-top:10px' }, h('summary', { text: '写入兼容探测（同步到 Apifox 前需要通过；在专用测试接口上执行）' }),
        h('div', { class: 'notice warn', text: '探测会对指定接口执行：原样写回说明、临时修改说明并恢复、原样写回参数/请求体/响应，并核对其余字段未变化。请只使用专用测试项目或测试分支中的接口。' }),
        h('div', { class: 'form-grid' },
          field('测试项目 ID', probeFields.project = h('input', { type: 'text', value: f.projectId.value })),
          field('测试分支名', probeFields.branch = h('input', { type: 'text', value: f.branch.value, placeholder: '留空为主分支' })),
          field('测试接口 ID', probeFields.endpoint = h('input', { type: 'text' })),
          field('确认', h('label', null, probeFields.confirm = h('input', { type: 'checkbox' }), ' 我确认这是可修改的测试接口'))),
        h('button', { class: 'primary', onclick: () => runProbe() }, '运行写入探测'),
        probeResult)));

    async function runProbe() {
      const body = { projectId: probeFields.project.value.trim(), branch: probeFields.branch.value.trim() || null, endpointId: +probeFields.endpoint.value, confirm: probeFields.confirm.checked };
      await runJob(api('POST', '/api/profiles/' + id + '/write-probe', body), job => {
        const r = job.result;
        clear(probeResult);
        if (!r) return;
        add(probeResult, h('div', { class: 'notice ' + (r.passed ? 'ok' : 'bad'), text: r.passed ? 'CLI ' + r.cliVersion + ' 写入探测通过，已开放同步写入。' : '探测未通过，写入保持关闭。' }),
          h('table', null, h('tbody', null, (r.checks || []).map(x => h('tr', null, h('td', { text: x.name }), h('td', null, badge(x.passed ? '通过' : '失败', x.passed ? 'ok' : 'bad')), h('td', { text: x.detail }))))));
      });
    }
  }

  if (item) showCapabilities(p.id);
  else if (canPick && !p.workspaceRoot) f.name.focus();
}

// ---------- 检查任务 ----------

function stageBadges(stages) {
  return h('div', { class: 'row small' },
    h('span', { class: 'muted', text: '源码' }), status(stages.source.status),
    h('span', { class: 'muted', text: '生成文档' }), status(stages.generated.status),
    h('span', { class: 'muted', text: 'Apifox' }), status(stages.remote.status));
}

function openTask(task) { navigate('task', { taskId: task.id, tab: nextStep(task).tab }); }

async function renderTasks(main) {
  const [tasks, profiles] = await Promise.all([api('GET', '/api/tasks'), api('GET', '/api/profiles')]);
  S.tasks = tasks;
  S.profiles = profiles;
  add(main, h('div', { class: 'row spread' }, h('h1', { text: '检查任务' }), profiles.length ? h('button', { class: 'primary', onclick: () => newTaskDialog() }, '新建任务') : null));
  if (!profiles.length || !tasks.length) add(main, onboarding(profiles.length > 0));
  if (!tasks.length) return;

  const table = h('table', null, h('thead', null, h('tr', null, ['任务', '配置 / 目标', '阶段', '下一步', '更新时间', ''].map(t => h('th', { text: t })))));
  const body = h('tbody');
  for (const { task, runningJob } of tasks) {
    const next = nextStep(task);
    add(body, h('tr', { class: 'clickable', onclick: () => openTask(task) },
      h('td', null, h('b', { text: task.name }), runningJob ? h('div', null, badge('运行中：' + runningJob.kind, 'warn')) : null),
      h('td', null, task.profileName, h('div', { class: 'muted small', text: (task.apifoxProjectName || task.apifoxProjectId || '未设置') + ' / ' + task.branch })),
      h('td', null, stageBadges(task.stages)),
      h('td', null, h('div', { class: 'next-label ' + (next.tone || ''), text: next.title }), h('div', { class: 'muted small', text: taskCounts(task) })),
      h('td', { class: 'small', text: fmtTime(task.updatedAt || task.createdAt) }),
      h('td', null, h('button', null, '继续'))));
  }
  add(table, body);
  add(main, h('div', { class: 'panel' }, table));
}

function taskCounts(task) {
  const s = task.stages.source;
  const r = task.stages.remote;
  const parts = [];
  if (s.status !== 'notStarted') parts.push('注释有效 ' + s.validSlots + ' / ' + s.slots + (s.accepted ? '，已接受 ' + s.accepted : ''));
  if (r.status !== 'notChecked') parts.push('变化 ' + (r.added + r.structureChanged + r.docChanged) + (r.blocked ? '，阻断 ' + r.blocked : ''));
  return parts.join(' · ');
}

async function newTaskDialog(presetProfileId) {
  // 每次重新读取：刚在设置页新建的配置也要出现在列表里。
  const profiles = await guard(() => api('GET', '/api/profiles'));
  if (!profiles) return;
  S.profiles = profiles;
  if (!profiles.length) { navigate('profile', { profileId: null }); return; }
  const f = {};
  const moduleBox = h('div', { class: 'row' });
  const refreshModules = () => {
    const profile = profiles.find(x => x.profile.id === f.profile.value)?.profile;
    fill(moduleBox, (profile?.modules || []).map(m => h('label', { class: 'nowrap' }, h('input', { type: 'checkbox', value: m.name, 'data-module': '1' }), ' ' + m.name + '（' + m.pathPrefixes.join(', ') + '）')));
    if (!profile?.modules?.length) add(moduleBox, h('span', { class: 'muted small', text: '配置中没有模块，可直接填写路径前缀或接口。' }));
  };
  const content = h('div', null,
    h('div', { class: 'form-grid' },
      field('项目配置', f.profile = h('select', { onchange: refreshModules }, profiles.map(x => h('option', { value: x.profile.id, text: x.profile.name, selected: x.profile.id === presetProfileId })))),
      field('任务名称', f.name = h('input', { type: 'text', placeholder: '例如：财务接口文档更新' })),
      field('模块', moduleBox),
      field('路径前缀', f.prefixes = h('textarea', { placeholder: '每行一个，例如 /api/finance/Bill' }), '与模块取并集；模块和路径都为空时检查 API 项目的全部接口'),
      field('指定接口', f.keys = h('textarea', { placeholder: '每行一个，例如 GET /api/finance/BillList' })),
      field('版本备注', f.note = h('input', { type: 'text', placeholder: '提交号或构建号（可选）' }))),
    h('details', { class: 'small', style: 'margin-top:10px' }, h('summary', { text: '高级选项' }),
      h('label', { class: 'row', style: 'margin-top:6px' }, f.offline = h('input', { type: 'checkbox' }), '离线比较：只比较两份 OpenAPI JSON（不访问 Apifox，同步不可用）')));
  refreshModules();

  const create = async scan => {
    const modules = [...moduleBox.querySelectorAll('input[data-module]:checked')].map(x => x.value);
    const task = await guard(() => api('POST', '/api/tasks', { profileId: f.profile.value, name: f.name.value, moduleNames: modules, pathPrefixes: lines(f.prefixes.value), endpointKeys: lines(f.keys.value), sourceVersionNote: f.note.value, offlineMode: f.offline.checked }));
    if (!task) return false;
    navigate('task', { taskId: task.id, tab: 'source' });
    if (scan) runJob(api('POST', '/api/tasks/' + task.id + '/source-scan'), afterJob(task.id, 'source'));
    return true;
  };
  setTimeout(() => f.name.focus(), 0);
  await modal('新建检查任务', content, [{ label: '仅创建', run: () => create(false) }, { label: '创建并扫描', primary: true, run: () => create(true) }]);
}

// ---------- 任务详情：阶段、下一步与页签 ----------

const TAB_GROUPS = [
  [['source', '源码注释'], ['patch', '注释补丁']],
  [['propagation', '生成文档']],
  [['contracts', '响应契约'], ['remote', 'Apifox 差异'], ['plan', '同步']],
  [['report', '报告']],
];
const TAB_KEYS = TAB_GROUPS.flat().map(([key]) => key);

function startScan(task) {
  runJob(api('POST', '/api/tasks/' + task.id + '/source-scan'), afterJob(task.id, 'source'));
}

/** 配置了 Swagger 但还没冻结时先加载，避免检查在不知情时退化为只按远端现状比较。 */
async function startCheck(task, reloadSwagger) {
  const swaggerType = task.profileSnapshot?.swagger?.type || 'none';
  if (swaggerType !== 'none' && (reloadSwagger || !task.stages.remote.sourceHash)) {
    const info = await guard(() => api('POST', '/api/tasks/' + task.id + '/swagger-load'));
    if (!info) return;
    toast('Swagger 已冻结：OpenAPI ' + info.version + '，' + info.operations + ' 个操作' + (info.problems.length ? '；' + info.problems.join('；') : ''), info.supported ? 'ok' : 'bad');
  }
  runJob(api('POST', '/api/tasks/' + task.id + '/check'), afterJob(task.id, 'remote'));
}

async function makeCommentPlan(task) {
  const plan = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-plan'));
  if (plan) navigate('task', { taskId: task.id, tab: 'patch' });
}

function needsSwaggerLoad(task) {
  const type = task.profileSnapshot?.swagger?.type;
  return !!type && type !== 'none' && !task.stages.remote.sourceHash;
}

/**
 * 根据三个阶段的状态给出唯一的“下一步”。只做引导，不改变任何状态；写入类操作仍在各自页签中确认。
 * 任务列表的摘要不含配置快照，此时按“未知是否配置 Swagger”处理。
 */
function nextStep(task) {
  const s = task.stages.source;
  const g = task.stages.generated;
  const r = task.stages.remote;
  const gaps = Math.max(0, (s.slots || 0) - (s.validSlots || 0));
  const remoteStarted = r.status !== 'notChecked';
  const step = (stage, tab, title, detail, extra) => Object.assign({ stage, tab, title, detail, tone: '' }, extra);
  const check = label => ({ label: label || (needsSwaggerLoad(task) ? '加载 Swagger 并检查' : '开始检查'), run: t => startCheck(t) });
  const skip = remoteStarted ? null : { label: '跳过，检查 Apifox', tab: 'remote' };
  const changed = (r.added || 0) + (r.structureChanged || 0) + (r.docChanged || 0);

  // 远端执行中、被中断或等待确认最紧迫。
  if (r.status === 'executing') return step('remote', 'plan', '同步执行中', '正在逐个写入并回读核对；取消会在当前动作完成后停止。', { primary: { label: '查看同步', tab: 'plan' } });
  if (r.status === 'interrupted') return step('remote', 'plan', '上次同步被中断', r.message || '请重新核对远端实际状态。', { tone: 'bad', primary: { label: '去重新核对', tab: 'plan' } });
  if (r.status === 'awaitingConfirm') return step('remote', 'plan', '确认同步到 Apifox', '同步预览已生成：核对写入清单与写入内容后确认执行。', { primary: { label: '查看同步预览', tab: 'plan' } });

  // 还没开始检查 Apifox：按源码 → 生成文档 → Apifox 的顺序引导。
  if (!remoteStarted) {
    if (s.status === 'notStarted') return step('source', 'source', '扫描源码注释', '只读取源码：定位接口、参数与模型，检查注释缺口并收集代码证据；不需要 Swagger，不执行 build/restore。', { primary: { label: '开始扫描', run: t => startScan(t) }, secondary: skip });
    if (s.status === 'failed') return step('source', 'patch', '注释回写失败', s.message || '查看回写结果，处理后重新生成注释计划。', { tone: 'bad', primary: { label: '查看回写结果', tab: 'patch' } });
    if (s.status === 'planReady') return step('source', 'patch', '应用注释补丁', '注释计划已生成：确认后写回源码，或导出补丁自行应用（自行应用后请重新扫描）。', { primary: { label: '查看补丁', tab: 'patch' }, secondary: skip });
    if (g.status === 'pending' || g.status === 'partial') return step('generated', 'propagation', '核对生成文档', g.message || '重新生成 XML 文档与 Swagger 后，核对注释是否传播到位。', { primary: { label: '去核对', tab: 'propagation' }, secondary: skip });
    const stale = s.message ? s.message + ' ' : '';
    if (s.status === 'reviewing' && s.accepted > 0) return step('source', 'source', '生成注释计划', stale + '已接受 ' + s.accepted + ' 条候选；生成计划后先预览补丁，再决定回写或导出。', { primary: { label: '生成注释计划', run: t => makeCommentPlan(t) }, secondary: { label: '继续审阅', tab: 'source' } });
    if (s.status === 'reviewing') return step('source', 'source', '审阅注释草稿', stale + '共 ' + s.candidates + ' 条候选：查看代码证据后接受，接受的候选才会进入补丁。', { primary: { label: '去审阅', tab: 'source' }, secondary: skip });
    if (gaps > 0) return step('source', 'source', '补全注释', gaps + ' 个位置缺少有效说明：用规则提取、模型生成或手工填写得到候选。', { primary: { label: '去生成草稿', tab: 'source' }, secondary: skip });
    if (task.offlineMode) return step('remote', 'remote', '导入“远端” OpenAPI 并检查', '离线比较：导入作为“远端”的 OpenAPI JSON 后开始检查（不访问 Apifox，同步不可用）。', { primary: { label: '去导入', tab: 'remote' } });
    return step('remote', 'remote', '检查 Apifox 差异', '只读：导出分支概览，再逐个读取范围内接口与模型详情，与 Swagger、响应契约和补充说明构成的目标比较。', { primary: check() });
  }

  // 已开始检查 Apifox 后，源码侧的回写失败、未应用的补丁、回写后未核对的生成文档仍然优先：它们会改变比较的目标。
  const toRemote = { label: 'Apifox 差异', tab: 'remote' };
  if (s.status === 'failed') return step('source', 'patch', '注释回写失败', s.message || '查看回写结果，处理后重新生成注释计划。', { tone: 'bad', primary: { label: '查看回写结果', tab: 'patch' }, secondary: toRemote });
  if (s.status === 'planReady') return step('source', 'patch', '应用注释补丁', '注释计划已生成：回写后重新生成 Swagger，再重新检查 Apifox；也可以导出补丁自行应用（之后请重新扫描）。', { primary: { label: '查看补丁', tab: 'patch' }, secondary: toRemote });
  if (g.status === 'pending') return step('generated', 'propagation', '核对生成文档', (g.message || '源码注释已回写。') + ' 重新生成 XML 文档与 Swagger 后核对，再在 Apifox 差异页“重新加载 Swagger 并检查”。', { primary: { label: '去核对', tab: 'propagation' }, secondary: toRemote });

  // Apifox。检查结果所基于的版本落后于任务（检查后又接受了注释、改了决策或补充说明）时，按过期处理。
  const checkStale = r.checkedRevision != null && task.revision != null && r.checkedRevision !== task.revision && (r.status === 'pending' || r.status === 'previewable');
  if (r.status === 'expired' || checkStale) return step('remote', 'remote', '重新检查', '检查或生成计划之后任务内容有变化，原结果已失效。源码或 Swagger 有更新时，请用“重新加载 Swagger 并检查”。', { tone: 'warn', primary: check('重新检查') });
  if (r.status === 'pending') return step('remote', 'remote', '处理待确认项', '阻断 ' + r.blocked + ' · 说明冲突 ' + r.conflicts + ' · 可同步变化 ' + changed + '：高风险变化逐项确认后保存并重新检查；其余接口可以直接勾选同步。', { tone: 'warn', primary: { label: '去处理', tab: 'remote' } });
  if (r.status === 'previewable' && changed > 0) return step('remote', 'remote', '选择要同步的接口', '新增 ' + r.added + ' · 结构变化 ' + r.structureChanged + ' · 说明变化 ' + r.docChanged + '：勾选后生成同步预览。', { primary: { label: '去选择', tab: 'remote' } });
  if (r.status === 'partial' || r.status === 'failed') return step('remote', 'plan', '同步未全部完成', r.message || '查看失败的动作后重新核对。', { tone: 'bad', primary: { label: '查看结果', tab: 'plan' } });
  if (r.status === 'offline') return step('report', 'report', '离线检查完成', '离线比较不能同步；可以查看差异或下载报告。', { primary: { label: '查看报告', tab: 'report' }, secondary: toRemote });
  if (r.status === 'verified') return step('report', 'report', '同步完成', r.message || '已写入并回读核对通过。', { tone: 'ok', primary: { label: '查看报告', tab: 'report' }, secondary: check('重新检查') });
  return step('report', 'report', 'Apifox 与目标一致', '范围内接口没有需要同步的变化；可以下载报告或前端交接说明。', { tone: 'ok', primary: { label: '查看报告', tab: 'report' }, secondary: toRemote });
}

function stepper(task, next) {
  const s = task.stages.source;
  const gaps = Math.max(0, (s.slots || 0) - (s.validSlots || 0));
  // 源码阶段的完成判定：本页把 partiallyApplied 直接算完成（历史口径，行为不改——本页已冻结，只修阻断性缺陷）。
  // 原生壳（Desktop/Native/StageSteps.cs 的 SourceStageDone）有意收紧为「部分回写且没有缺口才算完成」：
  // 部分回写本身就说明还有位置没写进去，此时进度条打 ✓ 而「下一步」横幅说「补全注释」是同一屏自相矛盾。
  // 这是有意的口径差异，定案见 docs/DESIGN.md §6「源码阶段完成口径」（第一百一十九轮）。
  const stages = [
    ['source', '源码注释', s.status, 'source', ['applied', 'partiallyApplied'].includes(s.status) || (s.status === 'scanned' && gaps === 0)],
    ['generated', '生成文档', task.stages.generated.status, 'propagation', task.stages.generated.status === 'verified'],
    ['remote', 'Apifox', task.stages.remote.status, 'remote', task.stages.remote.status === 'verified'],
  ];
  return h('div', { class: 'stepper' }, stages.map(([key, label, value, tab, done], i) =>
    h('button', { class: 'step' + (next.stage === key ? ' current' : '') + (done ? ' done' : ''), title: '打开' + label, onclick: () => navigate('task', { tab }) },
      h('span', { class: 'step-num', text: done ? '✓' : String(i + 1) }), h('span', { text: label }), status(value))));
}

function nextBanner(task, next) {
  const act = a => () => (a.run ? a.run(task) : navigate('task', { tab: a.tab }));
  // 已在该步骤的页签上时只做提示，操作由页签内的主按钮完成，避免同一页面出现两个相同的主按钮。
  const here = S.tab === next.tab;
  return h('div', { class: 'next ' + (next.tone || '') },
    h('div', { class: 'grow' },
      h('div', { class: 'next-title' }, h('span', { class: 'next-tag', text: '下一步' }), h('b', { text: next.title })),
      h('div', { class: 'small', text: next.detail })),
    !here && next.primary ? h('button', { class: 'primary', onclick: act(next.primary) }, next.primary.label) : null,
    next.secondary && !(next.secondary.tab && next.secondary.tab === S.tab) ? h('button', { onclick: act(next.secondary) }, next.secondary.label) : null);
}

function tabBar(next) {
  return h('div', { class: 'tabs' }, TAB_GROUPS.map((group, i) => [
    i > 0 ? h('span', { class: 'tab-sep' }) : null,
    group.map(([key, label]) => h('button', { class: 'tab' + (S.tab === key ? ' active' : ''), onclick: () => navigate('task', { tab: key }) },
      label, next.tab === key && S.tab !== key ? h('span', { class: 'dot', title: '下一步在这里' }) : null)),
  ]));
}

function drawTaskHeader(host, task) {
  const target = (task.profileSnapshot.apifox.projectName || task.profileSnapshot.apifox.projectId || '未设置') + ' / ' + (task.profileSnapshot.apifox.branchName || 'main');
  const next = nextStep(task);
  fill(host, 
    h('div', { class: 'row spread task-head' },
      h('div', null, h('h1', { text: task.name }), h('div', { class: 'muted small', text: '配置：' + task.profileSnapshot.name + ' · 目标：' + target + ' · 版本备注：' + (task.sourceVersionNote || '未绑定代码版本') + (task.offlineMode ? ' · 离线检查' : '') })),
      stepper(task, next)),
    nextBanner(task, next),
    tabBar(next));
}

async function renderTask(main) {
  const { task, runningJob } = await api('GET', '/api/tasks/' + S.taskId);
  S.task = task;
  if (runningJob && !S.job) runJob(Promise.resolve(runningJob), () => render());
  const header = h('div');
  drawTaskHeader(header, task);
  // 页签内的局部操作（如接受一条候选）只刷新任务头部的阶段与下一步，不重绘整个页签。
  S.refreshTask = async () => {
    const latest = await guard(() => api('GET', '/api/tasks/' + task.id));
    if (latest && S.task === task) {
      Object.assign(task, latest.task);
      drawTaskHeader(header, task);
    }
    return task;
  };
  const body = h('div');
  add(main, header, body);
  const renderers = { source: tabSource, patch: tabPatch, contracts: tabContracts, propagation: tabPropagation, remote: tabRemote, plan: tabPlan, report: tabReport };
  await (renderers[S.tab] || tabSource)(body, task);
}

// ---------- 源码注释 ----------

const SOURCE_FILTERS = [['review', '待审阅'], ['gaps', '需要补全'], ['missing', '缺失'], ['empty', '空标签'], ['stale', '失效 param'], ['candidates', '有候选'], ['accepted', '已接受'], ['valid', '有效'], ['all', '全部']];
const GAP_STATUSES = ['missing', 'empty', 'inheritDocUnresolved', 'stale', 'invalidXml'];

async function tabSource(body, task) {
  const [symbols, drafts] = await Promise.all([api('GET', '/api/tasks/' + task.id + '/symbols'), api('GET', '/api/tasks/' + task.id + '/comment-drafts')]);
  const scan = symbols.scan;
  const manifest = symbols.manifest;
  if (!scan) {
    add(body, h('div', { class: 'panel empty' },
      h('p', { text: '尚未扫描。扫描只读取源码：定位接口、参数、输入/输出类型与枚举，检查注释缺口并收集代码证据；不需要 Swagger，也不会执行 build/restore。' }),
      h('button', { class: 'primary', onclick: () => startScan(task) }, '扫描注释')));
    return;
  }
  const genMode = task.profileSnapshot.generation.mode;
  const bySlot = new Map();
  const rebuild = () => {
    bySlot.clear();
    for (const cand of drafts.candidates) {
      if (!bySlot.has(cand.slotId)) bySlot.set(cand.slotId, []);
      bySlot.get(cand.slotId).push(cand);
    }
  };
  rebuild();
  // 需要审阅：有因代码变化失效的候选，或者还没有接受任何候选且有待审阅的候选（已接受一条后，其余候选不再算待办）。
  const needsReview = s => {
    const cands = bySlot.get(s.id) || [];
    return cands.some(x => x.reviewState === 'stale') || (!cands.some(x => x.reviewState === 'accepted') && cands.some(x => x.reviewState === 'pending'));
  };
  const acceptedCount = () => drafts.candidates.filter(x => x.reviewState === 'accepted').length;
  const reviewCount = () => scan.slots.filter(needsReview).length;
  // 筛选与当前位置按任务保留：生成草稿或切换页签后回到原处。
  const view = S.sourceView[task.id] || (S.sourceView[task.id] = { status: reviewCount() ? 'review' : 'gaps', role: '', keyword: '', selected: null, page: 0 });
  const PAGE = 80;

  const toolbar = h('div', { class: 'row toolbar' });
  const summary = h('div');
  const listHost = h('div');
  const detailHost = h('div', { class: 'detail-col' });

  function drawToolbar() {
    const accepted = acceptedCount();
    fill(toolbar, 
      h('button', { onclick: () => startScan(task) }, '重新扫描'),
      menu('生成草稿', [
        { label: '规则提取', hint: '从代码证据中提取说明，本地立即完成', onclick: () => generateRule() },
        { label: '模型生成…', hint: genMode === 'openai' ? '发送材料前会先确认范围' : '未启用模型服务（项目设置 → 注释草稿生成）', disabled: genMode !== 'openai', onclick: () => generateModel() },
        { label: '导出材料', hint: '交给编码助手，按其中的说明生成候选', onclick: () => exportMaterial() },
        { label: '导入编码助手结果…', hint: '导入后逐条审阅', onclick: async () => importResult(await pickFile('.json')) },
      ], { class: drafts.candidates.length ? '' : 'primary' }),
      h('div', { class: 'grow' }),
      h('span', { class: 'muted small', text: accepted ? '已接受 ' + accepted + ' 条候选' : '接受候选后才能生成注释计划' }),
      h('button', { class: accepted ? 'primary' : '', disabled: !accepted, onclick: () => makeCommentPlan(task) }, '生成注释计划'));
  }

  function pick(value) {
    view.status = value;
    view.page = 0;
    filterSelect.value = value;
    draw();
    drawSummary();
  }

  function drawSummary() {
    const c = scan.coverage;
    const by = c.byStatus || {};
    const other = (by.inheritDocUnresolved || 0) + (by.invalidXml || 0);
    const total = Math.max(1, c.total);
    const seg = (n, cls, label) => (n > 0 ? h('div', { class: 'seg ' + cls, title: label + ' ' + n, style: 'width:' + (n / total * 100).toFixed(2) + '%' }) : null);
    fill(summary, h('div', { class: 'panel coverage' },
      h('div', { class: 'row' },
        h('b', { class: 'coverage-pct', text: Math.round(c.ratio * 1000) / 10 + '%' }),
        h('div', { class: 'grow' },
          h('div', { class: 'coverage-bar' }, seg(c.valid, 'ok', '有效'), seg(c.inheritDoc, 'ok2', '继承（待验证）'), seg(by.stale || 0, 'warn', '失效 param'), seg(by.missing || 0, 'bad', '缺失'), seg(by.empty || 0, 'bad2', '空标签'), seg(other, 'bad2', '其他问题')),
          h('div', { class: 'muted small', text: '注释覆盖率：' + (c.valid + c.inheritDoc) + ' / ' + c.total + ' 个应对外说明的位置有效（空标签与占位文字不计为完成）· 分析等级 ' + manifest.analysisLevel + ' · 扫描于 ' + fmtTime(manifest.scannedAt) }))),
      h('div', { class: 'row chips' },
        chip('待审阅', reviewCount(), 'warn', () => pick('review'), view.status === 'review'),
        chip('缺失', by.missing || 0, 'bad', () => pick('missing'), view.status === 'missing'),
        chip('空标签', by.empty || 0, 'bad', () => pick('empty'), view.status === 'empty'),
        chip('失效 param', by.stale || 0, 'warn', () => pick('stale'), view.status === 'stale'),
        other ? chip('其他问题', other, 'bad', () => pick('gaps'), false) : null,
        chip('已接受', acceptedCount(), 'ok', () => pick('accepted'), view.status === 'accepted'),
        chip('有效', c.valid + c.inheritDoc, 'ok', () => pick('valid'), view.status === 'valid'),
        scan.unresolvedSymbols.length ? chip('未解析符号', scan.unresolvedSymbols.length, '', null) : null)));
  }

  const filterSelect = h('select', { style: 'width:auto', onchange: e => pick(e.target.value) }, SOURCE_FILTERS.map(([v, t]) => h('option', { value: v, text: t, selected: v === view.status })));
  const filters = h('div', { class: 'row', style: 'margin:10px 0' },
    filterSelect,
    h('select', { style: 'width:auto', onchange: e => { view.role = e.target.value; view.page = 0; draw(); } },
      [['', '全部角色'], ['endpoint', '接口方法'], ['request', '请求类型'], ['response', '响应类型'], ['enum', '枚举']].map(([v, t]) => h('option', { value: v, text: t, selected: v === view.role }))),
    h('input', { type: 'text', value: view.keyword, placeholder: '关键词（符号、文件、接口）', style: 'max-width:320px', oninput: e => { view.keyword = e.target.value; view.page = 0; draw(); } }));

  drawToolbar();
  drawSummary();
  add(body, toolbar, summary);
  if (manifest.warnings.length || scan.unresolvedSymbols.length || scan.externalTypes.length) {
    add(body, h('details', { class: 'panel' }, h('summary', { text: '分析警告与降级（' + (manifest.warnings.length + scan.unresolvedSymbols.length) + '）' }),
      h('ul', { class: 'plain small' }, manifest.warnings.map(w => h('li', { text: w })), scan.unresolvedSymbols.map(w => h('li', { text: '未解析：' + w })), scan.externalTypes.map(w => h('li', { text: '外部类型（不可回写）：' + w })))));
  }
  if (drafts.runs.length) {
    const last = drafts.runs[drafts.runs.length - 1];
    add(body, h('div', { class: 'notice' + (last.status === 'failed' ? ' bad' : last.status === 'noResult' ? ' warn' : ''), text: '最近一次生成（' + ({ rule: '规则提取', model: '模型服务', file: '材料文件' }[last.mode] || last.mode) + '）：请求 ' + last.requestedSlots + '，产出 ' + last.produced + '，拒绝 ' + last.rejected + (last.errors.length ? '。' + last.errors.slice(0, 3).join('；') : '') }));
  }
  add(body, filters, h('div', { class: 'split' }, listHost, detailHost));

  function visible() {
    const keyword = view.keyword.trim().toLowerCase();
    return scan.slots.filter(s => {
      const cands = bySlot.get(s.id) || [];
      switch (view.status) {
        case 'review': if (!needsReview(s)) return false; break;
        case 'gaps': if (!GAP_STATUSES.includes(s.status)) return false; break;
        case 'missing': case 'empty': case 'stale': if (s.status !== view.status) return false; break;
        case 'valid': if (s.status !== 'valid' && s.status !== 'inheritDoc') return false; break;
        case 'candidates': if (!cands.length) return false; break;
        case 'accepted': if (!cands.some(x => x.reviewState === 'accepted')) return false; break;
        default: break;
      }
      if (view.role && !s.role.split(',').includes(view.role)) return false;
      if (keyword && !(s.display + ' ' + s.file + ' ' + s.endpointKeys.join(' ') + ' ' + (s.jsonName || '')).toLowerCase().includes(keyword)) return false;
      return true;
    });
  }

  function draw(scrollToSelected) {
    const rows = visible();
    const pages = Math.max(1, Math.ceil(rows.length / PAGE));
    if (view.page >= pages) view.page = pages - 1;
    const pageRows = rows.slice(view.page * PAGE, (view.page + 1) * PAGE);
    const table = h('table', null,
      h('colgroup', null, ['37%', '14%', '13%', '24%', '12%'].map(w => h('col', { style: 'width:' + w }))),
      h('thead', null, h('tr', null, ['位置', '标签', '状态', '现有说明 / 候选', ''].map(t => h('th', { text: t })))));
    const tb = h('tbody');
    for (const s of pageRows) {
      const cands = bySlot.get(s.id) || [];
      const accepted = cands.find(x => x.reviewState === 'accepted');
      const pending = needsReview(s) ? cands.filter(x => x.reviewState === 'pending').length : 0;
      add(tb, h('tr', { class: 'clickable' + (view.selected === s.id ? ' selected' : ''), onclick: () => select(s) },
        h('td', null, h('div', { text: s.display }), h('div', { class: 'muted small mono', text: s.file + ':' + s.line })),
        h('td', { class: 'mono small', text: s.tag + (s.parameterName ? ' ' + s.parameterName : '') }),
        h('td', null, status(s.status), s.editable ? null : h('div', { class: 'muted small', text: '不可回写' })),
        h('td', { class: 'small' }, accepted ? [badge('已接受', 'ok'), ' ', accepted.action === 'removeStale' ? '删除失效标签' : accepted.text] : (s.existingText || (cands.length ? cands.length + ' 个候选' : '—'))),
        h('td', null, cands.some(x => x.reviewState === 'stale') ? badge('需重审', 'warn') : pending ? badge('待审 ' + pending, 'warn') : null)));
    }
    add(table, tb);
    fill(listHost, h('div', { class: 'scroll' }, rows.length ? table : h('div', { class: 'empty', text: view.status === 'review' ? '没有待审阅的候选。可以切换到“需要补全”生成草稿，或生成注释计划。' : '没有符合条件的注释位置。' })),
      h('div', { class: 'pager' }, h('span', { class: 'muted small', text: '共 ' + rows.length + ' 项' }),
        h('button', { disabled: view.page === 0, onclick: () => { view.page--; draw(); } }, '上一页'),
        h('span', { class: 'small', text: (view.page + 1) + ' / ' + pages }),
        h('button', { disabled: (view.page + 1) * PAGE >= rows.length, onclick: () => { view.page++; draw(); } }, '下一页')));
    if (scrollToSelected) listHost.querySelector('tr.selected')?.scrollIntoView({ block: 'nearest' });
  }

  function select(s, scroll) {
    view.selected = s.id;
    const idx = visible().findIndex(x => x.id === s.id);
    if (idx >= 0) view.page = Math.floor(idx / PAGE);
    draw(scroll);
    showSlot(s);
  }

  function move(delta) {
    const rows = visible();
    const target = rows[rows.findIndex(x => x.id === view.selected) + delta];
    if (target) select(target, true);
  }

  /** 审阅动作完成后前进：当前位置仍在列表中就到下一条，已移出列表（如“待审阅”中已接受）就到原位置上的那一条。 */
  function advance(slot, indexBefore) {
    const rows = visible();
    const idx = rows.findIndex(x => x.id === slot.id);
    const target = idx >= 0 ? rows[idx + 1] : rows[Math.min(indexBefore, rows.length - 1)];
    if (target) { select(target, true); return; }
    draw();
    if (idx >= 0) { showSlot(slot); toast('已是当前列表的最后一条'); return; }
    view.selected = null;
    fill(detailHost, h('div', { class: 'panel empty', text: '当前筛选下已经全部处理完。' + (acceptedCount() ? '可以点击上方“生成注释计划”预览补丁。' : '') }));
  }

  let slotSeq = 0;
  async function showSlot(s) {
    const seq = ++slotSeq;
    const rows = visible();
    const idx = rows.findIndex(x => x.id === s.id);
    const nav = () => h('div', { class: 'row slot-nav' },
      h('button', { disabled: idx <= 0, onclick: () => move(-1) }, '‹ 上一条'),
      h('span', { class: 'muted small', text: idx >= 0 ? (idx + 1) + ' / ' + rows.length : '不在当前筛选中' }),
      h('button', { disabled: idx < 0 || idx >= rows.length - 1, onclick: () => move(1) }, '下一条 ›'));
    fill(detailHost, h('div', { class: 'panel' }, nav(), h('div', { class: 'muted small', text: '加载证据…' })));
    const evidence = s.evidenceIds.length ? await guard(() => api('GET', '/api/tasks/' + task.id + '/evidence?ids=' + encodeURIComponent(s.evidenceIds.join(',')))) : { items: [] };
    if (seq !== slotSeq) return;
    const cands = bySlot.get(s.id) || [];
    const manual = h('textarea', { placeholder: '手工填写说明（纯文本，保存时自动转义 XML 字符）' });
    const panel = h('div', { class: 'panel' },
      nav(),
      h('h3', { text: s.display + ' · ' + s.tag + (s.parameterName ? ' ' + s.parameterName : '') }),
      h('div', { class: 'kv small' },
        h('div', { text: '状态' }), h('div', null, status(s.status)),
        h('div', { text: '位置' }), h('div', { class: 'mono', text: s.file + ':' + s.line }),
        h('div', { text: '符号' }), h('div', { class: 'mono', text: s.symbolId }),
        s.jsonName ? [h('div', { text: '对外 JSON 名' }), h('div', { class: 'mono', text: s.jsonName })] : null,
        s.typeDisplay ? [h('div', { text: '类型' }), h('div', { class: 'mono', text: s.typeDisplay })] : null,
        h('div', { text: '使用接口' }), h('div', { class: 'small', text: s.endpointKeys.join('、') + (s.endpointKeys.length > 1 ? '（共享类型：只写可复用的字段含义）' : '') }),
        h('div', { text: '现有说明' }), h('div', { text: s.existingText || '—' }),
        s.notEditableReason ? [h('div', { text: '回写' }), h('div', { class: 'small', text: s.notEditableReason })] : null),
      s.issues.length ? h('div', { class: 'notice warn' }, h('ul', { class: 'plain' }, s.issues.map(i => h('li', { text: i })))) : null,
      h('h3', { text: '代码证据（' + (evidence?.items?.length || 0) + '）' }),
      (evidence?.items || []).length ? evidence.items.map(e => h('div', { class: 'card' },
        h('div', { class: 'row small' }, badge(e.id, 'primary'), badge(e.kind), h('span', { class: 'mono muted', text: e.file + ':' + e.startLine + '-' + e.endLine }), h('span', { text: e.summary || '' })),
        h('pre', { class: 'light evidence', text: e.snippet }))) : h('div', { class: 'muted small', text: '没有直接关联的证据；不能据此推断单位、权限或默认值。' }),
      h('h3', { text: '候选（' + cands.length + '）' }),
      cands.length ? cands.map(cand => candidateCard(cand, s)) : h('div', { class: 'muted small', text: '还没有候选：可以用上方“生成草稿”，或在下面手工填写。' }),
      h('h3', { text: '手工补充' }),
      manual,
      h('div', { class: 'row', style: 'margin-top:6px' },
        h('button', { onclick: () => addManual(s, manual.value, 'set') }, '添加为候选'),
        s.status === 'stale' ? h('button', { class: 'danger', onclick: () => addManual(s, '', 'removeStale') }, '删除失效标签（作为候选）') : null));
    fill(detailHost, panel);
    detailHost.scrollTop = 0;
  }

  function replaceCandidate(updated) {
    const i = drafts.candidates.findIndex(x => x.id === updated.id);
    if (i >= 0) drafts.candidates[i] = updated;
    else drafts.candidates.push(updated);
    // 与服务端一致：同一位置只保留一条已接受的候选，其余退回待审阅。
    if (updated.reviewState === 'accepted') {
      for (const other of drafts.candidates) {
        if (other.slotId === updated.slotId && other.id !== updated.id && other.reviewState === 'accepted') other.reviewState = 'pending';
      }
    }
    rebuild();
    drawToolbar();
    drawSummary();
    S.refreshTask();
  }

  function candidateCard(cand, s) {
    const text = h('textarea', { text: cand.text, disabled: cand.action === 'removeStale' });
    const answers = h('input', { type: 'text', value: cand.answers || '', placeholder: '确认结果（回答待确认问题后才能接受）' });
    const overwrite = h('input', { type: 'checkbox', checked: cand.overwriteExisting });
    const update = async (request, kind) => {
      const indexBefore = visible().findIndex(x => x.id === s.id);
      const result = await guard(() => api('PUT', '/api/tasks/' + task.id + '/comment-drafts/' + cand.id, request));
      if (!result) return;
      replaceCandidate(result);
      toast({ accepted: '已接受', deferred: '已暂缓', rejected: '已拒绝', pending: '已撤销接受', save: '已保存修改' }[kind], 'ok');
      // 保存修改、撤销接受，或同一位置还有其他待审阅候选时留在原处；否则前进到下一条。
      if (kind === 'save' || kind === 'pending' || (kind !== 'accepted' && needsReview(s))) { draw(); showSlot(s); return; }
      advance(s, indexBefore);
    };
    const acceptBody = () => ({ text: cand.action === 'set' ? text.value : null, answers: answers.value, overwriteExisting: overwrite.checked, reviewState: 'accepted' });
    return h('div', { class: 'card' + (cand.reviewState === 'accepted' ? ' accepted' : cand.reviewState === 'stale' ? ' stale' : '') },
      h('div', { class: 'row small' }, status(cand.reviewState === 'pending' ? 'pending' : cand.reviewState), status(cand.basis),
        badge({ rule: '规则提取', model: '模型', file: '材料文件', manual: '手工' }[cand.source] || cand.source), cand.action === 'removeStale' ? badge('删除失效标签', 'warn') : null,
        h('span', { class: 'muted', text: '证据：' + (cand.evidenceIds.join(', ') || '无') })),
      cand.action === 'removeStale' ? h('div', { class: 'small', text: '删除不存在于方法签名中的 <param name="' + s.parameterName + '">' }) : text,
      cand.openQuestions.length ? h('div', { class: 'notice warn small' }, '待确认：', h('ul', { class: 'plain' }, cand.openQuestions.map(q => h('li', { text: q }))), cand.basis === 'needsConfirmation' ? answers : null) : null,
      cand.warnings.length ? h('div', { class: 'muted small', text: cand.warnings.join('；') }) : null,
      s.status === 'valid' && cand.action === 'set' ? h('label', { class: 'small' }, overwrite, ' 覆盖已有注释（会在补丁中展示旧内容）') : null,
      h('div', { class: 'row', style: 'margin-top:6px' },
        cand.reviewState === 'accepted'
          ? h('button', { onclick: () => update({ reviewState: 'pending' }, 'pending') }, '撤销接受')
          : h('button', { class: 'primary', onclick: () => update(acceptBody(), 'accepted') }, '接受并下一条'),
        cand.action === 'set' ? h('button', { onclick: () => update({ text: text.value, answers: answers.value, overwriteExisting: overwrite.checked }, 'save') }, '保存修改') : null,
        cand.reviewState !== 'deferred' ? h('button', { onclick: () => update({ reviewState: 'deferred' }, 'deferred') }, '暂缓') : null,
        cand.reviewState !== 'rejected' ? h('button', { onclick: () => update({ reviewState: 'rejected' }, 'rejected') }, '拒绝') : null));
  }

  async function addManual(s, text, action) {
    const result = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-drafts/manual', { slotId: s.id, text, action }));
    if (!result) return;
    replaceCandidate(result);
    toast('已添加候选，请审阅后接受', 'ok');
    draw();
    showSlot(s);
  }

  // 生成或导入草稿后切到“待审阅”，直接开始逐条审阅。
  const reviewAfterGeneration = () => { view.status = 'review'; view.page = 0; view.selected = null; render(); };

  async function generateRule() {
    const result = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-drafts', { mode: 'rule', slotIds: [] }));
    if (result) { toast('规则提取：请求 ' + result.requested + '，产出 ' + result.produced + '。' + result.note, 'ok'); reviewAfterGeneration(); }
  }

  async function generateModel() {
    const preview = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-drafts/material', { slotIds: [] }));
    if (!preview) return;
    const ok = await modal('确认发送材料', h('div', null,
      h('p', { text: '将向配置的模型服务发送 ' + preview.slots + ' 个注释位置的材料，涉及以下源码文件中的证据片段（已排除配置/凭据文件并脱敏）：' }),
      h('pre', { class: 'light', text: preview.files.join('\n') || '（无证据片段）' }),
      h('p', { class: 'muted small', text: '模型只返回候选文字与证据编号，不会修改文件或调用 Apifox。候选需逐条审阅后才会进入补丁。' })),
      [{ label: '确认发送', value: true, primary: true }]);
    if (!ok) return;
    await runJob(api('POST', '/api/tasks/' + task.id + '/comment-drafts', { mode: 'model', slotIds: [], confirmSend: true }), job => {
      if (job.state === 'succeeded') reviewAfterGeneration();
      else render();
    });
  }

  async function exportMaterial() {
    const preview = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-drafts/material', { slotIds: [] }));
    if (!preview) return;
    const blob = new Blob([JSON.stringify(preview.material, null, 2)], { type: 'application/json' });
    const a = h('a', { href: URL.createObjectURL(blob), download: 'material-' + task.id + '.json' });
    document.body.append(a);
    a.click();
    // 与 download() 相同：桌面端“另存为”期间不能提前释放。
    setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 120000);
    toast('材料已导出：交给编码助手按其中的 instructions 生成候选后，用“生成草稿 → 导入编码助手结果”导回。', 'ok');
  }

  async function importResult(file) {
    if (!file) return;
    const text = await file.text();
    const result = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-drafts/import', text, true));
    if (!result) return;
    toast('导入 ' + result.imported + ' 条候选，拒绝 ' + result.rejected + ' 条' + (result.errors.length ? '：' + result.errors.slice(0, 3).join('；') : ''), result.rejected ? 'bad' : 'ok');
    reviewAfterGeneration();
  }

  draw();
  const current = view.selected && scan.slots.find(x => x.id === view.selected);
  if (current) showSlot(current);
  else fill(detailHost, h('div', { class: 'panel empty', text: '选择左侧的注释位置，查看候选、代码证据并审阅；接受后自动前进到下一条。' }));
}

// ---------- 注释补丁 ----------

function diffView(text) {
  const pre = h('pre');
  for (const line of String(text || '').split('\n')) {
    const cls = line.startsWith('+') && !line.startsWith('+++') ? 'diff-add' : line.startsWith('-') && !line.startsWith('---') ? 'diff-del' : line.startsWith('@@') ? 'diff-hunk' : '';
    add(pre, h('span', { class: cls, text: line.replace(/\r$/, '') + '\n' }));
  }
  return pre;
}

async function tabPatch(body, task) {
  const plan = await api('GET', '/api/tasks/' + task.id + '/comment-plan');
  const writable = plan ? plan.files.filter(f => f.writeAllowed) : [];
  const edits = writable.reduce((n, f) => n + f.edits.length, 0);
  add(body, h('div', { class: 'row toolbar' },
    h('button', { onclick: async () => { if (await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-plan'))) render(); } }, plan ? '重新生成注释计划' : '生成注释计划'),
    plan && plan.files.length ? h('button', { onclick: () => guard(async () => { await download('/api/tasks/' + task.id + '/comment-plan/patch', 'comments.patch'); render(); }) }, '导出补丁') : null,
    h('div', { class: 'grow' }),
    plan && plan.status === 'ready' && writable.length ? h('button', { class: 'primary', onclick: () => apply(plan) }, '确认应用注释（' + edits + ' 处）') : null));
  if (!plan) {
    add(body, h('div', { class: 'panel empty', text: '还没有注释计划。在“源码注释”中接受候选后生成计划；计划只包含文档注释改动。' }));
    return;
  }
  add(body, h('div', { class: 'notice' + (plan.status === 'expired' || plan.status === 'failed' ? ' bad' : plan.status === 'applied' ? ' ok' : ''), text: '计划状态：' + ({ ready: '待应用', applying: '回写中', applied: '已回写', partial: '部分回写', failed: '失败', expired: '已过期' }[plan.status] || plan.status) + ' · 指纹 ' + plan.hash.slice(0, 12) + (plan.message ? ' · ' + plan.message : '') }));
  if (plan.status === 'ready' && plan.files.length && !writable.length) {
    add(body, h('div', { class: 'notice warn', text: '没有文件在“允许回写范围”内，只能导出补丁。任务使用创建时的配置快照；需要由工具回写时，请先在项目设置中填写允许回写范围，再新建任务。' }));
  }
  add(body, h('div', { class: 'notice small', text: '手动应用导出的补丁时请使用：git -c core.autocrlf=false apply comments.patch（开启 autocrlf 时 git 会改写 LF 文件的换行）。导出不代表已应用，也不代表 Swagger 已更新。' }));
  if (plan.applyResults.length) {
    add(body, h('div', { class: 'panel' }, h('h3', { text: '回写结果' }), h('table', null, h('tbody', null, plan.applyResults.map(r => h('tr', null, h('td', { class: 'mono', text: r.relativePath }), h('td', null, badge(r.state, r.state === 'applied' ? 'ok' : r.state === 'skipped' ? '' : 'bad')), h('td', { class: 'small', text: r.message || '' })))))));
  }
  if (plan.skipped.length) {
    add(body, h('details', { class: 'panel', open: true }, h('summary', { text: '跳过的候选（' + plan.skipped.length + '）' }), h('ul', { class: 'plain small' }, plan.skipped.map(s => h('li', { text: s.display + '：' + s.reason })))));
  }
  for (const file of plan.files) {
    add(body, h('div', { class: 'panel' },
      h('div', { class: 'row spread' }, h('b', { class: 'mono', text: file.relativePath }),
        h('span', { class: 'small muted', text: file.edits.length + ' 处 · ' + file.eol + (file.hasBom ? ' · BOM' : '') + ' · ' + (file.writeAllowed ? '可回写' : file.writeBlockedReason) })),
      diffView(file.diff)));
  }

  async function apply(p) {
    const ok = await modal('确认应用注释', h('div', null,
      h('p', { text: '将修改 ' + writable.length + ' 个文件中的 ' + edits + ' 处文档注释。' }),
      h('ul', { class: 'plain small' }, writable.map(f => h('li', { class: 'mono', text: f.relativePath }))),
      h('p', { class: 'small muted', text: '应用前会再次核对文件指纹；只改 /// 注释行，执行代码、预处理指令、BOM 与换行符保持不变。前态备份保存在本地任务目录。不会写 Apifox。' })),
      [{ label: '确认应用', value: true, primary: true }]);
    if (!ok) return;
    const result = await guard(() => api('POST', '/api/tasks/' + task.id + '/comment-plan/apply', { planHash: p.hash }));
    if (result) { toast('回写结果：' + result.status, result.status === 'applied' ? 'ok' : 'bad'); render(); }
  }
}

// ---------- 响应契约 ----------

async function tabContracts(body, task) {
  const contracts = await api('GET', '/api/tasks/' + task.id + '/response-contracts');
  add(body, h('div', { class: 'notice small', text: '通用响应封装中 data 的实际类型由返回表达式、封装构造函数与调用链静态追踪得到。确认后只增强该接口的 200 响应结构，不修改共享封装；无法确定的分支保持阻断。确认或拒绝后需要重新检查 Apifox 差异才会生效。' }));
  if (!contracts.length) {
    add(body, h('div', { class: 'panel empty', text: '没有需要补充的响应契约（未配置通用响应封装，或接口返回具体类型）。请在项目设置中填写“通用响应封装”，并重新扫描。' }));
    return;
  }
  const pending = contracts.filter(c => c.reviewState === 'pending').length;
  if (pending) add(body, h('div', { class: 'muted small', style: 'margin-bottom:8px', text: '待确认 ' + pending + ' 个 / 共 ' + contracts.length + ' 个' }));
  const review = async (endpointKey, reviewState) => {
    const result = await guard(() => api('PUT', '/api/tasks/' + task.id + '/response-contract', { endpointKey, reviewState }));
    if (result) { toast('已更新', 'ok'); render(); }
  };
  for (const c of contracts) {
    add(body, h('div', { class: 'panel' },
      h('div', { class: 'row spread' },
        h('div', null, h('b', { text: c.endpointKey }), ' ', status(c.reviewState === 'pending' ? 'pending' : c.reviewState)),
        h('div', { class: 'row' },
          h('button', { class: 'primary', disabled: c.reviewState === 'blocked' || c.reviewState === 'confirmed', onclick: () => review(c.endpointKey, 'confirmed') }, '确认'),
          h('button', { disabled: c.reviewState === 'rejected', onclick: () => review(c.endpointKey, 'rejected') }, '拒绝'),
          c.reviewState !== 'pending' && c.reviewState !== 'blocked' ? h('button', { onclick: () => review(c.endpointKey, 'pending') }, '重置') : null)),
      h('div', { class: 'small', style: 'margin:6px 0' }, '封装 ', h('span', { class: 'mono', text: c.wrapperType || '（直接返回）' }), ' · ', c.dataJsonName || 'data', ' = ', h('b', { class: 'mono', text: c.dataType || '未确定' }), c.dataNullable ? '（异常分支为空）' : ''),
      c.unresolvedPoints.length ? h('div', { class: 'notice warn' }, h('ul', { class: 'plain small' }, c.unresolvedPoints.map(u => h('li', { text: u })))) : null,
      h('table', null, h('thead', null, h('tr', null, ['分支', 'HTTP', '条件', '业务状态', 'data', '位置'].map(t => h('th', { text: t })))),
        h('tbody', null, c.branches.map(b => h('tr', null,
          h('td', { text: b.branchKey }), h('td', { text: b.httpStatus ?? '200（直接返回）' }), h('td', { class: 'small', text: b.condition }),
          h('td', { text: b.businessStatus || '—' }), h('td', { class: 'small' }, badge(b.dataKind), ' ', b.dataType || b.note || ''),
          h('td', { class: 'mono small', text: b.file + ':' + b.line }))))),
      c.responseSchema ? h('details', null, h('summary', { text: '响应结构预览（OpenAPI 片段与 ' + Object.keys(c.components || {}).length + ' 个模型）' }), h('pre', { class: 'light', text: pretty({ schema: c.responseSchema, components: c.components }) })) : null));
  }
}

// ---------- 生成文档（传播核对） ----------

async function tabPropagation(body, task) {
  const [result, build] = await Promise.all([api('GET', '/api/tasks/' + task.id + '/propagation'), api('GET', '/api/tasks/' + task.id + '/build')]);
  add(body, h('div', { class: 'panel' },
    h('h3', { text: '刷新生成物（可选）' }),
    h('div', { class: 'small', text: '工具默认不构建、不 restore、不启动服务。需要刷新 XML 文档时，可以在本机终端执行，或确认后由工具执行下面的定向构建：' }),
    h('pre', { class: 'light', text: build.command }),
    h('div', { class: 'row' },
      h('button', { onclick: async () => {
        const ok = await modal('确认执行定向构建', h('p', { text: '将在工作区执行：' + build.command + '。会生成 bin/obj 输出，不会还原依赖或启动服务。' }), [{ label: '执行', value: true, primary: true }]);
        if (ok) runJob(api('POST', '/api/tasks/' + task.id + '/build', { confirm: true }), () => render());
      } }, '执行定向构建'),
      h('button', { class: 'primary', onclick: () => runJob(api('POST', '/api/tasks/' + task.id + '/propagation-check'), () => render()) }, '核对传播')),
    build.last ? h('details', null, h('summary', { text: '上次构建：退出码 ' + build.last.exitCode + '，' + build.last.seconds + ' 秒' }), h('pre', { text: build.last.outputTail })) : null));
  if (!result) {
    add(body, h('div', { class: 'panel empty', text: '尚未核对。核对检查已确认注释是否写入源码、出现在生成的 XML 文档，并被 Swagger 读取到正确位置。' }));
    return;
  }
  add(body, h('div', { class: 'notice' + (task.stages.generated.status === 'verified' ? ' ok' : ' warn'), text: task.stages.generated.message || '' }));
  if (result.swaggerMessage) add(body, h('div', { class: 'notice warn', text: result.swaggerMessage }));
  add(body, h('div', { class: 'panel' }, h('h3', { text: 'XML 文档文件' }), h('table', null, h('tbody', null, result.xmlFiles.map(x => h('tr', null,
    h('td', { text: x.project }), h('td', { class: 'mono small', text: x.path || '未启用' }), h('td', null, badge(x.exists ? '存在' : '不存在', x.exists ? 'ok' : 'bad')),
    h('td', { class: 'small', text: x.lastWrite ? fmtTime(x.lastWrite) + (x.olderThanSource ? '（早于源码修改）' : '') : '' })))))));
  add(body, h('div', { class: 'panel' }, h('table', null,
    h('thead', null, h('tr', null, ['位置', '标签', '源码', 'XML', 'Swagger', '对应位置 / 备注'].map(t => h('th', { text: t })))),
    h('tbody', null, result.entries.map(e => h('tr', null,
      h('td', null, e.display), h('td', { class: 'mono small', text: e.tag + (e.parameterName ? ' ' + e.parameterName : '') }),
      h('td', null, status(e.sourceState === 'written' ? 'written2' : e.sourceState)), h('td', null, status(e.xmlState)), h('td', null, status(e.swaggerState)),
      h('td', { class: 'small' }, h('div', { class: 'mono', text: e.swaggerLocation || '' }), e.notes.map(n => h('div', { class: 'muted', text: n })))))))));
}

// ---------- Apifox 差异 ----------

const SYNCABLE = ['new', 'structure', 'docs'];

async function tabRemote(body, task) {
  const data = await api('GET', '/api/tasks/' + task.id + '/diffs');
  const diff = data.diff;
  const decisions = data.decisions;
  const supplement = data.supplement;
  const swaggerType = task.profileSnapshot.swagger.type;
  // 页面上的确认（逐项确认、说明选择、模型绑定）先改内存；未保存（dirty）或保存后未重新检查（stale）时不能生成计划。
  let dirty = false;
  let stale = !!diff && diff.taskRevision !== task.revision;

  const toolbar = h('div', { class: 'row toolbar' });
  const importRemote = async () => {
    const file = await pickFile('.json');
    if (!file) return;
    const text = await file.text();
    if (await guard(() => api('POST', '/api/tasks/' + task.id + '/offline-remote', text, true))) { toast('已导入', 'ok'); render(); }
  };
  function drawToolbar() {
    const loaded = !!task.stages.remote.sourceHash;
    fill(toolbar, 
      h('span', { class: 'muted small', text: swaggerType === 'none' ? '未配置 Swagger：结构保持远端现状，只能补充说明。' : loaded ? 'Swagger 已冻结；源码或 Swagger 更新后请“重新加载 Swagger 并检查”。' : '检查前会先加载并冻结 Swagger。' }),
      task.offlineMode ? h('button', { onclick: importRemote }, '导入“远端” OpenAPI') : null,
      h('div', { class: 'grow' }),
      swaggerType !== 'none' && loaded ? h('button', { onclick: () => recheck(true) }, '重新加载 Swagger 并检查') : null,
      h('button', { class: !diff || stale ? 'primary' : '', onclick: () => recheck(false) }, diff ? (dirty ? '保存并重新检查' : '重新检查') : needsSwaggerLoad(task) ? '加载 Swagger 并检查' : '开始检查'));
  }

  async function recheck(reloadSwagger) {
    if (dirty && !(await saveDecisions())) return;
    startCheck(task, reloadSwagger);
  }

  async function saveDecisions() {
    if (!(await guard(() => api('PUT', '/api/tasks/' + task.id + '/decisions', decisions)))) return false;
    if (!(await guard(() => api('PUT', '/api/tasks/' + task.id + '/supplement', supplement)))) return false;
    dirty = false;
    await S.refreshTask();
    stale = !!diff && diff.taskRevision !== task.revision;
    drawToolbar();
    if (diff) { drawNotice(); drawActions(); }
    return true;
  }

  drawToolbar();
  add(body, toolbar);
  if (!diff) {
    add(body, h('div', { class: 'panel empty', text: '尚未检查。检查会一次导出整分支概览（只读），再逐个读取范围内接口与模型详情，与 Swagger + 已确认的响应契约 + 说明补充层构成的目标比较。' }));
    return;
  }

  const itemsByKey = new Map();
  for (const item of diff.items) {
    if (!itemsByKey.has(item.resourceKey)) itemsByKey.set(item.resourceKey, []);
    itemsByKey.get(item.resourceKey).push(item);
  }
  const itemsOf = key => itemsByKey.get(key) || [];
  const pendingCount = e => itemsOf(e.key).filter(i => i.needsDecision && decisions.items[i.id] !== 'accepted').length
    + e.docChanges.filter(c => c.decision === 'conflict' && !decisions.docChoices[c.id]).length;
  const counts = s => diff.endpoints.filter(e => e.status === s).length;
  const sel = S.selection[task.id] || (S.selection[task.id] = new Set());
  for (const key of [...sel]) if (!diff.endpoints.some(e => e.key === key && SYNCABLE.includes(e.status))) sel.delete(key);
  const filters = { status: '', risk: '', keyword: '' };
  let current = null;

  const noticeHost = h('div');
  const statsHost = h('div');
  const listHost = h('div');
  const detailHost = h('div', { class: 'detail-col' });
  const actionBar = h('div', { class: 'sticky-actions' });
  const statusSelect = h('select', { style: 'width:auto', onchange: e => setStatus(e.target.value) },
    [['', '全部状态'], ['pending', '待确认'], ['new', '新增'], ['structure', '结构变化'], ['docs', '说明变化'], ['blocked', '阻断'], ['unchanged', '无变化'], ['sourceOnly', '仅源码']].map(([v, t]) => h('option', { value: v, text: t })));

  function drawNotice() {
    clear(noticeHost);
    if (stale) {
      add(noticeHost, h('div', { class: 'notice warn row spread' },
        h('span', { text: '检查之后任务内容有变化（注释、契约、决策或补充说明），重新检查后才能生成同步计划。' }),
        h('button', { class: 'primary', onclick: () => recheck(false) }, '重新检查')));
    }
  }

  function setStatus(value) {
    filters.status = filters.status === value ? '' : value;
    statusSelect.value = filters.status;
    drawStats();
    drawList();
  }

  function drawStats() {
    const conflicts = diff.endpoints.reduce((n, e) => n + e.docChanges.filter(c => c.decision === 'conflict').length, 0);
    const c = (label, value, kind, key) => chip(label, value, kind, () => setStatus(key), filters.status === key);
    fill(statsHost, h('div', { class: 'row chips', style: 'margin:10px 0 4px' },
      c('新增', counts('new'), 'primary', 'new'), c('结构变化', counts('structure'), 'warn', 'structure'), c('说明变化', counts('docs'), 'primary', 'docs'),
      c('待确认', diff.endpoints.filter(e => pendingCount(e) > 0).length, 'warn', 'pending'), c('阻断', counts('blocked'), 'bad', 'blocked'),
      conflicts ? chip('说明冲突', conflicts, 'bad', () => setStatus('pending'), false) : null,
      c('无变化', counts('unchanged'), '', 'unchanged'),
      counts('sourceOnly') ? c('仅源码', counts('sourceOnly'), 'bad', 'sourceOnly') : null,
      chip('模型', diff.models.length, '', null)));
  }

  drawNotice();
  drawStats();
  add(body, noticeHost, statsHost,
    h('div', { class: 'muted small', text: '结构来源：' + ({ swagger: 'Swagger', remote: '远端现状（未提供 Swagger）', offline: '离线比较' }[diff.structureSource] || diff.structureSource) + ' · 快照：' + fmtTime(diff.snapshotAt) + (diff.snapshotComplete ? '（完整）' : '（不完整）') + ' · 检查时间：' + fmtTime(diff.computedAt) }));
  if (diff.problems.length) add(body, h('div', { class: 'notice warn' }, h('ul', { class: 'plain small' }, diff.problems.map(p => h('li', { text: p })))));
  add(body, h('div', { class: 'row', style: 'margin:10px 0' },
    statusSelect,
    h('select', { style: 'width:auto', onchange: e => { filters.risk = e.target.value; drawList(); } }, [['', '全部风险'], ['incompatible', '含潜在不兼容'], ['manual', '含需人工判断']].map(([v, t]) => h('option', { value: v, text: t }))),
    h('input', { type: 'text', placeholder: '关键词（路径、接口名）', style: 'max-width:280px', oninput: e => { filters.keyword = e.target.value.trim().toLowerCase(); drawList(); } })));
  add(body, h('div', { class: 'split' }, listHost, detailHost), actionBar);

  function markDirty() {
    dirty = true;
    drawToolbar();
    drawStats();
    drawList();
    drawActions();
  }

  function drawList() {
    const rows = diff.endpoints.filter(e =>
      (!filters.status || (filters.status === 'pending' ? pendingCount(e) > 0 : e.status === filters.status)) &&
      (!filters.risk || itemsOf(e.key).some(i => i.risk === filters.risk)) &&
      (!filters.keyword || (e.key + ' ' + (e.remoteName || '')).toLowerCase().includes(filters.keyword)));
    const table = h('table', null,
      h('colgroup', null, ['36px', '50%', '17%', '19%', '14%'].map(w => h('col', { style: 'width:' + w }))),
      h('thead', null, h('tr', null, ['', '接口', '状态', '待处理', '说明'].map(t => h('th', { text: t })))));
    const tb = h('tbody');
    for (const e of rows) {
      const items = itemsOf(e.key);
      const pending = pendingCount(e);
      const selectable = SYNCABLE.includes(e.status) && !task.offlineMode;
      const box = h('input', { type: 'checkbox', checked: sel.has(e.key), disabled: !selectable, title: selectable ? '纳入同步' : '该接口不能同步', onclick: ev => { ev.stopPropagation(); if (ev.target.checked) sel.add(e.key); else sel.delete(e.key); drawActions(); } });
      add(tb, h('tr', { class: 'clickable' + (current === e.key ? ' selected' : ''), onclick: () => showEndpoint(e) },
        h('td', null, box),
        h('td', null, h('div', { class: 'mono small', text: e.key }), h('div', { class: 'muted small', text: e.remoteName || (e.inRemote ? '' : 'Apifox 中不存在') })),
        h('td', null, status(e.status)),
        h('td', { class: 'small' }, pending ? badge('待确认 ' + pending, 'warn') : null, items.some(i => i.risk === 'incompatible') ? badge('不兼容', 'warn') : null, items.some(i => i.risk === 'manual') ? badge('人工', 'warn') : null),
        h('td', { class: 'small', text: e.docChanges.filter(c => ['fill', 'useTarget', 'supplement', 'conflict'].includes(c.decision)).length + ' 处' })));
    }
    add(table, tb);
    fill(listHost, h('div', { class: 'scroll' }, rows.length ? table : h('div', { class: 'empty', text: '没有符合条件的接口' })));
  }

  function drawActions() {
    const syncable = diff.endpoints.filter(e => SYNCABLE.includes(e.status) && pendingCount(e) === 0);
    const canPlan = sel.size > 0 && !task.offlineMode && !dirty && !stale;
    const why = task.offlineMode ? '离线检查不能同步' : dirty ? '先保存确认并重新检查' : stale ? '先重新检查' : sel.size ? '' : '先勾选接口';
    fill(actionBar, 
      h('b', { text: '已选择 ' + sel.size + ' 个接口' }),
      h('button', { disabled: !syncable.length || task.offlineMode, title: '勾选所有没有阻断、没有待确认项的变化接口', onclick: () => { syncable.forEach(e => sel.add(e.key)); drawList(); drawActions(); } }, '全选可同步（' + syncable.length + '）'),
      sel.size ? h('button', { onclick: () => { sel.clear(); drawList(); drawActions(); } }, '清空') : null,
      h('div', { class: 'grow' }),
      why && (dirty || stale || task.offlineMode) ? h('span', { class: 'warn-text small', text: why }) : null,
      dirty || stale ? h('button', { class: 'primary', onclick: () => recheck(false) }, dirty ? '保存并重新检查' : '重新检查') : null,
      h('button', { class: canPlan ? 'primary' : '', disabled: !canPlan, title: why, onclick: () => runJob(api('POST', '/api/tasks/' + task.id + '/plan', { endpointKeys: [...sel] }), afterJob(task.id, 'plan')) }, '生成同步预览'));
  }

  function showEndpoint(e) {
    current = e.key;
    drawList();
    const items = itemsOf(e.key);
    const panel = h('div', { class: 'panel' });
    add(panel, h('h3', { class: 'mono', text: e.key }), h('div', { class: 'small muted', text: (e.remoteId ? '远端 ID ' + e.remoteId + ' · ' + (e.remoteFolder || '') : '远端不存在') + ' · 源码 ' + (e.inSource ? '有' : '无') + ' · Swagger ' + (e.inSwagger ? '有' : '无') + (e.usesResponseContract ? ' · 使用响应契约' : '') }));
    if (e.blockers.length) add(panel, h('div', { class: 'notice bad' }, h('ul', { class: 'plain small' }, e.blockers.map(b => h('li', { text: b })))));
    if (e.notes.length) add(panel, h('div', { class: 'notice small' }, h('ul', { class: 'plain' }, e.notes.map(n => h('li', { text: n })))));

    const structural = items.filter(i => i.change !== 'info');
    add(panel, h('h3', { text: '字段对比（' + structural.length + '）' }));
    if (structural.length) {
      add(panel, h('table', null, h('thead', null, h('tr', null, ['位置', '变化', '远端', '目标', '风险', '确认'].map(t => h('th', { text: t })))),
        h('tbody', null, structural.map(i => h('tr', null,
          h('td', { class: 'mono small', text: i.location }),
          h('td', { class: 'small', text: i.field + ' · ' + ({ added: '新增', removed: '删除', changed: '变化' }[i.change] || i.change) + ' — ' + i.reason }),
          h('td', null, h('pre', { class: 'light', text: i.before === undefined || i.before === null ? '—' : pretty(i.before) })),
          h('td', null, h('pre', { class: 'light', text: i.after === undefined || i.after === null ? '—' : pretty(i.after) })),
          h('td', null, status(i.risk)),
          h('td', null, i.needsDecision ? h('label', { class: 'small nowrap' }, h('input', { type: 'checkbox', checked: decisions.items[i.id] === 'accepted', onchange: ev => { if (ev.target.checked) decisions.items[i.id] = 'accepted'; else delete decisions.items[i.id]; markDirty(); } }), ' 已确认') : '—'))))));
    } else {
      add(panel, h('div', { class: 'muted small', text: '没有结构差异。' }));
    }
    const infos = items.filter(i => i.change === 'info');
    if (infos.length) add(panel, h('details', null, h('summary', { text: '保留项（' + infos.length + '）' }), h('ul', { class: 'plain small' }, infos.map(i => h('li', { text: i.location + '：' + i.reason })))));

    add(panel, h('h3', { text: '说明合并' }));
    const docs = e.docChanges;
    if (docs.length) {
      add(panel, h('table', null, h('thead', null, h('tr', null, ['位置', '远端', '目标', '结果', '选择'].map(t => h('th', { text: t })))),
        h('tbody', null, docs.map(c => h('tr', null,
          h('td', { class: 'mono small', text: c.location + (c.field !== 'description' ? '（' + c.field + '）' : '') }),
          h('td', { class: 'small', text: c.remoteValue || '—' }),
          h('td', { class: 'small', text: (c.targetValue || '—') + '  [' + ({ swagger: 'Swagger', comment: '已确认注释', sourceDoc: '源码注释', supplement: '人工补充' }[c.origin] || c.origin) + ']' }),
          h('td', null, badge({ fill: '补充', keepRemote: '保留远端', useTarget: '使用目标', conflict: '冲突待选', supplement: '人工补充', suggestion: '建议（不自动改名）' }[c.decision] || c.decision, c.decision === 'conflict' ? 'bad' : '')),
          h('td', null, c.decision === 'conflict' || c.decision === 'useTarget' || c.decision === 'keepRemote' && c.origin === 'comment'
            ? h('select', { onchange: ev => { if (ev.target.value) decisions.docChoices[c.id] = ev.target.value; else delete decisions.docChoices[c.id]; markDirty(); } },
              [['', '未选择'], ['keepRemote', '保留远端'], ['useTarget', '使用源码注释']].map(([v, t]) => h('option', { value: v, text: t, selected: decisions.docChoices[c.id] === v })))
            : '—'))))));
    } else {
      add(panel, h('div', { class: 'muted small', text: '没有说明变化。' }));
    }

    add(panel, h('h3', { text: '业务说明补充' }));
    const entry = (location, fieldName) => supplement.entries.find(s => s.resourceKey === e.key && s.location === location && s.field === fieldName);
    const editors = [];
    const editor = (label, location, fieldName, currentText) => {
      const existing = entry(location, fieldName);
      const input = h('textarea', { text: existing ? existing.value || '' : '', placeholder: currentText ? '当前：' + currentText : '' });
      const keep = h('input', { type: 'checkbox', checked: existing?.keepAsIs });
      editors.push({ location, field: fieldName, input, keep });
      return [h('label', { text: label }), h('div', null, input, h('label', { class: 'small muted' }, keep, ' 保留现状，本次不补充'))];
    };
    add(panel, h('div', { class: 'form-grid' },
      editor('接口标题', 'operation', 'name', e.remote?.summary || e.target?.summary),
      editor('接口说明', 'operation', 'description', e.target?.description || e.remote?.description),
      Object.values(e.target?.parameters || {}).filter(p => p.in === 'query' || p.in === 'path').map(p => editor('参数 ' + p.name, p.in + '.' + p.name, 'description', p.description)),
      e.target?.requestBody ? editor('请求体', 'requestBody', 'description', e.target.requestBody.description) : null,
      e.target?.responses?.['200'] ? editor('200 响应', 'responses.200', 'description', e.target.responses['200'].description) : null));
    add(panel, h('div', { class: 'row', style: 'margin-top:6px' }, h('button', { onclick: async () => {
      supplement.entries = supplement.entries.filter(s => !(s.resourceKey === e.key && editors.some(x => x.location === s.location && x.field === s.field)));
      for (const x of editors) {
        if (x.keep.checked || x.input.value.trim()) supplement.entries.push({ resourceKey: e.key, location: x.location, field: x.field, value: x.keep.checked ? null : x.input.value.trim(), keepAsIs: x.keep.checked });
      }
      if (await saveDecisions()) toast(stale ? '已保存；重新检查后生效' : '已保存（内容没有变化）', 'ok');
    } }, '保存本接口补充')));

    add(panel, h('details', null, h('summary', { text: '最终预览（规范化后的目标与远端）' }),
      h('div', { class: 'split' }, h('div', null, h('div', { class: 'small muted', text: '目标' }), h('pre', { class: 'light', text: pretty(e.target) })), h('div', null, h('div', { class: 'small muted', text: '远端' }), h('pre', { class: 'light', text: pretty(e.remote) })))));
    fill(detailHost, panel);
    detailHost.scrollTop = 0;
  }

  drawList();
  drawActions();
  fill(detailHost, h('div', { class: 'panel empty', text: '选择接口查看字段对比、说明合并与最终预览。高风险变化需逐项确认，保存并重新检查后该接口才会纳入同步。' }));

  // 模型与影响
  if (diff.models.length) {
    const table = h('table', null, h('thead', null, h('tr', null, ['模型', '映射', '状态', '使用接口', '范围外影响', '处理'].map(t => h('th', { text: t })))));
    const tb = h('tbody');
    for (const m of diff.models) {
      let control = '—';
      if (m.mappingState === 'candidate' || m.mappingState === 'ambiguous' || m.mappingState === 'new') {
        control = h('select', { onchange: ev => { if (ev.target.value) decisions.modelBindings[m.name] = ev.target.value; else delete decisions.modelBindings[m.name]; markDirty(); } },
          h('option', { value: '', text: '未确认' }),
          (m.candidates || []).map(id => h('option', { value: String(id), text: '绑定远端 ' + id, selected: decisions.modelBindings[m.name] === String(id) })),
          h('option', { value: 'new', text: '新建模型', selected: decisions.modelBindings[m.name] === 'new' }));
      } else if (m.status === 'changed') {
        control = h('label', { class: 'small nowrap' }, h('input', { type: 'checkbox', checked: decisions.items['model:' + m.name] === 'accepted', onchange: ev => { if (ev.target.checked) decisions.items['model:' + m.name] = 'accepted'; else delete decisions.items['model:' + m.name]; markDirty(); } }), ' 确认更新共享模型');
      }
      add(tb, h('tr', null,
        h('td', null, h('b', { class: 'mono', text: m.name }), m.remoteId ? h('div', { class: 'muted small', text: 'ID ' + m.remoteId }) : null),
        h('td', null, status(m.mappingState)),
        h('td', null, status(m.status), m.blockers.length ? h('div', { class: 'small muted', text: m.blockers.join('；') }) : null),
        h('td', { class: 'small', text: m.usedBy.join('、') }),
        h('td', { class: 'small' }, m.impactOutOfScope.length ? h('details', null, h('summary', { text: m.impactOutOfScope.length + ' 个接口' }), h('ul', { class: 'plain' }, m.impactOutOfScope.map(x => h('li', { text: x.key + '（' + x.name + '）' })))) : (m.status === 'changed' ? '无' : '—')),
        h('td', null, control)));
    }
    add(table, tb);
    add(body, h('h2', { text: '数据模型与共享影响' }), h('div', { class: 'panel' }, h('div', { class: 'small muted', text: '首次绑定同名模型需要确认；共享模型更新会影响范围外接口，确认后才纳入计划。修改后点击下方“保存并重新检查”。' }), table));
  }
  if (diff.unmatchedRemote.length) {
    add(body, h('details', { class: 'panel' }, h('summary', { text: '远端未匹配的旧接口（' + diff.unmatchedRemote.length + '，第一版不自动删除）' }), h('ul', { class: 'plain small' }, diff.unmatchedRemote.map(x => h('li', { text: x })))));
  }
  if (diff.quality.length) {
    add(body, h('details', { class: 'panel' }, h('summary', { text: '文档质量（' + diff.quality.length + '）' }),
      h('table', null, h('tbody', null, diff.quality.map(q => h('tr', null, h('td', { class: 'mono small', text: q.resourceKey }), h('td', { class: 'mono small', text: q.location }), h('td', { class: 'small', text: q.message }), h('td', null, badge(q.severity === 'blocking' ? '阻断' : '建议', q.severity === 'blocking' ? 'bad' : ''))))))));
  }
}

// ---------- 同步 ----------

async function tabPlan(body, task) {
  const plan = await api('GET', '/api/tasks/' + task.id + '/plan');
  if (!plan) {
    add(body, h('div', { class: 'panel empty' }, h('p', { text: '还没有同步计划。在“Apifox 差异”中勾选接口并生成同步预览。' }), h('button', { onclick: () => navigate('task', { tab: 'remote' }) }, '去 Apifox 差异')));
    return;
  }
  const endpoints = plan.actions.filter(a => a.kind.endsWith('Endpoint')).length;
  const models = plan.actions.length - endpoints;
  // 计划生成后任务又有修改时，服务端会在确认时拒绝；这里提前提示，避免点了才知道。
  const outdated = plan.status === 'awaitingConfirm' && plan.taskRevision !== task.revision;
  add(body, h('div', { class: 'panel' },
    h('div', { class: 'row spread' },
      h('div', null, h('b', { text: '目标：' + (plan.projectName || '') + ' 项目 ' + plan.projectId + ' / 分支 ' + (plan.branch || 'main') }), h('div', { class: 'small muted', text: 'CLI ' + plan.cliVersion + ' · 计划指纹 ' + plan.hash.slice(0, 12) + ' · 生成于 ' + fmtTime(plan.createdAt) })),
      status(outdated ? 'expired' : plan.status)),
    outdated ? h('div', { class: 'notice warn row spread' }, h('span', { text: '生成计划后任务内容有变化（注释、决策或补充说明），这份计划不能再执行。请重新检查后重新生成同步预览。' }), h('button', { class: 'primary', onclick: () => navigate('task', { tab: 'remote' }) }, '去重新检查')) : null,
    plan.message ? h('div', { class: 'notice warn', text: plan.message }) : null,
    plan.warnings.length ? h('div', { class: 'notice warn' }, h('ul', { class: 'plain small' }, plan.warnings.map(w => h('li', { text: w })))) : null,
    h('div', { class: 'row', style: 'margin-top:8px' },
      h('button', { class: 'primary', disabled: plan.status !== 'awaitingConfirm' || outdated, onclick: () => confirmApply(plan) }, '确认同步 ' + endpoints + ' 个接口、' + models + ' 个模型'),
      h('button', { disabled: plan.status === 'awaitingConfirm' || plan.status === 'executing', onclick: () => runJob(api('POST', '/api/tasks/' + task.id + '/verify'), () => render()) }, '重新核对（只读）'))));
  add(body, h('div', { class: 'panel' }, h('h3', { text: '写入清单（按执行顺序）' }), h('table', null,
    h('thead', null, h('tr', null, ['#', '动作', '资源', '远端 ID', '改动', 'CLI 校验', '状态', ''].map(t => h('th', { text: t })))),
    h('tbody', null, plan.actions.map((a, i) => h('tr', null,
      h('td', { text: i + 1 }),
      h('td', { text: ({ createModel: '新建模型', updateModel: '更新模型', createEndpoint: '新建接口', updateEndpoint: '更新接口' }[a.kind] || a.kind) }),
      h('td', { class: 'mono small', text: a.resourceKey }),
      h('td', { text: a.remoteId || a.createdId || '—' }),
      h('td', { class: 'small' }, h('div', { text: (a.managedFields || []).length ? '写入字段：' + a.managedFields.join(', ') : '' }), a.changes.map(c => h('div', { text: c })), a.notes.map(n => h('div', { class: 'muted', text: n }))),
      h('td', { class: 'small', text: a.validation }),
      h('td', null, status(a.state), a.message ? h('div', { class: 'small muted', text: a.message }) : null),
      h('td', null, h('button', { class: 'link', onclick: async () => { const p = await guard(() => api('GET', '/api/tasks/' + task.id + '/plan/payload/' + a.actionId)); if (p) modal('写入内容：' + a.resourceKey, h('pre', { class: 'light', text: pretty(p) }), [{ label: '关闭', value: true }]); } }, '查看内容'))))))));
  if (plan.excluded.length) add(body, h('details', { class: 'panel', open: true }, h('summary', { text: '未纳入计划（' + plan.excluded.length + '）' }), h('ul', { class: 'plain small' }, plan.excluded.map(x => h('li', { text: x.resourceKey + '：' + x.reason })))));
  if (plan.outOfScopeImpact.length) add(body, h('div', { class: 'panel' }, h('h3', { text: '共享模型对范围外接口的影响' }), h('ul', { class: 'plain small' }, plan.outOfScopeImpact.map(x => h('li', { text: x.key + '（' + x.name + '，ID ' + x.id + '）' })))));
  add(body, h('div', { class: 'panel small' }, h('b', { text: '保留设置：' }), plan.preservedSettings.join('、'), h('div', { class: 'muted', text: '执行时逐个动作：重读目标并核对指纹 → 保存前态 → 写入 → 立即回读核对写入内容与受保护字段；首个失败即停止，不自动回滚，不重发超时写入。' })));

  async function confirmApply(p) {
    const ok = await modal('确认同步', h('div', null,
      h('p', { text: '将写入 Apifox 项目 ' + p.projectId + ' / 分支 ' + (p.branch || 'main') + '：' + endpoints + ' 个接口、' + models + ' 个模型。' }),
      p.outOfScopeImpact.length ? h('div', { class: 'notice warn', text: '共享模型更新会影响范围外 ' + p.outOfScopeImpact.length + ' 个接口的文档表现。' }) : null,
      h('p', { class: 'small muted', text: '同步期间请避免在 Apifox 中修改相同资源；本地锁不能阻止其他用户的修改。' })),
      [{ label: '确认同步', value: true, primary: true }]);
    if (ok) runJob(api('POST', '/api/tasks/' + task.id + '/plan/apply', { planHash: p.hash }), () => render());
  }
}

// ---------- 报告 ----------

async function tabReport(body, task) {
  const preview = await api('GET', '/api/tasks/' + task.id + '/reports/preview');
  add(body, h('div', { class: 'row toolbar' },
    h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/md', 'report.md')) }, '下载 Markdown'),
    h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/html', 'report.html')) }, '下载 HTML'),
    h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/json', 'report.json')) }, '下载 JSON'),
    h('div', { class: 'grow' }),
    h('button', { class: 'primary', onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/handoff', 'handoff.md')) }, '前端交接说明')));
  add(body, h('div', { class: 'notice small', text: '报告分别统计源码注释、XML/Swagger 传播、Apifox 一致性与业务运行验证（第一版不执行）；共享报告已脱敏。' }));
  add(body, h('pre', { class: 'light report', text: preview.markdown }));
}

async function renderReports(main) {
  const tasks = await api('GET', '/api/tasks');
  add(main, h('h1', { text: '历史报告' }));
  if (!tasks.length) { add(main, h('div', { class: 'panel empty', text: '还没有任务。' })); return; }
  const table = h('table', null, h('thead', null, h('tr', null, ['任务', '阶段', '创建时间', '下载'].map(t => h('th', { text: t })))));
  add(table, h('tbody', null, tasks.map(({ task }) => h('tr', null,
    h('td', null, h('button', { class: 'link', onclick: () => navigate('task', { taskId: task.id, tab: 'report' }) }, task.name)),
    h('td', null, stageBadges(task.stages)),
    h('td', { class: 'small', text: fmtTime(task.createdAt) }),
    h('td', null, h('div', { class: 'row' },
      h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/md', 'report.md')) }, 'Markdown'),
      h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/html', 'report.html')) }, 'HTML'),
      h('button', { onclick: () => guard(() => download('/api/tasks/' + task.id + '/reports/handoff', 'handoff.md')) }, '交接')))))));
  add(main, h('div', { class: 'panel' }, table));
}

// ---------- 启动 ----------

(async () => {
  S.session = await guard(() => api('GET', '/api/session'));
  if (S.session) document.getElementById('foot').textContent = 'v' + S.session.toolVersion + ' · 资料目录：' + S.session.dataRoot;
  const [view, extra] = parseHash();
  navigate(view, extra);
})();
