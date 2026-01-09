using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MedicalWeb.Controllers
{
    public class AppDefinitionsController : Controller
    {
        private readonly AppDbContext _context;

        public AppDefinitionsController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Список всех сущностей (подгружаем и категории для отображения)
        public async Task<IActionResult> Index()
        {
            var apps = await _context.AppDefinitions
                .Include(a => a.Fields)
                .Include(a => a.Category) // Важно: подгружаем раздел для связи
                .OrderByDescending(a => a.IsSystem)
                .ToListAsync();
            return View(apps);
        }

        // GET: AppDefinitions/Create
        public async Task<IActionResult> Create()
        {
            // Загружаем разделы для выпадающего списка
            var categories = await _context.AppCategories
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name");

            // Инициализируем модель (иконка по умолчанию "gear")
            return View(new AppDefinition { Icon = "gear", IsSystem = false });
        }

        // POST: AppDefinitions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,EntityCode,Description,Icon,IsSystem,AppCategoryId")] AppDefinition appDefinition)
        {
            if (ModelState.IsValid)
            {
                // Логика: проверяем уникальность системного кода
                if (await _context.AppDefinitions.AnyAsync(a => a.EntityCode == appDefinition.EntityCode))
                {
                    ModelState.AddModelError("EntityCode", "Сущность с таким кодом уже существует.");
                    await PopulateCategoriesViewBag(); // Перезаполняем список при ошибке
                    return View(appDefinition);
                }

                appDefinition.Id = Guid.NewGuid();
                _context.Add(appDefinition);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            await PopulateCategoriesViewBag();
            return View(appDefinition);
        }

        // Вспомогательный метод для заполнения списка категорий (чтобы не дублировать код)
        private async Task PopulateCategoriesViewBag()
        {
            var categories = await _context.AppCategories.OrderBy(c => c.SortOrder).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name");
        }

        // 2. Список полей конкретной сущности
        public async Task<IActionResult> Fields(Guid id)
        {
            var app = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (app == null) return NotFound();

            return View(app);
        }

        // 3. POST: Добавление нового поля
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddField(Guid appDefinitionId, string label, string systemName, FieldDataType dataType, bool isRequired)
        {
            if (string.IsNullOrEmpty(systemName))
            {
                return BadRequest("Системное имя обязательно");
            }

            var field = new AppFieldDefinition
            {
                Id = Guid.NewGuid(),
                AppDefinitionId = appDefinitionId,
                Label = label,
                SystemName = systemName.ToLower().Trim(),
                DataType = dataType,
                IsRequired = isRequired,
                SortOrder = await _context.AppFieldDefinitions
                    .Where(f => f.AppDefinitionId == appDefinitionId)
                    .CountAsync() + 1
            };

            _context.AppFieldDefinitions.Add(field);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Fields), new { id = appDefinitionId });
        }

        // 4. POST: Удаление поля
        [HttpPost]
        public async Task<IActionResult> DeleteField(Guid id)
        {
            var field = await _context.AppFieldDefinitions.FindAsync(id);
            if (field != null)
            {
                var appId = field.AppDefinitionId;
                _context.AppFieldDefinitions.Remove(field);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Fields), new { id = appId });
            }
            return NotFound();
        }
    }
}