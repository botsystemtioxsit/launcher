// Создано tools/mask-plus/generate.py из countries.json — руками не править.
const MASK_COUNTRIES = [
 {
  "cc": "DE",
  "name": "Германия",
  "city": "Берлин",
  "tz": "Europe/Berlin",
  "locale": "de-DE",
  "langs": [
   "de-DE",
   "de",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AT",
  "name": "Австрия",
  "city": "Вена",
  "tz": "Europe/Vienna",
  "locale": "de-AT",
  "langs": [
   "de-AT",
   "de",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "CH",
  "name": "Швейцария",
  "city": "Цюрих",
  "tz": "Europe/Zurich",
  "locale": "de-CH",
  "langs": [
   "de-CH",
   "de",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "FR",
  "name": "Франция",
  "city": "Париж",
  "tz": "Europe/Paris",
  "locale": "fr-FR",
  "langs": [
   "fr-FR",
   "fr",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "IT",
  "name": "Италия",
  "city": "Рим",
  "tz": "Europe/Rome",
  "locale": "it-IT",
  "langs": [
   "it-IT",
   "it",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "ES",
  "name": "Испания",
  "city": "Мадрид",
  "tz": "Europe/Madrid",
  "locale": "es-ES",
  "langs": [
   "es-ES",
   "es",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "PT",
  "name": "Португалия",
  "city": "Лиссабон",
  "tz": "Europe/Lisbon",
  "locale": "pt-PT",
  "langs": [
   "pt-PT",
   "pt",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "NL",
  "name": "Нидерланды",
  "city": "Амстердам",
  "tz": "Europe/Amsterdam",
  "locale": "nl-NL",
  "langs": [
   "nl-NL",
   "nl",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "BE",
  "name": "Бельгия",
  "city": "Брюссель",
  "tz": "Europe/Brussels",
  "locale": "nl-BE",
  "langs": [
   "nl-BE",
   "nl",
   "fr-BE",
   "fr",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "IE",
  "name": "Ирландия",
  "city": "Дублин",
  "tz": "Europe/Dublin",
  "locale": "en-IE",
  "langs": [
   "en-IE",
   "en"
  ]
 },
 {
  "cc": "GB",
  "name": "Великобритания",
  "city": "Лондон",
  "tz": "Europe/London",
  "locale": "en-GB",
  "langs": [
   "en-GB",
   "en"
  ]
 },
 {
  "cc": "SE",
  "name": "Швеция",
  "city": "Стокгольм",
  "tz": "Europe/Stockholm",
  "locale": "sv-SE",
  "langs": [
   "sv-SE",
   "sv",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "NO",
  "name": "Норвегия",
  "city": "Осло",
  "tz": "Europe/Oslo",
  "locale": "nb-NO",
  "langs": [
   "nb-NO",
   "nb",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "DK",
  "name": "Дания",
  "city": "Копенгаген",
  "tz": "Europe/Copenhagen",
  "locale": "da-DK",
  "langs": [
   "da-DK",
   "da",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "FI",
  "name": "Финляндия",
  "city": "Хельсинки",
  "tz": "Europe/Helsinki",
  "locale": "fi-FI",
  "langs": [
   "fi-FI",
   "fi",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "IS",
  "name": "Исландия",
  "city": "Рейкьявик",
  "tz": "Atlantic/Reykjavik",
  "locale": "is-IS",
  "langs": [
   "is-IS",
   "is",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "PL",
  "name": "Польша",
  "city": "Варшава",
  "tz": "Europe/Warsaw",
  "locale": "pl-PL",
  "langs": [
   "pl-PL",
   "pl",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "CZ",
  "name": "Чехия",
  "city": "Прага",
  "tz": "Europe/Prague",
  "locale": "cs-CZ",
  "langs": [
   "cs-CZ",
   "cs",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "HU",
  "name": "Венгрия",
  "city": "Будапешт",
  "tz": "Europe/Budapest",
  "locale": "hu-HU",
  "langs": [
   "hu-HU",
   "hu",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "RO",
  "name": "Румыния",
  "city": "Бухарест",
  "tz": "Europe/Bucharest",
  "locale": "ro-RO",
  "langs": [
   "ro-RO",
   "ro",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "GR",
  "name": "Греция",
  "city": "Афины",
  "tz": "Europe/Athens",
  "locale": "el-GR",
  "langs": [
   "el-GR",
   "el",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "TR",
  "name": "Турция",
  "city": "Стамбул",
  "tz": "Europe/Istanbul",
  "locale": "tr-TR",
  "langs": [
   "tr-TR",
   "tr",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "UA",
  "name": "Украина",
  "city": "Киев",
  "tz": "Europe/Kiev",
  "locale": "uk-UA",
  "langs": [
   "uk-UA",
   "uk",
   "ru",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "EE",
  "name": "Эстония",
  "city": "Таллин",
  "tz": "Europe/Tallinn",
  "locale": "et-EE",
  "langs": [
   "et-EE",
   "et",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "LV",
  "name": "Латвия",
  "city": "Рига",
  "tz": "Europe/Riga",
  "locale": "lv-LV",
  "langs": [
   "lv-LV",
   "lv",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "LT",
  "name": "Литва",
  "city": "Вильнюс",
  "tz": "Europe/Vilnius",
  "locale": "lt-LT",
  "langs": [
   "lt-LT",
   "lt",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "RU",
  "name": "Россия",
  "city": "Москва",
  "tz": "Europe/Moscow",
  "locale": "ru-RU",
  "langs": [
   "ru-RU",
   "ru",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "KZ",
  "name": "Казахстан",
  "city": "Астана",
  "tz": "Asia/Almaty",
  "locale": "ru-KZ",
  "langs": [
   "ru-KZ",
   "ru",
   "kk",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "GE",
  "name": "Грузия",
  "city": "Тбилиси",
  "tz": "Asia/Tbilisi",
  "locale": "ka-GE",
  "langs": [
   "ka-GE",
   "ka",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AM",
  "name": "Армения",
  "city": "Ереван",
  "tz": "Asia/Yerevan",
  "locale": "hy-AM",
  "langs": [
   "hy-AM",
   "hy",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AZ",
  "name": "Азербайджан",
  "city": "Баку",
  "tz": "Asia/Baku",
  "locale": "az-AZ",
  "langs": [
   "az-AZ",
   "az",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "IL",
  "name": "Израиль",
  "city": "Иерусалим",
  "tz": "Asia/Jerusalem",
  "locale": "he-IL",
  "langs": [
   "he-IL",
   "he",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AE",
  "name": "ОАЭ",
  "city": "Дубай",
  "tz": "Asia/Dubai",
  "locale": "ar-AE",
  "langs": [
   "ar-AE",
   "ar",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "IN",
  "name": "Индия",
  "city": "Дели",
  "tz": "Asia/Kolkata",
  "locale": "en-IN",
  "langs": [
   "en-IN",
   "en",
   "hi"
  ]
 },
 {
  "cc": "TH",
  "name": "Таиланд",
  "city": "Бангкок",
  "tz": "Asia/Bangkok",
  "locale": "th-TH",
  "langs": [
   "th-TH",
   "th",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "SG",
  "name": "Сингапур",
  "city": "Сингапур",
  "tz": "Asia/Singapore",
  "locale": "en-SG",
  "langs": [
   "en-SG",
   "en",
   "zh"
  ]
 },
 {
  "cc": "HK",
  "name": "Гонконг",
  "city": "Гонконг",
  "tz": "Asia/Hong_Kong",
  "locale": "zh-HK",
  "langs": [
   "zh-HK",
   "zh",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "CN",
  "name": "Китай",
  "city": "Пекин",
  "tz": "Asia/Shanghai",
  "locale": "zh-CN",
  "langs": [
   "zh-CN",
   "zh",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "JP",
  "name": "Япония",
  "city": "Токио",
  "tz": "Asia/Tokyo",
  "locale": "ja-JP",
  "langs": [
   "ja-JP",
   "ja",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "KR",
  "name": "Южная Корея",
  "city": "Сеул",
  "tz": "Asia/Seoul",
  "locale": "ko-KR",
  "langs": [
   "ko-KR",
   "ko",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AU",
  "name": "Австралия",
  "city": "Сидней",
  "tz": "Australia/Sydney",
  "locale": "en-AU",
  "langs": [
   "en-AU",
   "en"
  ]
 },
 {
  "cc": "NZ",
  "name": "Новая Зеландия",
  "city": "Окленд",
  "tz": "Pacific/Auckland",
  "locale": "en-NZ",
  "langs": [
   "en-NZ",
   "en"
  ]
 },
 {
  "cc": "US",
  "name": "США",
  "city": "Нью-Йорк",
  "tz": "America/New_York",
  "locale": "en-US",
  "langs": [
   "en-US",
   "en"
  ]
 },
 {
  "cc": "CA",
  "name": "Канада",
  "city": "Торонто",
  "tz": "America/Toronto",
  "locale": "en-CA",
  "langs": [
   "en-CA",
   "en",
   "fr-CA"
  ]
 },
 {
  "cc": "MX",
  "name": "Мексика",
  "city": "Мехико",
  "tz": "America/Mexico_City",
  "locale": "es-MX",
  "langs": [
   "es-MX",
   "es",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "BR",
  "name": "Бразилия",
  "city": "Сан-Паулу",
  "tz": "America/Sao_Paulo",
  "locale": "pt-BR",
  "langs": [
   "pt-BR",
   "pt",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "AR",
  "name": "Аргентина",
  "city": "Буэнос-Айрес",
  "tz": "America/Argentina/Buenos_Aires",
  "locale": "es-AR",
  "langs": [
   "es-AR",
   "es",
   "en-US",
   "en"
  ]
 },
 {
  "cc": "ZA",
  "name": "ЮАР",
  "city": "Йоханнесбург",
  "tz": "Africa/Johannesburg",
  "locale": "en-ZA",
  "langs": [
   "en-ZA",
   "en"
  ]
 },
 {
  "cc": "EG",
  "name": "Египет",
  "city": "Каир",
  "tz": "Africa/Cairo",
  "locale": "ar-EG",
  "langs": [
   "ar-EG",
   "ar",
   "en-US",
   "en"
  ]
 }
];
