# Roadmap

هر فاز یه چک‌پوینت داره؛ تا تأیید نشه سراغ فاز بعد نمی‌ریم.

## فاز ۰ — اسکلت پروژه
- [x] `sln` و `csproj` (`net8.0-windows`، WinForms + `UseWPF`، `ImplicitUsings=false`)
- [x] فولدرها، `.gitignore`، `README`، `config.example.json`
- [x] `Program.cs` حداقلی + فرم خالی

✅ `dotnet build` بدون خطا، پنجره باز می‌شه.

## فاز ۱ — Config
- [x] `AppConfig` با همه‌ی فیلدها و پیش‌فرض‌ها (+ محدودسازی مقادیر نامعتبر)
- [x] لود با `System.Text.Json` (camelCase، کامنت، کامای آخر)
- [x] جستجو: پوشه‌ی جاری ← کنار exe
- [x] نبود فایل → ساخت، پیام، باز کردن در Notepad، خروج
- [x] فال‌بک کلید به `GEMINI_API_KEY`
- [x] JSON خراب → پیام با شماره‌ی خط و اسم فیلد، باز کردن در Notepad

✅ اجرای اول `config.json` می‌سازه، اجرای دوم می‌خونه.

## فاز ۲ — خواندن Live Captions (پرریسک‌ترین)
- [x] پیدا/اجرا کردن پروسه‌ی `LiveCaptions` (فقط موقع شروع برنامه)
- [x] پیدا کردن `CaptionsTextBlock` با UIA + کش
- [x] مدیریت خطا: پاک کردن کش، re-find حداکثر ۱ بار در ثانیه
- [x] نرمال‌سازی + placeholder → خالی
- [x] حالت idle: تا اولین صدا `CaptionsTextBlock` وجود نداره (فقط `ReadyToCaptionTextBlock`) → "وصل، متن خالی"
- [x] `lock` دور دسترسی UIA
- [x] تست موقت: نمایش زنده‌ی متن در فرم (با صدای TTS + بستن/باز کردن Live Captions)

✅ با پخش ویدیو، متن زنده در فرم آپدیت می‌شه.

## فاز ۳ — AnchorText
- [x] `Extract(current, anchor)` با دنباله‌های 80/48/28/16
- [x] حذف تا ۳ کلمه‌ی آخر anchor و تلاش دوباره (دنباله‌ی کوتاه‌تر از ۸ کاراکتر بعد از حذف استفاده نمی‌شه)
- [x] فال‌بک: anchor کامل اسکرول شده → کل `current`
- [x] پاک کردن نقطه‌گذاری اول متن جدید (`dog` → `dog. Then` ⇒ `Then`)
- [x] مقایسه بدون حساسیت به نقطه‌گذاری و حروف بزرگ (Live Captions بعداً `basics what` رو می‌کنه `basics. What`)
- [x] چند تطابق → اونی که متن قبلش بیشتر با anchor یکیه؛ مساوی → اولی (تکرار بهتر از گم شدن سوال‌های وسط)
- [x] تطابق فقط در مرز کلمه (`ba` داخل `bank` حساب نمی‌شه)
- [x] پروژه‌ی تست کنسولی بدون NuGet: `tests/LiveCaptionsGemini.Tests` (۱۹ سناریو)

✅ همه‌ی سناریوها درست.

## فاز ۴ — هات‌کی سراسری
- [x] `WH_KEYBOARD_LL` + نگه داشتن delegate + unhook
- [x] هوک روی thread جدا با message loop خودش (UI شلوغ کیبورد سیستم رو کند نمی‌کنه و ویندوز هوک رو حذف نمی‌کنه)
- [x] ماشین حالت Ctrl+Shift: armed → spoiled → fire on release، فقط یک بار در هر فشار
- [x] وضعیت کلیدها از `GetAsyncKeyState` (هیچ کلیدی "گیر" نمی‌کنه)
- [x] کلید عادی (F8…) برای Send، F9 برای Skip، `None`؛ با نگه داشتن Ctrl/Shift/Alt/Win یا auto-repeat عمل نمی‌کنه
- [x] اعتبارسنجی در config (`Ctrl+T`، کلید تکراری → پیام خطا)
- [x] همیشه `CallNextHookEx`، کار با `BeginInvoke`
- [x] ۱۲ سناریوی شبیه‌سازی‌شده پاس شد
- [ ] تست دستی تو: Ctrl+Shift+T واقعی در مرورگر

✅ Ctrl+Shift یک بار fire، Ctrl+Shift+T هیچی، تایپ عادی مختل نمی‌شه.

## فاز ۵ — Overlay
- [x] بدون حاشیه، TopMost، تیره، opacity، گوشه‌ی پایین‌راست
- [x] `ShowWithoutActivation` + `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` + `MA_NOACTIVATE`
- [x] درگ از هدر، ریسایز از لبه (8px)
- [x] هدر: وضعیت + فلش ۱.۵ ثانیه + ✕
- [x] پیش‌نمایش pending (`CaptionsPoller` هر 150ms روی thread پس‌زمینه)
- [x] `RichTextBox`: `AppendSent` / `AppendToken` / `AppendStats` / `AppendError` (همه thread-safe)
- [x] دموی موقت در `Program.WireDemo` (جواب ساختگی؛ در فاز ۷ با `SendPipeline` جایگزین می‌شه)
- [x] تست: فوکوس با کلیک/درگ/هات‌کی گرفته نمی‌شه، درگ، ریسایز، ✕، اسکرین‌شات

