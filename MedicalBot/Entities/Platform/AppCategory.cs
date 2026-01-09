using System.ComponentModel.DataAnnotations;

namespace MedicalBot.Entities.Platform
{
    public class AppCategory
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [Display(Name = "Название раздела")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Иконка раздела")]
        public string Icon { get; set; } = "folder";

        [Display(Name = "Порядок сортировки")]
        public int SortOrder { get; set; }

        // Список приложений в этом разделе
        public List<AppDefinition> Apps { get; set; } = new();
    }
}