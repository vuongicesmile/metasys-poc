import { build, transform } from 'esbuild';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url));
const content = await transform(await readFile(resolve(here, 'src/snippets.ts'), 'utf8'), { loader: 'ts', format: 'esm' });
const { samples } = await import('data:text/javascript;base64,' + Buffer.from(content.code).toString('base64'));
for (const sample of samples.filter(s => ['TypeScript', 'TSX'].includes(s.language))) {
  await transform(sample.code, { loader: sample.language === 'TSX' ? 'tsx' : 'ts', sourcefile: sample.path });
}
console.log('Validated TypeScript/TSX sample syntax (not integration/type checking).');
await build({
  absWorkingDir: here, entryPoints: ['src/main.tsx'], bundle: true,
  outdir: resolve(here, 'dist'), minify: true, target: ['es2020'],
  define: { 'process.env.NODE_ENV': '"production"' },
  jsxFactory: 'React.createElement', jsxFragment: 'React.Fragment',
});
console.log('Built React documentation. Run npm start.');
