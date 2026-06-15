using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;

namespace HL7Gateway.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly Hl7GatewayDbContext _db;

    public UsersController(Hl7GatewayDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.UserId,
                u.Username,
                u.DisplayName,
                u.Role,
                u.IsActive,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _db.Users
            .Where(u => u.UserId == id)
            .Select(u => new
            {
                u.UserId,
                u.Username,
                u.DisplayName,
                u.Role,
                u.IsActive,
                u.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (user is null)
            return NotFound(new { message = "User not found" });

        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return Conflict(new { message = "Username already exists" });

        var user = new User
        {
            Username = req.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            DisplayName = req.DisplayName,
            Role = req.Role ?? "User",
            IsActive = true,
            CreatedAt = ChinaTime.Now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUser), new { id = user.UserId }, new
        {
            user.UserId,
            user.Username,
            user.DisplayName,
            user.Role,
            user.IsActive,
            user.CreatedAt
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = "User not found" });

        if (!string.IsNullOrWhiteSpace(req.Username) && req.Username != user.Username)
        {
            if (await _db.Users.AnyAsync(u => u.Username == req.Username && u.UserId != id))
                return Conflict(new { message = "Username already exists" });
            user.Username = req.Username;
        }

        if (!string.IsNullOrWhiteSpace(req.DisplayName))
            user.DisplayName = req.DisplayName;

        if (!string.IsNullOrWhiteSpace(req.Role))
            user.Role = req.Role;

        if (req.IsActive.HasValue)
            user.IsActive = req.IsActive.Value;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.UserId,
            user.Username,
            user.DisplayName,
            user.Role,
            user.IsActive,
            user.CreatedAt
        });
    }

    [HttpPost("{id:int}/change-password")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = "User not found" });

        if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "Old password is incorrect" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password changed successfully" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound(new { message = "User not found" });

        if (user.Role == "Admin")
        {
            var adminCount = await _db.Users.CountAsync(u => u.Role == "Admin");
            if (adminCount <= 1)
                return BadRequest(new { message = "Cannot delete the last admin user" });
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public record CreateUserRequest(string Username, string Password, string DisplayName, string? Role = null);

public record UpdateUserRequest(string? Username = null, string? DisplayName = null, string? Role = null, bool? IsActive = null);

public record ChangePasswordRequest(string OldPassword, string NewPassword);
