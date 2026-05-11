using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Data;
using ScamAlert.Data.Entities;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Controllers;

public sealed class CustomersController(
    ScamAlertDbContext dbContext,
    IPasswordHasher passwordHasher,
    IAuthorizationService authorizationService) : BaseApiController
{
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        if (request.Contacts.Count == 0)
        {
            return BadRequest("At least one contact is required.");
        }

        if (request.Contacts
            .GroupBy(x => x.EscalationOrder)
            .Any(x => x.Count() > 1))
        {
            return BadRequest("Contacts must have distinct escalation order values.");
        }

        if (request.Devices.Count == 0)
        {
            return BadRequest("At least one monitored device is required.");
        }

        var now = DateTimeOffset.UtcNow;
        var customerId = Guid.NewGuid();
        var deviceProvisioning = request.Devices
            .Select(x => new { Request = x, ApiKey = GenerateApiKey() })
            .ToList();

        var customer = new Customer
        {
            Id = customerId,
            Name = request.Name,
            Email = request.Email,
            CreatedUtc = now,
            UpdatedUtc = now,
            Subscriptions =
            [
                new Subscription
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customerId,
                    PlanCode = request.PlanCode,
                    Status = SubscriptionStatus.Active,
                    StartsUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }
            ],
            Contacts = request.Contacts.Select(x => new Contact
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                FullName = x.FullName,
                PhoneNumber = x.PhoneNumber,
                EscalationOrder = x.EscalationOrder,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList(),
            Devices = deviceProvisioning.Select(x => new MonitoredDevice
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                DeviceName = x.Request.DeviceName,
                ExternalDeviceId = x.Request.ExternalDeviceId,
                IngestApiKeyHash = passwordHasher.HashPassword(x.ApiKey),
                IngestApiKeyCreatedUtc = now,
                IsActive = true,
                CreatedUtc = now,
                UpdatedUtc = now
            }).ToList()
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, new
        {
            customer.Id,
            customer.Name,
            customer.Email,
            Devices = customer.Devices.Select(x => new
            {
                x.Id,
                x.DeviceName,
                x.ExternalDeviceId,
                ingestApiKey = deviceProvisioning
                    .Single(p => p.Request.ExternalDeviceId == x.ExternalDeviceId)
                    .ApiKey
            }),
            Contacts = customer.Contacts.Select(x => new { x.Id, x.FullName, x.PhoneNumber, x.EscalationOrder })
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Include(x => x.Contacts)
            .Include(x => x.Devices)
            .Include(x => x.Subscriptions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (customer is null)
        {
            return NotFound();
        }

        var auth = await authorizationService.AuthorizeAsync(User, customer.Id, AuthPolicies.CustomerScope);
        if (!auth.Succeeded)
        {
            return Forbid();
        }

        return Ok(new
        {
            customer.Id,
            customer.Name,
            customer.Email,
            Contacts = customer.Contacts
                .OrderBy(x => x.EscalationOrder)
                .Select(x => new { x.Id, x.FullName, x.PhoneNumber, x.EscalationOrder, x.IsActive }),
            Devices = customer.Devices
                .Select(x => new { x.Id, x.DeviceName, x.ExternalDeviceId, x.IsActive }),
            Subscriptions = customer.Subscriptions
                .Select(x => new { x.Id, x.PlanCode, x.Status, x.StartsUtc, x.EndsUtc })
        });
    }

    private static string GenerateApiKey()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }
}
