using System;

namespace MedicalBot.Entities
{
    public class Appointment
    {
        public Guid Id { get; set; }
        
        public DateTime DateAndTime { get; set; } // Полная дата и время приема
        
        public string DoctorName { get; set; }    // ФИО врача (например, "Чернов А.В.")
        
        public string PatientName { get; set; }   // ФИО пациента
        
        public string Procedure { get; set; }     // Процедура / Примечание
        
        public string PhoneNumber { get; set; }   // Телефон пациента
        
        public string SourceFile { get; set; }    // Откуда загрузили (для отладки)
    }
}