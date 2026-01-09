using MedicalBot.Data;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalWeb.Controllers;

public class GenericObjectsController(AppDbContext context) : BasePlatformController(context)
{
    public async Task<IActionResult> Index(string entityCode)
    {
        if (string.IsNullOrEmpty(entityCode)) return NotFound();

        var definition = await _context.AppDefinitions
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.EntityCode == entityCode);

        if (definition == null) return NotFound();

        ViewBag.Definition = definition;
        
        var objects = await _context.GenericObjects
            .Where(o => o.EntityCode == entityCode)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return View(objects);
    }

    public async Task<IActionResult> Create(string entityCode)
    {
        if (string.IsNullOrEmpty(entityCode)) return NotFound();
        
        await LoadDynamicFields(entityCode);
        ViewBag.EntityCode = entityCode;
        
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GenericObject obj, Dictionary<string, string> dynamicProps)
    {
        obj.CreatedAt = DateTime.UtcNow;
        // Передаем свойства в метод базового контроллера (он запишет их в Properties)
        SaveDynamicProperties(obj, dynamicProps);

        if (ModelState.IsValid)
        {
            obj.Id = Guid.NewGuid();
            _context.Add(obj);
            await _context.SaveChangesAsync();
            
            return Redirect($"/Data/{obj.EntityCode}");
        }

        await LoadDynamicFields(obj.EntityCode);
        ViewBag.EntityCode = obj.EntityCode;
        return View(obj);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var obj = await _context.GenericObjects.FindAsync(id);
        if (obj == null) return NotFound();

        await LoadDynamicFields(obj.EntityCode);
        return View(obj);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, GenericObject obj, Dictionary<string, string> dynamicProps)
    {
        if (id != obj.Id) return NotFound();

        SaveDynamicProperties(obj, dynamicProps);

        if (ModelState.IsValid)
        {
            _context.Update(obj);
            await _context.SaveChangesAsync();
            
            return Redirect($"/Data/{obj.EntityCode}");
        }

        await LoadDynamicFields(obj.EntityCode);
        return View(obj);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var obj = await _context.GenericObjects.FindAsync(id);
        if (obj == null) return NotFound();

        var entityCode = obj.EntityCode;
        _context.GenericObjects.Remove(obj);
        await _context.SaveChangesAsync();

        return Redirect($"/Data/{entityCode}");
    }
}