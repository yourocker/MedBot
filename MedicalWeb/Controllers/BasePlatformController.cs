using System.Collections.Generic;
using System.Linq;
using MedicalBot.Data;
using MedicalBot.Entities;
using MedicalBot.Entities.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace MedicalWeb.Controllers
{
    public abstract class BasePlatformController : Controller
    {
        protected readonly AppDbContext _context;
        protected readonly IWebHostEnvironment _hostingEnvironment;

        protected BasePlatformController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        protected async Task LoadDynamicFields(string systemCode)
        {
            var fields = await _context.AppFieldDefinitions
                .Where(f => f.AppDefinition.EntityCode == systemCode)
                .OrderBy(f => f.SortOrder)
                .ToListAsync();

            ViewBag.DynamicFields = fields;
        }

        protected async Task SaveDynamicProperties(IHasDynamicProperties entity, IFormCollection form, string entityCode)
        {
            var definitions = await _context.AppFieldDefinitions
                .Where(f => f.AppDefinition.EntityCode == entityCode)
                .ToListAsync();

            // 1. Загружаем СТАРЫЕ данные, чтобы не потерять существующие файлы и поля
            var resultDictionary = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(entity.Properties))
            {
                try 
                { 
                    resultDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.Properties) 
                                       ?? new Dictionary<string, object>(); 
                }
                catch { }
            }

            foreach (var def in definitions)
            {
                var formKey = $"DynamicProps[{def.SystemName}]";

                // --- ЛОГИКА ДЛЯ ФАЙЛОВ ---
                if (def.DataType == FieldDataType.File)
                {
                    var files = form.Files.GetFiles(formKey);

                    // Если пользователь загрузил НОВЫЕ файлы
                    if (files.Any())
                    {
                        var newPaths = new List<string>();

                        foreach (var file in files)
                        {
                            // Проверка размера (20 МБ)
                            if (file.Length > 20 * 1024 * 1024) 
                            {
                                ModelState.AddModelError(formKey, $"Файл '{file.FileName}' слишком велик (макс 20МБ).");
                                continue;
                            }

                            // СОЗДАЕМ УНИКАЛЬНУЮ ПАПКУ (GUID)
                            var folderGuid = Guid.NewGuid().ToString();
                            var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "uploads", folderGuid);
                            
                            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                            // Сохраняем файл с ОРИГИНАЛЬНЫМ именем
                            // Имя файла нужно обезопасить (на случай путей ../), но берем оригинал
                            var safeFileName = Path.GetFileName(file.FileName); 
                            var filePath = Path.Combine(uploadDir, safeFileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            // Путь: /uploads/{GUID}/{OriginalName}
                            newPaths.Add($"/uploads/{folderGuid}/{safeFileName}");
                        }

                        if (newPaths.Any())
                        {
                            if (def.IsArray)
                            {
                                // Если это массив, нужно добавить к старым (если они есть) или записать новые
                                // Здесь простая логика: если загрузили новые - перезаписываем или добавляем?
                                // Обычно в веб-формах загрузка новых добавляется.
                                // Но для простоты пока: если загрузили - это становится актуальным значением.
                                // Чтобы реализовать "добавление", нужно считывать старый массив и делать AddRange.
                                // Сейчас сделаем замену (как работает стандартный input type=file multiple),
                                // но так как у нас есть "старый" словарь, если мы сюда не зашли - старое не пропадет.
                                resultDictionary[def.SystemName] = newPaths.ToArray();
                            }
                            else
                            {
                                // Одиночное поле - заменяем старое на новое
                                resultDictionary[def.SystemName] = newPaths.First();
                            }
                        }
                    }
                    // Если files.Count == 0, мы просто пропускаем блок. 
                    // В resultDictionary остается старое значение (путь к файлу), которое мы загрузили в начале.
                    // Файл НЕ исчезнет.
                    
                    continue;
                }

                // --- ЛОГИКА ДЛЯ ОБЫЧНЫХ ПОЛЕЙ ---
                if (form.ContainsKey(formKey))
                {
                    var formValues = form[formKey];
                    var rawValues = formValues.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

                    // Если поле пришло пустым (пользователь стер текст) -> удаляем из словаря
                    if (!rawValues.Any()) 
                    {
                        if (def.IsRequired)
                            ModelState.AddModelError(formKey, $"Поле '{def.Label}' обязательно.");
                        else
                            resultDictionary.Remove(def.SystemName);

                        continue;
                    }

                    try
                    {
                        if (def.IsArray)
                        {
                            var parsedList = new List<object>();
                            foreach (var val in rawValues) parsedList.Add(ParseAndValidateValue(val, def, formKey));
                            resultDictionary[def.SystemName] = parsedList.ToArray();
                        }
                        else
                        {
                            resultDictionary[def.SystemName] = ParseAndValidateValue(rawValues.First(), def, formKey);
                        }
                    }
                    catch (FormatException) { continue; }
                }
                else if (def.IsRequired && !resultDictionary.ContainsKey(def.SystemName))
                {
                    // Поля нет в форме и нет в старых данных -> Ошибка
                    ModelState.AddModelError(formKey, $"Поле '{def.Label}' обязательно.");
                }
            }

            var options = new JsonSerializerOptions 
            { 
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };
            
            entity.Properties = JsonSerializer.Serialize(resultDictionary, options);
        }

        private object ParseAndValidateValue(string val, AppFieldDefinition def, string formKey)
        {
            switch (def.DataType)
            {
                case FieldDataType.Number:
                case FieldDataType.Money:
                    if (decimal.TryParse(val.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                        return d;
                    
                    ModelState.AddModelError(formKey, $"Значение '{val}' в поле '{def.Label}' не является числом.");
                    throw new FormatException();

                case FieldDataType.Date:
                case FieldDataType.DateTime:
                    if (DateTime.TryParse(val, out DateTime dt))
                        return dt;

                    ModelState.AddModelError(formKey, $"Значение '{val}' в поле '{def.Label}' не является корректной датой.");
                    throw new FormatException();

                case FieldDataType.Boolean:
                    return val.Contains("true", StringComparison.OrdinalIgnoreCase);

                default:
                    return val;
            }
        }
    }
}