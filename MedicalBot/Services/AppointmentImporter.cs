using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http; 
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Data; 
using ExcelDataReader;
using MedicalBot.Data;
using MedicalBot.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Services
{
    public class AppointmentImporter
    {
        private readonly string _connectionString;

        public AppointmentImporter(string connectionString)
        {
            _connectionString = connectionString;
        }

        // Метод 1: Скачивает файл по ссылке и запускает импорт
        public async Task<string> ImportFromUrlAsync(string fileUrl)
        {
            var sb = new StringBuilder();
            string tempFilePath = Path.GetTempFileName() + ".xlsx";

            try
            {
                sb.AppendLine("🌐 Скачиваю файл расписания...");
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                    var response = await client.GetAsync(fileUrl);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        return $"❌ Ошибка скачивания: {response.StatusCode}";
                    }

                    using (var fileStream = File.Create(tempFilePath))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                sb.AppendLine("✅ Файл скачан. Начинаю разбор...");
                sb.Append(await ImportAsync(tempFilePath));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ Ошибка загрузки: {ex.Message}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"🔍 Подробности: {ex.InnerException.Message}");
                }
            }
            finally
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }

            return sb.ToString();
        }

        // Метод 2: Основная логика разбора Excel
        public async Task<string> ImportAsync(string filePath)
        {
            var sb = new StringBuilder();
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(_connectionString);
            using var db = new AppDbContext(optionsBuilder.Options);

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var result = reader.AsDataSet();

            int addedCount = 0;
            int currentYear = DateTime.Now.Year;

            // Если нужно чистить таблицу перед каждым импортом, раскомментируй:
            // db.Appointments.RemoveRange(db.Appointments);
            // await db.SaveChangesAsync();

            foreach (DataTable table in result.Tables)
            {
                DateTime? blockDate = null;
                Dictionary<int, string> columnDoctors = new Dictionary<int, string>();

                for (int r = 0; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string firstCell = row[0]?.ToString()?.Trim();
                    
                    if (string.IsNullOrEmpty(firstCell)) continue;

                    // А. ПРОВЕРКА НА ДАТУ (Заголовок блока)
                    if (IsDateHeader(firstCell, currentYear, out DateTime foundDate))
                    {
                        blockDate = foundDate;
                        columnDoctors.Clear(); 
                        
                        // Ищем врачей в ближайших строках
                        for (int subR = 1; subR <= 3; subR++)
                        {
                            if (r + subR < table.Rows.Count)
                            {
                                FindDoctorsInRow(table.Rows[r + subR], columnDoctors);
                                if (columnDoctors.Count > 0) break;
                            }
                        }
                        continue;
                    }

                    // Б. ПРОВЕРКА НА ВРЕМЯ (Строка с записью)
                    if (blockDate.HasValue && columnDoctors.Count > 0 && IsTimeCell(firstCell, out TimeSpan time))
                    {
                        foreach (var kvp in columnDoctors)
                        {
                            int colIndex = kvp.Key;
                            string doctorName = kvp.Value;

                            if (colIndex >= table.Columns.Count) continue;

                            string cellContent = row[colIndex]?.ToString()?.Trim();
                            
                            // Фильтры мусора
                            if (string.IsNullOrWhiteSpace(cellContent)) continue;
                            if (cellContent.Contains("\\") || cellContent.Contains("//")) continue;
                            if (cellContent.Length < 2) continue;

                            // Обрезаем, если очень длинно
                            if (cellContent.Length > 250) cellContent = cellContent.Substring(0, 250);

                            DateTime finalDate = blockDate.Value.Add(time).ToUniversalTime();
                            if (finalDate.Year < 2000 || finalDate.Year > 2100) continue; 

                            // Ищем телефон
                            string phone = "";
                            if (colIndex + 1 < table.Columns.Count)
                            {
                                string nextCell = row[colIndex + 1]?.ToString()?.Trim();
                                if (IsPhoneNumber(nextCell)) phone = nextCell;
                            }
                            if (string.IsNullOrEmpty(phone))
                            {
                                phone = ExtractPhone(cellContent);
                            }

                            // 👇 ИСПРАВЛЕНИЕ: Заполняем поле Procedure пустой строкой, чтобы БД не ругалась
                            var appt = new Appointment
                            {
                                Id = Guid.NewGuid(),
                                DateAndTime = finalDate,
                                DoctorName = doctorName ?? "Неизвестно",
                                PatientName = cellContent, 
                                Procedure = "", // <--- ВОТ ТУТ БЫЛА ОШИБКА. Теперь тут пустота, а не NULL.
                                PhoneNumber = phone ?? "", 
                                SourceFile = table.TableName
                            };
                            
                            db.Appointments.Add(appt);
                            addedCount++;
                        }
                    }
                }
            }

            try
            {
                await db.SaveChangesAsync();
                sb.AppendLine($"✅ Журнал обработан.");
                sb.AppendLine($"📅 Добавлено записей: {addedCount}");
            }
            catch (Exception ex)
            {
                // Вывод деталей ошибки
                var inner = ex.InnerException?.Message ?? "";
                sb.AppendLine($"❌ ОШИБКА СОХРАНЕНИЯ: {ex.Message}");
                if(!string.IsNullOrEmpty(inner)) sb.AppendLine($"🔍 ДЕТАЛИ: {inner}");
            }

            Console.WriteLine(sb.ToString());
            return sb.ToString();
        }

        // --- Хелперы ---

        private bool IsDateHeader(string text, int year, out DateTime date)
        {
            date = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text)) return false;
            
            // Ищем ДД.ММ
            var match = Regex.Match(text, @"(\d{1,2})[\.\/](\d{1,2})");
            if (!match.Success) return false;
            
            if (text.Contains(":")) return false; 

            int day = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);

            if (day > 31 || month > 12 || day == 0 || month == 0) return false;

            try {
                date = new DateTime(year, month, day);
                return true;
            } catch { return false; }
        }

        private bool IsTimeCell(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (TimeSpan.TryParse(text, out time)) return true;
            
            var match = Regex.Match(text, @"^(\d{1,2})[:\-\.](\d{2})");
            if (match.Success)
            {
                int h = int.Parse(match.Groups[1].Value);
                int m = int.Parse(match.Groups[2].Value);
                time = new TimeSpan(h, m, 0);
                return true;
            }
            return false;
        }

        private void FindDoctorsInRow(DataRow row, Dictionary<int, string> map)
        {
            // Ключевые слова для поиска колонок с врачами
            string[] doctors = { "Чернов", "Кузьминых", "Перминов", "Анна Константиновна", "Лейсан", "Екатерина", "Дмитрий" };

            for (int c = 0; c < row.ItemArray.Length; c++)
            {
                string val = row[c]?.ToString()?.Trim();
                if (string.IsNullOrEmpty(val)) continue;

                foreach (var doc in doctors)
                {
                    if (val.IndexOf(doc, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        map[c] = val; 
                        break;
                    }
                }
            }
        }

        private bool IsPhoneNumber(string text)
        {
             return !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"\d{10}");
        }

        private string ExtractPhone(string text)
        {
             var match = Regex.Match(text, @"[78]\d{10}");
             return match.Success ? match.Value : "";
        }
    }
}