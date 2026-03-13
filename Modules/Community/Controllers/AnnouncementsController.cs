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
public class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SupabaseStorageService _blobService;

    public AnnouncementsController(AppDbContext context, SupabaseStorageService blobService)
    {
        _context = context;
        _blobService = blobService;
    }
    
    [HttpGet]
    [Authorize(Roles = "Residente,Guardia,Admin")]
    public async Task<ActionResult<IEnumerable<AnnouncementDto>>> GetAnnouncements()
    {
        var announcements = await _context.Announcements
            .Include(a => a.Author)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnnouncementDto
            {
                Id = a.Id,
                Title = a.Title,
                Content = a.Content,
                CreatedAt = a.CreatedAt,
                AuthorEmail = a.Author != null ? a.Author.Email : "Sistema"
            })
            .ToListAsync();
        
        return Ok(announcements);
    }
    
    // CORREGIDO: Usa [FromForm] CreateAnnouncementForm
    [HttpPost]
    [Authorize(Roles = "Admin,Guardia")]
    public async Task<ActionResult> CreateAnnouncement([FromForm] CreateAnnouncementForm form)
    {
        var authorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        string? imageUrl = null;
        if (form.Image != null)
        {
            imageUrl = await _blobService.UploadFileAsync(form.Image, "news");
        }

        var newAnnouncement = new Announcement
        {
            Title = form.Title,
            Content = form.Content,
            ImageUrl = imageUrl,
            AuthorId = authorId!,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Announcements.AddAsync(newAnnouncement);
        await _context.SaveChangesAsync();
        
        return Ok(new { message = "Anuncio publicado exitosamente" });
    }
    
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Guardia")]
    public async Task<IActionResult> DeleteAnnouncement(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var announcement = await _context.Announcements.FindAsync(id);
        if (announcement == null) return NotFound("Anuncio no encontrado");

        if (User.IsInRole("Guardia") && announcement.AuthorId != userId)
        {
            return Forbid("Solo puedes borrar tus propios anuncios.");
        }

        _context.Announcements.Remove(announcement);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Anuncio eliminado." });
    }
}