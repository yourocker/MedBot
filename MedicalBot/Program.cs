using System;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using ExcelDataReader;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json; // Нужен для чтения настроек
using File = System.IO.File;

namespace MedicalBot
{
    // Класс-шаблон для загрузки настроек из файла
    public class AppConfig
    {
        public string BotToken { get; set; }
        public string YandexLink { get; set; }
        public long[] DirectorIds { get; set; }
    }

    class Program
    {
        // Переменные (заполняются из файла appsettings.json при старте)
        private static string BotToken;
        private static string YandexPublicUrl;
        private static long[] AllowedUsers; 

        // Настройки
        private const string LocalFileName = "patients.xlsx";
        private const int MaxAutoShowResults = 15; // Лимит выдачи
        
        private static DateTime _lastUpdateDate = DateTime.MinValue;
        private static TelegramBotClient _botClient;
        private static readonly HttpClient _httpClient = new HttpClient();

        // Память бота для кнопок "Показать всё"
        private static readonly Dictionary<long, string> _pendingSearches = new();

        static async Task Main(string[] args)
        {
            // 1. Настройка кодировок (чтобы Excel читался корректно)
            Console.OutputEncoding = Encoding.UTF8;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // 2. ЗАГРУЗКА НАСТРОЕК ИЗ JSON
            string configPath = "appsettings.json";
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"📛 КРИТИЧЕСКАЯ ОШИБКА: Файл {configPath} не найден!");
                Console.WriteLine("Создайте файл appsettings.json с токеном и ID.");
                return; // Останавливаем программу, если нет настроек
            }

            try 
            {
                string jsonString = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(jsonString);

                // Переносим данные из файла в переменные программы
                BotToken = config.BotToken;
                YandexPublicUrl = config.YandexLink;
                AllowedUsers = config.DirectorIds;

                Console.WriteLine("⚙️ Настройки загружены успешно.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"📛 Ошибка чтения настроек: {ex.Message}");
                return;
            }

            // 3. Проверка даты существующего файла
            if (File.Exists(LocalFileName))
            {
                _lastUpdateDate = File.GetLastWriteTime(LocalFileName);
            }

            // 4. Запуск бота
            _botClient = new TelegramBotClient(BotToken);

            using CancellationTokenSource cts = new CancellationTokenSource();

            ReceiverOptions receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new [] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"✅ Бот запущен: @{me.Username}");
            
