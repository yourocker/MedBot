using MedicalBot.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers
{
    public class DataController : BasePlatformController
    {
        public DataController(AppDbContext context) : base(context)
        {
        }

        [Route("Data/{entityCode}")]
        public async Task<IActionResult> Index(string entityCode)
        {
            var definition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(a => a.EntityCode == entityCode);

            if (definition == null) return NotFound();

            ViewBag.Definition = definition;
            ViewBag.EntityCode = entityCode;

            return View();
        }

        public async Task<IActionResult> Create(string entityCode)
        {
            if (string.IsNullOrEmpty(entityCode)) return NotFound();

            var definition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(a => a.EntityCode == entityCode);

            if (definition == null) return NotFound();

            // Передаем объект как модель, чтобы View его видел
            return View(definition);
        }

        // Заглушка метода сохранения, чтобы View не выдавал ошибку
        [HttpPost]
        public async Task<IActionResult> Save(string entityCode, Dictionary<string, string> values)
        {
            // Логику сохранения напишем следующим шагом
            return RedirectToAction("Index", new { entityCode });
        }
    }
}