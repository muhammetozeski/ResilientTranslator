import fs from 'node:fs';
let cases=JSON.parse(fs.readFileSync('Corpus/cases.json','utf8'));
cases=cases.filter(x=>['tr','en'].includes(x.source)||[0,3,7,8,9].some(i=>x.id.endsWith('-'+i)));
const extra={
tr:[
'Çalıştır','Bağlantı','Başarısız','Çevirmen','Güncelleme','Bilgisayar','Pazartesi','Çarşamba','Bekliyorum','Görüşürüz',
'Çocuklar','Yardımcı','İyileştirme','Başlangıç','Kaydedildi','Gereksiz','Yanlışlıkla','Unutmuşum','Bilmiyorum','Kesinlikle',
'Metni buraya yapıştır.','Neden hâlâ cevap gelmedi?','İşlem tamamlanınca bana haber ver.','Bu dosyayı başka bir klasöre taşı.','Bugün dışarı çıkmak istemiyorum.',
'Bunu daha önce nerede gördüğümü hatırlamıyorum.','Çeviri yaparken pencere donmasın.','Yazdıklarım kaybolmuş olabilir.','Yarın sabah erkenden yola çıkacağız.','Bana bunun nasıl çalıştığını anlatır mısın?',
'İnternetim yok ama çeviri yapmak istiyorum.','Önceki çeviri hâlâ devam ediyor.','Sağdaki kutuya tıklamadan yazabilmeliyim.','Bu iş sandığımdan daha uzun sürdü.','Keşke dosyayı silmeden önce bir kopyasını alsaydım.',
'Yalnızca en son istediğim metni çevir.','Pencere küçülünce yazılar birbirine girmesin.','Teşekkür ederim, şimdi çok daha iyi oldu.','Şu anda bilgisayarımda hiçbir şey indirmiyorum.','Dün gece yarısına kadar bununla uğraştım.'
],
en:[
'Unfortunately','Nevertheless','Clipboard','Downloaded','Wednesday','Tomorrow','Yesterday','Cancelled','Preserved','Immediately',
'Background','Something','Without','Although','Meanwhile','Otherwise','Settings','Translation','Successfully','Unavailable',
'Paste your text here.','Why is there still no response?','Let me know when the task is finished.','Move this file to a different folder.','I do not feel like going outside today.',
'I cannot remember where I saw this before.','The window should stay responsive during translation.','The text I typed may have disappeared.','We are leaving early tomorrow morning.','Could you explain how this works?',
'I have no internet but I want to translate something.','The previous translation is still in progress.','I should be able to type without clicking the right box.','This took longer than I expected.','I wish I had made a copy before deleting the file.',
'Only translate the most recent text I requested.','The text should not overlap when the window gets smaller.','Thank you, this is much better now.','I am not downloading anything on my computer right now.','I worked on this until midnight yesterday.'
]};
for(const [source,texts] of Object.entries(extra))texts.forEach((text,i)=>cases.push({id:`${source}-extra-${i}`,source,category:i<20?'word':'sentence',text}));
// Avoid duplicate entries affecting the accuracy denominator.
cases=cases.filter((c,i,a)=>a.findIndex(x=>x.source===c.source&&x.text===c.text)===i);
fs.writeFileSync('Corpus/cases.json',JSON.stringify(cases,null,2));
console.log({total:cases.length,trEn:cases.filter(x=>['tr','en'].includes(x.source)).length});
