import { copyFile, cp, mkdir, rm } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const clientRoot = path.join(repositoryRoot, 'src', 'HearthCalendar.Client');
const assetsRoot = path.join(clientRoot, 'Assets');
const webRoot = path.join(clientRoot, 'wwwroot');
const watch = process.argv.includes('--watch');

await prepareWebRoot();
await compileStyles();
await compileScripts();

if (watch) {
    await Promise.all([
        runNodeTool(path.join('sass', 'sass.js'), ['--watch', path.join(assetsRoot, 'Styles', 'app.scss'), path.join(webRoot, 'css', 'app.css'), '--no-source-map']),
        runNodeTool(path.join('typescript', 'bin', 'tsc'), ['--project', path.join(clientRoot, 'tsconfig.assets.json'), '--watch', '--preserveWatchOutput'])
    ]);
}

async function prepareWebRoot() {
    await mkdir(path.join(webRoot, 'css'), { recursive: true });
    await rm(path.join(webRoot, 'offline-calendar.js'), { force: true });
    await rm(path.join(webRoot, 'service-worker.js'), { force: true });
    await rm(path.join(webRoot, 'service-worker.published.js'), { force: true });
    await rm(path.join(webRoot, 'css', 'app.css'), { force: true });

    await copyFile(path.join(assetsRoot, 'Pwa', 'index.html'), path.join(webRoot, 'index.html'));
    await copyFile(path.join(assetsRoot, 'Pwa', 'manifest.webmanifest'), path.join(webRoot, 'manifest.webmanifest'));
    await cp(path.join(assetsRoot, 'Pwa', 'icons'), webRoot, { recursive: true });
}

async function compileStyles() {
    await runNodeTool(path.join('sass', 'sass.js'), [
        path.join(assetsRoot, 'Styles', 'app.scss'),
        path.join(webRoot, 'css', 'app.css'),
        '--no-source-map',
        '--style=expanded'
    ]);
}

async function compileScripts() {
    await runNodeTool(path.join('typescript', 'bin', 'tsc'), ['--project', path.join(clientRoot, 'tsconfig.assets.json')]);
}

async function runNodeTool(toolPath, args) {
    await new Promise((resolve, reject) => {
        const child = spawn(process.execPath, [path.join(repositoryRoot, 'node_modules', toolPath), ...args], {
            cwd: repositoryRoot,
            stdio: 'inherit'
        });

        child.on('error', reject);
        child.on('exit', code => {
            if (code === 0) {
                resolve();
                return;
            }

            reject(new Error(`${toolPath} exited with code ${code}. Run npm ci before building assets.`));
        });
    });
}
