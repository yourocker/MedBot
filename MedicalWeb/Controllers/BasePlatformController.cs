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

        // Этап 1: Сохранение во временную папку (temp)
        protected async Task SaveDynamicProperties(IHasDynamicProperties entity, IFormCollection form, string entityCode)
        {
            var definitions = await _context.AppFieldDefinitions
                .Where(f => f.AppDefinition.EntityCode == entityCode)
                .ToListAsync();

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

                if (def.DataType == FieldDataType.File)
                {
                    var files = form.Files.GetFiles(formKey);

                    if (files.Any())
                    {
                        var newPaths = new List<string>();

                        foreach (var file in files)
                        {
                            if (file.Length > 20 * 1024 * 1024) 
                            {
                                ModelState.AddModelError(formKey, $"Файл '{file.FileName}' слишком велик (макс 20МБ).");
                                continue;
                            }

                            var folderGuid = Guid.NewGuid().ToString();
                            var uploadDir = Path.Combine(_hostingEnvironment.WebRootPath, "uploads", "temp", folderGuid);
                            
                            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                            var safeFileName = Path.GetFileName(file.FileName); 
                            var filePath = Path.Combine(uploadDir, safeFileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            newPaths.Add($"/uploads/temp/{folderGuid}/{safeFileName}");
                        }

                        if (newPaths.Any())
                        {
                            if (def.IsArray)
                            {
                                resultDictionary[def.SystemName] = newPaths.ToArray();
                            }
                            else
                            {
                                resultDictionary[def.SystemName] = newPaths.First();
                            }
                        }
                    }
                    continue;
                }

                if (form.ContainsKey(formKey))
                {
                    var formValues = form[formKey];
                    var rawValues = formValues.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

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

        // Этап 2: Перемещение файлов в постоянную папку (EntityCode/ID)
        protected void FinalizeDynamicFilePaths(IHasDynamicProperties entity, string entityCode, string recordId)
        {
            if (string.IsNullOrEmpty(entity.Properties)) return;

            var props = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.Properties);
            if (props == null) return;

            bool hasChanges = false;
            var finalBaseFolder = Path.Combine(_hostingEnvironment.WebRootPath, "uploads", entityCode, recordId);

            var keys = props.Keys.ToList();
            foreach (var key in keys)
            {
                var value = props[key];
                
                if (value is JsonElement jEl)
                {
                    // Одиночное строковое значение (путь к файлу)
                    if (jEl.ValueKind == JsonValueKind.String)
                    {
                        var path = jEl.GetString();
                        if (!string.IsNullOrEmpty(path) && path.Contains("/uploads/temp/"))
                        {
                            var newPath = MoveFileToFinal(path, finalBaseFolder);
                            if (newPath != null)
                            {
                                props[key] = newPath;
                                hasChanges = true;
                            }
                        }
                    }
                    // Массив (может быть массив файлов или массив других данных)
                    else if (jEl.ValueKind == JsonValueKind.Array)
                    {
                        // ИСПРАВЛЕНИЕ: Используем List<object>, чтобы хранить и строки, и булевы, и числа
                        var newItems = new List<object>();
                        bool arrayChanged = false;

                        foreach (var item in jEl.EnumerateArray())
                        {
                            // ПРОВЕРКА: Обрабатываем как файл ТОЛЬКО если это строка
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var path = item.GetString();
                                if (!string.IsNullOrEmpty(path) && path.Contains("/uploads/temp/"))
                                {
                                    var newPath = MoveFileToFinal(path, finalBaseFolder);
                                    if (newPath != null)
                                    {
                                        newItems.Add(newPath);
                                        arrayChanged = true;
                                        hasChanges = true;
                                    }
                                    else
                                    {
                                        newItems.Add(path);
                                    }
                                }
                                else
                                {
                                    newItems.Add(path);
                                }
                            }
                            else
                            {
                                // Если это не строка (например, false, 123), сохраняем как есть
                                newItems.Add(item);
                            }
                        }

                        if (arrayChanged) props[key] = newItems.ToArray();
                    }
                }
            }

            if (hasChanges)
            {
                var options = new JsonSerializerOptions 
                { 
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true
                };
                entity.Properties = JsonSerializer.Serialize(props, options);
            }
        }

        private string MoveFileToFinal(string tempWebPath, string finalBaseDir)
        {
            try
            {
                var tempSystemPath = _hostingEnvironment.WebRootPath + tempWebPath.Replace("/", Path.DirectorySeparatorChar.ToString());

                if (!System.IO.File.Exists(tempSystemPath)) return null;

                if (!Directory.Exists(finalBaseDir)) Directory.CreateDirectory(finalBaseDir);

                var fileName = Path.GetFileName(tempSystemPath);
                var finalSystemPath = Path.Combine(finalBaseDir, fileName);

                if (System.IO.File.Exists(finalSystemPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var newName = $"{nameWithoutExt}_{Guid.NewGuid().ToString().Substring(0, 4)}{ext}";
                    finalSystemPath = Path.Combine(finalBaseDir, newName);
                    fileName = newName;
                }

                System.IO.File.Move(tempSystemPath, finalSystemPath);

                var tempDir = Path.GetDirectoryName(tempSystemPath);
                if (Directory.Exists(tempDir) && !Directory.EnumerateFileSystemEntries(tempDir).Any())
                {
                    Directory.Delete(tempDir);
                }

                var relativeDir = finalBaseDir.Replace(_hostingEnvironment.WebRootPath, "").Replace("\\", "/");
                if (!relativeDir.StartsWith("/")) relativeDir = "/" + relativeDir;
                
                return $"{relativeDir.TrimEnd('/')}/{fileName}";
            }
            catch
            {
                return null;
            }
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