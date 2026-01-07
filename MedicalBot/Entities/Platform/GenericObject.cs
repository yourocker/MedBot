using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalBot.Entities.Platform
{
    public class GenericObject : IHasDynamicProperties
    {
        [Key]
        public Guid Id { get; set; }

        // Код типа объекта (например, "Equipment"), чтобы отличать их
        [Required]
        public string EntityCode { get; set; }

        // Основное имя/заголовок объекта (чтобы не искать в JSON для списков)
        [Required]
        public string Name { get; set; }

        // Дата создания
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Наш универсальный JSON-контейнер
        public string? Properties { get; set; }
    }
}