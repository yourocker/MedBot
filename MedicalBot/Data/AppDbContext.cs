using System;
using Microsoft.EntityFrameworkCore;
using MedicalBot.Entities; 

namespace MedicalBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            // Обязательная настройка для времени в PostgreSQL
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        // Наши таблицы
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Visit> Visits { get; set; }
        public DbSet<Appointment> Appointments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Ускоряем поиск по имени
            modelBuilder.Entity<Patient>()
                .HasIndex(p => p.NormalizedName);
        }
    }
}