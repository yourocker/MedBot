using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json;
using MedicalBot.Services;
using Microsoft.EntityFrameworkCore;

using File = System.IO.File;

namespace MedicalBot
{
    public class AppConfig
    {
        public string BotToken { get; set; }
        public string CashUrl { get; set; }      
        public string ScheduleUrl { get; set; } 
        public long[] DirectorIds { get; set; }
        public Dictionary<string, string> ConnectionStrings { get; set; }
    }

    enum UserState
    {
        None,
        WaitingForPatientName,
        WaitingForStartDate,
        WaitingForEndDate,
        WaitingForDailyReportDate
    }

    class Program
    {
        private static string BotToken;
        private static AppConfig _appConfig;
        private static string ConnectionString;

        private static readonly PatientService _patientService = new PatientService();
        private static readonly StatisticsService _statsService = new StatisticsService();
        
        private static Dictionary<long, UserState> _userStates = new();
        private static Dictionary<long, DateTime> _tempStartDates = new();
        private static readonly Dictionary<long, string> _pendingSearches = new();

        private static TelegramBotClient _botClient;
        private static readonly HttpClient _httpClient = new HttpClient();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            string configPath = "appsettings.json";
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"📛 ОШИБКА: Файл {configPath} не найден!");
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(configPath);
                _appConfig = JsonSerializer.Deserialize<AppConfig>(jsonString);
                BotToken = _appConfig.BotToken;
                
                if (_appConfig.ConnectionStrings != null && _appConfig.ConnectionStrings.ContainsKey("DefaultConnection"))
                {
                    ConnectionString = _appConfig.ConnectionStrings["DefaultConnection"];
                }
                else
                {
                    Console.WriteLine("⚠️ Нет строки подключения к БД!");
                    return;
                }
                Console.WriteLine("⚙️ Настройки загружены.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"📛 Ошибка настроек: {ex.Message}");
                return;
            }

            Console.WriteLine("\n🚀 Запускаю бота...");
            _botClient = new TelegramBotClient(BotToken);
            // --- АВТО-СОЗДАНИЕ БАЗЫ ---
            Console.WriteLine("🛠 Проверяю базу данных...");
            var optionsBuilder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<MedicalBot.Data.AppDbContext>();
            optionsBuilder.UseNpgsql(ConnectionString);
            using (var db = new MedicalBot.Data.AppDbContext(optionsBuilder.Options))
            {
                db.Database.EnsureCreated();
                Console.WriteLine("✅ База данных готова.");
            }
            // --------------------------