✅ overlay فوکوس نمی‌گیره، پیش‌نمایش زنده‌ست.

## فاز ۶ — GeminiProvider
- [x] `IChatProvider` + `ProviderResult`
- [x] `HttpClient` استاتیک (HTTP/2، ping، بدون timeout)
- [x] warm-up `GET models/{model}` + تکرار هر ۴ دقیقه (خطا فقط وقتی عوض بشه نمایش داده می‌شه)
- [x] `StreamAsync`: body، SSE، رد کردن `thought`، `blockReason` / `finishReason`
- [x] فال‌بک 400 "thinking": `minimal` → `low` → بدون thinkingConfig (حذف مستقیم = thinking پیش‌فرض بالا = کند)
- [x] `ChatHistory` (فقط بعد از موفقیت)
- [x] TTFT با `Stopwatch`
- [x] پیام خطای شبکه با زنجیره‌ی inner exception
- [x] تست زنده: `-- --live [--model X]` و تشخیص شبکه: `-- --diag [--model X]`
- [x] نتیجه: `gemini-3.5-flash-lite` اولین توکن ~0.6–1 ثانیه؛ `gemini-3.8-flash` 2.5 تا 20+ ثانیه (یا قطع در ۳۳ ثانیه)

✅ warm-up ✓ و جواب توکن به توکن استریم می‌شه.

## فاز ۷ — SendPipeline
- [x] Send: خواندن → `Extract` → جابه‌جایی anchor → پاک کردن پیش‌نمایش
- [x] متن خالی → "nothing new"؛ نادیده گرفتن فشار دوم < 300ms
- [x] Skip
- [x] `Channel<string>` برای هر درخواست + زنجیره‌ی نمایش ترتیبی (خطا یا exception زنجیره رو نمی‌شکنه)
- [x] warm-up هر ۴ دقیقه منتقل شد به `SendPipeline`؛ `Program` فقط سیم‌کشی می‌کنه
- [x] `IOverlayView` و `ICaptionSource` برای تست بدون پنجره/Live Captions
- [x] رویداد `RequestCompleted` (برای `log.csv` در فاز ۸)
- [x] ۱۳ تست pipeline با provider ساختگی + تست واقعی: اولین توکن 563ms

✅ دو Send پشت سر هم: دومی فوری ارسال، ولی بعد از اولی نمایش.

## فاز ۸ — لاگ و جمع‌بندی
- [x] `CsvLogger` (هدر + escape، خط جدید در خطا → فاصله، فایل قفل‌شده → پیام بدون crash)
- [x] یک ردیف برای هر درخواست، کنار `config.json`
- [x] تکمیل README (استفاده، جدول config، عیب‌یابی، افزودن provider)
- [x] مرور چک‌لیست "Done when":

| # | معیار | وضعیت |
|---|---|---|
| 1 | `dotnet build` بدون خطا | ✅ ۰ خطا، ۰ هشدار |
| 2 | پیش‌نمایش زنده با Live Captions | ✅ تست واقعی با صدای TTS |
| 3 | Ctrl+Shift فقط متن جدید؛ F9 skip؛ دو بار بدون متن جدید → هیچی | ✅ تست واقعی + تست‌های pipeline |
| 4 | Ctrl+Shift+T در مرورگر Send نمی‌کنه | ✅ شبیه‌سازی (Ctrl+Shift+F24) — ⏳ تست دستی در مرورگر واقعی با تو |
| 5 | استریم توکن به توکن + TTFT؛ بدون گرفتن فوکوس | ✅ اولین توکن 563–772ms؛ کلیک/درگ/هات‌کی فوکوس نمی‌گیرن |
| 6 | یک ردیف در `log.csv` برای هر درخواست | ✅ |

## فاز ۸.۵ — پنجره‌ی تنظیمات موقع شروع
- [x] `SettingsForm`: system prompt (چندخطی)، max output tokens، history turns
- [x] اندازه‌ی فونت در خود overlay: دکمه‌های `A−` / `A+` و Ctrl + چرخ ماوس؛ کل پنجره (حتی جواب‌های قبلی) با هم بزرگ/کوچک می‌شه و در config ذخیره می‌شه
- [x] exe با دبل‌کلیک (`publish.ps1` + میان‌بر دسکتاپ) و جلوگیری از اجرای دو نسخه
- [x] Start (`Ctrl+Enter`) ذخیره و شروع؛ Exit (`Esc`) خروج
- [x] ذخیره فقط همین مقادیر در `config.json` (کلید API، کامنت‌ها و بقیه‌ی فیلدها دست نمی‌خورن)
- [x] `showSettingsOnStart` برای غیرفعال کردن پنجره
- [x] ۶ تست ذخیره‌ی config + تست واقعی پنجره با UI Automation

## فاز ۹ — (بعداً) چند Provider و حالت مقایسه
- [ ] `ClaudeProvider`، `OpenAiProvider`
- [ ] `"providers": [...]`، `"compareMode": true`
- [ ] ارسال موازی + نمایش کنار هم با TTFT
