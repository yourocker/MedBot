using MedicalBot.Entities.Platform;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(AppDbContext context)
        {
            // 1. Инициализация категорий (Разделов меню), если их еще нет
            if (!await context.AppCategories.AnyAsync())
            {
                var categories = new List<AppCategory>
                {
                    new AppCategory { Id = Guid.NewGuid(), Name = "Компания", Icon = "building", SortOrder = 1 },
                    new AppCategory { Id = Guid.NewGuid(), Name = "Пациенты", Icon = "people", SortOrder = 2 },
                    new AppCategory { Id = Guid.NewGuid(), Name = "Справочники", Icon = "book", SortOrder = 3 },
                    new AppCategory { Id = Guid.NewGuid(), Name = "Конструктор", Icon = "tools", SortOrder = 4 }
                };

                context.AppCategories.AddRange(categories);
                await context.SaveChangesAsync();
            }

            // 2. Проверяем, есть ли уже определения сущностей. Если есть — выходим.
            if (await context.AppDefinitions.AnyAsync()) return;

            // Получаем ID категорий для связи
            var catCompany = await context.AppCategories.FirstAsync(c => c.Name == "Компания");
            var catPatients = await context.AppCategories.FirstAsync(c => c.Name == "Пациенты");

            var definitions = new List<AppDefinition>
            {
                new AppDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Сотрудники",
                    EntityCode = "Employee",
                    IsSystem = true,
                    Icon = "person-badge",
                    AppCategoryId = catCompany.Id // Привязка к разделу "Компания"
                },
                new AppDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Пациенты",
                    EntityCode = "Patient",
                    IsSystem = true,
                    Icon = "person-heart",
                    AppCategoryId = catPatients.Id // Привязка к разделу "Пациенты"
                },
                new AppDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Должности",
                    EntityCode = "Position",
                    IsSystem = true,
                    Icon = "briefcase",
                    AppCategoryId = catCompany.Id // Привязка к разделу "Компания"
                },
                new AppDefinition
                {
                    Id = Guid.NewGuid(),
                    Name = "Подразделения",
                    EntityCode = "Department",
                    IsSystem = true,
                    Icon = "diagram-3",
                    AppCategoryId = catCompany.Id // Привязка к разделу "Компания"
                }
            };

            context.AppDefinitions.AddRange(definitions);
            await context.SaveChangesAsync();
        }
    }
}