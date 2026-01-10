using System;
using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting; // Для доступа к путям файлов

namespace MedicalWeb.Controllers
{
    public class PatientsController : BasePlatformController
    {
        // Исправленный конструктор
        public PatientsController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        public async Task<IActionResult> Index()
        {
            var patients = await _context.Patients
                .OrderBy(p => p.FullName)
                .ToListAsync();

            return View(patients);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Используем стандартизированный метод загрузки полей
            await LoadDynamicFields("patient");
            
            var definition = await _context.AppDefinitions
                .Include(a => a.Fields)
                .FirstOrDefaultAsync(a => a.EntityCode.ToLower() == "patient");

            ViewBag.Definition = definition;
            ViewBag.EntityCode = "patient";

            return View("~/Views/GenericObjects/Create.cshtml", new GenericObject());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(GenericObject obj, IFormCollection form)
        {
            obj.EntityCode = "patient";
            
            // Вызываем асинхронный метод из базового контроллера для сохранения данных и файлов
            await SaveDynamicProperties(obj, form, "patient");
            
            if (ModelState.IsValid)
            {
                obj.Id = Guid.NewGuid();
                obj.CreatedAt = DateTime.UtcNow;
                _context.GenericObjects.Add(obj);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            
            await LoadDynamicFields("patient");
            ViewBag.EntityCode = "patient";
            return View("~/Views/GenericObjects/Create.cshtml", obj);
        }
        
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