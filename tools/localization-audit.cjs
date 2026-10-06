const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
function walk(dir) { return fs.readdirSync(dir, {withFileTypes:true}).flatMap(e => e.isDirectory() ? ['bin','obj','wwwroot','Localization'].includes(e.name) ? [] : walk(path.join(dir,e.name)) : [path.join(dir,e.name)]); }
const keys = new Set();
for (const file of walk(path.join(root,'src')).filter(f => /\.(cs|razor)$/.test(f))) {
  const text = fs.readFileSync(file,'utf8');
  for (const m of text.matchAll(/"([^"\r\n]*)"/g)) if (/[ąćęłńóśźżĄĆĘŁŃÓŚŹŻ]/.test(m[1]) && !/[{}@]|=>|class=/.test(m[1])) keys.add(m[1]);
  if (file.endsWith('.razor')) for (const m of text.matchAll(/>([^<>]+)</g)) if (!/[@{}\n]/.test(m[1]) && /[a-ząćęłńóśźż]/i.test(m[1])) keys.add(m[1].trim());
}
const resource = JSON.parse(fs.readFileSync(path.join(root,'src/Travelio.Application/Localization/en.json'),'utf8'));
const missing = [...keys].filter(k => !resource[k]).sort();
fs.mkdirSync(path.join(root,'artifacts'),{recursive:true});
fs.writeFileSync(path.join(root,'artifacts/localization-missing.json'),JSON.stringify(missing,null,2));
console.log(missing.length + ' candidates need review');
console.log(missing.map((v,i)=>`${i}: ${v}`).join('\n'));
