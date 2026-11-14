using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Models;
using RaceDay.Api.Security;

namespace RaceDay.Api.Controllers;

[ApiController]
[Route("api/results")]
[SessionAuthorize]
public class ResultsController(RaceDayDbContext db) : ControllerBase
{
    [HttpPost, SessionAuthorize(Roles.Organiser)]
    public async Task<IActionResult> Record(ResultRequest request)
    {
        var enrolment = await db.Enrolments.Include(e => e.Event).Include(e => e.Result).FirstOrDefaultAsync(e => e.Id == request.EnrolmentId);
        if (enrolment is null) return NotFound(new { message = "Enrolment not found." });
        if (enrolment.Event!.OrganiserId != HttpContext.Session.GetInt32("UserId"))
            return StatusCode(403, new { message = "You do not own this event." });
        if (request.FinishTime <= TimeSpan.Zero || request.FinishingPosition < 1)
            return BadRequest(new { message = "Finish time and position must be positive." });
        if (request.Status is not ("Finished" or "DNF" or "DQ"))
            return BadRequest(new { message = "Status must be Finished, DNF, or DQ." });
        if (enrolment.Result is not null) return Conflict(new { message = "A result already exists for this enrolment." });
        var result = new RaceResult { EnrolmentId = enrolment.Id, FinishTime = request.FinishTime,
            FinishingPosition = request.FinishingPosition, Status = request.Status };
        db.Results.Add(result);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(MyResults), new { id = result.Id },
            new { result.Id, result.EnrolmentId, result.FinishTime, result.FinishingPosition, result.Status, result.RecordedAt });
    }

    [HttpGet("me"), SessionAuthorize(Roles.Participant)]
    public async Task<IActionResult> MyResults()
    {
        var participantId = HttpContext.Session.GetInt32("UserId")!.Value;
        var results = await db.Results.Where(r => r.Enrolment!.ParticipantId == participantId)
            .Select(r => new { r.Id, Event = r.Enrolment!.Event!.Name, Date = r.Enrolment.Event.Date,
                Category = r.Enrolment.Category!.Name, r.FinishTime, r.FinishingPosition, r.Status,
                FinisherCount = db.Results.Count(other => other.Enrolment!.EventId == r.Enrolment.EventId &&
                    other.Enrolment.CategoryId == r.Enrolment.CategoryId && other.Status == "Finished") })
            .ToListAsync();
        return Ok(results.Select(r => new
        {
            r.Id, r.Event, r.Date, r.Category, r.FinishTime, r.FinishingPosition, r.Status, r.FinisherCount,
            PositionDisplay = $"{r.FinishingPosition} of {r.FinisherCount} finishers"
        }));
    }

    [HttpGet("event/{eventId:int}")]
    public async Task<IActionResult> Leaderboard(int eventId)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId)) return NotFound();
        return Ok(await db.Results.Where(r => r.Enrolment!.EventId == eventId)
            .OrderBy(r => r.Enrolment!.Category!.Name).ThenBy(r => r.FinishingPosition).ThenBy(r => r.FinishTime)
            .Select(r => new { r.Id, Participant = r.Enrolment!.Participant!.FullName,
                Category = r.Enrolment.Category!.Name, r.FinishTime, r.FinishingPosition, r.Status })
            .ToListAsync());
    }
}
