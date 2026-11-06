// Создано tools/mask-plus/generate.py из countries.json — руками не править.
const MASK_COUNTRIES = [
 {
  "cc": "DE",
  "name": "Германия",
  "city": "Берлин",
  "name_en": "Germany",
  "city_en": "Berlin",
  "name_de": "Deutschland",
  "city_de": "Berlin",
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
  "name_en": "Austria",
  "city_en": "Vienna",
  "name_de": "Österreich",
  "city_de": "Wien",
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
  "name_en": "Switzerland",
  "city_en": "Zurich",
  "name_de": "Schweiz",
  "city_de": "Zürich",
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
  "name_en": "France",
  "city_en": "Paris",
  "name_de": "Frankreich",
  "city_de": "Paris",
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
  "name_en": "Italy",
  "city_en": "Rome",
  "name_de": "Italien",
  "city_de": "Rom",
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
  "name_en": "Spain",
  "city_en": "Madrid",
  "name_de": "Spanien",
  "city_de": "Madrid",
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
  "name_en": "Portugal",
  "city_en": "Lisbon",
  "name_de": "Portugal",
  "city_de": "Lissabon",
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
  "name_en": "Netherlands",
  "city_en": "Amsterdam",
  "name_de": "Niederlande",
  "city_de": "Amsterdam",
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
  "name_en": "Belgium",
  "city_en": "Brussels",
  "name_de": "Belgien",
  "city_de": "Brüssel",
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
  "name_en": "Ireland",
  "city_en": "Dublin",
  "name_de": "Irland",
  "city_de": "Dublin",
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
  "name_en": "United Kingdom",
  "city_en": "London",
  "name_de": "Vereinigtes Königreich",
  "city_de": "London",
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
  "name_en": "Sweden",
  "city_en": "Stockholm",
  "name_de": "Schweden",
  "city_de": "Stockholm",
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
  "name_en": "Norway",
  "city_en": "Oslo",
  "name_de": "Norwegen",
  "city_de": "Oslo",
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
  "name_en": "Denmark",
  "city_en": "Copenhagen",
  "name_de": "Dänemark",
  "city_de": "Kopenhagen",
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
  "name_en": "Finland",
  "city_en": "Helsinki",
  "name_de": "Finnland",
  "city_de": "Helsinki",
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
  "name_en": "Iceland",
  "city_en": "Reykjavik",
  "name_de": "Island",
  "city_de": "Reykjavík",
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
  "name_en": "Poland",
  "city_en": "Warsaw",
  "name_de": "Polen",
  "city_de": "Warschau",
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
  "name_en": "Czechia",
  "city_en": "Prague",
  "name_de": "Tschechien",
  "city_de": "Prag",
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
  "name_en": "Hungary",
  "city_en": "Budapest",
  "name_de": "Ungarn",
  "city_de": "Budapest",
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
  "name_en": "Romania",
  "city_en": "Bucharest",
  "name_de": "Rumänien",
  "city_de": "Bukarest",
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
  "name_en": "Greece",
  "city_en": "Athens",
  "name_de": "Griechenland",
  "city_de": "Athen",
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
  "name_en": "Turkey",
  "city_en": "Istanbul",
  "name_de": "Türkei",
  "city_de": "Istanbul",
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
  "name_en": "Ukraine",
  "city_en": "Kyiv",
  "name_de": "Ukraine",
  "city_de": "Kiew",
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
  "name_en": "Estonia",
  "city_en": "Tallinn",
  "name_de": "Estland",
  "city_de": "Tallinn",
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
  "name_en": "Latvia",
  "city_en": "Riga",
  "name_de": "Lettland",
  "city_de": "Riga",
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
  "name_en": "Lithuania",
  "city_en": "Vilnius",
  "name_de": "Litauen",
  "city_de": "Vilnius",
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
  "name_en": "Russia",
  "city_en": "Moscow",
  "name_de": "Russland",
  "city_de": "Moskau",
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
  "name_en": "Kazakhstan",
  "city_en": "Astana",
  "name_de": "Kasachstan",
  "city_de": "Astana",
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
  "name_en": "Georgia",
  "city_en": "Tbilisi",
  "name_de": "Georgien",
  "city_de": "Tiflis",
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
  "name_en": "Armenia",
  "city_en": "Yerevan",
  "name_de": "Armenien",
  "city_de": "Eriwan",
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
  "name_en": "Azerbaijan",
  "city_en": "Baku",
  "name_de": "Aserbaidschan",
  "city_de": "Baku",
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
  "name_en": "Israel",
  "city_en": "Jerusalem",
  "name_de": "Israel",
  "city_de": "Jerusalem",
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
  "name_en": "UAE",
  "city_en": "Dubai",
  "name_de": "VAE",
  "city_de": "Dubai",
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
  "name_en": "India",
  "city_en": "Delhi",
  "name_de": "Indien",
  "city_de": "Delhi",
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
  "name_en": "Thailand",
  "city_en": "Bangkok",
  "name_de": "Thailand",
  "city_de": "Bangkok",
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
  "name_en": "Singapore",
  "city_en": "Singapore",
  "name_de": "Singapur",
  "city_de": "Singapur",
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
  "name_en": "Hong Kong",
  "city_en": "Hong Kong",
  "name_de": "Hongkong",
  "city_de": "Hongkong",
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
  "name_en": "China",
  "city_en": "Beijing",
  "name_de": "China",
  "city_de": "Peking",
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
  "name_en": "Japan",
  "city_en": "Tokyo",
  "name_de": "Japan",
  "city_de": "Tokio",
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
  "name_en": "South Korea",
  "city_en": "Seoul",
  "name_de": "Südkorea",
  "city_de": "Seoul",
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
  "name_en": "Australia",
  "city_en": "Sydney",
  "name_de": "Australien",
  "city_de": "Sydney",
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
  "name_en": "New Zealand",
  "city_en": "Auckland",
  "name_de": "Neuseeland",
  "city_de": "Auckland",
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
  "name_en": "USA",
  "city_en": "New York",
  "name_de": "USA",
  "city_de": "New York",
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
  "name_en": "Canada",
  "city_en": "Toronto",
  "name_de": "Kanada",
  "city_de": "Toronto",
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
  "name_en": "Mexico",
  "city_en": "Mexico City",
  "name_de": "Mexiko",
  "city_de": "Mexiko-Stadt",
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
  "name_en": "Brazil",
  "city_en": "São Paulo",
  "name_de": "Brasilien",
  "city_de": "São Paulo",
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
  "name_en": "Argentina",
  "city_en": "Buenos Aires",
  "name_de": "Argentinien",
  "city_de": "Buenos Aires",
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
  "name_en": "South Africa",
  "city_en": "Johannesburg",
  "name_de": "Südafrika",
  "city_de": "Johannesburg",
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
  "name_en": "Egypt",
  "city_en": "Cairo",
  "name_de": "Ägypten",
  "city_de": "Kairo",
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
