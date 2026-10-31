import fs from 'node:fs';
const groups = {
tr: [['Merhaba','Teşekkürler','Günaydın','Çevrimdışı','İptal'],['Dosyayı kaydet','Pencereyi kapatma','Bunu yarın yapabiliriz','Yeni bir istek geldiğinde önceki işlemi iptal et.','İnternet bağlantısı kesilse bile ayarlarım korunmalı.']],
en: [['Hello','Thanks','Goodbye','Settings','Unavailable'],['Save the file','Please do not close this window','We can finish this tomorrow','Cancel the previous operation when a new request arrives.','My settings should be preserved even if the connection drops.']],
de: [['Dankeschön','Gutenacht','Entschuldigung','Einstellungen','Übersetzung'],['Datei speichern','Bitte schließen Sie dieses Fenster nicht','Wir können das morgen erledigen','Wenn eine neue Anfrage eintrifft, brich den vorherigen Vorgang ab.','Meine Einstellungen sollen auch ohne Internetverbindung erhalten bleiben.']],
fr: [['Bonjour','Merci','Demain','Paramètres','Traduction'],['Enregistrer le fichier','Veuillez ne pas fermer cette fenêtre','Nous pouvons terminer cela demain','Annulez la tâche précédente lorsqu’une nouvelle demande arrive.','Mes paramètres doivent être conservés même sans connexion Internet.']],
es: [['Gracias','Buenosdías','Disculpa','Configuración','Traducción'],['Guardar el archivo','No cierres esta ventana','Podemos terminar esto mañana','Cancela la operación anterior cuando llegue una nueva solicitud.','Mi configuración debe conservarse aunque se pierda la conexión.']],
it: [['Buongiorno','Grazie','Arrivederci','Impostazioni','Traduzione'],['Salva il file','Non chiudere questa finestra','Possiamo finire questo lavoro domani','Annulla l’operazione precedente quando arriva una nuova richiesta.','Le mie impostazioni devono essere conservate anche senza connessione.']],
ru: [['Здравствуйте','Спасибо','Пожалуйста','Настройки','Перевод'],['Сохранить файл','Не закрывайте это окно','Мы можем закончить это завтра','При поступлении нового запроса отмените предыдущую операцию.','Мои настройки должны сохраняться даже при потере подключения.']]
};
const cases=[];
for(const [language,[words,others]] of Object.entries(groups)){
 words.forEach((text,i)=>cases.push({id:`${language}-word-${i}`,source:language,category:'word',text}));
 others.forEach((text,i)=>cases.push({id:`${language}-text-${i}`,source:language,category:i<3?'phrase':'sentence',text}));
}
const old=JSON.parse(fs.readFileSync('../Corpus/translation-cases.json','utf8'));
cases.push(...old.map(x=>({...x,id:'previous-'+x.id,category:'sentence'})));
fs.writeFileSync('Corpus/cases.json',JSON.stringify(cases,null,2));
console.log({cases:cases.length});
