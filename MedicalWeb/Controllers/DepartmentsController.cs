using System;
using System.Linq;
using System.Threading.Tasks;
using MedicalBot.Data;
using MedicalBot.Entities.Company;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace MedicalWeb.Controllers
{
    public class DepartmentsController : BasePlatformController
    {
        public DepartmentsController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        public async Task<IActionResult> Index()
        {
            var departments = await _context.Departments
                .Include(d => d.Manager)
                .Include(d => d.Parent)
                .Include(d => d.StaffAppointments)
                    .ThenInclude(sa => sa.Employee)
                .OrderBy(d => d.Name)
                .ToListAsync();

            return View(departments);
        }

        public async Task<IActionResult> Create()
        {
            await LoadDynamicFields("Department");
            await LoadLookupLists();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Department department, IFormCollection form)
        {
            // 1. Загрузка во временную папку
            await SaveDynamicProperties(department, form, "Department");

            if (ModelState.IsValid)
            {
                department.Id = Guid.NewGuid();
                _context.Add(department);
                
                // 2. Первичное сохранение
                await _context.SaveChangesAsync();

                // 3. Перемещение файлов в папку подразделения
                FinalizeDynamicFilePaths(department, "Department", department.Id.ToString());

                // 4. Обновление путей в БД
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }
            
            await LoadDynamicFields("Department");
            await LoadLookupLists();
            return View(department);
        }

        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var department = await _context.Departments.FindAsync(id);
            if (department == null) return NotFound();

            await LoadDynamicFields("Department");
            await LoadLookupLists(department.Id);
            return View(department);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Department department, IFormCollection form)
        {
            if (id != department.Id) return NotFound();

            // 1. Загрузка новых файлов во временную папку
            await SaveDynamicProperties(department, form, "Department");

            if (ModelState.IsValid)
            {
                try
                {
                    // 2. Перемещение новых файлов в папку подразделения
                    FinalizeDynamicFilePaths(department, "Department", department.Id.ToString());

                    _context.Update(department);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DepartmentExists(department.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            
            await LoadDynamicFields("Department");
            await LoadLookupLists(department.Id);
            return View(department);
        }

        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var department = await _context.Departments
                .FirstOrDefaultAsync(m => m.Id == id);
    
            if (department == null) return NotFound();

            bool hasActiveEmployees = await _context.StaffAppointments
                .AnyAsync(sa => sa.DepartmentId == id && sa.Employee != null && !sa.Employee.IsDismissed);
    
            bool hasSubDepartments = await _context.Departments
                .AnyAsync(d => d.ParentId == id);

            if (hasActiveEmployees || hasSubDepartments)
            {
                ViewBag.CanDelete = false;
                ViewBag.Reason = hasActiveEmployees 
                    ? "В подразделении есть действующие сотрудники. Удаление невозможно." 
                    : "У данного подразделения есть дочерние структуры. Удаление невозможно.";
            }
            else
            {
                ViewBag.CanDelete = true;
            }

            return View(department);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var department = await _context.Departments
                .Include(d => d.StaffAppointments)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (department != null)
            {
                if (department.StaffAppointments != null && department.StaffAppointments.Any())
                {
                    _context.StaffAppointments.RemoveRange(department.StaffAppointments);
                }

                _context.Departments.Remove(department);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadLookupLists(Guid? currentDeptId = null)
        {
            var employees = await _context.Employees
                .OrderBy(e => e.LastName)
                .Select(e => new { Id = e.Id, FullName = $"{e.LastName} {e.FirstName} {e.MiddleName}" })
                .ToListAsync();
            ViewBag.ManagerId = new SelectList(employees, "Id", "FullName");

            var parentsQuery = _context.Departments.AsQueryable();
            if (currentDeptId.HasValue)
                parentsQuery = parentsQuery.Where(d => d.Id != currentDeptId.Value);
            
            var parents = await parentsQuery.OrderBy(d => d.Name).ToListAsync();
            ViewBag.ParentId = new SelectList(parents, "Id", "Name");
        }

        private bool DepartmentExists(Guid id) => _context.Departments.Any(e => e.Id == id);
    }
}