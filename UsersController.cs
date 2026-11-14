using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Models;
using RaceDay.Api.Security;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/users")]
[SessionAuthorize]
public class UsersController(RaceDayDbContext db, IBlobStorageService blobStorage) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = HttpContext.Session.GetInt32("UserId");
        var user = await db.Users.FindAsync(id);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request)
    {
        var id = HttpContext.Session.GetInt32("UserId");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id))
            return Conflict(new { message = "An account with this email already exists." });
        user.FullName = request.FullName.Trim();
        user.Email = email;
        user.Phone = request.Phone;
        await db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    [HttpPost("me/profile-picture")]
    [RequestSizeLimit(ImageValidator.MaxFileSize + 65536)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile? file, CancellationToken cancellationToken)
    {
        if (!ImageValidator.IsValid(file, out var error)) return BadRequest(new { message = error });
        if (!blobStorage.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Image storage is not configured." });
        var user = await db.Users.FindAsync(HttpContext.Session.GetInt32("UserId"));
        if (user is null) return NotFound();
        await using var stream = file!.OpenReadStream();
        user.ProfilePictureUrl = await blobStorage.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);
        await db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    private static UserProfileDto ToDto(User u) => new(u.Id, u.FullName, u.Email, u.Role, u.Phone, u.ProfilePictureUrl);
}
