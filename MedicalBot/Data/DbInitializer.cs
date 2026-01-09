using MedicalBot.Entities.Platform;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(AppDbContext context)
        {
            // --- ШАГ 1: Инициализация разделов меню (AppCategories) ---
            // Эти записи определяют заголовки в левом меню (Компания, Пациенты и т.д.)
            var categories = new List<AppCategory>
            {
                new AppCategory { Id = Guid.Parse("a1111111-1111-1111-1111-111111111111"), Name = "Компания", Icon = "building", SortOrder = 1, IsSystem = true },
                new AppCategory { Id = Guid.Parse("b2222222-2222-2222-2222-222222222222"), Name = "Пациенты", Icon = "people", SortOrder = 2, IsSystem = true },
                new AppCategory { Id = Guid.Parse("c3333333-3333-3333-3333-333333333333"), Name = "Конструктор", Icon = "gear", SortOrder = 3, IsSystem = true }
            };

            foreach (var cat in categories)
            {
                var existingCat = await context.AppCategories.FirstOrDefaultAsync(x => x.Name == cat.Name);
                if (existingCat == null)
                {
                    context.AppCategories.Add(cat);
                }
                else
                {
                    // Гарантируем, что разделы помечены как системные и имеют верные иконки
                    existingCat.IsSystem = true;
                    existingCat.Icon = cat.Icon;
                    existingCat.SortOrder = cat.SortOrder;
                }
            }
            await context.SaveChangesAsync();

            // --- ШАГ 2: Инициализация системных сущностей (AppDefinitions) ---
            // Это описание таблиц, к которым вы сможете добавлять свои поля через шестеренку.
            
            var catCompany = await context.AppCategories.FirstOrDefaultAsync(c => c.Name == "Компания");
            var catPatients = await context.AppCategories.FirstOrDefaultAsync(c => c.Name == "Пациенты");

            var systemApps = new List<AppDefinition>
            {
                new AppDefinition { Name = "Сотрудники", EntityCode = "Employee", Icon = "person-badge", IsSystem = true, AppCategoryId = catCompany?.Id },
                new AppDefinition { Name = "Пациенты", EntityCode = "Patient", Icon = "person-heart", IsSystem = true, AppCategoryId = catPatients?.Id },
                new AppDefinition { Name = "Должности", EntityCode = "Position", Icon = "briefcase", IsSystem = true, AppCategoryId = catCompany?.Id },
                new AppDefinition { Name = "Подразделения", EntityCode = "Department", Icon = "diagram-3", IsSystem = true, AppCategoryId = catCompany?.Id }
            };

            foreach (var app in systemApps)
            {
                var existingApp = await context.AppDefinitions.FirstOrDefaultAsync(a => a.EntityCode == app.EntityCode);
                if (existingApp == null)
                {
                    app.Id = Guid.NewGuid();
                    context.AppDefinitions.Add(app);
                }
                else
                {
                    // Если сущность уже была в базе как "пользовательская" (IsSystem = false),
                    // мы принудительно делаем её системной, чтобы убрать кнопки редактирования/удаления.
                    existingApp.IsSystem = true; 
                    existingApp.Name = app.Name;
                    existingApp.Icon = app.Icon;
                    existingApp.AppCategoryId = app.AppCategoryId;
                }
            }

            await context.SaveChangesAsync();
        }
    }
}