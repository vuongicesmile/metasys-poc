// Power Apps accepts a single typed TSX file. Keep editable modules in src/ and
// flatten their named imports without transpiling away RuntimeTypes metadata.
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const root = __dirname;
const entry = path.join(root, 'src/Dashboard.tsx');
const output = path.join(root, 'trung-tam-van-hanh.tsx');
const visited = new Set();
const visiting = new Set();
const externals = new Map();
const declarations = new Map();
const chunks = [];

function claim(name, identity) {
    if (declarations.has(name) && declarations.get(name) !== identity)
        throw new Error(`Duplicate flattened identifier ${name}: ${identity} / ${declarations.get(name)}`);
    declarations.set(name, identity);
}

function visit(file) {
    if (visiting.has(file)) throw new Error(`Circular dependency: ${file}`);
    if (visited.has(file)) return;
    visiting.add(file);
    const source = ts.createSourceFile(file, fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
    if (source.parseDiagnostics.length) throw new Error(ts.flattenDiagnosticMessageText(source.parseDiagnostics[0].messageText, '\n'));
    const body = [];
    for (const node of source.statements) {
        if (ts.isImportDeclaration(node)) {
            const module = node.moduleSpecifier.text;
            const clause = node.importClause;
            if (!clause || clause.name || !clause.namedBindings || !ts.isNamedImports(clause.namedBindings))
                throw new Error(`Use named imports in ${file}: ${module}`);
            if (module.startsWith('.') && !module.endsWith('/RuntimeTypes')) {
                for (const item of clause.namedBindings.elements)
                    if (item.propertyName) throw new Error(`Local import aliases are unsupported: ${file}`);
                const base = path.resolve(path.dirname(file), module);
                const dependency = [base + '.ts', base + '.tsx'].find(fs.existsSync);
                if (!dependency || !dependency.startsWith(path.join(root, 'src') + path.sep))
                    throw new Error(`Local dependency must be inside src/: ${module}`);
                visit(dependency);
            } else {
                const target = module.endsWith('/RuntimeTypes') ? './RuntimeTypes' : module;
                const items = externals.get(target) || new Map();
                for (const item of clause.namedBindings.elements) {
                    const name = item.name.text;
                    const imported = item.propertyName?.text || name;
                    claim(name, `${target}:${imported}`);
                    const typeOnly = !!(clause.isTypeOnly || item.isTypeOnly);
                    const previous = items.get(name);
                    items.set(name, { imported, typeOnly: previous ? previous.typeOnly && typeOnly : typeOnly });
                }
                externals.set(target, items);
            }
            continue;
        }
        if (ts.isExportDeclaration(node)) throw new Error(`Re-exports are unsupported: ${file}`);
        if (ts.isExportAssignment(node)) {
            if (file !== entry || node.isExportEquals) throw new Error(`Only the entry can export default: ${file}`);
            body.push(node.getText(source));
            continue;
        }
        const names = ts.isVariableStatement(node)
            ? node.declarationList.declarations.map(d => d.name)
            : node.name ? [node.name] : [];
        for (const name of names) {
            if (!ts.isIdentifier(name)) throw new Error(`Use named top-level declarations: ${file}`);
            claim(name.text, file);
        }
        // Retain comments and typed declarations. Remove module exports only;
        // each internal symbol is checked for collisions before flattening.
        let text = node.getFullText(source);
        for (const modifier of [...(node.modifiers || [])].reverse()) {
            if (modifier.kind === ts.SyntaxKind.DefaultKeyword)
                throw new Error(`Use a separate export default statement: ${file}`);
            if (modifier.kind === ts.SyntaxKind.ExportKeyword) {
                const start = modifier.getStart(source) - node.getFullStart();
                text = text.slice(0, start) + text.slice(modifier.end - node.getFullStart());
            }
        }
        body.push(text.trim());
    }
    chunks.push(`// Source: ${path.relative(root, file).split(path.sep).join('/')}\n${body.join('\n\n')}`);
    visiting.delete(file);
    visited.add(file);
}

visit(entry);
const imports = [...externals].sort(([a], [b]) => a.localeCompare(b)).map(([module, items]) => {
    const symbols = [...items].sort(([a], [b]) => a.localeCompare(b)).map(([name, item]) =>
        `${item.typeOnly ? 'type ' : ''}${item.imported}${item.imported === name ? '' : ' as ' + name}`);
    return `import { ${symbols.join(', ')} } from ${JSON.stringify(module)};`;
});
const generated = '// Generated by npm run build. Edit src/; do not edit this file directly.\n' +
    imports.join('\n') + '\n\n' + chunks.join('\n\n') + '\n';
if (process.argv.includes('--check')) {
    // Git may check out this generated artifact with CRLF on Windows.
    if (!fs.existsSync(output) || fs.readFileSync(output, 'utf8').replace(/\r\n/g, '\n') !== generated)
        throw new Error('Dashboard artifact is stale. Run npm run build and include the generated TSX.');
    console.log(`PASS: ${visited.size} source modules match the Power Apps artifact.`);
} else {
    fs.writeFileSync(output, generated);
    console.log(`Built ${visited.size} modules -> ${path.basename(output)}`);
}
