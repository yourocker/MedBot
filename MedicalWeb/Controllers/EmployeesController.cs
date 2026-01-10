using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MedicalBot.Data;
using MedicalBot.Entities.Company;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace MedicalWeb.Controllers
{
    public class EmployeesController : BasePlatformController
    {
        public EmployeesController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        // --- СПИСОК СОТРУДНИКОВ ---
        public async Task<IActionResult> Index()
        {
            var employees = await _context.Employees
                .Include(e => e.StaffAppointments)
                .ThenInclude(a => a.Position)
                .Include(e => e.StaffAppointments)
                .ThenInclude(a => a.Department)
                .OrderBy(e => e.IsDismissed) 
                .ThenBy(e => e.LastName)
                .ToListAsync();
            return View(employees);
        }

        // --- СОЗДАНИЕ (GET) ---
        public async Task<IActionResult> Create()
        {
            await LoadDynamicFields("Employee"); 
            
            ViewBag.Positions = await _context.Positions.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
            return View();
        }

        // --- СОЗДАНИЕ (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee, Guid[] selectedPositions, Guid[] selectedDepartments, string[] Phones, string[] Emails, IFormCollection form)
        {
            employee.Phones = Phones?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
            employee.Emails = Emails?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new List<string>();

            // 1. Сохраняем во временную папку
            await SaveDynamicProperties(employee, form, "Employee");

            if (ModelState.IsValid)
            {
                employee.Id = Guid.NewGuid();
                _context.Add(employee);

                if (selectedPositions != null && selectedDepartments != null)
                {
                    for (int i = 0; i < selectedPositions.Length; i++)
                    {
                        if (i < selectedDepartments.Length)
                        {
                            _context.StaffAppointments.Add(new StaffAppointment
                            {
                                Id = Guid.NewGuid(),
                                EmployeeId = employee.Id,
                                PositionId = selectedPositions[i],
                                DepartmentId = selectedDepartments[i],
                                IsPrimary = (i == 0)
                            });
                        }
                    }
                }

                // 2. Первое сохранение (создаем запись в БД)
                await _context.SaveChangesAsync();

                // 3. Перемещаем файлы из Temp в постоянную папку
                FinalizeDynamicFilePaths(employee, "Employee", employee.Id.ToString());

                // 4. Обновляем пути в БД
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            await LoadDynamicFields("Employee");
            ViewBag.Positions = await _context.Positions.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
            return View(employee);
        }

        // --- РЕДАКТИРОВАНИЕ (GET) ---
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var employee = await _context.Employees
                .Include(e => e.StaffAppointments)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null) return NotFound();

            await LoadDynamicFields("Employee");
            ViewBag.Departments = await _context.Departments.ToListAsync();
            ViewBag.Positions = await _context.Positions.ToListAsync();

            return View(employee);
        }

        // --- РЕДАКТИРОВАНИЕ (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Employee employee, Guid[] selectedPositions, Guid[] selectedDepartments, string[] Phones, string[] Emails, IFormCollection form)
        {
            if (id != employee.Id) return NotFound();

            // 1. Сохраняем новые файлы во временную папку
            await SaveDynamicProperties(employee, form, "Employee");

            if (ModelState.IsValid)
            {
                try
                {
                    // 2. Перемещаем новые файлы в папку записи
                    FinalizeDynamicFilePaths(employee, "Employee", employee.Id.ToString());

                    employee.Phones = Phones?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
                    employee.Emails = Emails?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new List<string>();
                    
                    _context.Update(employee);

                    var existingApps = _context.StaffAppointments.Where(a => a.EmployeeId == id);
                    _context.StaffAppointments.RemoveRange(existingApps);

                    if (selectedPositions != null && selectedDepartments != null)
                    {
                        for (int i = 0; i < selectedPositions.Length; i++)
                        {
                            if (i < selectedDepartments.Length)
                            {
                                _context.StaffAppointments.Add(new StaffAppointment
                                {
                                    Id = Guid.NewGuid(),
                                    EmployeeId = id,
                                    PositionId = selectedPositions[i],
                                    DepartmentId = selectedDepartments[i],
                                    IsPrimary = (i == 0)
                                });
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!EmployeeExists(employee.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            await LoadDynamicFields("Employee");
            ViewBag.Positions = await _context.Positions.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
            return View(employee);
        }

        private bool EmployeeExists(Guid id)
        {
            return _context.Employees.Any(e => e.Id == id);
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(Guid id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee != null)
            {
                employee.IsDismissed = true;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Edit), new { id = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(Guid id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee != null)
            {
                employee.IsDismissed = false;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Edit), new { id = id });
        }
    }
}