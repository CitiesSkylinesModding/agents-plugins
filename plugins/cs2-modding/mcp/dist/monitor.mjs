var __create = Object.create;
var __getProtoOf = Object.getPrototypeOf;
var __defProp = Object.defineProperty;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __hasOwnProp = Object.prototype.hasOwnProperty;
function __accessProp(key) {
  return this[key];
}
var __toESMCache_node;
var __toESMCache_esm;
var __toESM = (mod, isNodeMode, target) => {
  var canCache = mod != null && typeof mod === "object";
  if (canCache) {
    var cache = isNodeMode ? __toESMCache_node ??= new WeakMap : __toESMCache_esm ??= new WeakMap;
    var cached = cache.get(mod);
    if (cached)
      return cached;
  }
  target = mod != null ? __create(__getProtoOf(mod)) : {};
  const to = isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target;
  for (let key of __getOwnPropNames(mod))
    if (!__hasOwnProp.call(to, key))
      __defProp(to, key, {
        get: __accessProp.bind(mod, key),
        enumerable: true
      });
  if (canCache)
    cache.set(mod, to);
  return to;
};
var __commonJS = (cb, mod) => () => (mod || cb((mod = { exports: {} }).exports, mod), mod.exports);
var __returnValue = (v) => v;
function __exportSetter(name, newValue) {
  this[name] = __returnValue.bind(null, newValue);
}
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, {
      get: all[name],
      enumerable: true,
      configurable: true,
      set: __exportSetter.bind(all, name)
    });
};

// src/monitor.ts
import process4 from "node:process";

// src/config.ts
import os from "node:os";
import path from "node:path";
import process from "node:process";
var STEAM_APP_ID = 949230;
function loadUserDataPath() {
  return resolveUserDataPath(process.env.CSII_USERDATAPATH, process.platform, os.homedir());
}
function resolveUserDataPath(configured, platform, home) {
  if (configured) {
    return configured;
  }
  const profile = platform == "win32" ? home : path.join(home, `.local/share/Steam/steamapps/compatdata/${STEAM_APP_ID}/pfx/drive_c/users/steamuser`);
  return path.join(profile, "AppData/LocalLow/Colossal Order/Cities Skylines II");
}
function loadLocalModsPath(userDataPath) {
  const configured = process.env.CSII_LOCALMODSPATH;
  if (configured) {
    return configured;
  }
  return path.join(userDataPath, "Mods");
}
function userManagerEnvironment() {
  const { env } = process;
  return env.XDG_RUNTIME_DIR || env.DBUS_SESSION_BUS_ADDRESS ? env : { ...env, XDG_RUNTIME_DIR: `/run/user/${os.userInfo().uid}` };
}
function wineEnvironment(prefix) {
  return { ...process.env, WINEPREFIX: prefix, LC_ALL: "C" };
}

