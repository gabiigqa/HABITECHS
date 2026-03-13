using HabiTechs.Modules.Access.Models;
using HabiTechs.Modules.Community.Models;
using HabiTechs.Modules.Bookings.Models; 
using HabiTechs.Modules.Finance.Models;
using HabiTechs.Modules.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HabiTechs.Core.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    
    // --- MÓDULO ACCESO ---
    public DbSet<Visit> Visits { get; set; } = null!;
    public DbSet<Parcel> Parcels { get; set; } = null!;
    public DbSet<GateLog> GateLogs { get; set; } = null!;
    
    // --- MÓDULO COMUNIDAD ---
    public DbSet<Announcement> Announcements { get; set; } = null!;
    public DbSet<Ticket> Tickets { get; set; } = null!;
    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
    
    // --- MÓDULO RESERVAS ---
    public DbSet<Booking> Bookings { get; set; } = null!;
    public DbSet<CommonArea> CommonAreas { get; set; } = null!;
    
    // --- MÓDULO FINANZAS (COMPLETO) ---
    public DbSet<Expense> Expenses { get; set; } = null!;           // Deudas a residentes
    public DbSet<Payment> Payments { get; set; } = null!;           // Historial de pagos (Ingresos)
    public DbSet<PaymentInstruction> PaymentInstructions { get; set; } = null!; // Datos QR/Banco del condominio
    public DbSet<OperationalExpense> OperationalExpenses { get; set; } = null!; // Gastos del Condominio (Egresos)
    // ------------------------------------

    // --- MÓDULO USUARIOS ---
    public DbSet<ResidentProfile> ResidentProfiles { get; set; } = null!;
}