using System.ComponentModel.DataAnnotations;

namespace MedicalBot.Entities
{
    public class Doctor
    {
        public int Id { get; set; } 

        [Required]
        public string FullName { get; set; } // Например: "Невролог Широбокова"

        // Связь: у врача много проведенных приемов
        public List<Visit> Visits { get; set; } = new();
    }
}