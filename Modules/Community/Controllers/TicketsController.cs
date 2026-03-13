using HabiTechs.Core.Data;
using HabiTechs.Modules.Community.DTOs;
using HabiTechs.Modules.Community.Models;
using HabiTechs.Services; // Supabase Service
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HabiTechs.Modules.Community.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SupabaseStorageService _blobService;

    public TicketsController(AppDbContext context, SupabaseStorageService blobService)
    {
        _context = context;
        _blobService = blobService;
    }
    
    // --- OBTENER MIS TICKETS (Residente) ---
    [HttpGet("my-tickets")]
    [Authorize(Roles = "Residente")]
    public async Task<ActionResult<IEnumerable<TicketDto>>> GetMyTickets()
    {
        var residentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tickets = await _context.Tickets
            .Include(t => t.Requester)
            .Where(t => t.RequesterId == residentId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TicketDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAt,
                ClosedAt = t.ClosedAt,
                RequesterEmail = t.Requester != null ? t.Requester.Email : "N/A"
            })
            .ToListAsync();
        
        return Ok(tickets);
    }
    
    // --- OBTENER TODOS (Admin) ---
    [HttpGet]
    [Authorize(Roles = "Admin,Guardia")]
    public async Task<ActionResult<IEnumerable<TicketDto>>> GetAllTickets()
    {
        var tickets = await _context.Tickets
            .Include(t => t.Requester)
            .OrderByDescending(t => t.Status == TicketStatus.Open)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new TicketDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAt,
                ClosedAt = t.ClosedAt,
                RequesterEmail = t.Requester != null ? t.Requester.Email : "N/A"
            })
            .ToListAsync();
        
        return Ok(tickets);
    }
    
    // --- CREAR TICKET (Residente - Con Foto Opcional) ---
    [HttpPost]
    [Authorize(Roles = "Residente")]
    public async Task<ActionResult> CreateTicket([FromForm] CreateTicketForm form)
    {
        var residentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        string? imageUrl = null;
        if (form.Image != null)
        {
            // Subimos la foto del problema a Azure
            imageUrl = await _blobService.UploadFileAsync(form.Image, "ticket_issue");
        }
        
        var newTicket = new Ticket
        {
            Title = form.Title,
            Description = form.Description,
            ImageUrl = imageUrl,
            RequesterId = residentId!,
            Status = TicketStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Tickets.AddAsync(newTicket);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ticket creado exitosamente." });
    }
    
    // --- CERRAR TICKET CON EVIDENCIA (Admin/Guardia) ---
    [HttpPut("{id}/close")]
    [Authorize(Roles = "Admin,Guardia")] 
    public async Task<IActionResult> CloseTicket(Guid id, [FromForm] CloseTicketForm form)
    {
        var ticket = await _context.Tickets.FindAsync(id);
        if (ticket == null) return NotFound("Ticket no encontrado.");

        if (ticket.Status == TicketStatus.Closed)
            return BadRequest("Este ticket ya fue cerrado anteriormente.");

        // 1. Subir evidencia de solución si existe (Foto del trabajo terminado)
        string? evidenceUrl = null;
        if (form.EvidenceImage != null)
        {
            evidenceUrl = await _blobService.UploadFileAsync(form.EvidenceImage, "ticket_resolution");
        }

        // 2. Actualizar estado y campos de cierre
        ticket.Status = TicketStatus.Closed;
        ticket.ClosedAt = DateTime.UtcNow;
        ticket.ResolutionComment = form.Comment;
        ticket.ResolutionImageUrl = evidenceUrl;
        
        _context.Tickets.Update(ticket);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Ticket cerrado con evidencia exitosamente." });
    }
}