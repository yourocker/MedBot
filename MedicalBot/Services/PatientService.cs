using System.Text;
using MedicalBot.Data;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Services
{
    public class SearchResult
    {
        public string Message { get; set; } = "";
        public int Count { get; set; }
        public bool IsTooMany { get; set; }
    }

    public class PatientService
    {
        private const int MaxAutoShowResults = 15;

        public SearchResult Search(string query, bool forceShowAll)
        {
            string searchKey = query.Replace(" ", "").ToUpper();

            using var db = new AppDbContextFactory().CreateDbContext(null);
            
            // Загружаем пациентов И их визиты
            var patients = db.Patients
                .Include(p => p.Visits)
                .Where(p => p.NormalizedName.Contains(searchKey))
                .ToList();

            // Считаем визиты
            int totalVisitsCount = patients.Sum(p => p.Visits.Count);

            if (totalVisitsCount == 0) return new SearchResult { Message = "Ничего не найдено в базе данных." };

            if (!forceShowAll && totalVisitsCount > MaxAutoShowResults)
            {
                return new SearchResult { Count = totalVisitsCount, IsTooMany = true };
            }

            StringBuilder sb = new StringBuilder();
            decimal totalGlobalSum = 0;

            foreach (var p in patients)
            {
                foreach (var v in p.Visits.OrderByDescending(v => v.Date))
                {
                    totalGlobalSum += v.TotalCost; // 👈 БЫЛО Cost, СТАЛО TotalCost
                    
                    sb.AppendLine($"👤 {p.FullName}");
                    sb.AppendLine($"📅 {v.Date:dd.MM.yyyy}");
                    sb.AppendLine($"🏥 {v.ServiceName}");
                    sb.AppendLine($"💰 {v.TotalCost:N0} руб."); // 👈 БЫЛО Cost, СТАЛО TotalCost
                    sb.AppendLine("➖➖➖➖➖➖");
                }
            }

            var header = $"🔎 Найдено записей: {totalVisitsCount} (Пациентов: {patients.Count})\n💰 Всего оплачено: {totalGlobalSum:N0} руб.\n\n";
            string finalMsg = header + sb.ToString();

            if (finalMsg.Length > 4000) 
                finalMsg = finalMsg.Substring(0, 4000) + "\n\n...(список обрезан)...";

            return new SearchResult { Message = finalMsg, Count = totalVisitsCount, IsTooMany = false };
        }
    }
}