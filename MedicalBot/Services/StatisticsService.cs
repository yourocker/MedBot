using System.Text;
using MedicalBot.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Services
{
    public class StatisticsService
    {
        public string GetPeriodReport(DateTime startDate, DateTime endDate)
        {
            using var db = new AppDbContextFactory().CreateDbContext(null);

            var startUtc = startDate.ToUniversalTime();
            // Конец дня
            var endUtc = endDate.AddDays(1).Date.ToUniversalTime(); 

            var visitsInPeriod = db.Visits
                .Where(v => v.Date >= startUtc && v.Date < endUtc)
                .ToList();

            if (!visitsInPeriod.Any())
            {
                return $"📉 За период с {startDate:dd.MM} по {endDate:dd.MM} данных нет.";
            }

            // 👇 НОВАЯ ЛОГИКА ПОДСЧЕТА
            decimal totalRevenue = visitsInPeriod.Sum(v => v.TotalCost); // Общая (Нал + Безнал)
            decimal totalCash = visitsInPeriod.Sum(v => v.AmountCash);   // Только Наличные
            decimal totalCashless = visitsInPeriod.Sum(v => v.AmountCashless); // Только Безнал

            int visitsCount = visitsInPeriod.Count;
            int uniquePatients = visitsInPeriod.Select(v => v.PatientId).Distinct().Count();

            var sb = new StringBuilder();
            sb.AppendLine($"📊 **Финансовый отчет**");
            sb.AppendLine($"📅 Период: {startDate:dd.MM.yyyy} — {endDate:dd.MM.yyyy}");
            sb.AppendLine("➖➖➖➖➖➖➖➖");
            
            // 👇 ТЕПЕРЬ ВЫВОДИМ ДЕТАЛИЗАЦИЮ
            sb.AppendLine($"💰 **ИТОГО: {totalRevenue:N0} руб.**");
            sb.AppendLine($"💵 Наличные: {totalCash:N0} руб.");
            sb.AppendLine($"💳 Безнал: {totalCashless:N0} руб.");
            sb.AppendLine("➖➖➖➖➖➖➖➖");
            
            sb.AppendLine($"👥 Пациентов: {uniquePatients}");
            sb.AppendLine($"🧾 Визитов (чеков): {visitsCount}");
            
            return sb.ToString();
        }
    }
}