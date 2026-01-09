using MedicalBot.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers
{
    // Этот контроллер будет обрабатывать пути вида /Data/SomeEntity
    public class DataController : BasePlatformController
    {
        public DataController(AppDbContext context) : base(context)
        {
        }

        // Главный метод реестра
        [Route("Data/{entityCode}")]
        public async Task<IActionResult> Index(string entityCode)
        {
            // Находим определение сущности в базе
            var definition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(a => a.EntityCode == entityCode);

            if (definition == null) return NotFound();

            ViewBag.Definition = definition;
            ViewBag.EntityCode = entityCode;

            // В будущем здесь мы будем подгружать данные из GenericObjects
            // Пока просто возвращаем пустой список для теста
            return View();
        }
    }
}