using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions; // Добавлено для проверки Regex
using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers;

public class AppDefinitionsController(AppDbContext context) : Controller
{
    private readonly AppDbContext _context = context;

    public async Task<IActionResult> Index()
    {
        var apps = await _context.AppDefinitions
            .Include(a => a.Fields)
            .OrderBy(a => a.Name)
            .ToListAsync();
        return View(apps);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        // Загружаем категории для выпадающего списка в форме
        var categories = await _context.AppCategories.OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Categories = new SelectList(categories, "Id", "Name");

        // Создаем модель с начальными значениями (иконка по умолчанию)
        var model = new AppDefinition { Icon = "box" };
        return View(model);
    }

    public async Task<IActionResult> Fields(Guid id)
    {
        var app = await _context.AppDefinitions
            .Include(a => a.Fields)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app == null) return NotFound();

        return View(app);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddField(Guid appDefinitionId, string label, string systemName, FieldDataType dataType, bool isRequired, bool isArray)
    {
        var app = await _context.AppDefinitions
            .Include(a => a.Fields)
            .FirstOrDefaultAsync(a => a.Id == appDefinitionId);

        if (app == null) return NotFound();

        // НОРМАЛИЗАЦИЯ: Приводим системное имя к нижнему регистру и убираем пробелы
        var normalizedSystemName = systemName?.Trim().ToLower();

        if (string.IsNullOrEmpty(normalizedSystemName))
        {
            TempData["Error"] = "Системное имя не может быть пустым.";
            return RedirectToAction(nameof(Fields), new { id = appDefinitionId });
        }

        // ПРОВЕРКА СИМВОЛОВ: Только латиница, цифры и подчеркивание. Должно начинаться с буквы.
        if (!Regex.IsMatch(normalizedSystemName, @"^[a-z][a-z0-9_]*$"))
        {
            TempData["Error"] = "Системное имя должно содержать только латиницу, цифры и начинаться с буквы.";
            return RedirectToAction(nameof(Fields), new { id = appDefinitionId });
        }

        // Проверка уникальности нормализованного имени с учетом того, что в БД могут быть старые записи разного регистра
        var isDuplicate = await _context.AppFieldDefinitions
            .AnyAsync(f => f.AppDefinitionId == appDefinitionId && f.SystemName.ToLower() == normalizedSystemName);

        if (isDuplicate)
        {
            TempData["Error"] = $"Поле с системным именем '{normalizedSystemName}' уже существует в этой сущности.";
            return RedirectToAction(nameof(Fields), new { id = appDefinitionId });
        }

        int nextSortOrder = app.Fields.Any() ? app.Fields.Max(f => f.SortOrder) + 1 : 0;

        var newField = new AppFieldDefinition
        {
            Id = Guid.NewGuid(),
            AppDefinitionId = appDefinitionId,
            Label = label,
            SystemName = normalizedSystemName, // Сохраняем в нижнем регистре
            DataType = dataType,
            IsRequired = isRequired,
            IsArray = isArray,
            SortOrder = nextSortOrder
        };

        _context.AppFieldDefinitions.Add(newField);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Fields), new { id = appDefinitionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteField(Guid id)
    {
        var field = await _context.AppFieldDefinitions.FindAsync(id);
        if (field == null) return NotFound();

        var appId = field.AppDefinitionId;
        _context.AppFieldDefinitions.Remove(field);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Fields), new { id = appId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AppDefinition app)
    {
        if (ModelState.IsValid)
        {
            app.Id = Guid.NewGuid();
            
            // Если иконка не выбрана, ставим стандартную
            if (string.IsNullOrEmpty(app.Icon)) app.Icon = "box";

            _context.AppDefinitions.Add(app);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // Если валидация не прошла, возвращаем категории обратно во вьюху
        var categories = await _context.AppCategories.OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Categories = new SelectList(categories, "Id", "Name");
        
        return View(app);
    }
}