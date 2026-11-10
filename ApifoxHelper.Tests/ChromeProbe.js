// 真浏览器布局探针（HTML 折行禁则的机械量测）。
//
// 存在理由：`ReportRender.HtmlStyle` 里的 `line-break:strict;word-break:normal` 与 `max-width:40em`
// 此前只有源码层的声明门（字符串在位）。声明在位不等于折行成立——这份 HTML 是全仓唯一会折行的交付面
// （用户会原样转发、会截成长图），所以 §3「行首行尾禁则」「正文行长」这两条要由真浏览器的排版结果来判。
//
// 手法：把渲染出来的产物文件**原样**交给 headless Chromium（一个字节都不改），经 CDP 的
// `Runtime.evaluate` 在页面里量测。产物自带的 CSP（`default-src 'none'`）禁止内联脚本，
// 而 `Runtime.evaluate` 不受页面 CSP 约束——不必为了塞探针而改掉产物，也不必关掉安全策略。
//
// 用法：node ChromeProbe.js --chrome=<chrome 可执行文件> --html=<产物 html 绝对路径>
//       --start-bad=<行首禁字集> --end-bad=<行尾禁字集> [--window-width=<视口宽，默认 1280>]
// 输出：stdout 一行 JSON。
// 退出码：0 有结果 / 2 参数或文件错 / 3 起不来、连不上或超时。
"use strict";
const { spawn } = require("node:child_process");
const fs = require("node:fs");

function arg(name) {
  const hit = process.argv.slice(2).find((a) => a.startsWith(`--${name}=`));
  return hit ? hit.slice(name.length + 3) : null;
}

const chrome = arg("chrome");
const html = arg("html");
const startBad = arg("start-bad") || "";
const endBad = arg("end-bad") || "";
const windowWidth = Number(arg("window-width") || "1280");
if (!chrome || !html || !startBad || !endBad) {
  console.error("缺参数：--chrome= --html= --start-bad= --end-bad=");
  process.exit(2);
}
if (!fs.existsSync(html)) {
  console.error("产物 HTML 不存在：" + html);
  process.exit(2);
}

const profileDir = fs.mkdtempSync("/tmp/ah-chrome-profile-");
const child = spawn(chrome, [
  "--headless=old",
  "--no-sandbox",
  "--disable-gpu",
  "--disable-dev-shm-usage",
  "--allow-file-access-from-files",
  "--user-data-dir=" + profileDir,
  "--window-size=" + windowWidth + ",900",
  "--remote-debugging-port=0",
  "about:blank",
], { stdio: ["ignore", "ignore", "pipe"] });

child.on("error", (e) => { console.error("启动 Chromium 失败：" + e.message); process.exit(3); });

let wsUrl = null;
child.stderr.on("data", (chunk) => {
  if (wsUrl) return;
  const hit = String(chunk).match(/DevTools listening on (ws:\/\/\S+)/);
  if (hit) wsUrl = hit[1];
});

const pending = new Map();
let nextId = 1;
let socket;

function send(method, params, sessionId) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }));
  });
}

function waitUntil(predicate, limitMs, label) {
  return new Promise((resolve, reject) => {
    const started = Date.now();
    const tick = () => predicate()
      ? resolve()
      : (Date.now() - started > limitMs ? reject(new Error("超时：" + label)) : setTimeout(tick, 50));
    tick();
  });
}

function cleanup() {
  try { child.kill("SIGKILL"); } catch { /* 已经退了 */ }
  try { fs.rmSync(profileDir, { recursive: true, force: true }); } catch { /* 临时目录 */ }
}
process.on("exit", cleanup);

