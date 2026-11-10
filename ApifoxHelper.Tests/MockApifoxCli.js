'use strict';
// 本地 mock Apifox CLI —— 只服务 ApifoxHelper.Tests/MockApifoxCliTests.cs（SHARED-3/4/7/8 端到端）。
//
// 它不在产品的运行路径里：主工程不编译、不打包、不引用它，产品源码里也没有它的名字。测试把这份文件
// 原样复制进临时夹具目录，再由 Infrastructure/ProcessRunner.cs 真实启动 —— 走的正是生产那条
// 「可执行文件 + 脚本 + 参数列表，不经 shell」的边界，输出解析也照走 Apifox/ApifoxCli.cs 的 Parse。
//
// 模仿的是 docs/NOTES.md「已核实的环境事实」里对本机 CLI 2.2.6 记下的协议：
//   · stdout = 一行前导说明 + 单个完整 JSON 对象（success / data / meta / error.code / error.message）
//   · 退出码 0 成功、1 操作或 API 失败、2 认证失败、3 输入或校验失败
//   · 「资源不存在」是 success:false + error.code "404000"，其他失败不得当成不存在
//   · update 只提交传入的顶层字段；数组与嵌套对象整体替换，不按 id 合并
//   · 平台会刷新 updatedAt 这类易变字段，其余内容不动
//
// 只实现这四条用例用到的命令；其余命令一律按「未知命令」以退出码 3 失败。将来加用例时必须显式加一支，
// 免得默默走进一个谁也没测过的分支。

const fs = require('fs');
const path = require('path');

const stateDir = path.join(path.dirname(process.argv[1]), 'state');
const remoteFile = path.join(stateDir, 'remote.json');
const callsFile = path.join(stateDir, 'calls.jsonl');

const OPTIONS = new Set(['--project', '--branch', '--file', '--output', '--format', '--page', '--page-size', '--name', '--type', '--path-contains']);
const arg = process.argv.slice(2);
const rest = [];
for (let i = 0; i < arg.length; i++) {
  if (arg[i].startsWith('--')) {
    if (OPTIONS.has(arg[i])) i++;
    continue;
  }
  rest.push(arg[i]);
}

function option(name) {
  const i = arg.indexOf(name);
  return i < 0 || i + 1 >= arg.length ? null : arg[i + 1];
}

function readJson(file, fallback) {
  if (!fs.existsSync(file)) return fallback;
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

function loadRemote() {
  return readJson(remoteFile, { endpoints: {}, schemas: {}, nextId: 900, clock: 0 });
}

function storeRemote(remote) {
  fs.writeFileSync(remoteFile, JSON.stringify(remote, null, 2));
}

function flag(name) {
  return readJson(path.join(stateDir, name), []);
}

function on(name) {
  return fs.existsSync(path.join(stateDir, name));
}

function record(entry) {
  fs.appendFileSync(callsFile, JSON.stringify(entry) + '\n');
}

function print(body) {
  console.log('apifox-cli(mock)：本地假数据，不访问网络');
  console.log(JSON.stringify(body));
  if (on('trailing-junk.json')) console.log('JSON 之后又冒出一行输出');
}

function succeed(data, meta) {
  const body = { success: true, data: data === null || data === undefined ? {} : data };
  if (meta) body.meta = meta;
  print(body);
  process.exit(0);
}

function fail(code, message, exitCode) {
  print({ success: false, error: { code: String(code), message: message } });
  process.exit(exitCode === undefined ? 1 : exitCode);
}

function fromFile() {
  const file = option('--file');
  if (!file) return null;
  return readJson(file, null);
}

function summary(endpoint) {
  return { id: endpoint.id, method: endpoint.method, path: endpoint.path, name: endpoint.name };
}

// —— 版本：CLI 的 --version 只输出一行版本号。
if (arg.includes('--version')) {
  console.log(readJson(path.join(stateDir, 'version.json'), '2.2.6'));
  process.exit(0);
}

// —— 写入前校验：真实 CLI 的 cli-schema validate，失败退出码 3。
if (rest[0] === 'cli-schema') {
  const key = rest[2];
  record({ cmd: 'cli-schema.validate', key: key, file: option('--file') });
  if (flag('fail-validate.json').includes(key)) fail(300100, '字段类型不匹配', 3);
  succeed({ valid: true });
}

const kind = rest[0];
const verb = rest[1];

if (kind === 'endpoint') {
  const remote = loadRemote();
  const endpoints = remote.endpoints;

  if (verb === 'list') {
    let all = Object.values(endpoints);
    const contains = option('--path-contains');
    record({ cmd: 'endpoint.list', project: option('--project'), branch: option('--branch'), pathContains: contains, total: Object.keys(endpoints).length });
    if (contains !== null) all = all.filter((e) => String(e.path).includes(contains));
    const page = option('--page') === null ? 0 : Number(option('--page'));
    const size = option('--page-size') === null ? 0 : Number(option('--page-size'));
    if (page > 0 && size > 0) {
      const start = (page - 1) * size;
      const slice = all.slice(start, start + size);
      const totalPages = Math.ceil(all.length / size);
      succeed(slice.map(summary), {
        total: all.length,
        returned: slice.length,
        page: page,
        pageSize: size,
        totalPages: totalPages,
        nextPage: page < totalPages ? page + 1 : null,
      });
    }
    succeed(all.map(summary));
  }

  if (verb === 'get') {
    const id = Number(rest[2]);
    record({ cmd: 'endpoint.get', id: id, project: option('--project'), branch: option('--branch') });
    const found = endpoints[String(id)];
    // 平台明确回答「不存在」：success:false + 404000，退出码 1。
    if (!found) fail(404000, 'Not found');
    succeed(found);
  }

  if (verb === 'update') {
    const id = Number(rest[2]);
    const payload = fromFile();
    record({ cmd: 'endpoint.update', id: id, project: option('--project'), branch: option('--branch'), keys: payload === null ? [] : Object.keys(payload) });
    if (payload === null) fail(300100, '写入文件无效', 3);
    const found = endpoints[String(id)];
    if (!found) fail(404000, 'Not found');
    if (flag('fail-writes.json').includes(id)) {
      // 写入失败：远端一个字都不改，也不留下半个版本。
      fail(500100, '接口保存失败：服务端校验未通过');
    }
    remote.clock += 1;
    const merged = Object.assign({}, found, payload, { id: id, updatedAt: remote.clock });
    if (flag('drop-protected.json').includes(id)) {
      // 平台没能保留受保护设置的场景：前后置脚本与 Mock 配置在整段替换中丢了。
      delete merged.script;
      delete merged.mock;
    }
    endpoints[String(id)] = merged;
    storeRemote(remote);
    succeed({ id: id });
  }

  if (verb === 'create') {
    const payload = fromFile();
    if (payload === null) fail(300100, '写入文件无效', 3);
    remote.clock += 1;
    const id = remote.nextId;
    remote.nextId += 1;
    endpoints[String(id)] = Object.assign({ name: payload.path, status: 'released', parameters: {}, requestBody: {}, responses: [] }, payload, { id: id, createdAt: remote.clock });
    storeRemote(remote);
    record({ cmd: 'endpoint.create', id: id, project: option('--project'), branch: option('--branch'), keys: Object.keys(payload) });
    // 真实 CLI 是否回传新资源 id 尚未验证过；这条分支由 create-returns-no-id.json 开关模拟。
    if (on('create-returns-no-id.json')) succeed({});
    succeed({ id: id });
  }
}

fail(300300, 'mock CLI 没有实现这个命令：' + rest.join(' '), 3);
