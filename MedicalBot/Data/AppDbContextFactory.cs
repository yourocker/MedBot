using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedicalBot.Data
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // 1. Получаем путь к папке, где лежит исполняемый файл
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            
            string connectionString;
            
            // 2. Если файла нет (мы в Rider), используем локальные настройки
            if (!File.Exists(configPath))
            {
                connectionString = "Host=localhost;Port=5433;Database=medical_db;Username=postgres;Password=postgres";
            }
            else
            {
                // 3. Если файл есть (мы на сервере), читаем настройки из него
                string jsonString = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(jsonString);
                connectionString = doc.RootElement
                    .GetProperty("ConnectionStrings")
                    .GetProperty("DefaultConnection")
                    .GetString();
            }

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}