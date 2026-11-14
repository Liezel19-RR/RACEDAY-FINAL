using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Models;
using RaceDay.Api.Security;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events/{eventId:int}/categories")]
public class CategoriesController(RaceDayDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetForEvent(int eventId)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId)) return NotFound(new { message = "Event not found." });
        return Ok(await db.Categories.Where(c => c.EventId == eventId)
            .Select(c => new { c.Id, c.EventId, c.Name, c.Description, c.DistanceKm, c.Price, c.Capacity,
                EnrolledCount = c.Event!.Enrolments.Count(e => e.CategoryId == c.Id && e.Status != "Cancelled") })
            .ToListAsync());
    }

    [HttpPost, SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Create(int eventId, CategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Category name is required." });
        var evt = await db.Events.FindAsync(eventId);
        if (evt is null) return NotFound(new { message = "Event not found." });
        if (evt.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        var category = new EventCategory { EventId = eventId, Name = request.Name.Trim(), Description = request.Description?.Trim(),
            DistanceKm = request.DistanceKm, Price = request.Price, Capacity = request.Capacity };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetForEvent), new { eventId },
            new { category.Id, category.EventId, category.Name, category.Description, category.DistanceKm, category.Price, category.Capacity });
    }

    [HttpPut("{categoryId:int}"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Update(int eventId, int categoryId, CategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message = "Category name is required." });
        var evt = await db.Events.FindAsync(eventId);
        if (evt is null) return NotFound();
        if (evt.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId && c.EventId == eventId);
        if (category is null) return NotFound();
        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.DistanceKm = request.DistanceKm;
        category.Price = request.Price;
        category.Capacity = request.Capacity;
        await db.SaveChangesAsync();
        return Ok(new { category.Id, category.EventId, category.Name, category.Description, category.DistanceKm, category.Price, category.Capacity });
    }

    [HttpDelete("{categoryId:int}"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Delete(int eventId, int categoryId)
    {
        var evt = await db.Events.FindAsync(eventId);
        if (evt is null) return NotFound();
        if (evt.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        var category = await db.Categories.Include(c => c.Event).FirstOrDefaultAsync(c => c.Id == categoryId && c.EventId == eventId);
        if (category is null) return NotFound();
        if (await db.Enrolments.AnyAsync(e => e.CategoryId == categoryId))
            return Conflict(new { message = "A category with enrolments cannot be deleted." });
        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
