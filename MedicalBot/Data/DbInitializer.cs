using MedicalBot.Entities.Platform;
using Microsoft.EntityFrameworkCore;

namespace MedicalBot.Data
{
    public static class DbInitializer
    {
        public static async Task Initialize(AppDbContext context)
        {
            await Task.CompletedTask;
        }
    }
}