import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, realpathSync, rmdirSync, symlinkSync, unlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const viteArguments = process.argv.slice(2);
let junctionPath = '';
let junctionParent = '';

function releaseJunction() {
  if (!junctionPath) return;
  try {
    unlinkSync(junctionPath);
  } catch {
    // The process may have been terminated after the junction was already removed.
  }
  try {
    rmdirSync(junctionParent);
  } catch {
    // Another dev server may still be using the shared parent directory.
  }
  junctionPath = '';
}

function createHashFreeWindowsPath() {
  if (process.platform !== 'win32' || !projectRoot.includes('#')) return projectRoot;

  const projectKey = createHash('sha256').update(projectRoot).digest('hex').slice(0, 10);
  // Vite 8 rejects Windows 8.3 short-name paths (for example ADMINI~1)
  // before evaluating server.fs.allow, so always build the junction below
  // the canonical long form of the system temp directory.
  junctionParent = join(realpathSync.native(tmpdir()), 'msa-vite');
  junctionPath = join(junctionParent, `front-${projectKey}`);
  mkdirSync(junctionParent, { recursive: true });

  if (existsSync(junctionPath)) {
    if (!lstatSync(junctionPath).isSymbolicLink() || realpathSync(junctionPath) !== realpathSync(projectRoot)) {
      throw new Error(`Vite 临时目录已被其他内容占用：${junctionPath}`);
    }
  } else {
    symlinkSync(projectRoot, junctionPath, 'junction');
  }

  if (realpathSync(junctionPath) !== realpathSync(projectRoot)) {
    throw new Error('Vite 临时目录没有正确指向前端项目。');
  }

  return junctionPath;
}

let runtimeRoot;
try {
  runtimeRoot = createHashFreeWindowsPath();
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  releaseJunction();
  process.exit(1);
}

const viteEntry = join(runtimeRoot, 'node_modules', 'vite', 'bin', 'vite.js');
const child = spawn(
  process.execPath,
  ['--preserve-symlinks', '--preserve-symlinks-main', viteEntry, ...viteArguments],
  { cwd: runtimeRoot, stdio: 'inherit' },
);

process.once('exit', releaseJunction);
process.once('SIGINT', () => {
  releaseJunction();
  child.kill('SIGINT');
});
process.once('SIGTERM', () => {
  releaseJunction();
  child.kill('SIGTERM');
});

child.once('error', (error) => {
  console.error(`Vite 启动失败：${error.message}`);
  releaseJunction();
  process.exitCode = 1;
});

child.once('exit', (code) => {
  releaseJunction();
  process.exitCode = code ?? 1;
});
