using MedicalBot.Data;
using MedicalBot.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MedicalWeb.Controllers
{
    public abstract class BasePlatformController : Controller
    {
        protected readonly AppDbContext _context;

        protected BasePlatformController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Загружает определения полей для указанной сущности и кладет их во ViewBag.
        /// Используется в GET методах (Create, Edit).
        /// </summary>
        /// <param name="systemCode">Системный код сущности (например, "Employee", "Position")</param>
        protected async Task LoadDynamicFields(string systemCode)
        {
            var fields = await _context.AppFieldDefinitions
                .Where(f => f.AppDefinition.EntityCode == systemCode)
                .OrderBy(f => f.SortOrder)
                .ToListAsync();

            ViewBag.DynamicFields = fields;
        }

        /// <summary>
        /// Сохраняет динамические поля из формы в JSON-свойство сущности.
        /// Используется в POST методах.
        /// </summary>
        /// <param name="entity">Объект сущности (Employee, Position и т.д.)</param>
        /// <param name="dynamicProps">Словарь данных из формы</param>
        protected void SaveDynamicProperties(IHasDynamicProperties entity, Dictionary<string, string> dynamicProps)
        {
            // Если пришли данные, сериализуем их в JSON
            if (dynamicProps != null && dynamicProps.Any())
            {
                // Опции нужны, чтобы кириллица не превращалась в \u0430...
                var options = new JsonSerializerOptions 
                { 
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };
                
                entity.Properties = JsonSerializer.Serialize(dynamicProps, options);
            }
            else
            {
                // Если данных нет, можно оставить null или записать пустой JSON "{}"
                // Пока оставим как есть, чтобы не затирать, если форма пустая пришла (хотя такое редкость)
            }
        }
    }
}