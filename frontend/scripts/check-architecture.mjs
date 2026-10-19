import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

const root = path.resolve('src');
const config = ts.readConfigFile('tsconfig.json', ts.sys.readFile);
const { options } = ts.parseJsonConfigFileContent(config.config, ts.sys, process.cwd());
const files = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
  const file = path.join(dir, entry.name);
  return entry.isDirectory() ? files(file) : /\.tsx?$/.test(file) ? [file] : [];
});
const graph = new Map();
for (const file of files(root)) {
  const name = path.relative(root, file);
  const feature = name.startsWith('features/') ? name.split('/')[1] : null;
  const source = ts.createSourceFile(file, fs.readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true);
  const edges = [];
  function inspect(node) {
    const specifier = (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) ? node.moduleSpecifier
      : ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword ? node.arguments[0] : null;
    if (specifier && ts.isStringLiteral(specifier) && specifier.text.startsWith('.')) {
      const resolved = ts.resolveModuleName(specifier.text, file, options, ts.sys).resolvedModule;
      // CSS is bundled by Vite; tsc checks remaining imports separately.
      if (!resolved) { assert.ok(specifier.text.endsWith('.css'), `${name}: unresolved ${specifier.text}`); }
      else {
        const target = path.relative(root, resolved.resolvedFileName);
        if (name.startsWith('shared/')) assert.ok(target.startsWith('shared/'), `${name} must not depend on ${target}`);
        if (feature) {
          assert.ok(!target.startsWith('app/') && target !== 'api.ts', `${name} must not depend on ${target}`);
          if (target.startsWith('features/') && target.split('/')[1] !== feature) {
            assert.ok(/^features\/[^/]+\/index\.ts$/.test(target) || target === 'features/auth/session.ts', `${name}: use the public entry point for ${target}`);
          }
        }
        const typeOnly = node.isTypeOnly || ts.isImportDeclaration(node) && (node.importClause?.isTypeOnly ||
          !node.importClause?.name && node.importClause?.namedBindings && ts.isNamedImports(node.importClause.namedBindings) && node.importClause.namedBindings.elements.every(e => e.isTypeOnly));
        if (!typeOnly) edges.push(resolved.resolvedFileName);
      }
    }
    ts.forEachChild(node, inspect);
  }
  inspect(source);
  graph.set(file, edges);
}
const visited = new Set();
function visit(file, chain = []) {
  assert.ok(!chain.includes(file), `Runtime import cycle: ${[...chain, file].map(f => path.relative(root, f)).join(' -> ')}`);
  if (visited.has(file)) return;
  for (const target of graph.get(file) ?? []) visit(target, [...chain, file]);
  visited.add(file);
}
for (const file of graph.keys()) visit(file);
console.log(`PASS: ${graph.size} source modules; shared/feature boundaries, public imports and runtime dependency cycles`);
