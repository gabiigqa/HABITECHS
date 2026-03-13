using HabiTechs.Core.Data;
using HabiTechs.Modules.Finance.DTOs;
using HabiTechs.Modules.Finance.Models;
using HabiTechs.Services; // Para SupabaseStorageService
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.Configuration; // Necesario para IConfiguration

namespace HabiTechs.Modules.Finance.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FinanceController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SupabaseStorageService _blobService;
    private readonly IConfiguration _config;

    public FinanceController(AppDbContext context, SupabaseStorageService blobService, IConfiguration config)
    {
        _context = context;
        _blobService = blobService;
        _config = config;
    }

    // ==========================================
    // 1. DASHBOARD / MÉTRICAS (ADMIN)
    // ==========================================

    [HttpGet("dashboard")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetAdminDashboardMetrics()
    {
        var now = DateTime.UtcNow;
        
        // 1. Ingresos Totales (Solo Pagos Aprobados)
        var totalIncome = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Approved)
            .SumAsync(p => p.Amount);

        // 2. Gastos Operacionales Totales (Egresos del Condominio)
        var totalOpExpenses = await _context.OperationalExpenses.SumAsync(o => o.Amount);

        // 3. Deudas Vencidas (Morosos)
        var morososCount = await _context.Expenses
            .CountAsync(e => !e.IsPaid && e.DueDate < now);
        
        // 4. Balance Neto
        var netBalance = totalIncome - totalOpExpenses; // Cálculo del Balance

        // 5. Histórico Mensual (Ingresos Aprobados)
        var monthlyIncome = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Approved)
            .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
            .Select(g => new 
            {
                Month = $"{g.Key.Month}/{g.Key.Year}",
                Amount = g.Sum(p => p.Amount)
            })
            .OrderBy(m => m.Month)
            .Take(6)
            .ToListAsync();

        return Ok(new 
        {
            NetBalance = netBalance, 
            TotalCollected = totalIncome,
            TotalSpent = totalOpExpenses,
            MorososCount = morososCount,
            MonthlyIncomeHistory = monthlyIncome
        });
    }

    // ==========================================
    // 2. RESIDENTE: GESTIÓN DE DEUDA Y PAGOS
    // ==========================================
    
    // GET: Mis Deudas (my-debt)
    [HttpGet("my-debt")]
    [Authorize(Roles = "Residente")]
    public async Task<ActionResult> GetMyDebt()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        // Deudas que NO estén marcadas como pagadas
        var debts = await _context.Expenses
            .Where(e => e.ResidentId == userId && !e.IsPaid)
            .ToListAsync();
        return Ok(debts);
    }
    
    // POST: Registrar Transferencia QR (Residente sube foto)
    [HttpPost("register-transfer")]
    [Authorize(Roles = "Residente")]
    public async Task<IActionResult> RegisterTransfer([FromForm] RegisterPaymentDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var expense = await _context.Expenses.FindAsync(dto.ExpenseId);

        if (expense == null) return NotFound("Deuda no encontrada.");
        if (expense.IsPaid) return BadRequest("Esta deuda ya está pagada.");
        if (dto.ProofImage == null) return BadRequest(new { message = "Se requiere adjuntar el comprobante." });

        // 1. Verificar si ya hay un pago pendiente para esta deuda
        var existingPending = await _context.Payments
            .AnyAsync(p => p.ExpenseId == dto.ExpenseId && p.Status == PaymentStatus.Pending);
        
        if (existingPending) return BadRequest(new { message = "Ya enviaste un comprobante para esta deuda. Espera la aprobación." });

        // 2. Subir foto a Azure
        string proofUrl = await _blobService.UploadFileAsync(dto.ProofImage, "payment_proof");

        // 3. Crear registro de Pago PENDIENTE
        var payment = new Payment
        {
            ResidentId = userId!,
            ExpenseId = expense.Id,
            Amount = expense.Amount,
            Method = PaymentMethod.TransferenciaQr,
            Status = PaymentStatus.Pending, 
            ProofUrl = proofUrl,
            PaymentDate = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Comprobante enviado. Esperando aprobación del administrador." });
    }

    // ==========================================
    // 3. ADMIN: GASTOS OPERACIONALES (EGRESOS)
    // ==========================================

    // POST: Registrar un gasto operativo (CORREGIDO CON DTO)
    [HttpPost("operational-expense")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateOperationalExpense([FromForm] CreateOperationalExpenseForm form)
    {
        string? proofUrl = null;
        if (form.ProofImage != null)
        {
            // Subida de imagen para el recibo/factura
            proofUrl = await _blobService.UploadFileAsync(form.ProofImage, "op_expense_proof");
        }

        var opEx = new OperationalExpense
        {
            Title = form.Title,
            Amount = form.Amount,
            ProofImageUrl = proofUrl,
            DateIncurred = DateTime.UtcNow
        };
        _context.OperationalExpenses.Add(opEx);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Gasto operativo registrado exitosamente." });
    }
    
    // GET: Ver lista de gastos operativos
    [HttpGet("operational-expense")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetOperationalExpenses()
    {
        var expenses = await _context.OperationalExpenses
            .OrderByDescending(e => e.DateIncurred)
            .ToListAsync();
        return Ok(expenses);
    }

    // ==========================================
    // 4. ADMIN: VALIDACIÓN DE PAGOS (APROBAR/RECHAZAR)
    // ==========================================

    // GET: Ver pagos pendientes de aprobación
    [HttpGet("pending-approvals")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> GetPendingApprovals()
    {
        var pending = await _context.Payments
            .Include(p => p.Resident) 
            .Include(p => p.Expense)  
            .Where(p => p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new 
            {
                PaymentId = p.Id,
                ResidentEmail = p.Resident != null ? p.Resident.Email : "Desconocido",
                ExpenseTitle = p.Expense != null ? p.Expense.Title : "Sin título",
                Amount = p.Amount,
                ProofUrl = p.ProofUrl,
                Date = p.PaymentDate
            })
            .ToListAsync();

        return Ok(pending);
    }

    // Aprobar un pago (Cierra el ciclo)
    [HttpPut("approve/{paymentId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ApprovePayment(Guid paymentId)
    {
        var payment = await _context.Payments.FindAsync(paymentId);
        if (payment == null) return NotFound("Pago no encontrado.");
        if (payment.Status != PaymentStatus.Pending) return BadRequest(new { message = "Estado inválido para aprobación." });

        // 1. Cambiar estado del pago
        payment.Status = PaymentStatus.Approved;
        payment.ValidatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // 2. Marcar la deuda (Expense) como pagada
        if (payment.ExpenseId != null)
        {
            var expense = await _context.Expenses.FindAsync(payment.ExpenseId);
            if (expense != null)
            {
                expense.IsPaid = true;
                expense.PaidAt = DateTime.UtcNow;
                _context.Expenses.Update(expense);
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Pago aprobado y deuda saldada." });
    }

    // Rechazar un pago
    [HttpPut("reject/{paymentId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RejectPayment(Guid paymentId)
    {
        var payment = await _context.Payments.FindAsync(paymentId);
        if (payment == null) return NotFound(new { message = "Pago no encontrado." });

        payment.Status = PaymentStatus.Rejected;
        payment.ValidatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        // NO marcamos la deuda como pagada
        await _context.SaveChangesAsync();
        return Ok(new { message = "Pago rechazado (el comprobante fue marcado como inválido)." });
    }
}