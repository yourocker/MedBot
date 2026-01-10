using System;
using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting; 

namespace MedicalWeb.Controllers
{
    public class PatientsController : BasePlatformController
    {
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
            
            // 1. Сохраняем файлы во временную папку (Temp)
            await SaveDynamicProperties(obj, form, "patient");
            
            if (ModelState.IsValid)
            {
                // 2. Присваиваем ID и сохраняем запись (чтобы получить валидную запись в БД)
                obj.Id = Guid.NewGuid();
                obj.CreatedAt = DateTime.UtcNow;
                _context.GenericObjects.Add(obj);
                await _context.SaveChangesAsync();

                // 3. ПЕРЕМЕЩАЕМ файлы из Temp в постоянную папку /uploads/patient/{ID}/
                FinalizeDynamicFilePaths(obj, "patient", obj.Id.ToString());

                // 4. Сохраняем обновленные пути в БД
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