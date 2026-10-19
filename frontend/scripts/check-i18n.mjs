import fs from 'node:fs';
import ts from 'typescript';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { build } from 'esbuild';
const source=JSON.parse(fs.readFileSync('src/shared/i18n/source.json','utf8'));
for(const lang of ['en','zh','ko','hi']){
 const catalog=JSON.parse(fs.readFileSync(`src/shared/i18n/${lang}.json`,'utf8'));
 assert.deepEqual(Object.keys(catalog).sort(),[...source].sort(),`${lang} keys`);
 for(const key of source){
  assert.ok(catalog[key].trim(),`${lang} empty: ${key}`);
  const placeholders=s=>[...s.matchAll(/\{\d+\}/g)].map(m=>m[0]).sort();
  assert.deepEqual(placeholders(catalog[key]),placeholders(key),`${lang} placeholders: ${key}`);
 }
}
function sourceFiles(dir) {
 return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
  const file = `${dir}/${entry.name}`;
  return entry.isDirectory() ? sourceFiles(file) : /\.tsx?$/.test(entry.name) && !entry.name.endsWith('.d.ts') ? [file.slice(4)] : [];
 });
}
// Catalog implementation and static Vietnamese SEO metadata are not localized UI.
const files = sourceFiles('src').filter(file => !file.startsWith('shared/i18n/') && !file.startsWith('app/seo/'));
for(const file of files){
 const ast=ts.createSourceFile(file,fs.readFileSync(`src/${file}`,'utf8'),ts.ScriptTarget.Latest,true,file.endsWith('tsx')?ts.ScriptKind.TSX:ts.ScriptKind.TS);
 const walk=n=>{
  if((ts.isStringLiteral(n)||ts.isJsxText(n)||ts.isNoSubstitutionTemplateLiteral(n))&&/[À-ỹĐđ]/.test(n.text)){
   const parent=n.parent;
   assert.ok(ts.isCallExpression(parent)&&parent.expression.getText(ast)==='t',`Unlocalized text in ${file}: ${n.text}`);
   assert.ok(source.includes(n.text),`Missing catalog key in ${file}: ${n.text}`);
  }
  ts.forEachChild(n,walk);
 };
 walk(ast);
}
const result=await build({ stdin:{contents:'export * from "./src/shared/i18n/runtime"; export * from "./src/shared/i18n/errors"; export {ApiError,formatVnd,periodLabel} from "./src/api";',resolveDir:process.cwd(),loader:'ts'},bundle:true,write:false,format:'cjs',platform:'node' });
function runtime(search='',saved=null,blocked=false){
 const storage=new Map(saved?[['rehouse.language',saved]]:[]);
 const document={ documentElement:{ lang:'' } };
 const window={ location:{ search,href:`https://example.invalid/t/test${search}` }, history:{state:{}, replaceState:(state,_,url)=>{window.history.state=state;window.location.href=String(url);} } };
 const localStorage={getItem:k=>{if(blocked)throw Error('blocked');return storage.get(k);},setItem:(k,v)=>{if(blocked)throw Error('blocked');storage.set(k,v);}};
 const exports={}; const module={exports}; vm.runInNewContext(result.outputFiles[0].text,{module,exports,window,document,localStorage,URL,URLSearchParams,Intl,Date,Set});
 return {api:module.exports,storage,window,document};
}
const a=runtime();assert.equal(a.api.getLanguage(),'vi');
let notifications=0;const unsubscribe=a.api.subscribeLanguage(()=>notifications++);
for(const lang of ['vi','en','zh','ko','hi']){
 a.api.setLanguage(lang);assert.equal(a.api.getLanguage(),lang);assert.equal(a.storage.get('rehouse.language'),lang);
 assert.ok(a.api.t('Trả phòng & quyết toán').length);
 const expectedLogin={vi:'Email hoặc mật khẩu không chính xác.',en:'Incorrect email or password.',zh:'邮箱或密码不正确。',ko:'이메일 또는 비밀번호가 올바르지 않습니다.',hi:'ईमेल या पासवर्ड गलत है।'};
 assert.equal(new a.api.ApiError('Email hoặc mật khẩu không chính xác.',401).message,expectedLogin[lang]);
 assert.equal(new a.api.ApiError(a.api.t('Không thể kết nối máy chủ. Vui lòng kiểm tra kết nối và thử lại.'),0).message,a.api.t('Không thể kết nối máy chủ. Vui lòng kiểm tra kết nối và thử lại.'));
 assert.ok(a.api.errorText('Receipt amount cannot exceed the outstanding balance.').length);
 assert.ok(a.api.periodLabel('2026-10').includes('2026'));
 assert.equal(a.api.periodLabel('bad-period'),'bad-period');assert.equal(a.api.t('Tên khách tự nhập'),'Tên khách tự nhập');
 assert.ok(a.api.formatDate('2026-10-13').includes('2026'));
 assert.equal(new Intl.NumberFormat(a.api.getLocale(),{style:'currency',currency:'VND'}).resolvedOptions().currency,'VND');
 assert.ok(a.api.t('Sau khi dùng cọc: cần thu thêm {0}, hoặc hoàn lại {1}.',['40 VND','30 VND']).includes('40 VND'));
}
assert.equal(notifications,5);unsubscribe();a.api.setLanguage('vi');assert.equal(notifications,5);
assert.equal(runtime('', 'ko').api.getLanguage(),'ko');
assert.equal(runtime('?lang=hi&payment=returned','en').api.getLanguage(),'hi');
const b=runtime('?lang=zh&payment=returned');assert.equal(b.storage.get('rehouse.language'),'zh');b.api.setLanguage('ko');assert.ok(b.window.location.href.includes('payment=returned'));assert.ok(b.window.location.href.includes('lang=ko'));
assert.equal(runtime('?lang=invalid','en').api.getLanguage(),'en');
assert.equal(runtime('',null,true).api.getLanguage(),'vi');runtime('',null,true).api.setLanguage('hi');
console.log(`PASS: ${source.length} keys × 4 catalogs; UI coverage; preference, URL, interpolation, VND and calendar-date checks.`);
