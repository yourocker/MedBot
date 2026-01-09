using System.ComponentModel.DataAnnotations;

namespace MedicalBot.Entities.Platform
{
    public class AppDefinition
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [Display(Name = "Название")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Системный код")]
        public string EntityCode { get; set; } = string.Empty;

        // Добавляем это поле
        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [Display(Name = "Иконка")]
        public string Icon { get; set; } = "gear";

        [Display(Name = "Системная")]
        public bool IsSystem { get; set; }
        
        // Добавь эти поля внутрь класса AppDefinition
        [Display(Name = "Раздел меню")]
        public Guid? AppCategoryId { get; set; }

        public AppCategory? Category { get; set; }

        // Навигационное свойство для полей
        public List<AppFieldDefinition> Fields { get; set; } = new();
    }
}