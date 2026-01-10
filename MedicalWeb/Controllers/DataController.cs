using System.Linq;
using System.Threading.Tasks;
using MedicalBot.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting; // ДОБАВЛЕНО: Для IWebHostEnvironment

namespace MedicalWeb.Controllers
{
    public class DataController : BasePlatformController
    {
        // ИСПРАВЛЕНО: Конструктор теперь принимает два параметра и передает их родителю
        public DataController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        [Route("Data/{entityCode}")]
        public async Task<IActionResult> Index(string entityCode)
        {
            var definition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(a => a.EntityCode == entityCode);

            if (definition == null) return NotFound();

            // Загружаем данные
            var objects = await _context.GenericObjects
                .Where(o => o.EntityCode == entityCode)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.Definition = definition;
            
            return View("~/Views/GenericObjects/Index.cshtml", objects);
        }
    }
}