// src/game-host.ts
import { spawn } from "node:child_process";
import { readFile } from "node:fs/promises";
import path2 from "node:path";
import process2 from "node:process";
import { setTimeout as sleep } from "node:timers/promises";
var STEAM_URL = `steam://rungameid/${STEAM_APP_ID}`;
var GAME_PROCESS = "Cities2.exe";
var COMMAND_TIMEOUT_MILLISECONDS = 30000;
var DRAIN_MILLISECONDS = 200;
var MILLISECONDS_PER_SECOND = 1000;
var pgrepExitCodes = [0, 1];
function createGameHost(userDataPath) {
  return process2.platform == "win32" ? windowsGameHost() : linuxGameHost(userDataPath);
}
function isMissing(error) {
  return error instanceof Error && "code" in error && error.code == "ENOENT";
}
function errorMessage(error) {
  return error instanceof Error ? error.message : String(error);
}
function parsePgrep(output) {
  return output.split(`
`).map(Number).filter((pid) => isProcessId(pid));
}
function parseReapers(output) {
  return [...output.matchAll(new RegExp(`^(\\d+) .*\\bAppId=${STEAM_APP_ID}\\b`, "gmu"))].map(([, pid]) => Number(pid)).filter((pid) => isProcessId(pid));
}
function parseTasklist(csv) {
  return [...csv.matchAll(/^"Cities2\.exe","(?<pid>\d+)"/gimu)].map((match) => Number(match.groups?.pid)).filter((pid) => isProcessId(pid));
}
function isProcessId(pid) {
  return Number.isSafeInteger(pid) && pid > 0;
}
var clock = {
  now: () => new Date,
  sleep: (milliseconds) => sleep(milliseconds),
  alarm: (seconds) => sleep(seconds * MILLISECONDS_PER_SECOND, undefined, { ref: false })
};
function linuxGameHost(userDataPath) {
  return {
    ...clock,
    findPids: findLinuxPids,
    async launch() {
      await runChecked("systemd-run", ["--user", "--collect", "--quiet", "steam", STEAM_URL], {
        env: userManagerEnvironment()
      });
    },
    askToEnd: () => askProtonToClose(userDataPath),
    async forceToEnd(stage) {
      const { game, reapers } = await findLinuxPids();
      for (const pid of [...game, ...reapers]) {
        try {
          process2.kill(pid, stage == "terminate" ? "SIGTERM" : "SIGKILL");
        } catch {}
      }
    }
  };
}
async function findLinuxPids() {
  const game = await runChecked("pgrep", ["-x", GAME_PROCESS], { exitCodes: pgrepExitCodes });
  const reapers = await runChecked("pgrep", ["-ax", "reaper"], { exitCodes: pgrepExitCodes });
  return { game: parsePgrep(game), reapers: parseReapers(reapers) };
}
async function askProtonToClose(userDataPath) {
  const prefixMark = `${path2.sep}pfx${path2.sep}`;
  const prefixAt = userDataPath.lastIndexOf(prefixMark);
  if (prefixAt == -1) {
    return `the user data folder "${userDataPath}" is not in a Proton prefix.`;
  }
  const compatDataPath = userDataPath.slice(0, prefixAt);
  const configInfoPath = path2.join(compatDataPath, "config_info");
  let configInfo = "";
  try {
    configInfo = await readFile(configInfoPath, "utf8");
  } catch (error) {
    if (!isMissing(error)) {
      return `${configInfoPath} could not be read: ${errorMessage(error)}`;
    }
  }
  const fontsSuffix = `/share/fonts/`;
  const fontsPath = configInfo.split(`
`)[1] ?? "";
  if (!fontsPath.endsWith(fontsSuffix)) {
    return `no Proton install named in ${configInfoPath}.`;
  }
  const wine = path2.join(fontsPath.slice(0, -fontsSuffix.length), "bin", "wine");
  try {
    const { output, errorOutput } = await run(wine, ["taskkill", "/IM", GAME_PROCESS], {
      env: wineEnvironment(path2.join(compatDataPath, "pfx"))
    });
    if (output.includes("Close message sent")) {
      return;
    }
    const answer = output.trim() || errorOutput.trim().split(`
`).at(-1);
    return `wine taskkill answered "${answer}".`;
  } catch (error) {
    return `${wine} did not run: ${errorMessage(error)}`;
  }
}
function windowsGameHost() {
  return {
    ...clock,
    async findPids() {
      const filter = `IMAGENAME eq ${GAME_PROCESS}`;
      const output = await runChecked("tasklist", ["/FI", filter, "/FO", "CSV", "/NH"]);
      return { game: parseTasklist(output), reapers: [] };
    },
    async launch() {
      await runChecked("cmd", ["/c", "start", STEAM_URL]);
    },
    async askToEnd() {
      try {
        const { code, output } = await run("taskkill", ["/IM", GAME_PROCESS]);
        return code == 0 ? undefined : `taskkill answered "${output.trim()}".`;
      } catch (error) {
        return `taskkill did not run: ${errorMessage(error)}`;
      }
    },
    async forceToEnd() {
      await run("taskkill", ["/F", "/IM", GAME_PROCESS]);
    }
  };
}
async function runChecked(file, args, options = {}) {
  const { output, errorOutput, code } = await run(file, args, options);
  if (code == null || !(options.exitCodes ?? [0]).includes(code)) {
    const reason = errorOutput.trim();
    throw new Error(`${file} ${args.join(" ")} exited with ${code ?? "a signal"}${reason ? `: ${reason}` : "."}`);
  }
  return output;
}
function run(file, args, options = {}) {
  const timeoutMilliseconds = options.timeoutMilliseconds ?? COMMAND_TIMEOUT_MILLISECONDS;
  return new Promise((resolve, reject) => {
    const child = spawn(file, args, {
      stdio: ["ignore", "pipe", "pipe"],
      ...options.env ? { env: options.env } : {}
    });
    const chunks = [];
    const errorChunks = [];
    const limit = setTimeout(() => {
      child.kill("SIGKILL");
      closePipes();
      child.unref();
      reject(new Error(`${file} did not answer in ${timeoutMilliseconds} ms.`));
    }, timeoutMilliseconds);
    function closePipes() {
      child.stdout.destroy();
      child.stderr.destroy();
    }
    function finish(code) {
      clearTimeout(limit);
      closePipes();
      resolve({ output: chunks.join(""), errorOutput: errorChunks.join(""), code });
    }
    child.once("error", (error) => {
      clearTimeout(limit);
      reject(error);
    });
    if (child.pid == null) {
      return;
    }
    child.stdout.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      chunks.push(chunk);
    });
    child.stderr.setEncoding("utf8");
    child.stderr.on("data", (chunk) => {
      errorChunks.push(chunk);
    });
    child.once("close", finish);
    child.once("exit", (code) => {
      setTimeout(finish, DRAIN_MILLISECONDS, code);
    });
  });
}

