using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Models;
using RaceDay.Api.Security;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController(RaceDayDbContext db, IBlobStorageService blobStorage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search, [FromQuery] string? type, [FromQuery] string? location,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool upcomingOnly = false)
    {
        var query = db.Events.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Name.Contains(search) || e.Description.Contains(search));
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(e => e.EventType == type);
        if (!string.IsNullOrWhiteSpace(location)) query = query.Where(e => e.Location.Contains(location));
        if (from.HasValue) query = query.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue) query = query.Where(e => e.Date < to.Value.Date.AddDays(1));
        if (upcomingOnly) query = query.Where(e => e.Date >= DateTime.UtcNow.Date);
        return Ok(await query.OrderBy(e => e.Date).Select(EventSummary).ToListAsync());
    }

    [HttpGet("mine"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Mine()
    {
        var organiserId = HttpContext.Session.GetInt32("UserId");
        return Ok(await db.Events.Where(e => e.OrganiserId == organiserId)
            .OrderBy(e => e.Date).Select(EventSummary).ToListAsync());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await db.Events.Where(e => e.Id == id).Select(EventSummary).SingleOrDefaultAsync();
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost, SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Create(EventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location))
            return BadRequest(new { message = "Event name and location are required." });
        if (!ValidType(request.EventType)) return BadRequest(new { message = "EventType must be Run, Walk, or Cycle." });
        var item = new RaceEvent { Name = request.Name.Trim(), Description = request.Description.Trim(), Date = request.Date,
            Location = request.Location.Trim(), DistanceKm = request.DistanceKm, EventType = request.EventType,
            OrganiserId = HttpContext.Session.GetInt32("UserId")!.Value };
        db.Events.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:int}"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Update(int id, EventRequest request)
    {
        var item = await db.Events.FindAsync(id);
        if (item is null) return NotFound();
        if (item.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location))
            return BadRequest(new { message = "Event name and location are required." });
        if (!ValidType(request.EventType)) return BadRequest(new { message = "EventType must be Run, Walk, or Cycle." });
        item.Name = request.Name.Trim(); item.Description = request.Description.Trim(); item.Date = request.Date;
        item.Location = request.Location.Trim(); item.DistanceKm = request.DistanceKm; item.EventType = request.EventType;
        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpPost("{id:int}/banner"), SessionAuthorize(Roles.Organiser)]
    [RequestSizeLimit(ImageValidator.MaxFileSize + 65536)]
    public async Task<IActionResult> UploadBanner(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!ImageValidator.IsValid(file, out var error)) return BadRequest(new { message = error });
        var item = await db.Events.FindAsync(id);
        if (item is null) return NotFound();
        if (item.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        if (!blobStorage.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Image storage is not configured." });
        await using var stream = file!.OpenReadStream();
        item.BannerImageUrl = await blobStorage.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);
        await db.SaveChangesAsync();
        return Ok(new { item.Id, item.BannerImageUrl });
    }

    [HttpDelete("{id:int}"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await db.Events.Include(e => e.Enrolments).FirstOrDefaultAsync(e => e.Id == id);
        if (item is null) return NotFound();
        if (item.OrganiserId != HttpContext.Session.GetInt32("UserId")) return StatusCode(403, new { message = "You do not own this event." });
        if (item.Enrolments.Count > 0) return Conflict(new { message = "An event with enrolments cannot be deleted." });
        db.Events.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static bool ValidType(string type) => new[] { "Run", "Walk", "Cycle" }.Contains(type, StringComparer.OrdinalIgnoreCase);
    private static readonly System.Linq.Expressions.Expression<Func<RaceEvent, object>> EventSummary =
        e => new { e.Id, e.Name, e.Description, e.Date, e.Location, e.DistanceKm, e.EventType, e.BannerImageUrl,
            e.OrganiserId, Categories = e.Categories.Select(c => new { c.Id, c.Name, c.Description, c.DistanceKm, c.Price, c.Capacity,
                EnrolledCount = c.Event!.Enrolments.Count(x => x.CategoryId == c.Id && x.Status != "Cancelled") }) };
}