            using CancellationTokenSource cts = new CancellationTokenSource();
            ReceiverOptions receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"✅ Бот запущен: @{me.Username}");

            await Task.Delay(-1);
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
                {
                    await HandleButtonPress(botClient, callback, cancellationToken);
                    return;
                }

                if (update.Message is not { } message) return;
                var userId = message.From.Id;
                var chatId = message.Chat.Id;

                if (!_appConfig.DirectorIds.Contains(userId))
                {
                    await botClient.SendTextMessageAsync(chatId, $"⛔ Нет доступа. Ваш ID: {userId}", cancellationToken: cancellationToken);
                    return;
                }

                // Обработка файлов
                if (message.Type == MessageType.Document && message.Document != null)
                {
                    var doc = message.Document;
                    var lowerName = doc.FileName?.ToLower() ?? "";

                    if (lowerName.EndsWith(".xlsx") || lowerName.EndsWith(".xls") || lowerName.EndsWith(".csv"))
                    {
                        if (lowerName.Contains("касса"))
                        {
                            await botClient.SendTextMessageAsync(chatId, "💰 Вижу КАССУ. Обрабатываю...", cancellationToken: cancellationToken);
                            string path = await DownloadTelegramFile(botClient, doc, "manual_cash.xlsx", cancellationToken);
                            var importer = new ExcelImporter(ConnectionString);
                            string report = await importer.ImportAsync(path);
                            await botClient.SendTextMessageAsync(chatId, report, cancellationToken: cancellationToken);
                        }
                        else if (lowerName.Contains("журнал") || lowerName.Contains("запис"))
                        {
                            await botClient.SendTextMessageAsync(chatId, "📅 Вижу ЖУРНАЛ ЗАПИСИ. Обрабатываю...", cancellationToken: cancellationToken);
                            string path = await DownloadTelegramFile(botClient, doc, "manual_schedule.xlsx", cancellationToken);
                            var importer = new AppointmentImporter(ConnectionString);
                            string report = await importer.ImportAsync(path);
                            await botClient.SendTextMessageAsync(chatId, report, cancellationToken: cancellationToken);
                        }
                        return;
                    }
                }

                if (message.Text is not { } messageText) return;

                // === КЛАВИАТУРЫ ===
                
                // Главное меню
                var mainKeyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton[] { "🔍 Поиск визитов" },
                    new KeyboardButton[] { "📅 Кассовый отчет за день", "💰 Отчет по выручке (период)" },
                    new KeyboardButton[] { "🔄 Обновить базу" }
                })
                {
                    ResizeKeyboard = true
                };

                // Клавиатура "Отмена"
                var cancelKeyboard = new ReplyKeyboardMarkup(new[]
                {
                    new KeyboardButton("❌ Отмена")
                })
                {
                    ResizeKeyboard = true
                };

                if (!_userStates.ContainsKey(userId)) _userStates[userId] = UserState.None;

                // --- 1. ГЛОБАЛЬНАЯ ОТМЕНА ---
                // Если нажали "Отмена", сбрасываем всё и возвращаем меню
                if (messageText == "❌ Отмена")
                {
                    _userStates[userId] = UserState.None;
                    await botClient.SendTextMessageAsync(chatId, "🔙 Возвращаемся в меню.", replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                    return;
                }

                // --- 2. СБРОС СОСТОЯНИЯ ПО КНОПКАМ МЕНЮ ---
                // Если пользователь нажал кнопку меню, находясь в режиме ввода даты
                if (messageText == "/start" || messageText == "🔍 Поиск визитов" || messageText == "📅 Кассовый отчет за день" || messageText == "💰 Отчет по выручке (период)" || messageText == "🔄 Обновить базу")
                {
                    _userStates[userId] = UserState.None; 
                }
                
                // --- 3. ОБРАБОТКА КОМАНД ---

                if (messageText == "/start")
                {
                    await botClient.SendTextMessageAsync(chatId, "👋 Добро пожаловать! Выберите действие:", replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                    return;
                }

                if (messageText == "🔍 Поиск визитов")
                {
                    _userStates[userId] = UserState.WaitingForPatientName;
                    // Показываем кнопку Отмена
                    await botClient.SendTextMessageAsync(chatId, "✍️ Введите **ФИО** пациента:", parseMode: ParseMode.Markdown, replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    return;
                }

                if (messageText == "📅 Кассовый отчет за день")
                {
                    _userStates[userId] = UserState.WaitingForDailyReportDate;
                    await botClient.SendTextMessageAsync(chatId, "📅 Введите **дату** отчета (ДД.ММ.ГГГГ):", parseMode: ParseMode.Markdown, replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    return;
                }

                if (messageText == "💰 Отчет по выручке (период)")
                {
                    _userStates[userId] = UserState.WaitingForStartDate;
                    await botClient.SendTextMessageAsync(chatId, "📅 Введите дату **начала** периода (ДД.ММ.ГГГГ):", parseMode: ParseMode.Markdown, replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    return;
                }

                if (messageText == "🔄 Обновить базу")
                {
                    await botClient.SendTextMessageAsync(chatId, "⏳ Начинаю полное обновление...", replyMarkup: mainKeyboard, cancellationToken: cancellationToken); // Оставляем меню, чтобы видеть прогресс

                    // 1. Касса
                    if (!string.IsNullOrEmpty(_appConfig.CashUrl))
                    {
                        await botClient.SendTextMessageAsync(chatId, "📥 1. Скачиваю Кассу...", cancellationToken: cancellationToken);
                        if (await DownloadYandexFileAsync(_appConfig.CashUrl, "auto_cash.xlsx"))
                        {
                            try
                            {
                                var importer = new ExcelImporter(ConnectionString);
                                string report = await importer.ImportAsync("auto_cash.xlsx");
                                await botClient.SendTextMessageAsync(chatId, $"💰 РЕЗУЛЬТАТ КАССЫ:\n{report}", cancellationToken: cancellationToken);
                            }
                            catch (Exception ex) { await botClient.SendTextMessageAsync(chatId, $"❌ Ошибка Кассы: {ex.Message}", cancellationToken: cancellationToken); }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "❌ Не удалось скачать Кассу.", cancellationToken: cancellationToken);
                        }
                    }

                    /*
                    // 2. Журнал
                    if (!string.IsNullOrEmpty(_appConfig.ScheduleUrl))
                    {
                        await botClient.SendTextMessageAsync(chatId, "📥 2. Скачиваю Журнал Записи...", cancellationToken: cancellationToken);
                        if (await DownloadYandexFileAsync(_appConfig.ScheduleUrl, "auto_schedule.xlsx"))
                        {
                            try
                            {
                                var importer = new AppointmentImporter(ConnectionString);
                                string report = await importer.ImportAsync("auto_schedule.xlsx");
                                await botClient.SendTextMessageAsync(chatId, $"📅 РЕЗУЛЬТАТ ЖУРНАЛА:\n{report}", cancellationToken: cancellationToken);
                            }
                            catch (Exception ex) { await botClient.SendTextMessageAsync(chatId, $"❌ Ошибка Журнала: {ex.Message}", cancellationToken: cancellationToken); }
                        }
                        else
                        {
                            await botClient.SendTextMessageAsync(chatId, "❌ Не удалось скачать Журнал.", cancellationToken: cancellationToken);
                        }
                    }
                    */

                    await botClient.SendTextMessageAsync(chatId, "✅ Готово. Выберите действие:", replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                    return;
                }


                // --- 4. ОБРАБОТКА ВВОДА ---
                
                var currentState = _userStates[userId];

                // Поиск
                if (currentState == UserState.WaitingForPatientName)
                {
                    if (messageText.Length < 2)
                    {
                        await botClient.SendTextMessageAsync(chatId, "⚠️ Минимум 2 буквы.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                        return;
                    }

                    var result = _patientService.Search(messageText, false);
                    if (result.IsTooMany)
                    {
                        _pendingSearches[chatId] = messageText;
                        var inlineKb = new InlineKeyboardMarkup(new[]
                        {
                            InlineKeyboardButton.WithCallbackData("✅ Показать всех", "show_all"),
                            InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel")
                        });
                        await botClient.SendTextMessageAsync(chatId, $"Найдено {result.Count} записей. Показать?", replyMarkup: inlineKb, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, result.Message, replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                        _userStates[userId] = UserState.None;
                    }
                    return;
                }

                // Отчет за ДЕНЬ
                if (currentState == UserState.WaitingForDailyReportDate)
                {
                    if (DateTime.TryParse(messageText, out DateTime date))
                    {
                        await botClient.SendTextMessageAsync(chatId, $"📊 Формирую отчет за {date:d}...", cancellationToken: cancellationToken);
                        string report = _statsService.GetPeriodReport(date, date);
                        await botClient.SendTextMessageAsync(chatId, report, parseMode: ParseMode.Markdown, replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                        _userStates[userId] = UserState.None;
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "❌ Неверная дата. (ДД.ММ.ГГГГ):", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    return;
                }

                // Отчет за ПЕРИОД (Начало)
                if (currentState == UserState.WaitingForStartDate)
                {
                    if (DateTime.TryParse(messageText, out DateTime start))
                    {
                        _tempStartDates[userId] = start;
                        _userStates[userId] = UserState.WaitingForEndDate;
                        await botClient.SendTextMessageAsync(chatId, "📅 Введите дату **конца** (ДД.ММ.ГГГГ):", parseMode: ParseMode.Markdown, replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "❌ Неверная дата.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    return;
                }

                // Отчет за ПЕРИОД (Конец)
                if (currentState == UserState.WaitingForEndDate)
                {
                    if (DateTime.TryParse(messageText, out DateTime end))
                    {
                        var start = _tempStartDates[userId];
                        await botClient.SendTextMessageAsync(chatId, "⏳ Считаю...", cancellationToken: cancellationToken);
                        string report = _statsService.GetPeriodReport(start, end);
                        await botClient.SendTextMessageAsync(chatId, report, parseMode: ParseMode.Markdown, replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                        _userStates[userId] = UserState.None;
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "❌ Неверная дата.", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    return;
                }

                // Если пишут просто так
                if (currentState == UserState.None)
                {
                    await botClient.SendTextMessageAsync(chatId, "⛔ **Выберите пункт меню.**", parseMode: ParseMode.Markdown, replyMarkup: mainKeyboard, cancellationToken: cancellationToken);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        private static async Task HandleButtonPress(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            var chatId = cb.Message.Chat.Id;
            var userId = cb.From.Id;
            
            // Восстанавливаем главное меню для метода возврата
            var mainKeyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🔍 Поиск визитов" },
                new KeyboardButton[] { "📅 Кассовый отчет за день", "💰 Отчет по выручке (период)" },
                new KeyboardButton[] { "🔄 Обновить базу" }
            }) { ResizeKeyboard = true };

            if (cb.Data == "cancel")
            {
                 await bot.EditMessageTextAsync(chatId, cb.Message.MessageId, "❌ Отмена.", cancellationToken: ct);
                 _userStates[userId] = UserState.None;
                 await bot.SendTextMessageAsync(chatId, "Главное меню:", replyMarkup: mainKeyboard, cancellationToken: ct);
                 return;
            }

            if (cb.Data == "show_all" && _pendingSearches.ContainsKey(chatId))
            {
                var query = _pendingSearches[chatId];
                var res = _patientService.Search(query, true);
                await bot.SendTextMessageAsync(chatId, res.Message, cancellationToken: ct);
                
                _userStates[userId] = UserState.None;
                await bot.SendTextMessageAsync(chatId, "Готово.", replyMarkup: mainKeyboard, cancellationToken: ct);
            }

            await bot.AnswerCallbackQueryAsync(cb.Id, cancellationToken: ct);
        }

        private static async Task<string> DownloadTelegramFile(ITelegramBotClient bot, Document doc, string localName, CancellationToken ct)
        {
            var fileInfo = await bot.GetFileAsync(doc.FileId, ct);
            using var fs = File.OpenWrite(localName);
            await bot.DownloadFileAsync(fileInfo.FilePath, fs, ct);
            return localName;
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
        {
            Console.WriteLine($"Telegram Error: {ex.Message}");
            return Task.CompletedTask;
        }

        private static async Task<bool> DownloadYandexFileAsync(string publicUrl, string localFileName)
        {
            try
            {
                Console.WriteLine($"🔗 Скачиваю: {publicUrl}");
                string apiUrl = $"https://cloud-api.yandex.net/v1/disk/public/resources/download?public_key={System.Net.WebUtility.UrlEncode(publicUrl)}";

                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                var response = await _httpClient.GetAsync(apiUrl);
                
                if (!response.IsSuccessStatusCode && publicUrl.Contains("disk.360.yandex.ru"))
                {
                    string altUrl = publicUrl.Replace("disk.360.yandex.ru", "disk.yandex.ru");
                    apiUrl = $"https://cloud-api.yandex.net/v1/disk/public/resources/download?public_key={System.Net.WebUtility.UrlEncode(altUrl)}";
                    response = await _httpClient.GetAsync(apiUrl);
                }

                if (!response.IsSuccessStatusCode) return false;

                string jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                if (!doc.RootElement.TryGetProperty("href", out var hrefElement)) return false;

                string downloadUrl = hrefElement.GetString();
                var fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(localFileName, fileBytes);

                Console.WriteLine($"✅ Сохранено: {localFileName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"📛 Ошибка Яндекса: {ex.Message}");
                return false;
            }
        }
    }
}