// src/cursor.ts
import { mkdir, readFile as readFile2, rename, writeFile } from "node:fs/promises";
import path3 from "node:path";
import process3 from "node:process";

// src/queue.ts
function createQueue() {
  let last = Promise.resolve();
  function enqueue(signal, step) {
    const previous = last;
    const result = runAfter(previous, signal, step);
    last = letGo(previous, result, signal);
    return result;
  }
  return { run: enqueue };
}
async function runAfter(previous, signal, step) {
  await previous;
  signal.throwIfAborted();
  return step();
}
async function letGo(previous, result, signal) {
  await previous;
  await Promise.race([whenSettled(result), whenAborted(signal)]);
}
async function whenSettled(step) {
  try {
    await step;
  } catch {}
}
function whenAborted(signal) {
  return new Promise((resolve) => {
    if (signal.aborted) {
      resolve();
      return;
    }
    signal.addEventListener("abort", () => {
      resolve();
    }, { once: true });
  });
}

// src/cursor.ts
var updates = createQueue();
var writes = 0;
function toolFolderPath(userDataPath) {
  return path3.join(userDataPath, ".cs2-dev");
}
async function readCursor(userDataPath) {
  try {
    const cursor = JSON.parse(await readFile2(cursorPath(userDataPath), "utf8"));
    return { ...cursor, offsets: cursor?.offsets ?? {} };
  } catch (error) {
    if (isMissing(error) || error instanceof SyntaxError) {
      return { offsets: {} };
    }
    throw error;
  }
}
function updateCursor({ userDataPath, signal }, change, shrunk = []) {
  return updates.run(signal, async () => {
    const current = await readCursor(userDataPath);
    const next = change(current);
    const isSameRun = next.runStart == current.runStart;
    await writeJson(cursorPath(userDataPath), {
      ...next,
      offsets: isSameRun ? keepLarger(current.offsets, next.offsets, shrunk) : next.offsets
    });
  });
}
function keepLarger(current, next, shrunk) {
  const larger = Object.entries(next).map(([file, offset]) => [
    file,
    shrunk.includes(file) ? offset : Math.max(offset, current[file] ?? 0)
  ]);
  return { ...current, ...Object.fromEntries(larger) };
}
async function writeJson(filePath, value) {
  writes++;
  const temporaryPath = `${filePath}.${process3.pid}.${writes}.tmp`;
  await mkdir(path3.dirname(filePath), { recursive: true });
  await writeFile(temporaryPath, JSON.stringify(value, undefined, 2));
  await rename(temporaryPath, filePath);
}
function cursorPath(userDataPath) {
  return path3.join(toolFolderPath(userDataPath), "cursor.json");
}

// src/hint.ts
import { readdir as readdir2, stat as stat2 } from "node:fs/promises";
import path6 from "node:path";

