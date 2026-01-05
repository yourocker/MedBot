using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using MedicalBot.Data;
using MedicalBot.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Services
{
    public class ExcelImporter
    {
        private readonly string _connectionString;

        public ExcelImporter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<string> ImportAsync(string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Начинаю импорт базы данных");

            // Регистрация кодировок для Excel
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connectionString);
            
            using var db = new AppDbContext(optionsBuilder.Options);
            
            // ОТКРЫВАЕМ ТРАНЗАКЦИЮ: Либо всё сохранится, либо ничего
            using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = ExcelReaderFactory.CreateReader(stream);
                var result = reader.AsDataSet();

                // === 1. ЗАГРУЗКА СУЩЕСТВУЮЩИХ ПАЦИЕНТОВ ===
                // AsNoTracking ускоряет чтение
                var patientsInDb = await db.Patients.AsNoTracking().ToListAsync();
                var existingPatients = new Dictionary<string, Patient>();

                foreach (var p in patientsInDb)
                {
                    if (!string.IsNullOrEmpty(p.NormalizedName))
                    {
                        if (!existingPatients.ContainsKey(p.NormalizedName))
                        {
                            existingPatients.Add(p.NormalizedName, p);
                        }
                    }
                }
                
                // === 2. ЗАГРУЗКА СУЩЕСТВУЮЩИХ ВИЗИТОВ (ДЛЯ ЗАЩИТЫ ОТ ДУБЛЕЙ) ===
                var existingVisits = await db.Visits
                    .AsNoTracking()
                    .Select(v => new { v.PatientId, v.Date, v.ServiceName, v.TotalCost })
                    .ToListAsync();

                // Используем HashSet для мгновенного поиска
                var visitSignatures = new HashSet<string>();
                
                // Формируем "отпечаток" каждого визита.
                // ВАЖНО: Используем InvariantCulture и формат F2 для денег, чтобы 1000.00 и 1000 совпадали
                foreach (var v in existingVisits)
                {
                    string key = CreateSignature(v.PatientId, v.Date, v.ServiceName, v.TotalCost);
                    visitSignatures.Add(key);
                }

                int newVisitsCount = 0;
                int currentYear = 2023;
                int lastMonth = 0;      
                
                // Список для локального трекинга добавленных пациентов в рамках ЭТОГО импорта
                // (чтобы не пытаться добавить одного и того же нового пациента дважды до SaveChanges)
                var newPatientsCache = new Dictionary<string, Patient>();

                foreach (DataTable table in result.Tables)
                {
                    string tabName = table.TableName.Trim();

                    // Парсинг даты листа
                    if (!TryParseSheetDate(tabName, ref currentYear, ref lastMonth, out DateTime sheetDate))
                    {
                        continue; 
                    }

                    // ВАЖНО: Не используем ToUniversalTime, чтобы не сдвигать дату,
                    // так как база работает в режиме timestamp without time zone.
                    // Указываем, что дата "неспецифицирована".
                    sheetDate = DateTime.SpecifyKind(sheetDate, DateTimeKind.Unspecified);

                    var visitsToAdd = new List<Visit>();

                    // Начинаем со 2-й строки (пропуск заголовков)
                    for (int i = 2; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        if (row.ItemArray.Length == 0) continue;

                        int colOffset = 0;
                        string col0 = row[0]?.ToString()?.Trim();
                        // Если в первой колонке цифра (№ п/п), смещаемся
                        if (int.TryParse(col0, out _)) colOffset = 1;

                        if (table.Columns.Count <= 0 + colOffset) continue;
                        string fioRaw = row[0 + colOffset]?.ToString()?.Trim();
                        
                        // Пропускаем мусорные строки
                        if (string.IsNullOrWhiteSpace(fioRaw) || 
                            fioRaw.ToLower().Contains("итого") || 
                            fioRaw.ToLower().Contains("всего")) continue;

                        string normalizedName = fioRaw.ToUpper().Replace(" ", "").Replace(".", "");

                        // --- ЛОГИКА ПОИСКА ПАЦИЕНТА ---
                        Patient patient;

                        // 1. Проверяем в БД (загруженный словарь)
                        if (existingPatients.TryGetValue(normalizedName, out var dbPatient))
                        {
                            patient = dbPatient;
                            // Если пациент из AsNoTracking, его нужно приаттачить, но проще использовать ID
                            // Здесь мы просто берем объект, EF сам разберется при вставке визита по ID, 
                            // но для надежности лучше использовать Tracked сущность, если она новая.
                        }
                        // 2. Проверяем, не создали ли мы его только что в этом цикле
                        else if (newPatientsCache.TryGetValue(normalizedName, out var newCachedPatient))
                        {
                            patient = newCachedPatient;
                        }
                        // 3. Создаем нового
                        else
                        {
                            patient = new Patient 
                            { 
                                Id = Guid.NewGuid(), 
                                FullName = fioRaw, 
                                NormalizedName = normalizedName 
                            };
                            
                            // Добавляем в контекст
                            db.Patients.Add(patient);
                            
                            // Добавляем в локальные кэши, чтобы не создать дубль
                            existingPatients[normalizedName] = patient; 
                            newPatientsCache[normalizedName] = patient;
                        }

                        // --- ЛОГИКА ПАРСИНГА ДАННЫХ ---
                        string serviceRaw = "-";
                        if (table.Columns.Count > 1 + colOffset) 
                            serviceRaw = row[1 + colOffset]?.ToString() ?? "-";
                        string service = serviceRaw.Trim();
                        if (string.IsNullOrEmpty(service)) service = "не указано";

                        decimal cash = 0;     
                        decimal cashless = 0; 

                        // Парсинг наличных
                        if (table.Columns.Count > 2 + colOffset)
                        {
                            string val = row[2 + colOffset]?.ToString()?.Trim().Replace(".", ",") ?? "0";
                            val = Regex.Match(val, @"[\d,]+").Value; 
                            if (decimal.TryParse(val, out decimal c1)) cash = c1;
                        }

                        // Парсинг безнала
                        if (table.Columns.Count > 3 + colOffset)
                        {
                            string val = row[3 + colOffset]?.ToString()?.Trim().Replace(".", ",") ?? "0";
                            val = Regex.Match(val, @"[\d,]+").Value;
                            if (decimal.TryParse(val, out decimal c2)) cashless = c2;
                        }

                        decimal totalCost = cash + cashless;
                        if (totalCost == 0) continue; // Пропускаем нулевые чеки

                        // --- ПРОВЕРКА НА ДУБЛИКАТЫ ---
                        // Генерируем сигнатуру текущей строки
                        string currentSignature = CreateSignature(patient.Id, sheetDate, service, totalCost);

                        // Если такая запись уже есть (в БД или добавлена в текущем импорте) - ПРОПУСКАЕМ
                        if (visitSignatures.Contains(currentSignature))
                        {
                            continue;
                        }

                        // Создаем визит
                        var visit = new Visit
                        {
                            Id = Guid.NewGuid(),
                            Date = sheetDate,
                            PatientId = patient.Id, // Явная связка через ID надежнее
                            // Если пациент новый, EF Core подхватит его через ChangeTracker.
                            // Если старый, ID достаточно.
                            ServiceName = service,
                            AmountCash = cash,
                            AmountCashless = cashless,
                            TotalCost = totalCost
                        };

                        visitsToAdd.Add(visit);
                        
                        // Запоминаем, что такой визит мы уже обработали
                        visitSignatures.Add(currentSignature);
                        newVisitsCount++;
                    }

                    // Сохраняем пачками по листам, но коммит транзакции будет в конце
                    if (visitsToAdd.Any())
                    {
                        db.Visits.AddRange(visitsToAdd);
                    }
                }

                // Сохраняем все изменения в базу
                await db.SaveChangesAsync();
                
                // Фиксируем транзакцию
                await transaction.CommitAsync();
                
                sb.AppendLine($"✅ Импорт завершен.");
                sb.AppendLine($"📅 Текущий год в обработке дошел до: {currentYear}");
                sb.AppendLine($"➕ Добавлено новых записей: {newVisitsCount}");
            }
            catch (Exception ex)
            {
                // Если ошибка - откатываем всё
                await transaction.RollbackAsync();
                sb.AppendLine($"❌ Ошибка импорта: {ex.Message}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"   Подробности: {ex.InnerException.Message}");
                }
                // Логируем в консоль для отладки
                Console.WriteLine(ex.ToString());
            }

            Console.WriteLine(sb.ToString());
            return sb.ToString();
        }

        // Метод для создания ЕДИНООБРАЗНОЙ сигнатуры
        private string CreateSignature(Guid patientId, DateTime date, string service, decimal cost)
        {
            // Используем InvariantCulture для денег (точка разделитель)
            // Используем F2, чтобы 100 превратилось в "100.00", а 100.5 в "100.50"
            // Trim и ToUpper для сервиса
            return $"{patientId}_{date:yyyyMMdd}_{cost.ToString("F2", CultureInfo.InvariantCulture)}_{service?.Trim().ToUpper()}";
        }

        private bool TryParseSheetDate(string tabName, ref int currentYear, ref int lastMonth, out DateTime date)
        {
            date = DateTime.MinValue;
            var match = Regex.Match(tabName, @"(\d{1,2})[\.\-\/](\d{1,2})([\.\-\/](\d{2,4}))?");
            
            if (!match.Success) return false;

            int day = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);
            
            if (match.Groups[4].Success)
            {
                string yearStr = match.Groups[4].Value;
                int explicitYear = int.Parse(yearStr);
                if (explicitYear < 100) explicitYear += 2000;
                
                currentYear = explicitYear;
                lastMonth = month;
                
                try {
                    date = new DateTime(currentYear, month, day);
                    return true;
                } catch { return false; }
            }

            if (lastMonth != 0 && month < lastMonth)
            {
                if (lastMonth > 6 && month < 6)
                {
                    currentYear++;
                }
            }

            lastMonth = month;
            
            try {
                date = new DateTime(currentYear, month, day);
                return true;
            } catch { return false; }
        }
    }
}