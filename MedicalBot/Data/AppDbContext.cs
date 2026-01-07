using System;
using Microsoft.EntityFrameworkCore;
using MedicalBot.Entities; 
using MedicalBot.Entities.Company;
using MedicalBot.Entities.Platform; // Добавили для доступа к AppDefinition и AppFieldDefinition

namespace MedicalBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        // --- ПЛАТФОРМЕННОЕ ЯДРО (Путь А) ---
        public DbSet<AppDefinition> AppDefinitions { get; set; }
        public DbSet<AppFieldDefinition> AppFieldDefinitions { get; set; }

        // --- БАЗОВЫЕ ТАБЛИЦЫ ---
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Visit> Visits { get; set; }
        public DbSet<Appointment> Appointments { get; set; } 

        // --- СТРУКТУРА КОМПАНИИ ---
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Position> Positions { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<StaffAppointment> StaffAppointments { get; set; }
        
        // ---УНИВЕРСАЛЬНЫЕ ОБЪЕКТЫ ---
        public DbSet<GenericObject> GenericObjects { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Настройка Сотрудника
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.Property(e => e.Phones).HasColumnType("jsonb");
                entity.Property(e => e.Emails).HasColumnType("jsonb");
                
                // Настройка гибкого хранилища для сотрудников
                entity.Property(e => e.Properties).HasColumnType("jsonb");
    
                entity.Ignore(e => e.FullName);
            });

            // Настройка Пациента
            modelBuilder.Entity<Patient>(entity =>
            {
                // Настройка гибкого хранилища для пациентов (не забудь добавить это поле в класс Patient.cs)
                entity.Property(p => p.Properties).HasColumnType("jsonb");
                
                entity.HasIndex(p => p.NormalizedName);
            });

            // Настройка иерархии отделов
            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasOne(d => d.Parent)
                    .WithMany(d => d.Children)
                    .HasForeignKey(d => d.ParentId);

                entity.HasOne(d => d.Manager)
                    .WithMany()
                    .HasForeignKey(d => d.ManagerId);
            });
            
            // Настройка Платформенных определений
            modelBuilder.Entity<AppFieldDefinition>(entity =>
            {
                // Настройки поля тоже будем хранить в JSON (для типов Table, Link и т.д.)
                entity.Property(f => f.SettingsJson).HasColumnType("jsonb");
            });
        }
    }
}