// src/game-log.ts
var EXCEPTION_LEVEL = "EXCEPTION";
var PLAYER_ENTRY_LINES = 2000;
var taggedLevels = [
  "VERBOSE",
  "TRACE",
  "DEBUG",
  "INFO",
  "WARN",
  "ERROR",
  "CRITICAL",
  "FATAL",
  "EMERGENCY"
];
var logLevels = [...taggedLevels, EXCEPTION_LEVEL];
var errorLevels = new Set([
  "ERROR",
  "CRITICAL",
  "FATAL",
  "EMERGENCY",
  EXCEPTION_LEVEL
]);
var entryPattern = /^\[(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}) (?<hours>\d{2}):(?<minutes>\d{2}):(?<seconds>\d{2}),(?<milliseconds>\d{3})\] \[(?<level>[A-Z]+)\] {1,2}(?<message>.*)$/u;
var taggedPattern = new RegExp(String.raw`^\[(?<logger>[^\]]+)\] \[(?<level>${taggedLevels.join("|")})\] {1,2}(?<message>.*)$`, "u");
var exceptionPattern = /^[\w.+`]*Exception\b/u;
var managedFramePattern = /^\s+at (?<frame>.+?)(?: \[0x[\da-f]+\])?(?: in .*)?$/u;
var unityFramePattern = /^(?<frame>[\w.+<>`$|]+:[\w.<>`$|]+\(.*?\))(?: \(at .*\))?$/u;
var loggingFramePattern = /^(?:UnityEngine|Colossal\.Logging)\./u;
var HANDLER_LINE = "UnityEngine.DebugLogHandler:Internal_LogException(";
var BURST_SENTENCE = `This Exception was thrown from a job compiled with Burst`;
var nativeFramePattern = /^0x[\da-f]{16} \((?![^()]+_win_x86_64\) [^(\s])/iu;
function isErrorLevel(level) {
  return errorLevels.has(level);
}
function topFrame(lines) {
  for (const line of lines) {
    const managed = managedFramePattern.exec(line)?.groups?.frame;
    if (managed) {
      return managed;
    }
    const unity = unityFramePattern.exec(line)?.groups?.frame;
    if (unity && !loggingFramePattern.test(unity)) {
      return unity;
    }
  }
  return "";
}
function parseLog(text, since = new Date(0)) {
  const entries = [];
  for (const line of text.split(/\r?\n/u)) {
    const groups = entryPattern.exec(line)?.groups;
    const last = entries.at(-1);
    if (groups) {
      entries.push({
        time: readStamp(groups),
        level: groups.level ?? "",
        message: groups.message ?? ""
      });
    } else if (line && last) {
      last.message += `
${line}`;
    }
  }
  return entries.filter((entry) => entry.time >= since);
}
function createLogParser() {
  let open;
  function push(line) {
    const groups = entryPattern.exec(line.text)?.groups;
    if (groups) {
      const ended = open;
      open = {
        line,
        logger: undefined,
        level: groups.level ?? "",
        time: readStamp(groups),
        lines: [groups.message ?? ""]
      };
      return ended && closeEntry(ended);
    }
    if (line.text && open) {
      open.lines.push(line.text);
    }
    return;
  }
  return {
    push,
    openOffset: () => open?.line.offset,
    finish: () => open && closeEntry(open)
  };
}
function createPlayerLogParser() {
  let open;
  let exception;
  let isConfirmed = false;
  function push(line) {
    const { text } = line;
    if (open) {
      if (!text) {
        return takeOpen();
      }
      const ended = open;
      if (startTagged(line)) {
        return closeEntry(ended);
      }
      if (!nativeFramePattern.test(text)) {
        open.lines.push(text);
      }
      return open.lines.length < PLAYER_ENTRY_LINES ? undefined : takeOpen();
    }
    if (exception) {
      return pushToException(exception, line);
    }
    if (text && !startTagged(line)) {
      startException(line);
    }
    return;
  }
  function pushToException(block, line) {
    const { text } = line;
    if (!text) {
      return isConfirmed ? takeException() : undefined;
    }
    if (startTagged(line)) {
      return takeException();
    }
    if (exceptionPattern.test(text)) {
      const ended = takeException();
      startException(line);
      return ended;
    }
    isConfirmed ||= confirmsException(block, text);
    if (block.lines.length < PLAYER_ENTRY_LINES && !nativeFramePattern.test(text)) {
      block.lines.push(text);
    }
    return;
  }
  function startTagged(line) {
    const groups = taggedPattern.exec(line.text)?.groups;
    if (!groups) {
      return false;
    }
    open = {
      line,
      logger: groups.logger,
      level: groups.level ?? "",
      time: undefined,
      lines: [groups.message ?? ""]
    };
    return true;
  }
  function startException(line) {
    if (exceptionPattern.test(line.text)) {
      exception = {
        line,
        logger: undefined,
        level: EXCEPTION_LEVEL,
        time: undefined,
        lines: [line.text]
      };
    }
  }
  function takeOpen() {
    const ended = open;
    open = undefined;
    return ended && closeEntry(ended);
  }
  function takeException() {
    const ended = isConfirmed ? exception : undefined;
    exception = undefined;
    isConfirmed = false;
    return ended && closeEntry(ended);
  }
  return {
    push,
    openOffset: () => (open ?? exception)?.line.offset,
    finish: () => takeOpen() ?? takeException()
  };
}
function confirmsException({ lines }, text) {
  return text.startsWith(HANDLER_LINE) || lines.length == 1 && text.startsWith(BURST_SENTENCE);
}
function closeEntry({ line, logger, level, time, lines }) {
  return {
    line: line.number,
    logger,
    level,
    time,
    text: lines.join(`
`)
  };
}
function readStamp(groups) {
  return new Date(Number(groups.year), Number(groups.month) - 1, Number(groups.day), Number(groups.hours), Number(groups.minutes), Number(groups.seconds), Number(groups.milliseconds));
}

// src/ignores.ts
import { readFile as readFile3, rm } from "node:fs/promises";
import path4 from "node:path";
import { createContext, Script } from "node:vm";
var IGNORE_INPUT_CHARACTERS = 4096;
var MATCH_MILLISECONDS = 50;
var updates2 = createQueue();
var matchScript = new Script(`new RegExp(source, 'i').test(text)`);
var matchSandbox = createContext({ source: "", text: "" });
async function readIgnores(userDataPath) {
  try {
    const parsed = JSON.parse(await readFile3(ignoresPath(userDataPath), "utf8"));
    return Array.isArray(parsed) ? parsed.filter((item) => isIgnorePattern(item)) : [];
  } catch (error) {
    if (isMissing(error) || error instanceof SyntaxError) {
      return [];
    }
    throw error;
  }
}
function updateIgnores({ userDataPath, signal }, change) {
  return updates2.run(signal, async () => {
    const next = change(await readIgnores(userDataPath));
    await writeJson(ignoresPath(userDataPath), next);
    return next;
  });
}
async function markDropped(context, dropped) {
  if (dropped.length == 0) {
    return;
  }
  const sources = new Set(dropped.map(({ source }) => source));
  await updateIgnores(context, (stored) => stored.map(({ source, hits, isDropped }) => ({
    source,
    hits,
    isDropped: sources.has(source) ? true : isDropped
  })));
}
function clearIgnores(userDataPath) {
  return rm(ignoresPath(userDataPath), { force: true });
}
function requireValidPattern(source) {
  try {
    new RegExp(source, "i");
  } catch (error) {
    throw new Error(`Not a valid regular expression: ${String(error)}`, { cause: error });
  }
}
function matchPattern(source, text) {
  matchSandbox.source = source;
  matchSandbox.text = text;
  try {
    return Boolean(matchScript.runInContext(matchSandbox, { timeout: MATCH_MILLISECONDS }));
  } catch (error) {
    if (typeof error == "object" && error != null && "code" in error && error.code == "ERR_SCRIPT_EXECUTION_TIMEOUT") {
      return "tooSlow";
    }
    return false;
  }
}
function isIgnorePattern(item) {
  return typeof item == "object" && item != null && "source" in item && typeof item.source == "string" && "hits" in item && typeof item.hits == "number";
}
function ignoresPath(userDataPath) {
  return path4.join(toolFolderPath(userDataPath), "ignores.json");
}

// src/scanner.ts
import { createReadStream } from "node:fs";
import { readdir, stat } from "node:fs/promises";
import path5 from "node:path";
var QUIET_SECONDS = 2;
var TWIN_HOLD_SECONDS = QUIET_SECONDS;
var HEAD_BYTES = 65536;
var FIRST_STAMP_BYTES = 1024;
var KIND_FIRST_LINE_CHARACTERS = 200;
var SCAN_WINDOW_BYTES = 67108864;
var PLAYER_LOG = "Player.log";
var numberPattern = /0x[\da-f]+|\b[\da-f]{8,}\b|\d+/giu;
async function scanLogs(request) {
  const { basePath, runStart, offsets, hasFile, signal, windowBytes } = request;
  const kinds = new Map;
  const reached = {};
  const shrunk = [];
  const twins = createTwinCheck(request);
  const namesMod = createModCheck(request.mod);
  const files = await listLogFiles(basePath);
  for (const file of files.filter((name) => hasFile?.(name) ?? true)) {
    const filePath = path5.join(basePath, file);
    const fileStat = await stat(filePath).catch(ignoreMissing);
    if (!fileStat) {
      continue;
    }
    const isPlayerLog = file == PLAYER_LOG;
    if (!isPlayerLog && fileStat.mtime < runStart) {
      continue;
    }
    const given = offsets[file] ?? 0;
    const start = given > fileStat.size ? 0 : given;
    if (start != given) {
      shrunk.push(file);
    }
    const seen = { ofRun: 0, older: 0 };
    const end = Math.min(fileStat.size, start + (windowBytes ?? Infinity));
    const reachedEnd = await readEntries({
      filePath,
      start,
      end,
      parser: isPlayerLog ? createPlayerLogParser() : createLogParser(),
      isSettled: end == fileStat.size && (!request.isRunning || isQuiet(request.host, fileStat.mtime)),
      signal
    }, async (entry) => {
      if (!isPlayerLog && entry.time && entry.time < runStart) {
        seen.older++;
        return;
      }
      seen.ofRun++;
      if (request.accepts(entry) && !(isPlayerLog && await twins.isTwin(entry))) {
        tally(kinds, request, { entry, file, namesMod });
      }
    });
    if (seen.ofRun > 0 || seen.older == 0) {
      reached[file] = reachedEnd;
    }
  }
  return { kinds, offsets: reached, shrunk };
}
function describeSource(place, { logger, level }) {
  return `${place}${logger == undefined ? "" : ` [${logger}]`} [${level}]`;
}
function readRunStart(basePath) {
  return readFirstStamp(sceneFlowPath(basePath));
}
function sceneFlowPath(basePath) {
  return path5.join(basePath, "Logs", "SceneFlow.log");
}
async function listLogFiles(basePath) {
  const names = await readdir(path5.join(basePath, "Logs")).catch((error) => {
    if (isMissing(error)) {
      return [];
    }
    throw error;
  });
  return [...names.filter((name) => name.endsWith(".log")).map((name) => `Logs/${name}`), PLAYER_LOG];
}
async function readHead(logPath, bytes = HEAD_BYTES) {
  const lines = [];
  try {
    for await (const line of readLines({ filePath: logPath, start: 0, end: bytes })) {
      lines.push(line.text);
    }
  } catch (error) {
    if (!isMissing(error)) {
      throw new Error(`Cannot read ${logPath}: ${errorMessage(error)}`, { cause: error });
    }
  }
  return lines.join(`
`);
}
async function readEntries(source, visit) {
  const { filePath, start, end, parser, isSettled, signal } = source;
  let reached = start;
  const lines = readLines({ filePath, start, end, shouldIncludePartial: isSettled, signal });
  for await (const line of lines) {
    const ended = parser.push(line);
    if (ended) {
      await visit(ended);
    }
    reached = parser.openOffset() ?? line.offset + line.bytes;
  }
  if (isSettled) {
    const last = parser.finish();
    if (last) {
      await visit(last);
    }
    return end;
  }
  return reached;
}
async function* readLines({
  filePath,
  start,
  end,
  shouldIncludePartial = true,
  signal
}) {
  if (end <= start) {
    return;
  }
  let pending = Buffer.alloc(0);
  let offset = start;
  let number = 1;
  const stream = createReadStream(filePath, { start, end: end - 1, signal });
  for await (const chunk of stream) {
    pending = Buffer.concat([pending, chunk]);
    for (;; ) {
      const newline = pending.indexOf(`
`);
      if (newline == -1) {
        break;
      }
      const bytes = newline + 1;
      const text = pending.toString("utf8", 0, newline).replace(/\r$/u, "");
      yield { text, number, offset, bytes };
      pending = pending.subarray(bytes);
      offset += bytes;
      number++;
    }
  }
  if (pending.length > 0 && shouldIncludePartial) {
    yield {
      text: pending.toString("utf8").replace(/\r$/u, ""),
      number,
      offset,
      bytes: pending.length
    };
  }
}
function createTwinCheck(request) {
  const { basePath, runStart, host, isRunning, shouldKeepTwins } = request;
  const known = new Map;
  let hasWaited = !isRunning;
  return { isTwin };
  async function isTwin({ logger }) {
    if (logger == undefined || shouldKeepTwins) {
      return false;
    }
    const answer = known.get(logger) ?? await hasOwnFile(logger);
    if (answer || hasWaited) {
      known.set(logger, answer);
      return answer;
    }
    hasWaited = true;
    await host.sleep(TWIN_HOLD_SECONDS * MILLISECONDS_PER_SECOND);
    const settled = await hasOwnFile(logger);
    known.set(logger, settled);
    return settled;
  }
  async function hasOwnFile(logger) {
    if (/[/\\]/u.test(logger)) {
      return false;
    }
    const logPath = path5.join(basePath, "Logs", `${logger}.log`);
    const time = await readFirstStamp(logPath).catch(() => {
      return;
    });
    return time != null && time >= runStart;
  }
}
function tally(kinds, request, {
  entry,
  file,
  namesMod
}) {
  const logger = entry.logger ?? path5.basename(file, ".log");
  const key = kindKey(logger, entry);
  const known = kinds.get(key);
  const isModNamed = namesMod(logger, entry.text);
  if (known) {
    known.count++;
    known.modCount += isModNamed ? 1 : 0;
    known.lastTime = entry.time ?? known.lastTime;
    return;
  }
  kinds.set(key, {
    key,
    file,
    entry,
    ignoredBy: request.findIgnore(ignoreText(file, entry)),
    count: 1,
    modCount: isModNamed ? 1 : 0,
    firstTime: entry.time,
    lastTime: entry.time
  });
}
function ignoreText(file, entry) {
  return `${describeSource(file, entry)}
${entry.text.slice(0, IGNORE_INPUT_CHARACTERS)}`;
}
function kindKey(logger, { level, text }) {
  const [first = "", ...rest] = text.split(`
`);
  const line = maskNumbers(first).slice(0, KIND_FIRST_LINE_CHARACTERS);
  return [maskNumbers(logger), level, line, maskNumbers(topFrame(rest))].join(`
`);
}
function maskNumbers(text) {
  return text.replace(numberPattern, "#");
}
function createModCheck(mod) {
  if (mod == undefined) {
    return () => false;
  }
  const name = mod.replaceAll(/[$()*+.?[\\\]^{|}]/gu, String.raw`\$&`);
  const pattern = new RegExp(String.raw`(?:^|[\s(/])${name}[.,]`, "mu");
  return (logger, text) => logger == mod || logger.startsWith(`${mod}.`) || pattern.test(text);
}
async function readFirstStamp(logPath) {
  return parseLog(await readHead(logPath, FIRST_STAMP_BYTES))[0]?.time;
}
function isQuiet(host, modified) {
  return host.now().getTime() - modified.getTime() >= QUIET_SECONDS * MILLISECONDS_PER_SECOND;
}
function ignoreMissing(error) {
  if (!isMissing(error)) {
    throw error;
  }
  return;
}

// src/hint.ts
var hints = createQueue();
function crashReportsPath(userDataPath) {
  return path6.join(userDataPath, ".cache", "backtrace", "crashpad", "reports");
}
function withinCeiling(host, seconds, step) {
  const abandoned = new AbortController;
  async function ceiling() {
    await host.alarm(seconds);
    abandoned.abort();
    return;
  }
  return Promise.race([step(abandoned.signal), ceiling()]);
}
function deliverHint(context) {
  return hints.run(context.signal, () => announceNews(context, false));
}
async function settleIgnored(context) {
  await hints.run(context.signal, () => announceNews(context, true));
}
async function moveCursorPastListing(context, scan, newlyDropped) {
  await hints.run(context.signal, async () => {
    const runStart = await readRunStart(context.userDataPath);
    if (!runStart) {
      return;
    }
    await markDropped(context, newlyDropped);
    await settleIgnores(context, scan, []);
    await updateCursor(context, (current) => ({ ...current, runStart: runStart.toISOString(), offsets: scan.offsets }), scan.shrunk);
  });
}
async function readNews(context, chooseOffsets) {
  const { host, userDataPath, signal } = context;
  const runStart = await readRunStart(userDataPath);
  if (!runStart) {
    return;
  }
  const run2 = runStart.toISOString();
  const cursor = await readCursor(userDataPath);
  const { game } = await host.findPids();
  const isRunning = game.length > 0;
  const stored = await readIgnores(userDataPath);
  const matcher = createIgnoreMatcher(stored);
  const scan = await scanLogs({
    host,
    basePath: userDataPath,
    runStart,
    isRunning,
    offsets: chooseOffsets(cursor.runStart == run2 ? cursor.offsets : {}, run2),
    mod: cursor.mod,
    signal,
    windowBytes: SCAN_WINDOW_BYTES,
    accepts: ({ level }) => isErrorLevel(level),
    findIgnore: matcher.findIgnore
  });
  await markDropped(context, matcher.newlyDropped);
  return {
    run: run2,
    cursor,
    stored,
    newlyDropped: matcher.newlyDropped,
    scan,
    isGoneUntold: !isRunning && isGoneUnannounced(cursor, run2, runStart),
    newReport: await readNewReport(userDataPath, runStart, cursor)
  };
}
function createIgnoreMatcher(patterns) {
  const newlyDropped = [];
  const live = new Set(patterns.filter(({ isDropped }) => !isDropped).map(({ source }) => source));
  function findIgnore(text) {
    for (const source of live) {
      const matched = matchPattern(source, text);
      if (matched == "tooSlow") {
        live.delete(source);
        const pattern = patterns.find((item) => item.source == source);
        newlyDropped.push({ source, hits: pattern?.hits ?? 0, isDropped: true });
      } else if (matched) {
        return source;
      }
    }
    return;
  }
  return { newlyDropped, findIgnore };
}
function isGoneUnannounced(cursor, run2, runStart) {
  const lastStop = cursor.lastStop == undefined ? undefined : new Date(cursor.lastStop);
  return cursor.goneRun != run2 && (lastStop == undefined || lastStop < runStart);
}
async function announceNews(context, shouldHoldLine) {
  const news = await readNews(context, (announced) => announced);
  if (!news) {
    return;
  }
  const { run: run2, cursor, stored, newlyDropped, scan, isGoneUntold, newReport } = news;
  const dropped = [...stored, ...newlyDropped].filter(({ isDropped }) => isDropped);
  const line = [
    ...isGoneUntold ? [`The game went away with no cs2-dev stop.`] : [],
    ...newReport ? [`A new crash report appeared, which may be a hang snapshot.`] : [],
    ...describeErrors(scan, cursor.mod),
    ...dropped.map(({ source }) => `Dropped ignore pattern ${source}: it took too long.`)
  ].join(" ");
  if (shouldHoldLine && line) {
    return;
  }
  await settleIgnores(context, scan, dropped);
  await updateCursor(context, (current) => ({
    ...current,
    runStart: run2,
    ...isGoneUntold ? { goneRun: run2 } : {},
    ...newReport ? { lastReport: newReport.toISOString() } : {},
    offsets: scan.offsets
  }), scan.shrunk);
  return line || undefined;
}
function describeErrors(scan, mod) {
  const kinds = [...scan.kinds.values()];
  const shown = kinds.filter((kind) => kind.ignoredBy == undefined);
  const count = sum(shown, (kind) => kind.count);
  if (count == 0) {
    return [];
  }
  const ignored = sum(kinds, (kind) => kind.ignoredBy == undefined ? 0 : kind.count);
  const naming = mod == undefined ? "" : `, ${sum(shown, (kind) => kind.modCount)} naming ${mod}`;
  const suffix = ignored > 0 ? ` (${ignored} ignored)` : "";
  const kindCount = `${shown.length} kind${shown.length == 1 ? "" : "s"}`;
  return [`Errors: ${count} new, ${kindCount}${naming}${suffix}. See logs.`];
}
async function settleIgnores(context, scan, named) {
  const added = new Map;
  for (const { ignoredBy, count } of scan.kinds.values()) {
    if (ignoredBy != null) {
      added.set(ignoredBy, (added.get(ignoredBy) ?? 0) + count);
    }
  }
  const retired = new Set(named.map(({ source }) => source));
  if (added.size == 0 && retired.size == 0) {
    return;
  }
  await updateIgnores(context, (stored) => stored.filter(({ source }) => !retired.has(source)).map(({ source, hits, isDropped }) => ({
    source,
    hits: hits + (added.get(source) ?? 0),
    isDropped
  })));
}
async function readNewReport(userDataPath, runStart, { lastReport }) {
  const folder = crashReportsPath(userDataPath);
  const names = await readdir2(folder).catch(() => []);
  const after = Math.max(runStart.getTime(), lastReport ? Date.parse(lastReport) : 0);
  const times = await Promise.all(names.map((name) => stat2(path6.join(folder, name)).then(({ mtime }) => mtime.getTime(), () => 0)));
  const newest = Math.max(0, ...times);
  return newest > after ? new Date(newest) : undefined;
}
function sum(items, value) {
  return items.reduce((total, item) => total + value(item), 0);
}

// src/notify-gate.ts
var NEWS_SECONDS = 10;
var SETTLE_SECONDS = 2;
var GAP_SECONDS = 30;
var MAX_GAP_SECONDS = 600;
var initialGate = {
  lastSeen: new Map,
  newsSince: undefined,
  lastLine: undefined,
  gap: GAP_SECONDS
};
function advanceGate(state, { now, kinds, isUrgent }) {
  const isNews = kinds.some((kind) => {
    const seen = state.lastSeen.get(kind);
    return seen == undefined || now - seen >= NEWS_SECONDS;
  });
  const lastSeen = new Map([...state.lastSeen, ...kinds.map((kind) => [kind, now])]);
  const newsSince = state.newsSince ?? (isNews ? now : undefined);
  const { lastLine, gap } = state;
  if (isUrgent) {
    return { state: { lastSeen, newsSince: undefined, lastLine: now, gap }, shouldSpeak: true };
  }
  const isGapIdle = lastLine != null && (newsSince ?? now) >= lastLine + gap;
  const currentGap = isGapIdle ? GAP_SECONDS : gap;
  const isSettled = newsSince != null && now - newsSince >= SETTLE_SECONDS;
  const isGapOver = lastLine == undefined || now >= lastLine + currentGap;
  if (!isSettled || !isGapOver) {
    return { state: { lastSeen, newsSince, lastLine, gap: currentGap }, shouldSpeak: false };
  }
  const wasHeld = lastLine != null && !isGapIdle;
  return {
    state: {
      lastSeen,
      newsSince: undefined,
      lastLine: now,
      gap: wasHeld ? Math.min(currentGap * 2, MAX_GAP_SECONDS) : currentGap
    },
    shouldSpeak: true
  };
}

// src/monitor-loop.ts
var SCAN_MILLISECONDS = 2000;
var SCAN_CEILING_SECONDS = 30;
async function watchForNews(context) {
  const { host, isDone } = context;
  let state = {
    run: undefined,
    offsets: {},
    gate: initialGate,
    hasUntoldKinds: false
  };
  while (!isDone()) {
    state = await scanOnce(context, state);
    await host.sleep(SCAN_MILLISECONDS);
  }
}
async function scanOnce(context, state) {
  try {
    const next = await withinCeiling(context.host, SCAN_CEILING_SECONDS, (signal) => watchStep(context, state, signal));
    return next ?? state;
  } catch {
    return state;
  }
}
async function watchStep({ host, userDataPath, print }, state, signal) {
  const context = { host, userDataPath, signal };
  const news = await readNews(context, (announced, run3) => keepLarger(offsetsOf(state, run3), announced, []));
  if (!news) {
    return state;
  }
  const { run: run2, stored, scan, isGoneUntold, newReport } = news;
  const shown = [...scan.kinds.values()].filter((kind) => kind.ignoredBy == undefined);
  const { state: gate, shouldSpeak } = advanceGate(state.gate, {
    now: host.now().getTime() / MILLISECONDS_PER_SECOND,
    kinds: shown.map((kind) => kind.key),
    isUrgent: isGoneUntold || newReport != null || stored.some((item) => item.isDropped)
  });
  const wasUntold = state.run == run2 && state.hasUntoldKinds;
  const hasUntoldKinds = !shouldSpeak && (wasUntold || shown.length > 0);
  if (shouldSpeak) {
    const line = await deliverHint(context);
    if (line != null) {
      print(line);
    }
  } else if (scan.kinds.size > 0 && !hasUntoldKinds) {
    await settleIgnored(context);
  }
  return { run: run2, offsets: { ...offsetsOf(state, run2), ...scan.offsets }, gate, hasUntoldKinds };
}
function offsetsOf(state, run2) {
  return state.run == run2 ? state.offsets : {};
}

// src/monitor.ts
var userDataPath = loadUserDataPath();
await watchForNews({
  host: createGameHost(userDataPath),
  userDataPath,
  print: (line) => {
    process4.stdout.write(`${line}
`);
  },
  isDone: () => false
});
