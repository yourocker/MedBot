using System;
using System.Linq;
using System.Threading.Tasks;
using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting; // ДОБАВЛЕНО: Для IWebHostEnvironment

namespace MedicalWeb.Controllers
{
    public class AppCategoriesController : BasePlatformController
    {
        // ИСПРАВЛЕНО: Конструктор теперь принимает два параметра и передает их родителю
        public AppCategoriesController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.AppCategories
                .OrderBy(c => c.SortOrder)
                .ToListAsync();
            return View(categories);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AppCategory category)
        {
            if (ModelState.IsValid)
            {
                category.Id = Guid.NewGuid();
                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var category = await _context.AppCategories.FindAsync(id);
            if (category == null) return NotFound();

            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, AppCategory category)
        {
            if (id != category.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(category);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CategoryExists(category.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(category);
        }

        // Удаление раздела
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var category = await _context.AppCategories
                .Include(c => c.Apps)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (category == null) return NotFound();

            // Проверка: нельзя удалить раздел, если в нем есть сущности
            if (category.Apps.Any())
            {
                ViewBag.CanDelete = false;
                ViewBag.Reason = "В данном разделе есть привязанные сущности. Сначала переместите их в другой раздел.";
            }
            else
            {
                ViewBag.CanDelete = true;
            }

            return View(category);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var category = await _context.AppCategories.FindAsync(id);
            if (category != null)
            {
                _context.AppCategories.Remove(category);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool CategoryExists(Guid id) => _context.AppCategories.Any(e => e.Id == id);
    }
}