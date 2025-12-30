using System.ComponentModel.DataAnnotations;

namespace MedicalBot.Entities
{
    public class Patient
    {
        public Guid Id { get; set; } // Уникальный ID пациента

        [Required]
        public string FullName { get; set; } // Как написано в файле: "Иванов И.И."

        public string NormalizedName { get; set; } // Для поиска: "ИВАНОВ И И"

        // Связь: у одного пациента может быть много визитов
        public List<Visit> Visits { get; set; } = new();
    }
}