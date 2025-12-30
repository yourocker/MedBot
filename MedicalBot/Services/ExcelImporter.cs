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
            sb.AppendLine("🚀 Начинаю ХРОНОЛОГИЧЕСКИЙ импорт...");

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var result = reader.AsDataSet();

            // Кэши
            var existingPatients = await db.Patients.ToDictionaryAsync(p => p.NormalizedName, p => p);
            
            var existingVisits = await db.Visits
                .Select(v => new { v.PatientId, v.Date, v.ServiceName, v.TotalCost })
                .ToListAsync();

            var visitSignatures = new HashSet<string>();
            foreach (var v in existingVisits)
            {
                string key = $"{v.PatientId}_{v.Date:yyyyMMdd}_{v.ServiceName}_{v.TotalCost}";
                visitSignatures.Add(key);
            }

            int newVisitsCount = 0;
            
            // === ЛОГИКА КАЛЕНДАРЯ ===
            int currentYear = 2023; // Стартуем с 2023 года (по твоему указанию)
            int lastMonth = 0;      // Чтобы отслеживать переход через Новый Год

            foreach (DataTable table in result.Tables)
            {
                string tabName = table.TableName.Trim();

                // 1. Попытка определить дату листа
                if (!TryParseSheetDate(tabName, ref currentYear, ref lastMonth, out DateTime sheetDate))
                {
                    // Если это не дата (например "ВСК", "ЗЕНИТ") - пропускаем
                    continue; 
                }

                // Переводим в UTC для базы
                sheetDate = sheetDate.ToUniversalTime();
                var visitsToAdd = new List<Visit>();

                // Начинаем со 2-й строки
                for (int i = 2; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    if (row.ItemArray.Length == 0) continue;

                    // Определение сдвига (если есть колонка №)
                    int colOffset = 0;
                    string col0 = row[0]?.ToString()?.Trim();
                    if (int.TryParse(col0, out _)) colOffset = 1;

                    // Чтение ФИО
                    if (table.Columns.Count <= 0 + colOffset) continue;
                    string fioRaw = row[0 + colOffset]?.ToString()?.Trim();
                    
                    if (string.IsNullOrWhiteSpace(fioRaw) || 
                        fioRaw.ToLower().Contains("итого") || 
                        fioRaw.ToLower().Contains("всего")) continue;

                    string normalizedName = fioRaw.ToUpper().Replace(" ", "").Replace(".", "");

                    // Пациент
                    if (!existingPatients.TryGetValue(normalizedName, out Patient patient))
                    {
                        patient = new Patient { Id = Guid.NewGuid(), FullName = fioRaw, NormalizedName = normalizedName };
                        existingPatients[normalizedName] = patient;
                        db.Patients.Add(patient);
                    }

                    // Услуга
                    string serviceRaw = "-";
                    if (table.Columns.Count > 1 + colOffset) 
                        serviceRaw = row[1 + colOffset]?.ToString() ?? "-";
                    string service = serviceRaw.Trim();
                    if (string.IsNullOrEmpty(service)) service = "не указано";

                    // Деньги
                    decimal cash = 0;     
                    decimal cashless = 0; 

                    // Нал (Колонка 2 + сдвиг)
                    if (table.Columns.Count > 2 + colOffset)
                    {
                        string val = row[2 + colOffset]?.ToString()?.Trim().Replace(".", ",") ?? "0";
                        val = Regex.Match(val, @"[\d,]+").Value; 
                        if (decimal.TryParse(val, out decimal c1)) cash = c1;
                    }

                    // Безнал (Колонка 3 + сдвиг)
                    if (table.Columns.Count > 3 + colOffset)
                    {
                        string val = row[3 + colOffset]?.ToString()?.Trim().Replace(".", ",") ?? "0";
                        val = Regex.Match(val, @"[\d,]+").Value;
                        if (decimal.TryParse(val, out decimal c2)) cashless = c2;
                    }

                    decimal totalCost = cash + cashless;
                    if (totalCost == 0) continue;

                    // Проверка дубля
                    string currentSignature = $"{patient.Id}_{sheetDate:yyyyMMdd}_{service}_{totalCost}";

                    if (visitSignatures.Contains(currentSignature))
                    {
                        continue;
                    }

                    var visit = new Visit
                    {
                        Id = Guid.NewGuid(),
                        Date = sheetDate,
                        Patient = patient,
                        ServiceName = service,
                        AmountCash = cash,
                        AmountCashless = cashless,
                        TotalCost = totalCost
                    };

                    visitsToAdd.Add(visit);
                    visitSignatures.Add(currentSignature);
                    newVisitsCount++;
                }

                if (visitsToAdd.Any())
                {
                    db.Visits.AddRange(visitsToAdd);
                }
            }

            await db.SaveChangesAsync();
            
            sb.AppendLine($"✅ Импорт завершен.");
            sb.AppendLine($"📅 Текущий год в обработке дошел до: {currentYear}");
            sb.AppendLine($"➕ Добавлено новых записей: {newVisitsCount}");
            
            Console.WriteLine(sb.ToString());
            return sb.ToString();
        }

        // Умный парсер даты, который следит за сменой года
        private bool TryParseSheetDate(string tabName, ref int currentYear, ref int lastMonth, out DateTime date)
        {
            date = DateTime.MinValue;
            
            // 1. Чистим имя от мусора (например "01.11 (1)" -> "01.11")
            // Ищем что-то похожее на дату
            var match = Regex.Match(tabName, @"(\d{1,2})[\.\-\/](\d{1,2})([\.\-\/](\d{2,4}))?");
            
            if (!match.Success) return false; // Не похоже на дату (например "ЗЕНИТ")

            int day = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);
            
            // Если есть явный год в названии (например 01.01.2025)
            if (match.Groups[4].Success)
            {
                string yearStr = match.Groups[4].Value;
                int explicitYear = int.Parse(yearStr);
                if (explicitYear < 100) explicitYear += 2000; // 24 -> 2024
                
                currentYear = explicitYear; // Обновляем текущий год
                lastMonth = month;
                
                try {
                    date = new DateTime(currentYear, month, day);
                    return true;
                } catch { return false; }
            }

            // Если года НЕТ (формат 14.06), используем умную логику
            // Если месяц стал МЕНЬШЕ предыдущего (был 12, стал 01) -> Новый год
            if (lastMonth != 0 && month < lastMonth)
            {
                // Проверка на здравый смысл: если разница небольшая (май -> апрель), это может быть ошибка сортировки листов,
                // но если разница большая (декабрь -> январь), то это точно новый год.
                // Будем считать переход только если мы были во второй половине года, а стали в первой.
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