using HabiTechs.Core.Data;
using HabiTechs.Modules.Users.DTOs;
using HabiTechs.Modules.Users.Models;
using HabiTechs.Modules.Users.Services;
using HabiTechs.Services; // Supabase Service
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HabiTechs.Modules.Users.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ResidentCodeService _codeService;
    private readonly SupabaseStorageService _blobService;

    public UsersController(
        AppDbContext context, 
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ResidentCodeService codeService,
        SupabaseStorageService blobService)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _codeService = codeService;
        _blobService = blobService;
    }

    // ==========================================
    // 1. GESTIÓN DE PERFIL (PROPIO)
    // ==========================================

    [HttpGet("me")]
    public async Task<ActionResult<ProfileDto>> GetMyProfile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        var profile = await _context.ResidentProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        if (user == null) return NotFound("Usuario no encontrado.");

        // Auto-creación si no existe (Safety check)
        if (profile == null)
        {
            profile = new ResidentProfile
            {
                UserId = userId,
                FullName = user.Email ?? "Usuario",
                ResidentCode = await _codeService.GenerateNextCodeAsync(),
                User = user
            };
            _context.ResidentProfiles.Add(profile);
            await _context.SaveChangesAsync();
        }

        return Ok(new ProfileDto
        {
            UserId = user.Id,
            FullName = profile.FullName,
            Email = user.Email!,
            PhotoUrl = profile.PhotoUrl ?? ""
        });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile([FromForm] UserProfileForm form)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var profile = await _context.ResidentProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        if (profile == null) return NotFound("Perfil no encontrado.");

        // Actualizar campos
        profile.FullName = form.FullName;
        profile.IdentityCard = form.IdentityCard;
        profile.Occupation = form.Occupation;
        profile.PhoneNumber = form.PhoneNumber;
        profile.SecondaryPhone = form.SecondaryPhone;
        profile.LicensePlate = form.LicensePlate; // <-- Importante: Placa
        profile.UpdatedAt = DateTime.UtcNow;

        // Subir foto
        if (form.Photo != null)
        {
            string url = await _blobService.UploadFileAsync(form.Photo!, "profile");
            profile.PhotoUrl = url;
        }

        _context.ResidentProfiles.Update(profile);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Perfil actualizado exitosamente.", photoUrl = profile.PhotoUrl });
    }

    // ==========================================
    // 2. SEGURIDAD (CONTRASEÑAS)
    // ==========================================

    // Usuario cambia SU propia contraseña (requiere la actual)
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId!);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { message = "Contraseña actualizada correctamente." });
    }

    // ADMIN restablece contraseña de OTRO (no requiere la actual)
    [HttpPost("admin-reset-password")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminResetPassword(AdminResetPasswordDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.TargetEmail);
        if (user == null) return NotFound($"Usuario con email '{dto.TargetEmail}' no encontrado.");

        // Generamos un token de reseteo forzado
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        
        // Aplicamos la nueva contraseña
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded) return BadRequest(result.Errors);

        return Ok(new { message = $"Contraseña de {dto.TargetEmail} restablecida exitosamente." });
    }

    // ==========================================
    // 3. GESTIÓN DE ROLES Y CUENTAS (ADMIN)
    // ==========================================

    // Crear cuenta secundaria (Familiares)
    [HttpPost("sub-account")]
    public async Task<IActionResult> CreateSubAccount(CreateSubAccountDto dto)
    {
        var parentId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null) return BadRequest("El correo ya está registrado.");

        var newUser = new IdentityUser { UserName = dto.Email, Email = dto.Email };
        var result = await _userManager.CreateAsync(newUser, dto.Password);

        if (!result.Succeeded) return BadRequest(result.Errors);

        await _userManager.AddToRoleAsync(newUser, "Residente");

        var newProfile = new ResidentProfile
        {
            UserId = newUser.Id,
            FullName = dto.FullName,
            ResidentCode = await _codeService.GenerateNextCodeAsync(),
            AccountType = AccountType.Secundaria,
            ParentProfileId = parentId
        };

        _context.ResidentProfiles.Add(newProfile);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cuenta secundaria creada exitosamente." });
    }

    // Directorio para Contactos (Admins y Guardias)
    [HttpGet("directory")]
    [AllowAnonymous] 
    public async Task<ActionResult<IEnumerable<object>>> GetDirectory()
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        var guards = await _userManager.GetUsersInRoleAsync("Guardia");

        var directory = new List<object>();

        foreach (var user in admins)
        {
            var profile = await _context.ResidentProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            directory.Add(new 
            {
                id = user.Id,
                fullName = profile?.FullName ?? "Admin",
                role = "Administrador",
                phoneNumber = profile?.PhoneNumber ?? "",
                email = user.Email
            });
        }

        foreach (var user in guards)
        {
            var profile = await _context.ResidentProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            directory.Add(new 
            {
                id = user.Id,
                fullName = profile?.FullName ?? "Guardia",
                role = "Guardia",
                phoneNumber = profile?.PhoneNumber ?? "",
                email = user.Email
            });
        }

        return Ok(directory);
    }

    // Asignar/Quitar Roles
    [HttpPost("manage-role")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ManageUserRole([FromForm] string email, [FromForm] string roleName, [FromForm] bool isPromoting)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return NotFound($"Usuario con email '{email}' no encontrado.");

        if (!await _roleManager.RoleExistsAsync(roleName)) 
            return BadRequest("El rol especificado no existe.");

        if (isPromoting)
        {
            if (!await _userManager.IsInRoleAsync(user, roleName))
            {
                await _userManager.AddToRoleAsync(user, roleName);
            }
            return Ok(new { message = $"{user.Email} ahora es {roleName}." });
        }
        else
        {
            if (await _userManager.IsInRoleAsync(user, roleName))
            {
                // Protección: No borrar al último admin
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                if (roleName == "Admin" && admins.Count <= 1)
                {
                    return BadRequest("No puedes eliminar al último Administrador.");
                }
                await _userManager.RemoveFromRoleAsync(user, roleName);
            }
            return Ok(new { message = $"{user.Email} ya no es {roleName}." });
        }
    }
}