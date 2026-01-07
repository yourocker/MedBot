using MedicalBot.Data;
using MedicalBot.Entities.Company;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// System.Text.Json больше не нужен здесь, так как сериализация ушла в базовый контроллер

namespace MedicalWeb.Controllers
{
    // 1. Наследуемся от BasePlatformController
    public class EmployeesController : BasePlatformController
    {
        // Локальное поле _context удалено, так как оно есть в родительском классе (protected)

        // 2. Передаем context в базовый конструктор
        public EmployeesController(AppDbContext context) : base(context)
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
            // 3. Загружаем поля через универсальный метод родителя
            await LoadDynamicFields("Employee"); 
            
            ViewBag.Positions = await _context.Positions.OrderBy(p => p.Name).ToListAsync();
            ViewBag.Departments = await _context.Departments.OrderBy(d => d.Name).ToListAsync();
            return View();
        }

        // --- СОЗДАНИЕ (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee, Guid[] selectedPositions, Guid[] selectedDepartments, string[] Phones, string[] Emails, Dictionary<string, string> DynamicProps)
        {
            employee.Phones = Phones?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
            employee.Emails = Emails?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new List<string>();

            // 4. Сохраняем динамические данные одной строкой (метод родителя)
            SaveDynamicProperties(employee, DynamicProps);

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

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            // Если ошибка, перезагружаем поля, чтобы форма не сломалась
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

            // 5. Загружаем поля для редактирования
            await LoadDynamicFields("Employee");

            ViewBag.Departments = await _context.Departments.ToListAsync();
            ViewBag.Positions = await _context.Positions.ToListAsync();

            return View(employee);
        }

        // --- РЕДАКТИРОВАНИЕ (POST) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Employee employee, Guid[] selectedPositions, Guid[] selectedDepartments, string[] Phones, string[] Emails, Dictionary<string, string> DynamicProps)
        {
            if (id != employee.Id) return NotFound();

            // Сохраняем динамические данные перед проверкой модели
            SaveDynamicProperties(employee, DynamicProps);

            if (ModelState.IsValid)
            {
                try
                {
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

            // При ошибке снова грузим поля
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