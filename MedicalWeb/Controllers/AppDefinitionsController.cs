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
                .Include(a => a.Category) // Подгружаем раздел для связи
                .OrderByDescending(a => a.IsSystem)
                .ToListAsync();
            return View(apps);
        }

        // GET: AppDefinitions/Create
        public async Task<IActionResult> Create()
        {
            // Используем вспомогательный метод для загрузки категорий
            await PopulateCategoriesViewBag();

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
                // Проверяем уникальность системного кода
                if (await _context.AppDefinitions.AnyAsync(a => a.EntityCode == appDefinition.EntityCode))
                {
                    ModelState.AddModelError("EntityCode", "Сущность с таким кодом уже существует.");
                    await PopulateCategoriesViewBag();
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

        // GET: AppDefinitions/Edit/5
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var appDefinition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (appDefinition == null) return NotFound();

            // Загружаем категории для выпадающего списка
            await PopulateCategoriesViewBag(appDefinition.AppCategoryId);
            
            return View(appDefinition);
        }

        // POST: AppDefinitions/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, [Bind("Id,Name,EntityCode,Description,Icon,IsSystem,AppCategoryId")] AppDefinition appDefinition)
        {
            if (id != appDefinition.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(appDefinition);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AppDefinitionExists(appDefinition.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            await PopulateCategoriesViewBag(appDefinition.AppCategoryId);
            return View(appDefinition);
        }

        // Вспомогательный метод для заполнения списка категорий
        private async Task PopulateCategoriesViewBag(Guid? selectedId = null)
        {
            var categories = await _context.AppCategories.OrderBy(c => c.SortOrder).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", selectedId);
        }

        private bool AppDefinitionExists(Guid id)
        {
            return _context.AppDefinitions.Any(e => e.Id == id);
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
        // GET: AppDefinitions/Delete/id
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var definition = await _context.AppDefinitions
                .Include(a => a.Category)
                .FirstOrDefaultAsync(m => m.Id == id);
        
            if (definition == null) return NotFound();

            return View(definition);
        }

// POST: AppDefinitions/Delete/id
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var definition = await _context.AppDefinitions.FindAsync(id);
            if (definition != null)
            {
                // Удаляем саму сущность (каскадное удаление полей должно быть в БД, 
                // либо добавим здесь, если нужно)
                _context.AppDefinitions.Remove(definition);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}