// 页面里执行的量测表达式：只读产物自己的样式与文字，不改它们。
// 判据是「折行点落在哪里」，所以只有段内折出来的行首（index>0）与行尾（index<len-1）算违例：
// 一段话的第一个字与最后一个字不是折行造出来的，把它们判成违例会把仪器变成瞎报。
const EXPRESSION = `
(function () {
  var START_BAD = ${JSON.stringify(startBad)};
  var END_BAD = ${JSON.stringify(endBad)};

  // 一个 text node 折成了哪几行：逐字取矩形，按行顶边分组；每行记住首字、尾字与其位置。
  function linesOf(node) {
    var boxes = [];
    for (var i = 0; i < node.data.length; i++) {
      var range = document.createRange();
      range.setStart(node, i);
      range.setEnd(node, i + 1);
      var rect = range.getClientRects()[0];
      if (rect) boxes.push({ i: i, ch: node.data[i], top: Math.round(rect.top), left: rect.left, right: rect.right });
    }
    var lines = [];
    boxes.forEach(function (b) {
      var line = lines.length && b.top === lines[lines.length - 1].top ? lines[lines.length - 1] : null;
      if (!line) { lines.push({ top: b.top, firstCh: b.ch, firstIndex: b.i, lastCh: b.ch, lastIndex: b.i, minLeft: b.left, maxRight: b.right }); return; }
      if (b.left < line.minLeft) { line.minLeft = b.left; line.firstCh = b.ch; line.firstIndex = b.i; }
      if (b.right > line.maxRight) { line.maxRight = b.right; line.lastCh = b.ch; line.lastIndex = b.i; }
    });
    return lines;
  }

  function countViolations(lines, length) {
    var start = 0, end = 0;
    for (var k = 1; k < lines.length; k++) {
      if (START_BAD.indexOf(lines[k].firstCh) >= 0 && lines[k].firstIndex > 0) start++;
      if (END_BAD.indexOf(lines[k - 1].lastCh) >= 0 && lines[k - 1].lastIndex < length - 1) end++;
    }
    return { lines: lines.length, start: start, end: end };
  }

  function firstTextNodeOf(el) {
    for (var i = 0; i < el.childNodes.length; i++) {
      var node = el.childNodes[i];
      if (node.nodeType === 3 && node.data.length) return node;
    }
    return null;
  }

  function textNodes(root) {
    var out = [];
    (function walk(el) {
      for (var i = 0; i < el.childNodes.length; i++) {
        var child = el.childNodes[i];
        if (child.nodeType === 3) { if (child.data.length) out.push(child); } else { walk(child); }
      }
    })(root);
    return out;
  }

  // ① 产物正文本身：真样式、真文字、真容器，逐段量折行。
  var prose = { nodes: 0, visible: 0, lines: 0, start: 0, end: 0, wrappedNodes: 0 };
  textNodes(document.body).forEach(function (node) {
    var v = countViolations(linesOf(node), node.data.length);
    prose.nodes++;
    if (v.lines > 0) prose.visible++;
    prose.lines += v.lines;
    prose.start += v.start;
    prose.end += v.end;
    if (v.lines > 1) prose.wrappedNodes++;
  });

  // ② 取产物里最长的那一句，换成窄容器重排：这一支才是「窄到必须折行」时的禁则量测。
  var longest = "";
  textNodes(document.body).forEach(function (node) {
    if (node.data.length > longest.length) longest = node.data;
  });

  function sweep(text, cssWidth, extraStyle) {
    var box = document.createElement("div");
    box.setAttribute("style", "width:" + cssWidth + "px;" + (extraStyle || ""));
    box.textContent = text;
    document.body.appendChild(box);
    var node = firstTextNodeOf(box);
    var v = countViolations(linesOf(node), node.data.length);
    box.remove();
    return v;
  }

  var widths = [];
  for (var w = 100; w <= 420; w += 20) widths.push(w);

  var inherited = { start: 0, end: 0, lines: 0 };
  widths.forEach(function (w) {
    var v = sweep(longest, w, "");
    inherited.start += v.start; inherited.end += v.end; inherited.lines += v.lines;
  });

  // ③ 同一批宽度上，四个显式取值与「不写」各跑一遍。
  //    这一支不判产物，判的是「本机这个浏览器到底能不能把这条声明的区别跑出来」——
  //    跑得出来与跑不出来都是要写进 NOTES 的事实，不能靠推测。
  var variantNames = ["", "line-break:auto", "line-break:strict", "line-break:loose", "word-break:break-all"];
  var variants = variantNames.map(function (style) {
    var acc = { start: 0, end: 0, lines: 0, marks: [] };
    widths.forEach(function (w) {
      var v = sweep(longest, w, style);
      acc.start += v.start; acc.end += v.end; acc.lines += v.lines;
      acc.marks.push(v.lines + ":" + v.start + ":" + v.end);
    });
    return { name: style || "(unwritten)", start: acc.start, end: acc.end, lines: acc.lines, fingerprint: acc.marks.join(",") };
  });

  // ④ 仪器自测：拿产物自己那句话，人为在最前面一个禁字前后插一道硬换行。
  //    插完之后必须被①②同一套判据判成违例；判不出来，上面的「零违例」只是仪器是瞎的。
  function forced(text, at, breakBefore) {
    var box = document.createElement("div");
    box.setAttribute("style", "width:420px;");
    var cut = breakBefore ? at : at + 1;
    box.appendChild(document.createTextNode(text.slice(0, cut)));
    box.appendChild(document.createElement("br"));
    box.appendChild(document.createTextNode(text.slice(cut)));
    document.body.appendChild(box);

    var lines = [];
    var start = 0, end = 0;
    for (var n = 0; n < box.childNodes.length; n++) {
      var node = box.childNodes[n];
      if (node.nodeType !== 3) continue;
      var nodeLines = linesOf(node);
      // 行首违例：本 node 内的折行点（index>0），以及跨 <br> 的那一处（第二个 node 的第一个字）。
      start += countViolations(nodeLines, node.data.length).start;
      if (n > 0 && nodeLines.length && START_BAD.indexOf(nodeLines[0].firstCh) >= 0) start++;
      end += countViolations(nodeLines, node.data.length).end;
      // 硬换行把开号留在前一段的末位：这一处不是折出来的，但正是仪器要看得见的形状。
      if (n === 0 && nodeLines.length && END_BAD.indexOf(nodeLines[nodeLines.length - 1].lastCh) >= 0) end++;
      lines = lines.concat(nodeLines);
    }
    var total = lines.length;
    box.remove();
    return { lines: total, start: start, end: end };
  }

  var firstStartBad = -1;
  for (var c = 1; c < longest.length; c++) {
    if (START_BAD.indexOf(longest[c]) >= 0) { firstStartBad = c; break; }
  }

  // 行尾对照组单独取样：全篇最长的那句不一定带开号，硬要求它带会把对照组做成「做不出来」。
  var endSample = "";
  textNodes(document.body).forEach(function (node) {
    var holds = false;
    for (var i = 0; i < node.data.length; i++) {
      if (END_BAD.indexOf(node.data[i]) >= 0) { holds = true; break; }
    }
    if (!holds) return;
    if (node.data.length > endSample.length) endSample = node.data;
  });
  var firstEndBad = -1;
  for (var e = 0; e < endSample.length - 1; e++) {
    if (END_BAD.indexOf(endSample[e]) >= 0) { firstEndBad = e; break; }
  }

  // ⑤ 正文列宽：视口 1280px，body 宽度必须由 max-width 决定，而不是跟着视口走。
  var bodyStyle = getComputedStyle(document.body);
  var bodyRect = document.body.getBoundingClientRect();

  return JSON.stringify({
    viewport: window.innerWidth,
    fontSizePx: parseFloat(bodyStyle.fontSize),
    maxWidthComputed: bodyStyle.maxWidth,
    bodyWidthPx: Math.round(bodyRect.width),
    lineBreakComputed: bodyStyle.lineBreak,
    wordBreakComputed: bodyStyle.wordBreak,
    prose: prose,
    longestLength: longest.length,
    endSampleLength: endSample.length,
    firstStartBadIndex: firstStartBad,
    firstEndBadIndex: firstEndBad,
    sweepWidths: widths.length,
    sweep: { start: inherited.start, end: inherited.end, lines: inherited.lines },
    variants: variants.map(function (v) {
      return { name: v.name, start: v.start, end: v.end, lines: v.lines, fingerprint: v.fingerprint };
    }),
    forcedStart: firstStartBad < 0 ? null : forced(longest, firstStartBad, true),
    forcedEnd: firstEndBad < 0 ? null : forced(endSample, firstEndBad, false),
  });
})()
`;

