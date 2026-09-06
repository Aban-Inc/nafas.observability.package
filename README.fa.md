# Nafas.Observability

[![NuGet](https://img.shields.io/nuget/v/Nafas.Observability?logo=nuget)](https://www.nuget.org/packages/Nafas.Observability)
[![License: FSL-1.1-ALv2](https://img.shields.io/badge/license-FSL--1.1--ALv2-blue)](Nafas.Observability/LICENSE.fa.md)
[![.NET Standard 2.1](https://img.shields.io/badge/.NET-netstandard2.1-512BD4?logo=dotnet)](Nafas.Observability/Nafas.Observability.csproj)

داشبورد observability خوداستقرار (self-hosted) و embeddable برای ASP.NET Core — لاگ، متریک، تریس و هشدار، با ingestion واقعی و بدون هیچ وابستگی خارجی. دقیقاً به همون روشی که [Hangfire](https://www.hangfire.io/) توزیع می‌شه: یک پکیج NuGet، دو خط کد، هیچ‌چیز دیگه‌ای برای استقرار لازم نیست.

For English documentation: [README.md](README.md)

## چی داره؟

این repo از دو پروژه‌ی .NET تشکیل شده که با `nafas.observability.sln` به هم وصل شدن:

| پروژه | چیه |
|---|---|
| [`Nafas.Observability/`](Nafas.Observability) | خودِ پکیج NuGet — کتابخانه‌ای که به پروژه‌ی خودتون اضافه می‌کنید |
| [`Nafas.Dashboard.TestHost/`](Nafas.Dashboard.TestHost) | یک اپ نمونه‌ی حداقلی ASP.NET Core MVC که فقط برای تست دستی پکیج ساخته شده — **الگوی استفاده نیست**، صرفاً یه محیط برای دیدن داشبورد سرِ کار |

داخل `Nafas.Observability/` هم این بخش‌ها هستن:

- **کد اصلی سی‌شارپ** (ریشه‌ی پروژه) — رجیستر سرویس‌ها (`AddNafasServer`)، middleware داشبورد (`UseNafasDashboard`)، router دستی API.
- **`Storage/`** — لایه‌ی دیتابیس (SQLite پیش‌فرض، SQL Server اختیاری) برای خواندن/نوشتن داده.
- **`Ingestion/`** — بخشی که واقعاً لاگ/متریک/تریس اپ شما رو می‌گیره (بدون نیاز به کد اضافه توی اپ خودتون).
- **`ClientApp/`** — رابط کاربری داشبورد؛ یک SPA با Vue 3 + Vite که از قبل build شده و توی فایل DLL خود پکیج embed میشه.
- **`wwwroot/`** — خروجی build شده‌ی `ClientApp` (از قبل توی repo موجوده، نیازی به build دوباره نیست مگر بخواید UI رو تغییر بدید).
- **`LICENSE.md`** / **`LICENSE.fa.md`** — لایسنس پکیج (نسخه‌ی انگلیسی رسمی و معتبره، فارسی صرفاً ترجمه‌ی غیررسمیه).

## چی باید بسازه؟

برای استفاده‌ی معمولی، **فقط باید سالوشن دات‌نت رو build کنید** — هیچ مرحله‌ی اضافه‌ای لازم نیست:

```bash
dotnet build nafas.observability.sln
```

خروجی `ClientApp` (رابط کاربری داشبورد) از قبل build شده و توی `Nafas.Observability/wwwroot/` کامیت شده، پس نیازی به Node.js یا npm برای استفاده‌ی عادی از پکیج نیست.

فقط اگر می‌خواید **خودِ رابط کاربری داشبورد رو تغییر بدید**، این مراحل رو طی کنید:

```bash
cd Nafas.Observability/ClientApp
npm install          # فقط بار اول
npm run build         # خروجی می‌ره توی ClientApp/dist/

# بعد محتوای dist/ رو جایگزین wwwroot/ کنید (نه merge — جایگزین)
```

سپس دوباره `dotnet build` بزنید تا فایل‌های جدید embed بشن.

## چه‌طور استفاده کنه؟

### گزینه‌ی ۱: نصب از NuGet (وقتی منتشر بشه)

```bash
dotnet add package Nafas.Observability
```

### گزینه‌ی ۲: ارجاع مستقیم به پروژه (توسعه‌ی محلی)

```bash
dotnet add reference path/to/Nafas.Observability/Nafas.Observability.csproj
```

### کد لازم توی اپ شما

فقط همین دو خط، توی `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNafasServer();   // ذخیره‌سازی + ingestion واقعی + ارزیابی هشدار
// یا با تنظیمات دلخواه:
// builder.Services.AddNafasServer(options =>
// {
//     options.Provider = DatabaseProvider.Sqlite;   // پیش‌فرض
//     options.RetentionDays = 30;                   // پیش‌فرض
//     options.AlertWebhookUrl = "https://example.com/webhooks/nafas"; // اختیاری
// });

var app = builder.Build();

app.UseNafasDashboard("/nafas");     // داشبورد روی https://your-app/nafas بالا میاد

app.Run();
```

همین. از این لحظه، هر خط لاگ (`ILogger`)، هر span از `Activity`، و هر اندازه‌گیری از `Meter` که اپ شما تولید می‌کنه، به‌صورت خودکار گرفته و توی داشبورد نمایش داده می‌شه — بدون نیاز به کد اضافه.

### تست محلی با `Nafas.Dashboard.TestHost`

اگر می‌خواید خودِ پکیج رو قبل از استفاده امتحان کنید:

```bash
cd Nafas.Dashboard.TestHost
dotnet run
```

بعد به آدرس `http://localhost:<port>/nafas` برید.

## تنظیمات

همه‌ی این‌ها داخل `AddNafasServer(options => { ... })` قابل تنظیمن:

| گزینه | پیش‌فرض | توضیح |
|---|---|---|
| `Provider` | `DatabaseProvider.Sqlite` | `Sqlite` یا `SqlServer` |
| `ConnectionString` | `Data Source=nafas.db` (فقط SQLite) | یا مستقیم ست کنید، یا از `ConnectionStringName` استفاده کنید |
| `ConnectionStringName` | — | از بخش `ConnectionStrings` در `appsettings.json`، و در نبود آن از `web.config` کلاسیک خونده می‌شه |
| `Schema` | `dbo` | فقط SQL Server — جداول رو زیر یک schema مشخص نگه می‌داره |
| `RetentionDays` | `30` | لاگ/متریک/تریس چند روز نگه داشته بشه قبل از پاک‌سازی خودکار |
| `AlertWebhookUrl` | — | آدرسی که رخدادهای هشدار به‌صورت JSON بهش POST می‌شن. بدون تنظیم این، قوانین و رخدادهای هشدار همچنان کار می‌کنن، فقط جایی ارسال نمی‌شن |
| `ServiceName` | `IHostEnvironment.ApplicationName` | نامی که روی همه‌ی داده‌های دریافتی به‌عنوان نام سرویس ثبت می‌شه رو override می‌کنه |

## لایسنس

[FSL-1.1-ALv2](https://fsl.software) — رایگان برای هر استفاده‌ای، به‌جز ساختن یک محصول یا سرویس رقیب؛ هر نسخه دو سال بعد از انتشارش به‌طور خودکار به Apache License 2.0 تبدیل می‌شه. متن رسمی و معتبر: [LICENSE.md](Nafas.Observability/LICENSE.md) (انگلیسی). ترجمه‌ی فارسی (غیررسمی، صرفاً برای درک بهتر): [LICENSE.fa.md](Nafas.Observability/LICENSE.fa.md).
