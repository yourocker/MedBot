using System;

namespace MedicalBot.Entities
{
    public class Visit
    {
        public Guid Id { get; set; }
        public DateTime Date { get; set; }

        public Guid PatientId { get; set; }
        public Patient Patient { get; set; }

        public string ServiceName { get; set; }

        // 👇 НОВЫЕ ПОЛЯ ДЛЯ ДЕНЕГ
        public decimal AmountCash { get; set; }      // Наличные (Желтая колонка)
        public decimal AmountCashless { get; set; }  // Безнал (Голубая колонка)
        
        public decimal TotalCost { get; set; }       // Итоговая сумма (Нал + Безнал)
    }
}