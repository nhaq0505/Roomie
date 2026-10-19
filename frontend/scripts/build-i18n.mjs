import fs from 'node:fs';
const source = JSON.parse(fs.readFileSync('src/shared/i18n/source.json', 'utf8'));
const catalogs = Object.fromEntries(['en','zh','ko','hi'].map(lang=>[lang,{}]));
const seen = new Set();
for (const line of fs.readFileSync('src/shared/i18n/translations.tsv','utf8').split('\n').filter(Boolean)) {
 const [id,...values] = line.split('|'); const index=Number(id);
 if (seen.has(index)||!source[index]||values.length!==4||values.some(v=>!v.trim())) throw new Error(`Invalid row ${id}`);
 seen.add(index);
 values.forEach((value,i)=>{
  const key=source[index];
  const placeholders=s=>[...s.matchAll(/\{\d+\}/g)].map(m=>m[0]).sort().join(',');
  if(placeholders(key)!==placeholders(value))throw new Error(`Placeholder mismatch ${id}`);
  catalogs[['en','zh','ko','hi'][i]][key]=value;
 });
}
if(seen.size!==source.length)throw new Error(`Missing translations: ${source.map((_,i)=>i).filter(i=>!seen.has(i))}`);
for(const [lang,catalog] of Object.entries(catalogs))fs.writeFileSync(`src/shared/i18n/${lang}.json`,JSON.stringify(catalog,null,2)+'\n');
console.log(`Validated ${seen.size} entries in 4 catalogs`);