(async () => {
  try {
    await waitUntil(() => wsUrl, 30000, "DevTools 端口");
    socket = new WebSocket(wsUrl);
    await new Promise((resolve, reject) => {
      socket.addEventListener("open", resolve);
      socket.addEventListener("error", () => reject(new Error("CDP 连接失败")));
    });
    socket.addEventListener("message", (event) => {
      const msg = JSON.parse(event.data);
      if (!msg.id || !pending.has(msg.id)) return;
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(msg.error.message + " " + JSON.stringify(msg.error.data || "")));
      else resolve(msg.result);
    });

    const { targetId } = await send("Target.createTarget", { url: "file://" + html });
    const { sessionId } = await send("Target.attachToTarget", { targetId, flatten: true });
    await send("Page.enable", {}, sessionId);

    let ready = "";
    for (let i = 0; i < 150 && ready !== "complete"; i++) {
      const probe = await send("Runtime.evaluate", { expression: "document.readyState", returnByValue: true }, sessionId);
      ready = probe.result && probe.result.value;
      if (ready !== "complete") await new Promise((r) => setTimeout(r, 100));
    }
    if (ready !== "complete") { console.error("产物文档没进 complete 状态"); process.exit(3); }

    const result = await send("Runtime.evaluate", { expression: EXPRESSION, returnByValue: true }, sessionId);
    if (result.exceptionDetails) {
      console.error("页面里抛错：" + JSON.stringify(result.exceptionDetails).slice(0, 600));
      process.exit(3);
    }
    process.stdout.write(String(result.result.value) + "\n");
    socket.close();
    process.exit(0);
  } catch (e) {
    console.error("探针失败：" + e.message);
    process.exit(3);
  }
})();
