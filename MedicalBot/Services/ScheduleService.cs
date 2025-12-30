using System;
using System.Linq;
using System.Text;
using MedicalBot.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Services
{
    public class ScheduleService
    {
        private readonly string _connectionString;

        // Конструктор без параметров (для простоты инициализации в Program.cs)
        public ScheduleService()
        {
            // Получаем строку подключения так же, как в Program.cs (через чтение конфига или хардкод для теста)
            // Но чтобы не усложнять, мы будем передавать строку при создании, 
            // либо читать её из Program.ConnectionString. 
        }

        public string GetDailyReport(DateTime date, string connectionString)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📅 **Расписание на {date:dd.MM.yyyy}**");
            sb.AppendLine("➖➖➖➖➖➖➖➖");

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using (var db = new AppDbContext(optionsBuilder.Options))
            {
                // 1. Берем все записи на эту дату
                // Важно: сравниваем только Date, игнорируем время
                var appointments = db.Appointments
                    .Where(a => a.DateAndTime.Date == date.Date)
                    .OrderBy(a => a.DoctorName)     // Сначала сортируем по врачам
                    .ThenBy(a => a.DateAndTime)     // Потом по времени
                    .ToList();

                if (!appointments.Any())
                {
                    return $"📅 На {date:dd.MM.yyyy} записей не найдено.";
                }

                // 2. Группируем по врачам
                var grouped = appointments.GroupBy(a => a.DoctorName);

                foreach (var group in grouped)
                {
                    string doctorName = group.Key;
                    if (string.IsNullOrEmpty(doctorName)) doctorName = "Без врача";

                    sb.AppendLine($"👨‍⚕️ **{doctorName}**");

                    foreach (var item in group)
                    {
                        string time = item.DateAndTime.ToString("HH:mm");
                        string patient = item.PatientName ?? "Не указано";
                        
                        // Очистка имени пациента от лишних пробелов
                        patient = patient.Replace("\n", " ").Replace("\r", "").Trim();
                        
                        // Если есть телефон, добавляем иконку
                        string phoneInfo = !string.IsNullOrEmpty(item.PhoneNumber) ? $" 📞 {item.PhoneNumber}" : "";

                        sb.AppendLine($"   🕒 `{time}` — {patient}{phoneInfo}");
                    }
                    sb.AppendLine(); // Пустая строка между врачами
                }
            }

            return sb.ToString();
        }
    }
}