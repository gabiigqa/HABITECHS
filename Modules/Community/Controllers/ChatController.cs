using HabiTechs.Core.Data;
using HabiTechs.Modules.Community.DTOs;
using HabiTechs.Modules.Community.Models;
using HabiTechs.Services; // Azure Service
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HabiTechs.Modules.Community.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly SupabaseStorageService _blobService;
    private readonly UserManager<IdentityUser> _userManager;

    public ChatController(AppDbContext context, SupabaseStorageService blobService, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _blobService = blobService;
        _userManager = userManager;
    }

    [HttpGet("conversation/{otherUserId}")]
    public async Task<ActionResult<IEnumerable<ChatMessageDto>>> GetConversation(string otherUserId)
    {
        var myId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var messages = await _context.ChatMessages
            .Where(m => m.SentAt >= thirtyDaysAgo)
            .Where(m => (m.SenderId == myId && m.ReceiverId == otherUserId) || 
                        (m.SenderId == otherUserId && m.ReceiverId == myId))
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        var dtos = new List<ChatMessageDto>();
        foreach(var m in messages)
        {
            var senderUser = await _userManager.FindByIdAsync(m.SenderId);
            dtos.Add(new ChatMessageDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                SenderName = senderUser?.UserName ?? "Usuario",
                Message = m.Message ?? "",
                ImageUrl = m.ImageUrl ?? "",
                SentAt = m.SentAt,
                IsMine = m.SenderId == myId
            });
        }
        return Ok(dtos);
    }

    // CORREGIDO: Usa [FromForm] SendMessageForm
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromForm] SendMessageForm form)
    {
        var myId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (string.IsNullOrEmpty(form.Message) && form.Image == null)
            return BadRequest("Debes enviar texto o una imagen.");

        string? imageUrl = null;
        if (form.Image != null)
        {
            imageUrl = await _blobService.UploadFileAsync(form.Image, "chat");
        }

        var chatMsg = new ChatMessage
        {
            SenderId = myId,
            ReceiverId = form.ReceiverId,
            Message = form.Message,
            ImageUrl = imageUrl,
            SentAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(chatMsg);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Enviado" });
    }
}