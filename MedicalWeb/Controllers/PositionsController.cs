using System;
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
    public class PositionsController : BasePlatformController
    {
        public PositionsController(AppDbContext context, IWebHostEnvironment hostingEnvironment) 
            : base(context, hostingEnvironment)
        {
        }

        // GET: Positions
        public async Task<IActionResult> Index()
        {
            return View(await _context.Positions.OrderBy(p => p.Name).ToListAsync());
        }

        // GET: Positions/Create
        public async Task<IActionResult> Create()
        {
            await LoadDynamicFields("Position");
            return View();
        }

        // POST: Positions/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Position position, IFormCollection form)
        {
            // 1. Загрузка файлов во временную папку
            await SaveDynamicProperties(position, form, "Position");

            if (ModelState.IsValid)
            {
                position.Id = Guid.NewGuid();
                _context.Add(position);
                
                // 2. Сохраняем запись
                await _context.SaveChangesAsync();
                
                // 3. Перемещаем файлы из Temp в папку должности
                FinalizeDynamicFilePaths(position, "Position", position.Id.ToString());
                
                // 4. Обновляем пути
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }
            
            await LoadDynamicFields("Position");
            return View(position);
        }

        // GET: Positions/Edit/5
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return NotFound();

            var position = await _context.Positions.FindAsync(id);
            if (position == null) return NotFound();

            await LoadDynamicFields("Position");
            return View(position);
        }

        // POST: Positions/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, Position position, IFormCollection form)
        {
            if (id != position.Id) return NotFound();

            // 1. Загрузка новых файлов во временную папку
            await SaveDynamicProperties(position, form, "Position");

            if (ModelState.IsValid)
            {
                try
                {
                    // 2. Перемещаем новые файлы в постоянную папку
                    FinalizeDynamicFilePaths(position, "Position", position.Id.ToString());

                    _context.Update(position);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PositionExists(position.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            
            await LoadDynamicFields("Position");
            return View(position);
        }

        // GET: Positions/Delete/5
        public async Task<IActionResult> Delete(Guid? id)
        {
            if (id == null) return NotFound();

            var position = await _context.Positions.FirstOrDefaultAsync(m => m.Id == id);
            if (position == null) return NotFound();

            return View(position);
        }

        // POST: Positions/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var position = await _context.Positions.FindAsync(id);
            if (position != null)
            {
                _context.Positions.Remove(position);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool PositionExists(Guid id)
        {
            return _context.Positions.Any(e => e.Id == id);
        }
    }
}