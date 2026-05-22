using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Promote a user to Admin role. Only existing admins can do this.
    /// For the FIRST admin, use: POST /api/admin/bootstrap with the secret key.
    /// </summary>
    [HttpPost("promote")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> PromoteToAdmin([FromBody] PromoteDto input)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user == null) return NotFound(new { message = "User not found." });

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return Ok(new { message = "User is already an Admin." });

        await _userManager.AddToRoleAsync(user, "Admin");
        return Ok(new { message = $"{user.Name} has been promoted to Admin." });
    }

    /// <summary>
    /// Bootstrap the FIRST admin. Uses a secret key from appsettings.
    /// Call this once to make yourself admin, then use /promote for others.
    /// POST /api/admin/bootstrap { "email": "you@example.com", "secretKey": "your-secret" }
    /// </summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    public async Task<ActionResult> BootstrapAdmin([FromBody] BootstrapAdminDto input, [FromServices] IConfiguration config)
    {
        // Use a secret key from configuration (or fallback for dev)
        var expectedKey = config["AdminBootstrapKey"] ?? "admin-setup-key-2026";
        
        if (input.SecretKey != expectedKey)
            return Unauthorized(new { message = "Invalid secret key." });

        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user == null) return NotFound(new { message = "User not found. Register first." });

        // Ensure role exists
        if (!await _roleManager.RoleExistsAsync("Admin"))
            await _roleManager.CreateAsync(new IdentityRole("Admin"));

        if (await _userManager.IsInRoleAsync(user, "Admin"))
            return Ok(new { message = "User is already an Admin." });

        await _userManager.AddToRoleAsync(user, "Admin");
        return Ok(new { message = $"{user.Name} is now an Admin. Log out and log back in to activate." });
    }

    [HttpGet("check")]
    public async Task<ActionResult> CheckAdminStatus()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        return Ok(new { isAdmin, userName = user.UserName, email = user.Email });
    }
}

public class PromoteDto
{
    public required string Email { get; set; }
}

public class BootstrapAdminDto
{
    public required string Email { get; set; }
    public required string SecretKey { get; set; }
}
