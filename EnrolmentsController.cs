using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Models;
using RaceDay.Api.Security;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/enrolments")]
[SessionAuthorize]
public class EnrolmentsController(RaceDayDbContext db) : ControllerBase
{
    [HttpPost, SessionAuthorize(Roles.Participant)]
    public async Task<IActionResult> Enrol(EnrolRequest request)
    {
        var participantId = HttpContext.Session.GetInt32("UserId")!.Value;
        var evt = await db.Events.FindAsync(request.EventId);
        if (evt is null) return NotFound(new { message = "Event not found." });
        if (evt.Date.Date < DateTime.UtcNow.Date) return BadRequest(new { message = "You cannot enrol in a past event." });
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId && c.EventId == request.EventId);
        if (category is null) return BadRequest(new { message = "Choose a category belonging to this event." });
        var enrolment = await db.Enrolments.FirstOrDefaultAsync(e => e.ParticipantId == participantId && e.EventId == request.EventId);
        if (enrolment is not null && enrolment.Status != "Cancelled")
            return Conflict(new { message = "You have already enrolled for this event." });
        var activeCount = await db.Enrolments.CountAsync(e => e.CategoryId == category.Id && e.Status != "Cancelled");
        if (category.Capacity.HasValue && activeCount >= category.Capacity.Value)
            return Conflict(new { message = "This category is full." });
        if (enrolment is null)
        {
            enrolment = new Enrolment { ParticipantId = participantId, EventId = request.EventId, CategoryId = request.CategoryId };
            db.Enrolments.Add(enrolment);
        }
        else
        {
            enrolment.CategoryId = request.CategoryId;
            enrolment.Status = "Confirmed";
            enrolment.EnrolledAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(MyEnrolments), new { id = enrolment.Id },
            new { enrolment.Id, enrolment.EventId, enrolment.CategoryId, enrolment.Status, enrolment.EnrolledAt });
    }

    [HttpGet("mine"), SessionAuthorize(Roles.Participant)]
    public async Task<IActionResult> MyEnrolments()
    {
        var id = HttpContext.Session.GetInt32("UserId")!.Value;
        return Ok(await db.Enrolments.Where(e => e.ParticipantId == id)
            .Select(e => new { e.Id, e.Status, e.EnrolledAt, Event = e.Event!.Name, EventDate = e.Event!.Date,
                EventType = e.Event.EventType, BannerImageUrl = e.Event.BannerImageUrl,
                Category = e.Category!.Name, Participant = e.Participant!.FullName,
                Result = e.Result == null ? null : new { e.Result.Status, e.Result.FinishTime, e.Result.FinishingPosition } })
            .ToListAsync());
    }

    [HttpGet("event/{eventId:int}"), SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> ForEvent(int eventId)
    {
        var userId = HttpContext.Session.GetInt32("UserId")!.Value;
        if (!await db.Events.AnyAsync(e => e.Id == eventId && e.OrganiserId == userId)) return NotFound();
        return Ok(await db.Enrolments.Where(e => e.EventId == eventId)
            .Select(e => new { e.Id, e.EventId, Event = e.Event!.Name, EventDate = e.Event.Date,
                ParticipantId = e.ParticipantId, Participant = e.Participant!.FullName, Email = e.Participant.Email,
                CategoryId = e.CategoryId, Category = e.Category!.Name, e.Status, e.EnrolledAt,
                Result = e.Result == null ? null : new { e.Result.Status, e.Result.FinishTime, e.Result.FinishingPosition } })
            .ToListAsync());
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, EnrolmentStatusRequest request)
    {
        var enrolment = await db.Enrolments.Include(e => e.Event).Include(e => e.Result).FirstOrDefaultAsync(e => e.Id == id);
        if (enrolment is null) return NotFound();
        var userId = HttpContext.Session.GetInt32("UserId")!.Value;
        var role = HttpContext.Session.GetString("Role");
        var participantOwnEntry = role == Roles.Participant && enrolment.ParticipantId == userId && request.Status == "Cancelled";
        var organiserOwnEvent = role == Roles.Organiser && enrolment.Event!.OrganiserId == userId;
        if (!participantOwnEntry && !organiserOwnEvent)
            return StatusCode(403, new { message = "You cannot change this enrolment." });
        if (request.Status is not ("Confirmed" or "Cancelled"))
            return BadRequest(new { message = "Status must be Confirmed or Cancelled." });
        if (request.Status == "Cancelled" && enrolment.Result is not null)
            return Conflict(new { message = "An enrolment with a result cannot be cancelled." });
        if (request.Status == "Confirmed" && enrolment.Status == "Cancelled")
        {
            var category = await db.Categories.FindAsync(enrolment.CategoryId);
            if (category?.Capacity is int capacity &&
                await db.Enrolments.CountAsync(e => e.CategoryId == category.Id && e.Status != "Cancelled") >= capacity)
                return Conflict(new { message = "This category is full." });
        }
        enrolment.Status = request.Status;
        await db.SaveChangesAsync();
        return Ok(new { enrolment.Id, enrolment.Status });
    }
}
