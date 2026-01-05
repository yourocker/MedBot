using MedicalBot.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers
{
    public class PatientsController : Controller
    {
        private readonly AppDbContext _context;

        public PatientsController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Загружаем ВСЕХ пациентов для таблицы
            // Сортировку и пагинацию сделает интерфейс
            var patients = await _context.Patients
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return View(patients);
        }
        
        // Исправили тип id на Guid, как в твоей базе
        public async Task<IActionResult> Details(Guid id)
        {
            var patient = await _context.Patients.FindAsync(id);
            if (patient == null) return NotFound();

            var visits = await _context.Visits
                .Where(v => v.PatientId == id)
                .OrderByDescending(v => v.Date)
                .ToListAsync();

            ViewBag.Visits = visits;

            return View(patient);
        }
    }
}