            // Бесконечное ожидание (чтобы программа не закрылась)
            await Task.Delay(-1);
        }

        private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // === ОБРАБОТКА КНОПОК (CALLBACK) ===
                if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
                {
                    await HandleButtonPress(botClient, callback, cancellationToken);
                    return;
                }

                // === ОБРАБОТКА СООБЩЕНИЙ ===
                if (update.Message is not { } message) return;
                if (message.Text is not { } messageText) return;
                
                var userId = message.From.Id;
                var chatId = message.Chat.Id;

                // Проверка доступа
                if (!AllowedUsers.Contains(userId))
                {
                    Console.WriteLine($"⛔ Попытка входа ID: {userId} ({message.From.FirstName})");
                    await botClient.SendTextMessageAsync(chatId, $"⛔ Нет доступа. Ваш ID: {userId}", cancellationToken: cancellationToken);
                    return;
                }

                Console.WriteLine($"📩 Запрос от {userId}: {messageText}");

                var keyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton[] { "🔄 Обновить базу из кассы" } })
                {
                    ResizeKeyboard = true
                };

                // Команда /start
                if (messageText == "/start")
                {
                    await botClient.SendTextMessageAsync(chatId, "Введите ФИО для поиска.", replyMarkup: keyboard, cancellationToken: cancellationToken);
                    return;
                }

                // Кнопка обновления
                if (messageText == "🔄 Обновить базу из кассы")
                {
                    await botClient.SendTextMessageAsync(chatId, "⏳ Скачиваю файл с Диска...", cancellationToken: cancellationToken);
                    if (await ForceUpdateFileAsync())
                        await botClient.SendTextMessageAsync(chatId, $"✅ База успешно обновлена!\n🕒 Дата файла: {_lastUpdateDate}", replyMarkup: keyboard, cancellationToken: cancellationToken);
                    else
                        await botClient.SendTextMessageAsync(chatId, "❌ Ошибка скачивания. Проверьте ссылку в настройках.", replyMarkup: keyboard, cancellationToken: cancellationToken);
                    return;
                }

                // Проверка длины запроса
                if (messageText.Length < 2)
                {
                    await botClient.SendTextMessageAsync(chatId, "Минимум 2 буквы для поиска.", replyMarkup: keyboard, cancellationToken: cancellationToken);
                    return;
                }

                // --- ПОИСК ---
                await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: cancellationToken);
                
                // Ищем с учетом лимита (forceShowAll = false)
                var searchResult = SearchInExcel(messageText, forceShowAll: false);

                if (searchResult.IsTooMany)
                {
                    // Сохраняем, что искал пользователь
                    _pendingSearches[chatId] = messageText;

                    var inlineKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        new [] { InlineKeyboardButton.WithCallbackData("✅ Да, показать все", "show_all") },
                        new [] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_search") }
                    });

                    await botClient.SendTextMessageAsync(
                        chatId, 
                        $"⚠️ Найдено слишком много записей (**{searchResult.Count}**).\nПоказать их все?", 
                        parseMode: ParseMode.Markdown,
                        replyMarkup: inlineKeyboard, 
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, searchResult.Message, replyMarkup: keyboard, cancellationToken: cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Global Error: {ex.Message}");
            }
        }

        // Логика кнопок "Да / Нет"
        private static async Task HandleButtonPress(ITelegramBotClient botClient, CallbackQuery callback, CancellationToken ct)
        {
            var chatId = callback.Message.Chat.Id;
            var data = callback.Data;

            // Убираем индикатор загрузки с кнопки
            await botClient.AnswerCallbackQueryAsync(callback.Id, cancellationToken: ct);

            if (data == "cancel_search")
            {
                await botClient.EditMessageTextAsync(chatId, callback.Message.MessageId, "❌ Поиск отменен.", cancellationToken: ct);
                _pendingSearches.Remove(chatId);
                return;
            }

            if (data == "show_all")
            {
                if (_pendingSearches.TryGetValue(chatId, out string originalQuery))
                {
                    await botClient.EditMessageTextAsync(chatId, callback.Message.MessageId, $"⏳ Загружаю полный список по запросу: *{originalQuery}*...", parseMode: ParseMode.Markdown, cancellationToken: ct);
                    
                    // Повторный поиск БЕЗ лимита
                    var result = SearchInExcel(originalQuery, forceShowAll: true);
                    
                    await botClient.SendTextMessageAsync(chatId, result.Message, cancellationToken: ct);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "⚠️ Запрос устарел. Введите фамилию заново.", cancellationToken: ct);
                }
            }
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Telegram API Error: {exception.Message}");
            return Task.CompletedTask;
        }

        // Скачивание файла с Яндекса
        private static async Task<bool> ForceUpdateFileAsync()
        {
            try
            {
                string apiUrl = $"https://cloud-api.yandex.net/v1/disk/public/resources/download?public_key={System.Net.WebUtility.UrlEncode(YandexPublicUrl)}";
                var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode) return false;
                
                var jsonString = await response.Content.ReadAsStringAsync();
                string downloadUrl = ExtractHrefFromJson(jsonString); // Парсим JSON вручную или через класс
                
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    var fileBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(LocalFileName, fileBytes);
                    _lastUpdateDate = DateTime.Now; 
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        // Парсинг ссылки на скачивание
        private static string ExtractHrefFromJson(string json)
        {
            // Простой поиск строки "href":"..."
            var key = "\"href\":\"";
            int start = json.IndexOf(key);
            if (start == -1) return string.Empty;
            start += key.Length;
            int end = json.IndexOf("\"", start);
            return json.Substring(start, end - start);
        }

        // Класс для результата поиска
        class SearchResultInfo
        {
            public string Message { get; set; } = "";
            public int Count { get; set; }
            public bool IsTooMany { get; set; }
        }

        // Логика чтения Excel
        private static SearchResultInfo SearchInExcel(string query, bool forceShowAll)
        {
            if (!File.Exists(LocalFileName)) return new SearchResultInfo { Message = "⚠️ База еще не скачана. Нажмите кнопку 'Обновить базу'." };

            var matches = new List<string>();
            int count = 0;
            decimal totalGlobalSum = 0;

            try 
            {
                using (var stream = File.Open(LocalFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet();
                    foreach (DataTable table in result.Tables)
                    {
                        string dateFromTabName = table.TableName; // Имя вкладки как дата
                        
                        // Начинаем со 2-й строки (пропускаем заголовки)
                        for (int i = 2; i < table.Rows.Count; i++)
                        {
                            var row = table.Rows[i];
                            string fio = row[0]?.ToString() ?? ""; 

                            if (fio.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                count++;
                                
                                string service = row[1]?.ToString() ?? "-";
                                decimal cost = 0;
                                // Складываем две колонки цены (индексы 2 и 3)
                                if (decimal.TryParse(row[2]?.ToString(), out decimal c1)) cost += c1;
                                if (decimal.TryParse(row[3]?.ToString(), out decimal c2)) cost += c2;
                                totalGlobalSum += cost;

                                var block = $"👤 {fio.ToUpper()}\n📅 {dateFromTabName}\n🏥 {service}\n💰 {cost} руб.\n➖➖➖➖➖➖";
                                matches.Add(block);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { return new SearchResultInfo { Message = $"Ошибка чтения Excel: {ex.Message}" }; }

            if (count == 0) return new SearchResultInfo { Message = "Ничего не найдено." };

            // Если записей много и не нажата кнопка "Показать все"
            if (!forceShowAll && count > MaxAutoShowResults)
            {
                return new SearchResultInfo { Count = count, IsTooMany = true };
            }

            // Формируем итоговый текст
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"🔎 Найдено записей: {count}");
            sb.AppendLine($"💰 Всего оплачено: {totalGlobalSum} руб.\n");
            
            foreach (var block in matches)
            {
                sb.AppendLine(block);
            }
            
            sb.AppendLine($"\n🕒 Актуальность базы: {_lastUpdateDate}");
            
            // Обрезаем, если слишком длинное (лимит Telegram)
            string finalMsg = sb.ToString();
            if (finalMsg.Length > 4000) 
                finalMsg = finalMsg.Substring(0, 4000) + "\n\n...(список обрезан, слишком много данных)...";

            return new SearchResultInfo { Message = finalMsg, Count = count, IsTooMany = false };
        }
    }
}