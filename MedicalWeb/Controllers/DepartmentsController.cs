using MedicalBot.Data;
using MedicalBot.Entities.Company;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers
{
    public class DepartmentsController : BasePlatformController
    {
        public DepartmentsController(AppDbContext context) : base(context)
        {
        }

        public async Task<IActionResult> Index()
        {
            var departments = await _context.Departments
                .Include(d => d.Manager)
                .Include(d => d.Parent)
                .Include(d => d.StaffAppointments) // Подгружаем связи
                    .ThenInclude(sa => sa.Employee) // Подгружаем сотрудников для отображения в списке
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
        public async Task<IActionResult> Create(Department department, Dictionary<string, string> dynamicProps)
        {
            SaveDynamicProperties(department, dynamicProps);

            if (ModelState.IsValid)
            {
                department.Id = Guid.NewGuid();
                _context.Add(department);
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
        public async Task<IActionResult> Edit(Guid id, Department department, Dictionary<string, string> dynamicProps)
        {
            if (id != department.Id) return NotFound();

            SaveDynamicProperties(department, dynamicProps);

            if (ModelState.IsValid)
            {
                try
                {
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